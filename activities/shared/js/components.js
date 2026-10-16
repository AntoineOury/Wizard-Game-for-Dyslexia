// Shared component library (Vision VZ-8, RP-10): the shell every activity renders into, the
// replay control, progress markers, caption, letter cards, start and completion surfaces, and
// the confirmation tone. Activities compose these; they do not re-implement them.

const SVG_NS = "http://www.w3.org/2000/svg";

function el(tag, attrs = {}, children = []) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k === "class") node.className = v;
    else if (k === "text") node.textContent = v;
    else if (k.startsWith("on") && typeof v === "function") node.addEventListener(k.slice(2), v);
    else if (v !== null && v !== undefined) node.setAttribute(k, v);
  }
  for (const c of children) if (c) node.append(c);
  return node;
}

function svgIcon(paths, { viewBox = "0 0 24 24" } = {}) {
  const svg = document.createElementNS(SVG_NS, "svg");
  svg.setAttribute("viewBox", viewBox);
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("focusable", "false");
  for (const p of paths) {
    const path = document.createElementNS(SVG_NS, "path");
    for (const [k, v] of Object.entries(p)) path.setAttribute(k, v);
    svg.append(path);
  }
  return svg;
}

export const Icons = {
  speaker: () => svgIcon([
    { d: "M4 9.5v5a1 1 0 0 0 1 1h3l4.2 3.4a.8.8 0 0 0 1.3-.6V5.7a.8.8 0 0 0-1.3-.6L8 8.5H5a1 1 0 0 0-1 1z", fill: "currentColor" },
    { d: "M16 9.2a4 4 0 0 1 0 5.6", fill: "none", stroke: "currentColor", "stroke-width": "2", "stroke-linecap": "round", class: "ol-wave" },
    { d: "M18.6 6.6a7.6 7.6 0 0 1 0 10.8", fill: "none", stroke: "currentColor", "stroke-width": "2", "stroke-linecap": "round", class: "ol-wave" },
  ]),
  sfxOn: () => svgIcon([
    { d: "M9 7h3l4 4-4 4H9a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2z", fill: "none", stroke: "currentColor", "stroke-width": "2", "stroke-linejoin": "round" },
    { d: "M15.5 12h4", fill: "none", stroke: "currentColor", "stroke-width": "2", "stroke-linecap": "round" },
  ]),
  sfxOff: () => svgIcon([
    { d: "M9 7h3l4 4-4 4H9a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2z", fill: "none", stroke: "currentColor", "stroke-width": "2", "stroke-linejoin": "round" },
    { d: "M5 5l14 14", fill: "none", stroke: "currentColor", "stroke-width": "2", "stroke-linecap": "round" },
  ]),
  play: () => svgIcon([
    { d: "M8 5.5v13a1 1 0 0 0 1.5.9l10-6.5a1 1 0 0 0 0-1.7l-10-6.5A1 1 0 0 0 8 5.5z", fill: "currentColor" },
  ]),
  check: () => svgIcon([
    { d: "M5 12.5l4.2 4.2L19 7.5", fill: "none", stroke: "currentColor", "stroke-width": "2.6", "stroke-linecap": "round", "stroke-linejoin": "round" },
  ]),
  pointer: () => svgIcon([
    { d: "M10 2.5a1.5 1.5 0 0 1 3 0V11h1V8.5a1.5 1.5 0 0 1 3 0V12h1v-1.5a1.5 1.5 0 0 1 3 0V15c0 3.9-2.6 6.5-6.5 6.5h-1c-2.4 0-4.2-1-5.6-3L4.3 13a1.6 1.6 0 0 1 2.6-1.8L10 14.5V2.5z", fill: "currentColor" },
  ]),
};

/**
 * Build the standard activity shell inside `root`.
 * Returns references the activity uses. Nothing here is activity-specific.
 */
