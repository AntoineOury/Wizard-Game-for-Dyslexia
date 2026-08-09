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

### Biomes

Add entries to the **Biomes** list to split the world into region types — each
with its own asset lists, height shaping and ground colors. With the list empty
the streamer behaves exactly as before (one uniform world).

Which biome owns a position comes from a second, much lower-frequency "climate"
noise field (`Biome Scale`, default 600) — still a pure function of seed and
position, so the infinite world stays deterministic. Each biome takes a share of
the 0-1 climate axis proportional to its `Coverage`, **in list order** — a biome
only ever borders its list neighbours, so order the list like a climate
gradient (Winter, Forest, Desert reads sensibly; Winter, Desert, Forest puts
snow against sand). The colored strip in the Inspector shows the layout.

Per biome:

| Field | Effect |
| --- | --- |
| `Coverage` | Relative share of the world (2 = twice the area of 1) |
| `Height Multiplier / Curve / Offset` | Height shaping: winter at 50 beside forest at 30 gives visibly higher peaks |
| `Color By Height` | Ground gradient: whites for winter, greens for forest |
| `Environment Assets` | Assets that appear ONLY in this biome |

The streamer's global `Environment Assets` list still spawns in every biome —
use it for universal props (rocks), and biome lists for specific ones (snowy
pines). The drop area gains an **"Add Dropped Assets To"** selector.

**Transitions are smooth by construction.** Every biome shapes the same base
noise through its own curve/multiplier, and the results are blended by weights
that crossfade across `Biome Blend` of the climate axis — heights, colors and
normals all cross the border without seams because the weights do. Assets use
the same weights as acceptance probability, so winter trees thin out across the
transition while forest trees thicken, instead of swapping at a hard line.

`DominantBiomeAt(worldPosition)` is public — poll it from a player script to
switch ambience, music or fog when the player crosses into a new region.

### Seeing it in the Scene view

The streamer builds nothing in edit mode by default — `Update` and coroutines
only run during play — so the Scene view would otherwise be empty until you
press Play.

**Scene Preview** (button at the top of the Inspector) builds a bounded block of
chunks while not playing. Because generation is deterministic this is not an
approximation: what appears is exactly what the player will walk through.

- `Preview Radius In Chunks` — small on purpose, the preview builds
  synchronously rather than budgeted across frames.
- `Preview Follows Scene Camera` — new terrain appears as you fly around.
  Turn off to pin the preview to the streamer's own position.
- `Preview Includes Props` — off gives a faster terrain-only preview.

Preview objects carry `HideFlags.DontSave`, so **saving the scene never bakes
them in** — the whole point of a streaming world is that its contents don't live
in the scene file. They're also cleared when you press Play, deselect the
object, or toggle the preview off, so a stale preview can never sit on top of
the chunks the streamer builds for real.

Press **Refresh** after changing noise or asset settings; the preview only
rebuilds by itself when the camera crosses into a different chunk.

### Performance: incremental builds and LOD

Chunk building is time-sliced: the vertex grid is computed a few rows per
frame under `Build Budget Ms` (default 3), so streaming never hitches a frame
no matter the resolution. A chunk keeps showing its previous mesh until the
new one applies, so LOD swaps and rebuilds never flash a hole.

LOD rings: chunks within `Lod0 Radius` build at full resolution, out to
`Lod1 Radius` at half, beyond at quarter (a quarter-res chunk is ~11x fewer
triangles). The collider ring is pinned to full resolution — physics must
match visuals. Vertical skirts hang from every chunk edge to hide the
hairline cracks where different LODs meet; depth is `Lod Skirt Depth`.

### Water

Enable `Water Enabled` for a translucent, rippling surface wherever terrain
dips below the Water zone threshold. The surface samples the same
biome-blended height pipeline as the ground, so it is seamless across chunks
and lakes sit level with their local terrain rules. Chunks whose lowest point
is above the waterline skip the surface entirely. Material auto-creates from
`OtherwiseLabs/Terrain Water`, or assign your own.

### Grass (GPU instanced)

Add the **Terrain Grass Renderer** component (anywhere; it auto-finds the
streamer): thousands of swaying blades drawn via `DrawMeshInstanced` — a few
draw calls, zero GameObjects. Placement is deterministic and restricted to the
Grass zone on walkable slopes; density halves each chunk ring outward.

### Domain warp

`Warp Strength` bends every height and biome lookup through a second noise
field, breaking up Perlin's characteristic round blobs. **0 = off and the
default: existing worlds keep their exact shape. Any other value is a
different world for the same seed** — choose it before you get attached to a
layout.

### World edits (player changes that stick)

`WorldEdits` stores player changes as deltas over the deterministic baseline —
the Minecraft save-file trick. `WorldEdits.RemoveProp(gameObject)` fells a
scattered prop permanently (every prop carries a `SpawnedPropId` tag);
`RecordAddition` plants one. The streamer consults removals after candidates
resolve and respawns additions after scatter, so edits survive streaming out
and back. Saved as JSON in `Application.persistentDataPath` on quit or via
`WorldEdits.Save()`. Only edited chunks occupy memory or disk.

### Biome ambience

`BiomeDefinition` now carries an `Ambient Loop` clip, volume, and an optional
fog color override. Add the **Biome Ambience** component (the streamer object
is fine): it crossfades soundscapes and tints the fog as the player crosses
borders. Fog tint needs fog enabled in Lighting > Environment.

### Determinism regression test

`Tools > Otherwise Labs > Terrain > Record Determinism Baseline` fingerprints
five reference chunks (heights, climate, scatter candidates) into
`ProjectSettings/OtherwiseLabsTerrainBaseline.json`. After any terrain code
change, run **Verify Determinism**: a FAIL means the code now generates a
different world from the same settings — walk-back persistence would silently
break. Changing Inspector settings legitimately changes the world; Verify
detects that separately and asks for a re-record instead of failing.

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
