// Hear Letter Name, See Letter, Tap Letter — Activity Corpus ORT-17 (Hear, See Tap - Target + Distractors)
// with ORT-15 (Hear, See, Tap - Single Target) as its model phase. Letter Unit: Group 1 (lower-case a–z).
//
// The tutor's exercise this digitizes: letter-card identification — the tutor lays out a few letter cards and
// says "Point to the letter m." The child points. If the child points to the wrong card, the tutor names the
// card they chose, contrasts it with the target (by shape, or by name when the names sound alike), points to
// the right card, and has the child point to it; the card comes back later in the drill (Vision FI-1, FI-4).
//
// Rules cited inline by ID: Vision (FI, AC, NA, TN, GM, VZ, PL) and PEDAGOGY (L, P, F, M, R, A).

import { makeRng, randomSeed } from "../shared/js/rng.js";
import { Narrator, loadManifest } from "../shared/js/narration.js";
import { Telemetry } from "../shared/js/telemetry.js";
import { readParams, parseUnits, runKeyFor, RunState, exitToHost } from "../shared/js/host.js";
import { buildShell, renderProgress, letterCard, showStart, showComplete, makeTone, debugDrawer } from "../shared/js/components.js";

export const ACTIVITY_ID = "ort-17-hear-see-tap-letter-g1";
export const VERSION = "1.0.0";

// Timing constants. Latency is measured, never displayed (PEDAGOGY L-16, Vision TN-7).
export const TIMING = {
  // PEDAGOGY M-6 "acceptable" for a letter name = 2 s; Vision AC-11 multiplies by 1.5 for this population.
  acceptableMs: 3000,
  // PEDAGOGY R-9: wait 2x acceptable with no visible countdown, then a neutral re-prompt; a second timeout is a
  // no-response (not an error). The clock starts when the prompt has finished playing.
  timeoutMs: 6000,
  // Vision NA-12: a deliberate pause after the prompt before expecting a response.
  postPromptPauseMs: 800,
  // A first tap faster than this, i.e. before the letter name could have been processed, is treated as a guess
  // (PEDAGOGY F-11: fast-and-wrong -> slow the pace, do not reteach).
  fastWrongMs: 900,
  confirmHoldMs: 650,
  demoHoldMs: 1100,
  betweenItemsMs: 380,
  slowPaceExtraMs: 900,
  // Vision AC-3: target <= 2 min of learner time; hard ceiling 3 min, then end gracefully with partial results.
  ceilingActiveMs: 180000,
  // Idle beyond this inside one item is not counted as learner time (R-10: never penalize idle).
  perItemActiveCapMs: 20000,
};

const PHASES = new Set(["model", "guided", "independent"]);

