#!/usr/bin/env node
// Generate the narration audio assets for an activity from its narration manifest (Vision NA-6, NA-7, NA-10).
//
// Engine: Kokoro-82M v1.0 (Apache-2.0), run OFFLINE through kokoro-js + onnxruntime. The quantized weights come
// from the `kokoro-q8-shards` npm package and are reassembled here; the tokenizer/config files live in
// activities/tools/kokoro/. Nothing is fetched from Hugging Face, so the build works behind a strict proxy.
//
// Each utterance is generated as ONE complete sentence (NA-7), lightly trimmed, padded, and encoded to MP3 with
// ffmpeg. A review table (durations + the phoneme string the engine actually spoke) is written next to the audio
// so letter-name pronunciation can be checked (NA-9 analogue for letter names).
//
// Usage:
//   node activities/tools/generate-narration.mjs [--activity ort-17-hear-see-tap-letter-g1] [--force] [--only key,key]
//   npm --prefix activities/tools run narration:generate
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const activitiesRoot = path.resolve(here, "..");

const args = process.argv.slice(2);
const flag = (name, def) => { const i = args.indexOf(`--${name}`); return i >= 0 ? (args[i + 1] ?? true) : def; };
const activity = flag("activity", "ort-17-hear-see-tap-letter-g1");
const force = args.includes("--force");
const only = flag("only", "") ? String(flag("only", "")).split(",") : null;

const activityDir = path.join(activitiesRoot, activity);
const manifestPath = path.join(activityDir, "narration/manifest.json");
const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
const audioDir = path.join(activityDir, "narration");
const voice = manifest.voice?.voice || "af_heart";
const speed = manifest.voice?.speed || 0.9;

// ---- 1. Assemble the local model directory (idempotent) ----
const modelId = "onnx-community/Kokoro-82M-v1.0-ONNX";
const cacheRoot = path.join(here, ".cache", "models");
const modelDir = path.join(cacheRoot, modelId);
const onnxPath = path.join(modelDir, "onnx", "model_quantized.onnx");
if (!fs.existsSync(onnxPath)) {
  const shardsDir = path.dirname(require.resolve("kokoro-q8-shards/package.json"));
  const parts = fs.readdirSync(shardsDir).filter((f) => /^kokoro-q8\.part\d+\.bin$/.test(f)).sort((a, b) => Number(a.match(/\d+/)[0]) - Number(b.match(/\d+/)[0]));
  if (parts.length === 0) throw new Error("kokoro-q8-shards not installed: run `npm install` in activities/tools");
  fs.mkdirSync(path.dirname(onnxPath), { recursive: true });
  const out = fs.openSync(onnxPath, "w");
  for (const p of parts) fs.writeSync(out, fs.readFileSync(path.join(shardsDir, p)));
  fs.closeSync(out);
  console.log(`assembled ${path.relative(activitiesRoot, onnxPath)} from ${parts.length} shards`);
}
for (const f of ["config.json", "tokenizer.json", "tokenizer_config.json"]) {
  const dst = path.join(modelDir, f);
  if (!fs.existsSync(dst)) fs.copyFileSync(path.join(here, "kokoro", f), dst);
}

// ---- 2. Load the engine ----
const K = await import("kokoro-js");
const { env } = await import("@huggingface/transformers");
const { phonemize } = await import("phonemizer");
env.allowRemoteModels = false;
env.allowLocalModels = true;
env.localModelPath = cacheRoot + path.sep;
const t0 = Date.now();
const tts = await K.KokoroTTS.from_pretrained(modelId, { dtype: "q8", device: "cpu" });
console.log(`Kokoro ready in ${Date.now() - t0} ms · voice ${voice} · speed ${speed}`);

