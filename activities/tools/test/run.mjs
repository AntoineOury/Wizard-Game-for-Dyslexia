#!/usr/bin/env node
// Headless browser test for the activity (Playwright + Chromium).
//
// Serves activities/ over a local static server, drives the activity like a learner (a scripted child who
// gets most items right, taps one visually confusable distractor, and goes quiet on one item), and asserts
// the contract: item flow, feedback + reproduction + requeue, no-response handling, resume, completion
// telemetry, touch-target sizes, and a clean console. Run with the audio assets present to also verify
// that Chromium decodes every narration file (the start event reports narration_missing).
//
//   node activities/tools/test/run.mjs            # all scenarios
//   node activities/tools/test/run.mjs --headed   # watch it
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";
import { execSync } from "node:child_process";

const here = path.dirname(fileURLToPath(import.meta.url));
const activitiesRoot = path.resolve(here, "../..");
const headed = process.argv.includes("--headed");

async function loadPlaywright() {
  try { return await import("playwright"); } catch { /* fall through */ }
  const globalRoot = execSync("npm root -g").toString().trim();
  const req = createRequire(path.join(globalRoot, "noop.js"));
  return req("playwright");
}

const MIME = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".mjs": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8", ".json": "application/json; charset=utf-8", ".woff2": "font/woff2", ".mp3": "audio/mpeg", ".txt": "text/plain; charset=utf-8", ".svg": "image/svg+xml" };
function serve(root) {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      const urlPath = decodeURIComponent(new URL(req.url, "http://x").pathname);
      let file = path.join(root, urlPath);
      if (!file.startsWith(root)) { res.writeHead(403); return res.end(); }
      if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, "index.html");
      if (!fs.existsSync(file)) { res.writeHead(404); return res.end("not found"); }
      res.writeHead(200, { "Content-Type": MIME[path.extname(file)] || "application/octet-stream", "Cache-Control": "no-store" });
      fs.createReadStream(file).pipe(res);
    });
    server.listen(0, "127.0.0.1", () => resolve({ server, base: `http://127.0.0.1:${server.address().port}` }));
  });
}

