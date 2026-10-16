#!/usr/bin/env node
// Builds the narration manifest for ORT-17 / ORT-15 (Hear Letter Name, See Letter, Tap Letter).
//
// Vision NA-10: every narration string lives in a versioned manifest with a stable key, the text,
// the audio asset path, and the contexts that use it. No narration string exists only in code.
// Vision NA-7: every utterance is a COMPLETE sentence recorded on its own — per-letter variants are
// separate utterances, never "Tap the letter" + "b" stitched together.
//
// Usage: node activities/tools/build-narration-manifest.mjs
// Reads:  activities/shared/data/letters-group1.json
// Writes: activities/ort-17-hear-see-tap-letter-g1/narration/manifest.json
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..");
const lettersPath = path.join(root, "shared/data/letters-group1.json");
const outPath = path.join(root, "ort-17-hear-see-tap-letter-g1/narration/manifest.json");

const letters = JSON.parse(fs.readFileSync(lettersPath, "utf8")).letters;

// How the letter NAME is written for the speech engine. On-screen text keeps the lower-case
// symbol (that is the unit); the engine is given the upper-case letter so it reads a letter name
// ("ay", "bee") and never an English word ("a" as an article, "I" as a pronoun is fine).
// Overrides fix engines that mis-read a bare capital.
const ttsName = {
  a: "A", b: "B", c: "C", d: "D", e: "E", f: "F", g: "G", h: "H", i: "I", j: "J", k: "K",
  l: "L", m: "M", n: "N", o: "O", p: "P", q: "Q", r: "R", s: "S", t: "T", u: "U", v: "V",
  w: "W", x: "X", y: "Y", z: "Z",
};

// How each letter NAME is written when it opens a sentence (the shape lines). A capital letter reads as the
// letter name for every letter except "A" (read as the article) and "I" (read unstressed), which get a
// spelling espeak-ng reads as /eɪ/ and /aɪ/. Verified against the phonemizer for all 26 letters.
const ttsShapeStart = (s) => ({ a: "Eigh", i: "Eye" })[s] ?? s.toUpperCase();

const utterances = [];
function add(key, text, { tts = text, contexts, group }) {
  utterances.push({ key, text, tts_text: tts, audio: `audio/${key.replace(/\./g, "_")}.mp3`, contexts, group });
}

// Generic lines (shared language bank, Vision TN-9; PEDAGOGY A-8 authoring standard).
add("intro", "Listen, then tap the letter.", { contexts: ["activity start"], group: "generic" });
add("together", "Let's do it together.", { contexts: ["second error on the same item (PEDAGOGY M-13, Vision TN language bank)"], group: "generic" });
add("stop_now", "That's all for now.", { contexts: ["3-minute ceiling reached (Vision AC-3) or host asked to stop"], group: "generic" });
const numberWords = { 3: "three", 4: "four", 5: "five", 6: "six", 7: "seven" };
for (const n of [3, 4, 5, 6, 7]) {
  add(`done.${n}`, `That's all ${numberWords[n]}.`, { contexts: [`completion with ${n} units (Vision TN-3)`], group: "generic" });
}

// Per-letter lines.
for (const L of letters) {
  const s = L.symbol;
  const N = ttsName[s];
  add(`prompt.${s}`, `Tap the letter ${s}.`, { tts: `Tap the letter ${N}.`, contexts: ["item prompt (guided, independent, model)", "neutral re-prompt after a timeout", "re-prompt after demonstration"], group: "prompt" });
  add(`name.${s}`, `${s}.`, { tts: `${N}.`, contexts: ["letter name repeated as the letter is tapped (ORT-15, MM-5)"], group: "name" });
  add(`this_is.${s}`, `This is ${s}.`, { tts: `This is ${N}.`, contexts: ["demonstration after an error", "model phase", "model support after two consecutive errors"], group: "this_is" });
  add(`thats.${s}`, `That's ${s}.`, { tts: `That's ${N}.`, contexts: ["verification after tapping this letter when it was not the target (PEDAGOGY F-1)"], group: "thats" });
  add(`listen.${s}`, `Listen: ${s}.`, { tts: `Listen. ${N}.`, contexts: ["name-contrast elaboration when the tapped letter's name sounds like the target's (Activity Corpus, Hear See Tap family)"], group: "listen" });
  add(`shape.${s}`, L.shape_line, { tts: L.shape_line.replace(new RegExp(`^${s} `), `${ttsShapeStart(s)} `), contexts: ["shape-contrast elaboration when the tapped letter looks like the target (Activity Corpus, Hear See Tap family)"], group: "shape" });
}

const manifest = {
  schema: "narration/v1",
  activity: "ort-17-hear-see-tap-letter-g1",
  version: "1.0.0",
  language: "en-US",
  voice: {
    engine: "Kokoro-82M v1.0 (ONNX, q8), generated offline by activities/tools/generate-narration.mjs",
    voice: "af_heart",
    speed: 0.9,
    note: "One consistent adult voice, warm and unhurried, about 10% slower than conversational (Vision NA-8). Pre-generated and cached as assets (NA-6); never browser speechSynthesis.",
  },
  rules: ["NA-1", "NA-6", "NA-7", "NA-8", "NA-10", "TN-9", "A-8"],
  utterances,
};

fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, JSON.stringify(manifest, null, 2) + "\n");
console.log(`wrote ${path.relative(root, outPath)} with ${utterances.length} utterances`);
