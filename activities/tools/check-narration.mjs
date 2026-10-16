#!/usr/bin/env node
// Build-time narration check (Vision NA-10: "No orphaned or missing assets at build time — this is a
// build-breaking check"), plus the PEDAGOGY A-8 authoring standard on instruction and feedback lines.
//
//   node activities/tools/check-narration.mjs [--activity ort-17-hear-see-tap-letter-g1]
// Exit code 1 on any failure.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const activitiesRoot = path.resolve(here, "..");
const args = process.argv.slice(2);
const i = args.indexOf("--activity");
const activity = i >= 0 ? args[i + 1] : "ort-17-hear-see-tap-letter-g1";
const dir = path.join(activitiesRoot, activity);

const manifest = JSON.parse(fs.readFileSync(path.join(dir, "narration/manifest.json"), "utf8"));
const letters = JSON.parse(fs.readFileSync(path.join(activitiesRoot, "shared/data/letters-group1.json"), "utf8")).letters.map((l) => l.symbol);
const code = fs.readFileSync(path.join(dir, "activity.js"), "utf8");

const failures = [], warnings = [];
const keys = new Set();

// 1. Manifest integrity.
for (const u of manifest.utterances) {
  if (!u.key || !u.text || !u.audio) failures.push(`utterance missing key/text/audio: ${JSON.stringify(u)}`);
  if (keys.has(u.key)) failures.push(`duplicate key ${u.key}`);
  keys.add(u.key);
  if (!/[.!?]$/.test(u.text.trim())) warnings.push(`${u.key}: text is not a complete sentence: "${u.text}"`);
  const words = u.text.replace(/[^\w' ]/g, "").trim().split(/\s+/).filter(Boolean).length;
  const isInstruction = /^(prompt|intro|shape|listen|thats|this_is|together|done|stop_now)/.test(u.key);
  if (isInstruction && words > 8) failures.push(`${u.key}: ${words} words exceeds the K–1 limit of 8 (PEDAGOGY A-8): "${u.text}"`);
}

// 2. Every audio asset exists; no orphan audio files.
const audioDir = path.join(dir, "narration");
const referenced = new Set();
for (const u of manifest.utterances) {
  const file = path.join(audioDir, u.audio);
  referenced.add(path.resolve(file));
  if (!fs.existsSync(file)) failures.push(`missing audio asset ${u.audio} (key ${u.key})`);
  else if (fs.statSync(file).size < 1000) failures.push(`suspiciously small audio asset ${u.audio}`);
}
const audioFiles = fs.existsSync(path.join(audioDir, "audio")) ? fs.readdirSync(path.join(audioDir, "audio")).map((f) => path.resolve(audioDir, "audio", f)) : [];
for (const f of audioFiles) if (!referenced.has(f)) failures.push(`orphan audio file ${path.relative(dir, f)}`);

// 3. Every key the code can request exists. Static keys: say("intro"). Per-letter families: `prompt.${u}` etc.
for (const m of code.matchAll(/say(?:All)?\(\s*["']([a-z_.0-9]+)["']/g)) if (!keys.has(m[1])) failures.push(`code uses unknown key "${m[1]}"`);
for (const m of code.matchAll(/`([a-z_]+)\.\$\{/g)) {
  const family = m[1];
  if (family === "done") continue;
  for (const l of letters) if (!keys.has(`${family}.${l}`)) failures.push(`family "${family}" lacks key for letter ${l}`);
}
for (const n of [3, 4, 5, 6, 7]) if (!keys.has(`done.${n}`)) failures.push(`missing done.${n}`);

// 4. Identical wording for the same interaction across letters (A-8: "the same interaction always uses the identical wording").
const promptTemplates = new Set(manifest.utterances.filter((u) => u.key.startsWith("prompt.")).map((u) => u.text.replace(/ [a-z]\.$/, " X.")));
if (promptTemplates.size !== 1) failures.push(`prompt wording varies: ${[...promptTemplates].join(" | ")}`);

for (const w of warnings) console.log(`warn  ${w}`);
for (const f of failures) console.log(`FAIL  ${f}`);
console.log(`${manifest.utterances.length} utterances, ${audioFiles.length} audio files, ${failures.length} failures, ${warnings.length} warnings`);
process.exit(failures.length ? 1 : 0);