const results = [];
function check(name, ok, detail = "") {
  results.push({ name, ok, detail });
  console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? `  — ${detail}` : ""}`);
}

/** Wire telemetry capture + console capture into a page. */
async function instrument(page) {
  const events = [];
  const consoleErrors = [];
  await page.exposeFunction("__olEvent", (msg) => events.push(msg));
  await page.addInitScript(() => {
    for (const t of ["activity:start", "activity:item", "activity:complete"]) {
      window.addEventListener(t, (e) => window.__olEvent(e.detail));
    }
  });
  page.on("console", (m) => { if (m.type() === "error") consoleErrors.push(m.text()); if (process.env.OL_TEST_VERBOSE) console.log("   [page]", m.type(), m.text().slice(0, 200)); });
  page.on("pageerror", (e) => consoleErrors.push(String(e)));
  return { events, consoleErrors };
}

/** The one gesture a tablet needs before audio may play: the child taps the start button. */
async function startActivity(page) {
  await page.waitForSelector(".ol-start__btn:not([disabled])", { timeout: 30000 });
  await page.click(".ol-start__btn");
}

const targetFromCaption = (caption) => caption.match(/Tap the letter ([a-z])\./)?.[1] ?? null;

/** Wait until the activity is asking for a tap (cards present, caption is a prompt, cards enabled). */
/** Returns the prompt caption, or null once the completion surface is up. */
async function waitForPrompt(page, timeout = 20000) {
  try {
    await page.waitForFunction(() => {
      if (document.querySelector(".ol-complete.is-shown")) return true;
      const cards = [...document.querySelectorAll(".ol-card")].filter((c) => !c.classList.contains("is-dim"));
      const cap = document.querySelector(".ol-caption")?.textContent || "";
      return cards.length > 0 && /^Tap the letter [a-z]\.$/.test(cap);
    }, null, { timeout });
  } catch (err) {
    const state = await page.evaluate(() => ({
      caption: document.querySelector(".ol-caption")?.textContent,
      cards: [...document.querySelectorAll(".ol-card")].map((c) => c.className),
      start: document.querySelector(".ol-start")?.className, startBtnDisabled: document.querySelector(".ol-start__btn")?.disabled,
      hint: document.querySelector(".ol-start__hint")?.textContent, state: document.querySelector(".ol-cards")?.dataset.state,
      body: document.body.innerText.slice(0, 200),
    })).catch((e) => String(e));
    throw new Error(`waitForPrompt timed out; page state: ${JSON.stringify(state)}`);
  }
  if (await page.$(".ol-complete.is-shown")) return null;
  return page.$eval(".ol-caption", (el) => el.textContent);
}

async function tapLetter(page, letter) {
  await page.dispatchEvent(`.ol-card[data-letter="${letter}"]`, "pointerdown", { pointerId: 1, isPrimary: true, pointerType: "touch" });
  await page.dispatchEvent(`.ol-card[data-letter="${letter}"]`, "pointerup", { pointerId: 1, isPrimary: true, pointerType: "touch" });
}

async function scenarioGuided(browser, base) {
  console.log("\n— guided: scripted learner (one visual error, one silence, rest correct) —");
  const page = await browser.newPage({ viewport: { width: 1180, height: 820 }, hasTouch: true });
  const { events, consoleErrors } = await instrument(page);
  await page.goto(`${base}/ort-17-hear-see-tap-letter-g1/index.html?phase=guided&units=a,b,c,d,e&seed=4242&run=t1&fast=1&narration=text`);
  await startActivity(page);

  // Touch targets (Vision VZ-9): cards and the replay control are at least 60 px.
  await waitForPrompt(page);
  const sizes = await page.$$eval(".ol-card, .ol-replay", (els) => els.map((e) => { const r = e.getBoundingClientRect(); return [Math.round(r.width), Math.round(r.height)]; }));
  check("touch targets >= 60px", sizes.every(([w, h]) => w >= 60 && h >= 60), JSON.stringify(sizes));
  const gap = await page.$$eval(".ol-card", (els) => { const r = els.map((e) => e.getBoundingClientRect()).sort((a, b) => a.left - b.left); return r.length > 1 ? Math.round(r[1].left - r[0].right) : 99; });
  check("card separation >= 16px", gap >= 16, `${gap}px`);
  const cardCount = await page.$$eval(".ol-card", (els) => els.length);
  check("guided shows 2 options", cardCount === 2, `${cardCount}`);

  let wrongDone = false, silentDone = false, taps = 0;
  for (let guard = 0; guard < 40; guard++) {
    const caption = await waitForPrompt(page, 30000);
    if (!caption) break;
    const target = targetFromCaption(caption);
    const options = await page.$$eval(".ol-card:not(.is-dim)", (els) => els.map((e) => e.dataset.letter));
    const state = await page.$eval(".ol-cards", (el) => el.dataset.state).catch(() => "prompt");
    const inRepair = state === "repair" || state === "together";
    if (!wrongDone && target === "b" && !inRepair) {
      await tapLetter(page, options.find((o) => o !== "b")); wrongDone = true; // a diagnostic distractor → "That's x." + contrast + demonstration
    } else if (!silentDone && target === "c" && !inRepair) {
      silentDone = true; // say nothing: two timeouts → neutral re-prompt, then no-response + demonstration
      await page.waitForFunction(() => document.querySelector(".ol-cards")?.dataset.state === "after-no-response", null, { timeout: 30000 });
      if (!(await waitForPrompt(page, 30000))) break;
      await tapLetter(page, target);
    } else {
      await tapLetter(page, target);
    }
    taps++;
    await page.waitForTimeout(150);
  }
  await page.waitForFunction(() => !!document.querySelector(".ol-complete.is-shown"), null, { timeout: 60000 });

  const items = events.filter((e) => e.type === "activity:item").map((e) => e.payload);
  const complete = events.find((e) => e.type === "activity:complete")?.payload;
  check("completed", complete?.status === "completed", `status=${complete?.status}`);
  check("10 planned items + 2 requeues reported", items.length === 12, `${items.length} items`);
  const wrong = items.find((i) => i.outcome === "incorrect");
  check("incorrect item classified, repaired by reproduction, requeued", ["visual", "name", "visual_name", "other"].includes(wrong?.error_class) && wrong?.repair?.reproduced === true && wrong?.requeued === true, JSON.stringify({ tapped: wrong?.first_tap, target: wrong?.unit, error_class: wrong?.error_class, repair: wrong?.repair, requeued: wrong?.requeued }));
  const silent = items.find((i) => i.outcome === "no_response");
  check("silence logged as no_response (not an error) and requeued", !!silent && silent.requeued && silent.reprompts === 2, JSON.stringify({ reprompts: silent?.reprompts, requeued: silent?.requeued }));
  const requeued = items.filter((i) => i.attempt === 2);
  check("requeued items return later with attempt=2 and never eligible for mastery", requeued.length === 2 && requeued.every((i) => i.eligible_for_mastery === false), `${requeued.length}`);
  const idx = items.findIndex((i) => i.outcome === "incorrect");
  const again = items.findIndex((i) => i.attempt === 2 && i.unit === wrong?.unit);
  check("requeued item has at least one item in between", again - idx >= 2, `${idx} → ${again}`);
  check("every item carries latency, options, target_position, scaffold, narration_mode", items.every((i) => Number.isFinite(i.latency_ms) && Array.isArray(i.options) && i.target_position >= 0 && typeof i.scaffold === "string" && i.narration_mode), "");
  check("correct first-attempt unscaffolded items are mastery-eligible", items.filter((i) => i.outcome === "correct" && i.attempt === 1 && i.scaffold === "none").every((i) => i.eligible_for_mastery), "");
  const positions = new Set(items.map((i) => i.target_position));
  check("target position varies (P-14)", positions.size > 1, [...positions].join(","));
  check("no console errors", consoleErrors.length === 0, consoleErrors.slice(0, 3).join(" | "));
  await page.close();
}

async function scenarioModelAndIndependent(browser, base) {
  console.log("\n— model (1 option) and independent (4 options) —");
  for (const [phase, expected] of [["model", 1], ["independent", 4]]) {
    const page = await browser.newPage({ viewport: { width: 1024, height: 768 }, hasTouch: true });
    const { events, consoleErrors } = await instrument(page);
    await page.goto(`${base}/ort-17-hear-see-tap-letter-g1/index.html?phase=${phase}&units=m,n,h,r,u&seed=7&run=t2${phase}&fast=1&narration=text&passes=1`);
    await startActivity(page);
    await waitForPrompt(page);
    const n = await page.$$eval(".ol-card", (els) => els.length);
    check(`${phase} shows ${expected} option(s)`, n === expected, `${n}`);
    for (let guard = 0; guard < 10; guard++) {
      const caption = await waitForPrompt(page, 30000);
      if (!caption) break;
      await tapLetter(page, targetFromCaption(caption));
      await page.waitForTimeout(120);
    }
    await page.waitForFunction(() => !!document.querySelector(".ol-complete.is-shown"), null, { timeout: 30000 });
    const items = events.filter((e) => e.type === "activity:item").map((e) => e.payload);
    if (phase === "model") check("model items are 'presentation' and never mastery-eligible", items.length === 5 && items.every((i) => i.outcome === "presentation" && !i.eligible_for_mastery), `${items.length}`);
    else check("independent distractors are drawn from the unit set", items.every((i) => i.options.every((o) => "mnhru".includes(o)) && i.options.length === 4), "");
    check(`${phase}: no console errors`, consoleErrors.length === 0, consoleErrors.slice(0, 2).join(" | "));
    await page.close();
  }
}

async function scenarioResume(browser, base) {
  console.log("\n— resume after interruption (AC-5) —");
  const context = await browser.newContext({ viewport: { width: 1024, height: 768 }, hasTouch: true });
  const page = await context.newPage();
  const { events } = await instrument(page);
  const url = `${base}/ort-17-hear-see-tap-letter-g1/index.html?phase=guided&units=a,b,c&seed=99&run=resume1&fast=1&narration=text`;
  await page.goto(url);
  await startActivity(page);
  for (let i = 0; i < 2; i++) { const cap = await waitForPrompt(page); await tapLetter(page, targetFromCaption(cap)); await page.waitForTimeout(150); }
  await page.waitForFunction(() => window.__olEvent && document.querySelectorAll(".ol-progress__dot.is-done").length >= 2, null, { timeout: 20000 });
  const doneBefore = await page.$$eval(".ol-progress__dot.is-done", (els) => els.length);
  await page.reload();
  await startActivity(page);
  await waitForPrompt(page);
  const doneAfter = await page.$$eval(".ol-progress__dot.is-done", (els) => els.length);
  const startEvt = events.filter((e) => e.type === "activity:start").pop()?.payload;
  check("progress survives a reload", doneAfter === doneBefore && doneBefore === 2, `${doneBefore} → ${doneAfter}`);
  check("telemetry record resumed (same run, resumed_count=1)", startEvt?.resumed_count === 1, `resumed_count=${startEvt?.resumed_count}`);
  await context.close();
}

async function scenarioAudioDecode(browser, base) {
  console.log("\n— audio assets decode in Chromium (NA-6/NA-13 preload) —");
  const page = await browser.newPage({ viewport: { width: 1024, height: 768 } });
  const { events } = await instrument(page);
  await page.goto(`${base}/ort-17-hear-see-tap-letter-g1/index.html?phase=independent&units=a,b,c,d,e,f,g&pool=h,i,j,k,l,m,n,o,p,q,r,s,t,u,v,w,x,y,z&seed=1&run=audio1`);
  await startActivity(page);
  await page.waitForFunction(() => window.__olEvent && document.querySelector(".ol-caption")?.textContent?.length > 0, null, { timeout: 60000 }).catch(() => {});
  await page.waitForTimeout(500);
  const start = events.find((e) => e.type === "activity:start")?.payload;
  const hasAudio = fs.existsSync(path.join(activitiesRoot, "ort-17-hear-see-tap-letter-g1/narration/audio/prompt_a.mp3"));
  if (!hasAudio) { check("audio assets present", false, "narration/audio is empty — run narration:generate"); }
  else check("all preloaded narration decoded (none missing)", start && start.narration_missing?.length === 0 && start.narration_mode === "audio", JSON.stringify({ missing: start?.narration_missing?.length, mode: start?.narration_mode }));
  await page.close();
}

const { chromium } = await loadPlaywright();
const { server, base } = await serve(activitiesRoot);
const browser = await chromium.launch({ headless: !headed });
try {
  await scenarioGuided(browser, base);
  await scenarioModelAndIndependent(browser, base);
  await scenarioResume(browser, base);
  await scenarioAudioDecode(browser, base);
} catch (err) {
  check("test run completed without exceptions", false, String(err?.stack || err).split("\n").slice(0, 3).join(" "));
} finally {
  await browser.close();
  server.close();
}
const failed = results.filter((r) => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exit(failed.length ? 1 : 0);
