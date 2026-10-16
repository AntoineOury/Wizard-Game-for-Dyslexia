// Seeded randomness so a run is reproducible from its seed (telemetry carries the seed) while
// item order, distractors and positions still vary between runs (Vision AC-6, PEDAGOGY P-14).

export function makeRng(seed) {
  // mulberry32
  let a = (seed >>> 0) || 0x9e3779b9;
  const next = () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  return {
    next,
    int(maxExclusive) { return Math.floor(next() * maxExclusive); },
    pick(arr) { return arr[Math.floor(next() * arr.length)]; },
    shuffle(arr) {
      const out = arr.slice();
      for (let i = out.length - 1; i > 0; i--) {
        const j = Math.floor(next() * (i + 1));
        [out[i], out[j]] = [out[j], out[i]];
      }
      return out;
    },
    // Weighted sampling without replacement. items: [{ value, weight }]
    weightedSample(items, count) {
      const pool = items.filter((it) => it.weight > 0).slice();
      const out = [];
      while (out.length < count && pool.length) {
        const total = pool.reduce((s, it) => s + it.weight, 0);
        let r = next() * total;
        let idx = 0;
        for (; idx < pool.length; idx++) {
          r -= pool[idx].weight;
          if (r <= 0) break;
        }
        out.push(pool[Math.min(idx, pool.length - 1)].value);
        pool.splice(Math.min(idx, pool.length - 1), 1);
      }
      return out;
    },
  };
}

export function randomSeed() {
  if (globalThis.crypto?.getRandomValues) {
    const b = new Uint32Array(1);
    globalThis.crypto.getRandomValues(b);
    return b[0];
  }
  return (Math.random() * 4294967296) >>> 0;
}