export function buildShell(root, { hand = "right", lines = "1", onReplay, onToggleSfx, sfxOn = true } = {}) {
  root.innerHTML = "";
  const shell = el("div", { class: "ol-shell", "data-hand": hand, "data-lines": lines });

  const progress = el("div", { class: "ol-progress", role: "progressbar", "aria-label": "Progress" });
  const sfxBtn = el("button", { class: "ol-iconbtn ol-iconbtn--small ol-sfx", type: "button", "aria-label": "Sound effects", "aria-pressed": String(sfxOn), onclick: () => onToggleSfx?.() }, [sfxOn ? Icons.sfxOn() : Icons.sfxOff()]);
  const top = el("div", { class: "ol-top" }, [progress, sfxBtn]);

  const caption = el("div", { class: "ol-caption", "aria-live": "polite" });
  const cards = el("div", { class: "ol-cards" });
  const work = el("div", { class: "ol-work" }, [caption, cards]);

  const replayBtn = el("button", { class: "ol-iconbtn ol-replay", type: "button", "aria-label": "Hear it again", onclick: () => onReplay?.() }, [Icons.speaker()]);
  const bottomSpacer = el("div");
  const bottom = el("div", { class: "ol-bottom" }, [replayBtn, bottomSpacer]);

  shell.append(top, work, bottom);
  root.append(shell);

  return {
    shell, progress, caption, work, cards, replayBtn, sfxBtn,
    setSfx(on) {
      sfxBtn.setAttribute("aria-pressed", String(on));
      sfxBtn.replaceChildren(on ? Icons.sfxOn() : Icons.sfxOff());
    },
    setPlaying(playing) { replayBtn.classList.toggle("is-playing", playing); },
    setCaption(text) { caption.textContent = text || ""; },
  };
}

/** Progress: one marker per planned item (not per correct answer — no score, GM-7/TN-1). */
export function renderProgress(container, total, done, current) {
  container.setAttribute("aria-valuemin", "0");
  container.setAttribute("aria-valuemax", String(total));
  container.setAttribute("aria-valuenow", String(done));
  if (container.childElementCount !== total) {
    container.replaceChildren(...Array.from({ length: total }, () => el("span", { class: "ol-progress__dot" })));
  }
  [...container.children].forEach((dot, i) => {
    dot.classList.toggle("is-done", i < done);
    dot.classList.toggle("is-current", i === current && i < total);
  });
}

/**
 * A letter card. Guide lines (ascender, midline, baseline) sit under the glyph so the letter's
 * position on the line is visible (b and p differ by where they sit). Baseline metrics come from
 * the Andika font (measured: x-height 0.508 em, ascender top 0.781 em, descender 0.239 em).
 */
export function letterCard(letter, { onTap, id } = {}) {
  const btn = el("button", { class: "ol-card", type: "button", "data-letter": letter, "aria-label": `letter ${letter}`, id });
  const svg = document.createElementNS(SVG_NS, "svg");
  svg.setAttribute("viewBox", "0 0 100 100");
  const base = 74, fontSize = 52;
  const xh = fontSize * 0.508, asc = fontSize * 0.781;
  const line = (y, cls) => {
    const l = document.createElementNS(SVG_NS, "line");
    l.setAttribute("x1", "14"); l.setAttribute("x2", "86");
    l.setAttribute("y1", String(y)); l.setAttribute("y2", String(y));
    l.setAttribute("class", cls);
    return l;
  };
  svg.append(line(base - asc, "ol-guide"), line(base - xh, "ol-guide"), line(base, "ol-guide ol-guide--base"));
  const text = document.createElementNS(SVG_NS, "text");
  text.setAttribute("x", "50");
  text.setAttribute("y", String(base));
  text.setAttribute("text-anchor", "middle");
  text.setAttribute("font-size", String(fontSize));
  text.setAttribute("class", "ol-glyph");
  text.textContent = letter;
  svg.append(text);
  const pointer = el("span", { class: "ol-pointer", "aria-hidden": "true" }, [Icons.pointer()]);
  btn.append(svg, pointer);

  if (onTap) {
    // pointerdown gives the earliest, most honest latency; ignore a second simultaneous finger (palm/multi-touch).
    let activePointers = 0;
    btn.addEventListener("pointerdown", (e) => {
      activePointers++;
      if (activePointers > 1) return;
      if (btn.disabled || btn.classList.contains("is-dim")) return;
      e.preventDefault();
      onTap(letter, e);
    });
    const release = () => { activePointers = Math.max(0, activePointers - 1); };
    btn.addEventListener("pointerup", release);
    btn.addEventListener("pointercancel", release);
    btn.addEventListener("pointerleave", release);
    // Keyboard: supported, never required (PL-2).
    btn.addEventListener("keydown", (e) => {
      if ((e.key === "Enter" || e.key === " ") && !btn.disabled) { e.preventDefault(); onTap(letter, e); }
    });
  }
  return btn;
}

