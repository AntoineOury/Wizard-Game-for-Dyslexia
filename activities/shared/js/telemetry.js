// Telemetry — uniform item-level results (Vision RP-3, PEDAGOGY R-12), telemetry_schema "v1".
//
// Per item: unit, response mode, outcome (correct | incorrect | uncertain | no_response | presentation),
// latency from prompt onset, hint count, scaffold state, capture confidence, plus the repair loop.
// The activity EMITS and EXITS; it never decides mastery or what comes next (AC-4, RP-5).
//
// Delivery: postMessage to the embedding host (parent window / opener) and a DOM CustomEvent on
// window, so a session layer can listen either way. Records also buffer in sessionStorage so an
// interrupted run loses nothing (AC-5, PL-8) and a resumed run continues the same record.

export const TELEMETRY_SCHEMA = "v1";

export class Telemetry {
  constructor({ activityId, version, runKey, host }) {
    this.activityId = activityId;
    this.version = version;
    this.runKey = runKey;
    this.host = host || null;
    this.storageKey = `ol:telemetry:${activityId}:${runKey}`;
    this.record = null;
  }

  start(meta) {
    const existing = this._load();
    if (existing && (existing.status === "in_progress" || existing.status === "interrupted")) {
      this.record = existing;
      this.record.status = "in_progress";
      this.record.resumed_count = (this.record.resumed_count || 0) + 1;
      this.record.resumed_at = new Date().toISOString();
    } else {
      this.record = {
        telemetry_schema: TELEMETRY_SCHEMA,
        activity_id: this.activityId,
        activity_version: this.version,
        run_key: this.runKey,
        status: "in_progress", // in_progress | completed | abandoned | ceiling
        started_at: new Date().toISOString(),
        ended_at: null,
        ...meta,
        items: [],
      };
    }
    this._save();
    this._emit("activity:start", this.summary());
    return this.record;
  }

  item(rec) {
    rec.item_index = this.record.items.length + 1;
    rec.recorded_at = new Date().toISOString();
    this.record.items.push(rec);
    this._save();
    this._emit("activity:item", rec);
    return rec;
  }

  setNarrationMode(mode) {
    if (this.record) { this.record.narration_mode = mode; this._save(); }
  }

  end(status, extra = {}) {
    this.record.status = status;
    this.record.ended_at = new Date().toISOString();
    Object.assign(this.record, extra);
    this._save();
    this._emit("activity:complete", this.record);
    return this.record;
  }

  summary() {
    const { items, ...rest } = this.record;
    return { ...rest, item_count: items.length };
  }

  clear() {
    try { sessionStorage.removeItem(this.storageKey); } catch { /* ignore */ }
  }

  _load() {
    try {
      const raw = sessionStorage.getItem(this.storageKey);
      return raw ? JSON.parse(raw) : null;
    } catch { return null; }
  }

  _save() {
    try { sessionStorage.setItem(this.storageKey, JSON.stringify(this.record)); } catch { /* storage may be unavailable */ }
  }

  _emit(type, payload) {
    const msg = { source: "otherwise-labs-activity", type, activity_id: this.activityId, payload };
    try { window.dispatchEvent(new CustomEvent(type, { detail: msg })); } catch { /* ignore */ }
    try {
      if (window.parent && window.parent !== window) window.parent.postMessage(msg, "*");
      if (window.opener) window.opener.postMessage(msg, "*");
    } catch { /* cross-origin host without a listener */ }
    if (this.host?.onMessage) { try { this.host.onMessage(msg); } catch { /* ignore */ } }
  }
}