const DEFAULTS = {
  phase: "guided",
  units: "a,b,c,d,e",
  pool: "",
  passes: "",
  seed: "",
  sfx: "1",
  hand: "right",
  lines: "1",
  narration: "", // testing override: audio | tts | text
  debug: "0",
  return: "",
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** One open "gate" per response window: the first tap through it is the scored response (E-SELECT). */
class TapGate {
  constructor() { this.reset(); }
  reset() { this.value = null; this._resolve = null; this.promise = null; this.openedAt = 0; }
  open() {
    this.value = null;
    this.openedAt = performance.now();
    this.promise = new Promise((res) => { this._resolve = res; });
    return this.promise;
  }
  get isOpen() { return !!this._resolve; }
  tap(letter) {
    if (!this._resolve) return false;
    this.value = { letter, at: performance.now() };
    const r = this._resolve;
    this._resolve = null;
    r(this.value);
    return true;
  }
  close() { this._resolve = null; }
  /** Resolve with the tap, or null after `ms`. */
  waitFor(ms) {
    if (this.value) return Promise.resolve(this.value);
    return Promise.race([this.promise, sleep(ms).then(() => null)]);
  }
}

export function buildQueue(units, passes, rng) {
  const items = [];
  let prev = null;
  for (let p = 0; p < passes; p++) {
    const order = rng.shuffle(units);
    if (units.length > 1 && order[0] === prev) order.push(order.shift()); // never the same letter twice in a row
    for (const u of order) items.push({ id: `${p}:${u}`, unit: u, attempt: 1, pass: p + 1 });
    prev = order[order.length - 1];
  }
  return items;
}

/**
 * Diagnostic distractors (PEDAGOGY F-9): letters confusable with the target by shape and/or by name are weighted
 * up so the choice the child makes is the choice the skill is about; every other pool letter stays possible so the
 * set never becomes predictable (P-14). Positions are shuffled.
 */
export function buildOptions(target, pool, count, rng, letters) {
  const T = letters.get(target);
  const cands = pool.filter((c) => c !== target).map((c) => {
    const C = letters.get(c);
    const visual = T.visual_confusions.includes(c) || C.visual_confusions.includes(target);
    const name = T.name_confusions.includes(c) || C.name_confusions.includes(target);
    return { value: c, weight: 1 + (visual ? 2 : 0) + (name ? 1 : 0) };
  });
  const distractors = rng.weightedSample(cands, Math.max(0, count - 1));
  return rng.shuffle([target, ...distractors]);
}

/** Error taxonomy for this activity (PEDAGOGY F-8), per the corpus family's error patterns. */
export function classifyError(tapped, target, letters, latencyMs, fastMs = TIMING.fastWrongMs) {
  const T = letters.get(target), X = letters.get(tapped);
  const visual = T.visual_confusions.includes(tapped) || X.visual_confusions.includes(target);
  const name = T.name_confusions.includes(tapped) || X.name_confusions.includes(target);
  const cls = visual && name ? "visual_name" : visual ? "visual" : name ? "name" : "other";
  return { error_class: cls, fast_wrong: latencyMs < fastMs };
}

async function main() {
  const root = document.getElementById("activity");
  const params = readParams(DEFAULTS);
  const debug = params.debug === "1";
  // Test hook (?fast=1): scale every wait by 0.1 so a scripted run finishes in seconds. Never used with children.
  const fast = params.fast === "1";
  const timing = fast
    ? Object.fromEntries(Object.entries(TIMING).map(([k, v]) => [k, k === "ceilingActiveMs" || k === "perItemActiveCapMs" ? v : Math.max(30, Math.round(v * 0.1))]))
    : TIMING;

  const data = await (await fetch("../shared/data/letters-group1.json")).json();
  const letters = new Map(data.letters.map((l) => [l.symbol, l]));
  const allowed = new Set(letters.keys());

  const phase = PHASES.has(params.phase) ? params.phase : "guided";
  const units = parseUnits(params.units, allowed, { min: phase === "model" ? 1 : 3, max: 7 }); // AC-1: 3–7 units
  const poolExtra = params.pool ? parseUnits(params.pool, allowed, { min: 1, max: 26 }) : [];
  const pool = [...new Set([...units, ...poolExtra])];
  const passes = Math.max(1, Math.min(4, Number(params.passes) || (phase === "model" ? 1 : 2)));
  // Corpus: guided = 2 options; independent = 3–4 options; model = the single target (ORT-15).
  const optionCount = phase === "model" ? 1 : phase === "guided" ? 2 : Math.min(4, pool.length);

  const runKey = runKeyFor({ ...params, phase, units: units.join(",") });
  const saved = RunState.load(ACTIVITY_ID, runKey);
  const seed = saved?.seed ?? (params.seed ? Number(params.seed) >>> 0 : randomSeed());
  const rng = makeRng(saved ? (seed ^ ((saved.resumes + 1) * 0x9e3779b9)) >>> 0 : seed);

  // ---- Shell + components (shared library, VZ-8) ----
  let sfxOn = params.sfx !== "0";
  const ui = buildShell(root, {
    hand: params.hand === "left" ? "left" : "right",
    lines: params.lines === "0" ? "0" : "1",
    sfxOn,
    onReplay: () => { if (started) narrator.replay(); }, // NA-2: unlimited, never counted
    onToggleSfx: () => { sfxOn = !sfxOn; ui.setSfx(sfxOn); },
  });
  const drawer = debug ? debugDrawer(root) : null;

  // ---- Narration (NA-1, NA-6, NA-13) ----
  const manifest = await loadManifest("./narration/manifest.json");
  const narrator = new Narrator({
    manifest,
    baseUrl: new URL("./narration/", location.href).href,
    onCaption: (t) => ui.setCaption(t),
    onPlayingChange: (p) => ui.setPlaying(p),
    forceMode: ["audio", "tts", "text"].includes(params.narration) ? params.narration : null,
    textHoldScale: fast ? 0.1 : 1,
  });
  const playTone = makeTone(() => narrator.ctx);

  const keysForRun = ["intro", "together", "stop_now", `done.${units.length}`];
  for (const u of units) keysForRun.push(`prompt.${u}`, `name.${u}`, `this_is.${u}`, `thats.${u}`, `listen.${u}`, `shape.${u}`);
  for (const c of pool) keysForRun.push(`thats.${c}`, `name.${c}`);

  // ---- Telemetry (RP-3, R-12) ----
  const telemetry = new Telemetry({
    activityId: ACTIVITY_ID, version: VERSION, runKey,
    host: drawer ? { onMessage: (m) => drawer.log(`${m.type} ${JSON.stringify(m.payload).slice(0, 400)}`) } : null,
  });

  // ---- Run state (AC-5) ----
  let queue = saved?.queue ?? buildQueue(units, passes, rng);
  const plannedTotal = saved?.plannedTotal ?? queue.length;
  let planDone = saved?.planDone ?? 0;
  let consecutiveErrors = saved?.consecutiveErrors ?? 0;
  let pendingModelSupport = saved?.pendingModelSupport ?? false;
  let activeMs = saved?.activeMs ?? 0;
  const resumes = saved ? (saved.resumes + 1) : 0;
  let started = false;

  const persist = () => RunState.save(ACTIVITY_ID, runKey, { seed, queue, plannedTotal, planDone, consecutiveErrors, pendingModelSupport, activeMs, resumes });

  renderProgress(ui.progress, plannedTotal, planDone, planDone);

  // ---- Start surface: the one gesture that unlocks audio on tablets (then NA-11: speaking within 300 ms) ----
  const start = showStart(root, {
    label: saved ? "Tap to continue" : "Tap to start",
    hint: "",
    onStart: async () => {
      if (started) return;
      started = true;
      await narrator.unlock();
      start.hide();
      run();
    },
  });

  const preload = await narrator.preload(keysForRun, (d, t) => start.setHint(d < t ? `Getting ready… ${d}/${t}` : ""));
  start.setHint("");
  if (preload.missing.length) {
    console.warn(`[${ACTIVITY_ID}] ${preload.missing.length} narration assets missing; falling back (recorded as a deviation):`, preload.missing);
  }
  start.ready();

  // ---- The activity ----
  const gate = new TapGate();
  let cardsByLetter = new Map();

  function renderCards(options) {
    ui.cards.classList.toggle("is-single", options.length === 1);
    ui.cards.replaceChildren();
    setState("prompt");
    cardsByLetter = new Map();
    for (const letter of options) {
      const card = letterCard(letter, {
        onTap: (l) => {
          if (!gate.isOpen) return; // later taps earn nothing (E-SELECT); taps during feedback are ignored
          gate.tap(l);
          narrator.stop(); // barge-in (NA-5)
        },
      });
      ui.cards.append(card);
      cardsByLetter.set(letter, card);
    }
  }
  const card = (l) => cardsByLetter.get(l);
  const setState = (state) => { ui.cards.dataset.state = state; };
  const clearStates = ({ keepDim = false } = {}) => {
    for (const c of cardsByLetter.values()) {
      c.classList.remove("is-highlight", "is-confirm", "is-noted");
      if (!keepDim) c.classList.remove("is-dim");
    }
  };

  /** The tutor points at the right card and names it. */
  async function demonstrate(unit, { hold = timing.demoHoldMs, keepHighlight = false } = {}) {
    clearStates({ keepDim: true });
    card(unit)?.classList.add("is-highlight");
    await narrator.say(`this_is.${unit}`);
    await sleep(hold);
    if (!keepHighlight) card(unit)?.classList.remove("is-highlight");
  }

  async function confirm(unit) {
    card(unit)?.classList.add("is-confirm");
    if (sfxOn) playTone();
    await narrator.say(`name.${unit}`); // the name is repeated as the letter is tapped (ORT-15; MM-5 pairing in time)
    await sleep(timing.confirmHoldMs);
  }

  /**
   * Open the response window, speak the prompt, and wait for the FIRST tap with R-9 timeout handling.
   * Returns { tap, latencyMs, reprompts, noResponse }.
   */
  async function promptAndWait(unit, { allowTimeouts = true } = {}) {
    gate.open();
    const onset = performance.now();
    await narrator.say(`prompt.${unit}`, { prompt: true });
    if (!gate.value) await gate.waitFor(timing.postPromptPauseMs);
    let reprompts = 0, noResponse = false;
    while (true) {
      const tap = await gate.waitFor(allowTimeouts ? timing.timeoutMs : 1e9);
      if (tap) return { tap, latencyMs: Math.round(tap.at - onset), reprompts, noResponse };
      reprompts++;
      if (reprompts === 1) {
        await narrator.say(`prompt.${unit}`, { prompt: true }); // neutral re-prompt, identical wording (A-8)
      } else {
        // Second timeout: no-response (logged, never an error). Show the way forward and wait as long as it takes (FI-5).
        noResponse = true;
        gate.close();
        setState("after-no-response");
        await demonstrate(unit);
        gate.open();
        await narrator.say(`prompt.${unit}`, { prompt: true });
        allowTimeouts = false;
      }
    }
  }

  async function runItem(item, index) {
    const t0 = performance.now();
    const unit = item.unit;
    const options = buildOptions(unit, pool, optionCount, rng, letters);
    renderCards(options);
    renderProgress(ui.progress, plannedTotal, planDone, planDone);

    const rec = {
      unit, phase, attempt: item.attempt, pass: item.pass,
      response_mode: "tap", options, target_position: options.indexOf(unit),
      outcome: null, first_tap: null, latency_ms: null, error_class: null, fast_wrong: false,
      hint_count: 0, scaffold: "none", reprompts: 0, no_response_first: false,
      repair: null, requeued: false, capture_confidence: 1.0, eligible_for_mastery: false,
    };

    if (phase === "model") {
      // ORT-15: presentation with a response; the name is repeated as the letter is tapped. Not counted toward mastery.
      rec.scaffold = "model";
      await demonstrate(unit, { hold: 500 });
      const r = await promptAndWait(unit);
      rec.reprompts = r.reprompts; rec.no_response_first = r.noResponse;
      rec.latency_ms = r.latencyMs; rec.first_tap = r.tap.letter; rec.outcome = "presentation";
      gate.close();
      await confirm(unit);
      planDone++;
      return finishItem(rec, t0);
    }

    if (pendingModelSupport) {
      // PEDAGOGY M-13: after two consecutive errors, return to modeling for the next item. Recorded as a scaffold.
      pendingModelSupport = false;
      rec.scaffold = "model_support";
      await demonstrate(unit);
    }

    const r = await promptAndWait(unit);
    rec.reprompts = r.reprompts; rec.no_response_first = r.noResponse;
    rec.latency_ms = r.latencyMs; rec.first_tap = r.tap.letter;
    gate.close();

    if (r.noResponse) {
      // The scored window ended at the second timeout (R-9). The eventual tap was a guided reproduction, not a response.
      rec.outcome = "no_response";
      rec.repair = { reproduced: r.tap.letter === unit, taps: 1, together: false };
      await confirm(unit);
      if (r.tap.letter !== unit) await demonstrate(unit, { hold: 600 });
      requeue(item, rec);
      planDone++;
      return finishItem(rec, t0);
    }

    if (r.tap.letter === unit) {
      rec.outcome = "correct";
      rec.eligible_for_mastery = item.attempt === 1 && rec.scaffold === "none"; // M-3: unhinted, unscaffolded, not previously missed
      consecutiveErrors = 0;
      await confirm(unit);
      planDone++;
      return finishItem(rec, t0);
    }

    // ---- Incorrect first tap: verification + ONE elaboration, demonstration, reproduction, requeue (F-1, F-12, TN-4) ----
    rec.outcome = "incorrect";
    setState("feedback");
    Object.assign(rec, classifyError(r.tap.letter, unit, letters, r.latencyMs, timing.fastWrongMs));
    consecutiveErrors++;
    card(r.tap.letter)?.classList.add("is-noted"); // neutral mark: dashed outline, no red, no buzzer (TN-1)

    await narrator.say(`thats.${r.tap.letter}`); // "That's d."
    if (rec.fast_wrong) {
      // Guessing (F-11): slow the pace; do not lecture.
      await sleep(timing.slowPaceExtraMs);
    } else if (rec.error_class === "visual" || rec.error_class === "visual_name") {
      await narrator.say(`shape.${unit}`); // contrast on shape: "b has a tall line, then a ball."
    } else if (rec.error_class === "name") {
      await narrator.say(`listen.${unit}`); // contrast on name: "Listen: b."
    }
    await demonstrate(unit); // "This is b." with the pointer on the card

    // The child reproduces the correct response (F-12). Not credited (E-SELECT: first response only).
    clearStates();
    setState("repair");
    const r2 = await promptAndWait(unit);
    gate.close();
    rec.repair = { reproduced: r2.tap.letter === unit, taps: 1, together: false, latency_ms: r2.latencyMs, reprompts: r2.reprompts };
    if (r2.tap.letter === unit) {
      await confirm(unit);
    } else {
      // Second error on the same item: drop to model — "Let's do it together." (TN language bank, M-13)
      rec.repair.together = true;
      rec.repair.taps = 2;
      await narrator.say("together");
      clearStates();
      setState("together");
      for (const [l, c] of cardsByLetter) if (l !== unit) c.classList.add("is-dim");
      card(unit)?.classList.add("is-highlight");
      const r3 = await promptAndWait(unit, { allowTimeouts: true });
      gate.close();
      rec.repair.taps = 3;
      rec.repair.reproduced = r3.tap.letter === unit;
      await confirm(unit);
    }
    requeue(item, rec);
    planDone++;
    return finishItem(rec, t0);
  }

  /** A missed item returns later in the activity (F-12), once, with at least one other item in between. */
  function requeue(item, rec) {
    if (item.attempt !== 1) return;
    const again = { ...item, id: `${item.id}:again`, attempt: 2 };
    const at = Math.min(2, queue.length);
    queue.splice(at, 0, again);
    rec.requeued = true;
  }

  function finishItem(rec, t0) {
    const elapsed = performance.now() - t0;
    activeMs += Math.min(elapsed, timing.perItemActiveCapMs);
    rec.elapsed_ms = Math.round(elapsed);
    rec.scaffold_state = { model_support: rec.scaffold === "model_support", model: rec.scaffold === "model" };
    rec.narration_mode = narrator.mode;
    telemetry.item(rec);
    if (consecutiveErrors >= 2) { pendingModelSupport = true; consecutiveErrors = 0; }
    persist();
    renderProgress(ui.progress, plannedTotal, Math.min(planDone, plannedTotal), Math.min(planDone, plannedTotal));
  }

  async function run() {
    telemetry.start({
      corpus_entry: phase === "model" ? "ORT-15 Hear Letter Name, See Letter, Tap Letter (Single Target)" : "ORT-17 Hear Letter Name, See Letter, Tap Letter",
      phase, units, pool, passes, option_count: optionCount, seed, resumes,
      band: "K-1", latency_multiplier: 1.5, timeout_ms: timing.timeoutMs,
      narration_mode: narrator.mode, narration_missing: preload.missing,
      hand: params.hand === "left" ? "left" : "right",
    });

    if (!saved) {
      await narrator.say("intro"); // "Listen, then tap the letter."
      await sleep(300);
    }

    let index = planDone;
    while (queue.length) {
      if (activeMs >= timing.ceilingActiveMs) {
        // AC-3 hard ceiling: end gracefully, report partial results (R-11).
        telemetry.setNarrationMode(narrator.mode);
        return complete("ceiling");
      }
      const item = queue[0];
      await runItem(item, index++);
      queue.shift();
      persist();
      clearStates();
      await sleep(timing.betweenItemsMs);
    }
    complete("completed");
  }

  async function complete(status) {
    ui.cards.replaceChildren();
    ui.setCaption("");
    telemetry.setNarrationMode(narrator.mode);
    const record = telemetry.end(status, { active_ms: Math.round(activeMs), planned_items: plannedTotal, items_done: planDone });
    RunState.clear(ACTIVITY_ID, runKey);
    const doneKey = status === "completed" && narrator.has(`done.${units.length}`) ? `done.${units.length}` : "stop_now";
    const line = narrator.text(doneKey);
    showComplete(root, {
      letters: units,
      line,
      doneLabel: "Done",
      onDone: () => {
        telemetry.clear();
        exitToHost(params);
      },
    });
    await narrator.say(doneKey); // "That's all five." — factual, no praise (TN-2, TN-3)
    if (debug) drawer.log(record);
  }

  // Abandonment (R-11): if the page is left mid-run, state is already persisted per item; mark the record.
  window.addEventListener("pagehide", () => {
    if (started && queue.length && telemetry.record && telemetry.record.status === "in_progress") {
      telemetry.record.status = "interrupted";
      telemetry._save();
    }
  });
}

main().catch((err) => {
  console.error(err);
  const root = document.getElementById("activity");
  root.innerHTML = `<div style="padding:32px;max-width:640px;margin:0 auto;font-size:20px"><p>This activity could not start.</p><pre style="white-space:pre-wrap;font-size:14px;color:#6d665e">${String(err.message || err)}</pre></div>`;
});