export function showStart(root, { label = "Tap to start", hint = "", onStart }) {
  const btn = el("button", { class: "ol-start__btn", type: "button", disabled: "" }, [Icons.play(), el("span", { text: label })]);
  const hintEl = el("div", { class: "ol-start__hint", text: hint });
  const wrap = el("div", {}, [btn, hintEl]);
  const layer = el("div", { class: "ol-start" }, [wrap]);
  root.append(layer);
  btn.addEventListener("click", () => onStart?.());
  return {
    layer,
    ready() { btn.removeAttribute("disabled"); },
    setHint(t) { hintEl.textContent = t; },
    hide() { layer.classList.add("is-hidden"); setTimeout(() => layer.remove(), 350); },
  };
}

export function showComplete(root, { letters, line, doneLabel = "Done", onDone }) {
  const tiles = el("div", { class: "ol-complete__letters" }, letters.map((l, i) => {
    const t = el("div", { class: "ol-complete__letter", text: l });
    t.style.animationDelay = `${120 + i * 140}ms`;
    return t;
  }));
  const lineEl = el("div", { class: "ol-complete__line", text: line });
  const done = el("button", { class: "ol-complete__done", type: "button", onclick: () => onDone?.() }, [Icons.check(), el("span", { text: doneLabel })]);
  const layer = el("div", { class: "ol-complete" }, [tiles, lineEl, done]);
  root.append(layer);
  requestAnimationFrame(() => layer.classList.add("is-shown"));
  // GM-3: the milestone is <= 3 s, non-interactive, skippable: a tap anywhere finishes the settle animation.
  layer.addEventListener("pointerdown", () => {
    for (const t of tiles.children) { t.style.animation = "none"; t.style.opacity = "1"; t.style.transform = "none"; }
  }, { once: true });
  return { layer, remove() { layer.remove(); } };
}

/** A soft two-note confirmation tone, synthesized (no asset). Sound effects only; narration is separate (NA-4). */
export function makeTone(getContext) {
  return function playConfirm() {
    const ctx = getContext();
    if (!ctx) return;
    const now = ctx.currentTime;
    const notes = [[523.25, 0], [783.99, 0.11]];
    for (const [freq, at] of notes) {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = "sine";
      osc.frequency.value = freq;
      gain.gain.setValueAtTime(0.0001, now + at);
      gain.gain.exponentialRampToValueAtTime(0.18, now + at + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, now + at + 0.32);
      osc.connect(gain).connect(ctx.destination);
      osc.start(now + at);
      osc.stop(now + at + 0.36);
    }
  };
}

export function debugDrawer(root) {
  const pre = el("pre", { class: "ol-debug" });
  root.append(pre);
  const lines = [];
  return {
    log(obj) {
      lines.push(typeof obj === "string" ? obj : JSON.stringify(obj));
      if (lines.length > 60) lines.shift();
      pre.textContent = lines.join("\n");
      pre.scrollTop = pre.scrollHeight;
    },
  };
}

export { el };
