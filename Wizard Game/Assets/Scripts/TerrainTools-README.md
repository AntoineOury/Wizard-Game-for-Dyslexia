# Terrain Tools

Two components, sharing one noise/scatter core:

| Component | Use for |
| --- | --- |
| `ProceduralTerrainGenerator` | A single finite terrain built in the editor. Authored, saved in the scene. |
| `InfiniteTerrainStreamer` | An endless world streamed around the player at runtime. |

**Shared** between them: `EnvironmentAssetRule` and everything on it (zones,
weights, footprints), the category presets applied on prefab drop, and the
`OtherwiseLabs/Terrain Vertex Color` shader. An asset rule tuned in one behaves
the same way in the other.

**Not shared:** height sampling. `InfiniteTerrainStreamer` uses `TerrainNoise`;
`ProceduralTerrainGenerator` kept its own inline copy of the same formula, so
that existing scenes reproduce their terrain byte-for-byte. The two are
therefore *not* interchangeable — see "Same seed, different worlds" below.

Collision also differs by necessity: the finite generator places sequentially
into a `ScatterOccupancy`, while the streamer needs the order-independent
`CandidateField` so separately-generated chunks agree at their borders.

---

## Asset overlap (rock inside a tree)

`Min Spacing` only ever separated an asset from **copies of itself** — each rule
kept its own private list of placed positions, so a rock rule had no idea a tree
rule had already put a trunk there.

Each rule now also has a **`Footprint Radius`**: the room it claims against
*every other* asset. Two instances conflict when their centres are closer than
the sum of their radii. All rules share one `ScatterOccupancy` — a spatial hash,
so cost stays near-constant instead of O(n²) as instance counts climb.

- `Footprint Radius = 0` estimates from the prefab's renderer bounds, halved.
  The halving is deliberate: the full extent is the **canopy**, and tree crowns
  are supposed to interlock. It's the trunks that must not share ground.
- Ground cover defaults to `0.2` so grass and flowers still tuck under trees.
- Rules are placed **largest footprint first**, otherwise a field of grass
  placed early leaves nowhere legal for a house.
- Toggle off with `Prevent Asset Overlap` to compare against the old behaviour.

---

## The infinite world

### Why nothing is saved

Every chunk's terrain *and* its props are a pure function of
`hash(seed, chunkCoord)`. Regenerating a chunk reproduces it exactly, so
unloading loses nothing — the world isn't stored, it's **recomputed**. That is
what allows it to be effectively infinite and still feel like a real place when
the player walks back.

Verified by simulation: a chunk generated twice yields identical props, and
walking a chunk sequence forwards vs backwards produces identical worlds.

### Problem 1 — the world gets too heavy

Cost is tied to **view distance, not distance travelled**. Three nested radii,
drawn as gizmos when the component is selected:

| Radius | Default | What it controls |
| --- | --- | --- |
| `View Distance In Chunks` | 4 | Terrain mesh. Cheap — it's just triangles. |
| `Asset Distance In Chunks` | 2 | Props. Expensive: a forest 500 m out costs a fortune and reads as a green smudge. |
| `Collider Distance In Chunks` | 1 | Mesh colliders. Only what the player can physically reach. |

Chunks beyond the radius are unloaded and their props returned to a
`PrefabPool` rather than destroyed — a player pacing over one border would
otherwise create and destroy hundreds of trees per crossing, and the GC spikes
are exactly the stutter that makes streaming feel bad.

Chunk builds are budgeted (`Chunks Built Per Frame`) and queued nearest-first,
so the ground under the player appears before the horizon does. The Inspector
shows a live triangle/instance estimate and warns before the numbers get silly.

### Problem 2 — walking back shows a different world

Solved by the determinism above, but only if generation never depends on
*order of visit*. Two details make that true:

**Seamless terrain.** Heights are sampled at absolute world coordinates, so
neighbouring chunks sampling a shared edge compute the same vertex height and
the seam closes exactly. Normals are computed analytically from the noise rather
than by `RecalculateNormals`, which would only see one chunk's triangles and
leave a visible lighting seam at every border.

**Border-consistent props.** A chunk resolves collisions against candidates from
its 8 neighbours as well as its own. The rule is: *a candidate loses if any
lower-ordered candidate overlaps it* — where "order" derives purely from
`(chunkCoord, ruleIndex, candidateIndex)`, never from generation order.

Greedy acceptance would pack slightly denser, but it makes a candidate's fate
depend on its neighbour's fate, which depends on candidates outside the halo —
so two chunks could disagree and you'd get a rock growing out of a tree exactly
on the border. This rule needs nothing beyond footprint range, so every chunk
reaches the same verdict independently. Marginally sparser, always consistent.

A one-chunk halo suffices because conflicts can only reach two radii, so keep
**footprints well below `Chunk Size`**.

### Same seed, different worlds

Putting the same seed into both components does **not** produce the same
landscape, for two reasons:

- The finite generator samples noise across a local `0..terrainSize` rectangle;
  the streamer samples absolute world coordinates, so a chunk's position in the
  world decides its shape.
- The streamer shifts every sample by `TerrainNoise.NoiseOrigin` to dodge the
  Perlin mirror described below. The finite generator does not.

This is intentional — matching them would mean changing the finite generator's
output and invalidating scenes already built with it. Treat the seed as
meaningful *within* a component, not across the two.

### Perlin mirrors at the origin

`Mathf.PerlinNoise` mirrors around zero, so sampling negative world coordinates
would make the world visibly symmetric about `(0,0)`. Sampling is shifted by
`TerrainNoise.NoiseOrigin` (100000) to stay in positive space. The finite
generator passes `0` here, preserving its original output exactly.

---

## Setup

1. `GameObject > 3D Object > Infinite Terrain Streamer (Otherwise Labs)`
2. Assign **Viewer** (the player) and a **Terrain Material** using the
   `OtherwiseLabs/Terrain Vertex Color` shader.
3. Drop prefabs into the drop area. Note **`Max Instances` is per chunk here**,
   not per world — the drop area defaults to 40 rather than 150.
4. Press Play and walk.

Tune `View Distance` / `Asset Distance` against the Inspector's budget estimate.

---

## Not handled: player edits

Determinism preserves *generated* state only. If the game later lets players
change the world — fell a tree, place a wall — those are **deltas** against the
generated baseline and do need storing.

The shape that fits this architecture is a `Dictionary<Vector2Int, ChunkEdits>`
of per-chunk changes (removed instance indices, added objects), consulted in
`ScatterChunk` after candidates resolve and before instantiation. Only visited
*and modified* chunks ever occupy memory or disk, which is exactly how Minecraft
keeps an infinite world's save file finite.
