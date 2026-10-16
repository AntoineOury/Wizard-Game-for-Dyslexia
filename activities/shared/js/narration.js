// Narrator — manifest-driven narration player shared by every activity (Vision §5, RP-10).
//
//  * Every utterance is a pre-generated audio asset named in the narration manifest (NA-6, NA-10).
//  * Assets for a run are fetched and decoded BEFORE the activity starts (NA-13, PL-7).
//  * One tap on the replay control repeats the current prompt, unlimited, never counted (NA-2).
//  * Barge-in: the activity calls stop() the instant the learner responds (NA-5).
//  * If an asset is missing or undecodable, the player degrades in a RECORDED way: browser
//    speechSynthesis if present (mode "tts"), else caption text only (mode "text"). Either is a
//    deviation the activity reports in telemetry (NA-4, FI-3); neither is the intended path.

export class Narrator {
  /**
   * @param {object} opts
   * @param {object} opts.manifest   parsed narration manifest ({ utterances: [{ key, text, audio }] })
   * @param {string} opts.baseUrl    URL the manifest's audio paths are relative to
   * @param {(text: string) => void} [opts.onCaption]
   * @param {(playing: boolean) => void} [opts.onPlayingChange]
   * @param {"audio"|"tts"|"text"} [opts.forceMode]  testing override
   * @param {number} [opts.textHoldScale]  testing: scales the caption-only hold time
   */
  constructor({ manifest, baseUrl, onCaption, onPlayingChange, forceMode, textHoldScale } = {}) {
    this.manifest = manifest;
    this.baseUrl = baseUrl;
    this.onCaption = onCaption || (() => {});
    this.onPlayingChange = onPlayingChange || (() => {});
    this.forceMode = forceMode || null;
    this.textHoldScale = textHoldScale || 1;
    this.byKey = new Map(manifest.utterances.map((u) => [u.key, u]));
    this.buffers = new Map(); // key -> AudioBuffer
    this.missing = new Set();
    this.ctx = null;
    this.current = null; // { cancel(): void, settle(mode, interrupted): void }
    this.lastPromptKeys = null;
    this.playing = false;
    this.modeUsed = new Set(); // which delivery modes were actually used during the run
    this.rate = 1.0;
  }

  has(key) { return this.byKey.has(key); }
  text(key) { return this.byKey.get(key)?.text ?? ""; }

  /** The delivery mode this run has actually used: "audio", or a deviation ("tts" / "text" / "mixed"). */
  get mode() {
    if (this.modeUsed.size === 0) return "audio";
    if (this.modeUsed.size === 1) return [...this.modeUsed][0];
    return "mixed";
  }

  _ensureContext() {
    if (!this.ctx) {
      const AC = globalThis.AudioContext || globalThis.webkitAudioContext;
      this.ctx = AC ? new AC() : null;
    }
    return this.ctx;
  }

  /** Call from the first user gesture: resumes the AudioContext so playback is allowed. */
  async unlock() {
    const ctx = this._ensureContext();
    if (ctx && ctx.state !== "running") {
      // resume() settles only once the browser accepts a user gesture; never let that strand the activity.
      await Promise.race([ctx.resume().catch(() => {}), new Promise((r) => setTimeout(r, 600))]);
    }
    if (ctx && ctx.state === "running") {
      // A silent tick guarantees the context is "started" on iOS.
      const buf = ctx.createBuffer(1, 1, ctx.sampleRate);
      const src = ctx.createBufferSource();
      src.buffer = buf; src.connect(ctx.destination); src.start();
    }
  }

  /** Fetch + decode every listed key. Resolves with { loaded, missing }. Never throws. */
  async preload(keys, onProgress) {
    const ctx = this._ensureContext();
    const unique = [...new Set(keys)].filter((k) => this.byKey.has(k) && !this.buffers.has(k));
    let done = 0;
    const limit = 6;
    const queue = unique.slice();
    const worker = async () => {
      while (queue.length) {
        const key = queue.shift();
        const u = this.byKey.get(key);
        try {
          if (this.forceMode && this.forceMode !== "audio") throw new Error("forced non-audio mode");
          const res = await fetch(new URL(u.audio, this.baseUrl).href, { cache: "force-cache" });
          if (!res.ok) throw new Error(`HTTP ${res.status}`);
          const bytes = await res.arrayBuffer();
          if (!ctx) throw new Error("no AudioContext");
          const buffer = await ctx.decodeAudioData(bytes.slice(0));
          this.buffers.set(key, buffer);
        } catch {
          this.missing.add(key);
        }
        done++;
        onProgress?.(done, unique.length);
      }
    };
    await Promise.all(Array.from({ length: Math.min(limit, unique.length) }, worker));
    return { loaded: unique.length - this.missing.size, missing: [...this.missing] };
  }

