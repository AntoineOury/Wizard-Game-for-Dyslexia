// Host contract — how an activity receives its inputs and persists an interrupted run.
//
// Vision AC-4: an activity is self-contained and stateless across sessions. It receives its unit
// set, phase and learner parameters as INPUT (URL query parameters here, so a session layer, a
// tutor's assignment link or the catalog page can all launch it) and emits results as OUTPUT
// (see telemetry.js). It never reads or writes mastery state.
//
// Vision AC-5: interruptible and resumable — in-progress state lives in sessionStorage under a
// key derived from the launch URL, so reloading the same link continues the same run.

export function readParams(defaults) {
  const q = new URLSearchParams(location.search);
  const out = { ...defaults };
  for (const [k, v] of q.entries()) out[k] = v;
  return out;
}

export function parseUnits(raw, allowed, { min, max }) {
  const units = String(raw || "")
    .toLowerCase()
    .split(/[\s,]+/)
    .map((s) => s.trim())
    .filter(Boolean);
  const seen = new Set();
  const valid = units.filter((u) => allowed.has(u) && !seen.has(u) && seen.add(u));
  if (valid.length < min) throw new Error(`units: need ${min}–${max} letters from the unit group, got "${raw}"`);
  if (valid.length > max) valid.length = max; // AC-1: 3–7 units; silently trimming would hide a host bug, so report it too
  return valid;
}

export function runKeyFor(params) {
  // Same launch parameters => same run (so a reload resumes). A host may pass an explicit `run` id.
  if (params.run) return String(params.run);
  const keys = Object.keys(params).filter((k) => !["debug", "narration", "sfx"].includes(k)).sort();
  return keys.map((k) => `${k}=${params[k]}`).join("&") || "default";
}

export const RunState = {
  key: (activityId, runKey) => `ol:state:${activityId}:${runKey}`,
  load(activityId, runKey) {
    try {
      const raw = sessionStorage.getItem(this.key(activityId, runKey));
      return raw ? JSON.parse(raw) : null;
    } catch { return null; }
  },
  save(activityId, runKey, state) {
    try { sessionStorage.setItem(this.key(activityId, runKey), JSON.stringify(state)); } catch { /* ignore */ }
  },
  clear(activityId, runKey) {
    try { sessionStorage.removeItem(this.key(activityId, runKey)); } catch { /* ignore */ }
  },
};

/** Tell the embedding host we are done. Standalone, fall back to a return link if one was given. */
export function exitToHost(params) {
  const inFrame = window.parent && window.parent !== window;
  if (!inFrame && params.return) {
    location.href = params.return;
  }
  // When embedded, the host already received activity:complete and decides what comes next (RP-5).
}