// ---- 3. Generate ----
function trimAndPad(samples, sr, { threshold = 0.004, padMs = 120 } = {}) {
  let start = 0, end = samples.length;
  while (start < end && Math.abs(samples[start]) < threshold) start++;
  while (end > start && Math.abs(samples[end - 1]) < threshold) end--;
  const pad = Math.round((sr * padMs) / 1000);
  const out = new Float32Array(end - start + 2 * pad);
  out.set(samples.subarray(start, end), pad);
  // Short fades so the clip never clicks.
  const fade = Math.min(Math.round(sr * 0.008), out.length / 2);
  for (let i = 0; i < fade; i++) { const g = i / fade; out[pad + i] *= g; out[out.length - pad - 1 - i] *= g; }
  return out;
}

function writeWav(file, samples, sr) {
  const buf = Buffer.alloc(44 + samples.length * 2);
  buf.write("RIFF", 0); buf.writeUInt32LE(36 + samples.length * 2, 4); buf.write("WAVE", 8);
  buf.write("fmt ", 12); buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(1, 22);
  buf.writeUInt32LE(sr, 24); buf.writeUInt32LE(sr * 2, 28); buf.writeUInt16LE(2, 32); buf.writeUInt16LE(16, 34);
  buf.write("data", 36); buf.writeUInt32LE(samples.length * 2, 40);
  for (let i = 0; i < samples.length; i++) buf.writeInt16LE(Math.max(-32768, Math.min(32767, Math.round(samples[i] * 32767))), 44 + i * 2);
  fs.writeFileSync(file, buf);
}

const review = [];
const tmpDir = fs.mkdtempSync(path.join(here, ".cache", "wav-"));
let generated = 0, skipped = 0;
for (const u of manifest.utterances) {
  if (only && !only.includes(u.key)) continue;
  const mp3 = path.join(audioDir, u.audio);
  if (!force && fs.existsSync(mp3)) { skipped++; continue; }
  const text = u.tts_text || u.text;
  // kokoro-js generate() for one complete sentence (its stream() API does not settle for plain strings in Node 22);
  // the phoneme string comes from the same espeak-ng phonemizer kokoro-js uses, for the review table.
  const audio = await tts.generate(text, { voice, speed });
  const phonemes = (await phonemize(text, "en-us")).join(" ");
  const sr = audio.sampling_rate;
  const samples = trimAndPad(audio.audio, sr);
  const wav = path.join(tmpDir, `${u.key.replace(/\./g, "_")}.wav`);
  writeWav(wav, samples, sr);
  fs.mkdirSync(path.dirname(mp3), { recursive: true });
  execFileSync("ffmpeg", ["-y", "-loglevel", "error", "-i", wav, "-ac", "1", "-ar", "24000", "-codec:a", "libmp3lame", "-b:a", "64k", mp3]);
  fs.unlinkSync(wav);
  const dur = samples.length / sr;
  review.push({ key: u.key, text: u.text, tts_text: text, phonemes, duration_s: Number(dur.toFixed(2)) });
  generated++;
  process.stdout.write(`${u.key.padEnd(14)} ${dur.toFixed(2).padStart(5)} s  ${phonemes}\n`);
}
fs.rmSync(tmpDir, { recursive: true, force: true });

// ---- 4. Review table (merge with any previous run so skipped keys keep their entry) ----
const reviewPath = path.join(audioDir, "generated.json");
let previous = [];
try { previous = JSON.parse(fs.readFileSync(reviewPath, "utf8")).utterances || []; } catch { /* first run */ }
const merged = new Map(previous.map((r) => [r.key, r]));
for (const r of review) merged.set(r.key, r);
fs.writeFileSync(reviewPath, JSON.stringify({
  engine: "Kokoro-82M v1.0 ONNX q8 via kokoro-js (offline)", voice, speed, sample_rate: 24000, format: "mp3 64 kbps mono",
  generated_at: new Date().toISOString(),
  utterances: [...merged.values()].sort((a, b) => a.key.localeCompare(b.key)),
}, null, 2) + "\n");
console.log(`\n${generated} generated, ${skipped} already present → ${path.relative(activitiesRoot, audioDir)}/audio`);