  /** Stop whatever is playing (barge-in). The pending say() resolves with { interrupted: true }. */
  stop() {
    const cur = this.current;
    if (cur) {
      this.current = null;
      try { cur.cancel(); } catch { /* ignore */ }
      cur.settle(true);
    }
    this._setPlaying(false);
  }

  _setPlaying(v) {
    if (this.playing !== v) {
      this.playing = v;
      this.onPlayingChange(v);
    }
  }

  /**
   * Speak one utterance by key. Resolves when it finishes or is stopped.
   * @returns {Promise<{interrupted: boolean, mode: string}>}
   */
  say(key, { prompt = false } = {}) {
    const u = this.byKey.get(key);
    if (!u) {
      console.warn(`[Narrator] unknown key "${key}"`);
      return Promise.resolve({ interrupted: false, mode: "none" });
    }
    if (prompt) this.lastPromptKeys = [key];
    this.stop();
    this.onCaption(u.text);
    this._setPlaying(true);

    return new Promise((resolve) => {
      let mode = "audio";
      const entry = {
        cancel: () => {},
        settle: (interrupted) => {
          if (this.current === entry) this.current = null;
          if (!interrupted) this._setPlaying(false);
          resolve({ interrupted, mode });
        },
      };
      const finish = () => { if (this.current === entry) entry.settle(false); };
      this.current = entry;

      const buffer = this.buffers.get(key);
      const ctx = this.ctx;
      if (ctx && ctx.state !== "running") ctx.resume().catch(() => {}); // a later utterance will get audio once it runs
      if (buffer && ctx && ctx.state === "running" && (!this.forceMode || this.forceMode === "audio")) {
        mode = "audio";
        this.modeUsed.add("audio");
        const source = ctx.createBufferSource();
        source.buffer = buffer;
        source.playbackRate.value = this.rate;
        source.connect(ctx.destination);
        source.onended = finish;
        entry.cancel = () => { source.onended = null; source.stop(); };
        try { source.start(); } catch { finish(); }
        return;
      }

      // Deviation path 1: browser speech synthesis (recorded as "tts").
      const synth = globalThis.speechSynthesis;
      if (synth && typeof SpeechSynthesisUtterance !== "undefined" && this.forceMode !== "text") {
        mode = "tts";
        this.modeUsed.add("tts");
        const utt = new SpeechSynthesisUtterance(u.tts_text || u.text);
        utt.lang = this.manifest.language || "en-US";
        utt.rate = 0.9 * this.rate;
        utt.onend = finish;
        utt.onerror = finish;
        // Some engines never fire onend; guard with a generous timer based on text length.
        const guard = setTimeout(finish, 1500 + 90 * u.text.length);
        entry.cancel = () => { clearTimeout(guard); utt.onend = null; utt.onerror = null; try { synth.cancel(); } catch { /* ignore */ } };
        try { synth.cancel(); synth.speak(utt); } catch { finish(); }
        return;
      }

      // Deviation path 2: caption only (recorded as "text"). Hold roughly reading time.
      mode = "text";
      this.modeUsed.add("text");
      const timer = setTimeout(finish, Math.round((900 + 70 * u.text.length) * this.textHoldScale));
      entry.cancel = () => clearTimeout(timer);
    });
  }

  /** Speak several utterances in order with a short natural gap. Stops at the first interruption. */
  async sayAll(keys, { gapMs = 260, prompt = false } = {}) {
    if (prompt) this.lastPromptKeys = keys.slice();
    for (let i = 0; i < keys.length; i++) {
      const r = await this.say(keys[i]);
      if (r.interrupted) return { interrupted: true };
      if (i < keys.length - 1) {
        const waited = await this._gap(gapMs);
        if (waited.interrupted) return { interrupted: true };
      }
    }
    return { interrupted: false };
  }

  _gap(ms) {
    return new Promise((resolve) => {
      const entry = {
        cancel: () => clearTimeout(timer),
        settle: (interrupted) => { if (this.current === entry) this.current = null; resolve({ interrupted }); },
      };
      const timer = setTimeout(() => entry.settle(false), ms);
      this.current = entry;
    });
  }

  /** Replay the most recent prompt (NA-2). Never counted as a hint. */
  replay() {
    if (!this.lastPromptKeys) return Promise.resolve({ interrupted: false });
    return this.sayAll(this.lastPromptKeys);
  }

  /** Duration in seconds of a loaded asset, or an estimate. */
  duration(key) {
    const b = this.buffers.get(key);
    if (b) return b.duration;
    return 0.9 + 0.07 * (this.text(key).length);
  }
}

export async function loadManifest(url) {
  const res = await fetch(url, { cache: "no-cache" });
  if (!res.ok) throw new Error(`narration manifest: HTTP ${res.status}`);
  return res.json();
}
