# Letter Lens — find letters hidden in perspective

A standalone mini-game in the PCG World, inspired by *Viewfinder*. Letters are
hidden in the landscape by **forced perspective**: a letter is built from
ordinary props (mushrooms, pebbles, rocks, stumps, barrels, crystals)
scattered at different distances, and they only line up into the letter's
shape from **one standpoint**. Walk around, notice a shape coming together,
press **C** to lock in the view, then **trace the letter you see** on the
screen. A trace that matches a letter that really lines up from that view
finds it.

*Letter recognition in the wild — spotting a letter shape among clutter,
then forming it with the hand.*

It does not feed the vial / paper economy yet. Finding a letter raises
`LetterLensGame.LetterFound` (a `char`) and adds it to the "Letters found"
line; what *receiving* a letter means is the next step.

## How it plays

1. **Wander** — hidden letters come in three sizes:

   | Size     | Letter height | Stands about … from its spot | Typical props            |
   |----------|---------------|------------------------------|--------------------------|
   | Bush     | ~1.2 m        | 5.5 m                        | mushrooms, pebbles, gems |
   | House    | ~5 m          | 17 m                         | rocks, stumps, barrels, crystal clusters |
   | Building | ~14 m         | 42 m                         | boulders, big crystal clusters |

   From most places a letter is just loose scenery. Dots whose sight line
   reaches the ground rest on the ground; the others float and turn slowly
   in place (magic, and turning keeps them exactly on their sight line).
2. **Notice** — as the player comes near the right spot, a faint **ring of
   gold dots** appears on the ground: that is where the eyes must be. The
   hint line says "Something is starting to line up..." when close, and
   "Something lines up here... press C to lock in!" when it does (turn
   `Alignment Hints` off for a pure-discovery mode).
3. **Lock in** — **C** (or the **Lock in** button in touch mode) freezes the
   view: walking and looking stop, the cursor is freed, and viewfinder
   brackets frame the screen.
4. **Trace** — draw over the letter with the mouse or a finger, as many
   strokes as it needs. About 1.4 s after the last stroke the trace is
   checked:
   - it must be **on** the letter (a good letter drawn elsewhere on screen
     does not count),
   - the letter must really **line up** from this view, and
   - the shape must pass the same grader as the creature capture
     (`LetterShapes.ScoreTrace`: on the path *and* covering it, forgiving
     of wobble, honest about b/d/p/q because it never flips or rotates).
5. **Found** — the letter's props swell and settle, a big gold letter pops
   on screen, it joins the "Letters found" list, and the view unlocks.
   Otherwise the hint says why: trace again, "almost lined up — take a tiny
   step", or "no letter lines up from here". **C** / **Step out** leaves at
   any time.

## How the alignment works

`HiddenLetter`'s transform is the vantage: position = the eye point,
forward = toward the letter. For each dot of the letter's path
(`LetterShapes.SamplePath`, the same shapes the creature game traces), the
sight ray from the eye is computed, a random distance along it is chosen
(0.5–1.6× the letter's distance, depending on size), and a prop is placed
there, scaled so it appears the same size as every other dot from the
vantage. Moving away makes near dots slide past far ones (parallax), and the
letter falls apart.

To measure it, each dot is projected from the current eye, the letter
template is fitted onto the projections (uniform scale + offset, no
rotation), and the leftover error in letter units is the misalignment.
`Align Tolerance` (default 0.07) is how much is accepted — about 0.35 m of
wiggle room for a bush letter, 1 m for a house, 2 m for a building.

**What you see is what counts**: alignment is measured from the live camera.
Vantage rings are built for the first-person eye height, so first person is
the intended view; in third person the camera is behind and above the body,
so the body has to stand ahead of the ring.

## Setup

Nothing to wire: `LetterLensGame` bootstraps itself whenever the
**PCG World** scene loads, waits for the player to land, then hides
`Bush Letters` + `House Letters` + `Building Letters` letters (3 + 3 + 2) on
dry, gentle ground inside the terrain, each with a clear line of sight. The
first one is a bush letter 9–22 m from the player, so the idea is
discovered quickly. Letters are picked from `Alphabet` without repeats.
`Seed` 0 = a new layout every play.

To tune it, add a `LetterLensGame` component to any object in the scene (a
scene-placed one wins over the bootstrap) and edit it in the Inspector.

**Hand-placed letters:** add `HiddenLetter` to an empty GameObject at eye
height (about 1.6 m above the ground) where the player should stand, facing
where the letter should be. Set the letter and size; it builds itself on
Start and the game picks it up alongside the spawned ones. The selected
gizmo shows the eye point and the viewing line.

**Props:** `Assets/Resources/LetterLensPalette.asset` lists the prefabs per
size. Any prefab works and needs no resizing: the factory measures its
render bounds, scales it to the dot size needed at its distance, centers it
on its sight line, strips colliders (props are scenery, not obstacles — the
creature game's ground raycasts ignore them too) and converts built-in
"Standard" materials to URP Lit so the FantasyEnvironments rocks and barrels
don't render pink. Each letter uses two prop kinds from its list, so letters
have a character (a mushroom W, a crystal A). Missing or empty entries fall
back to plain grey stones.

The terrain helpers it uses (`WaterSurfaceAt`, `WorldFootprint`) are on
`ProceduralTerrainGenerator`; see the Terrain Tools README.

## Browser and touch

- Ink comes from `TruePointer`, so it lands under the pointer in WebGL at any
  display scale, and is placed on screen by viewport position.
- In touch mode the on-screen joystick and look pad nap while the view is
  locked (same trick as the creature capture), and a **Lock in / Step out**
  button sits on the right edge.
- On desktop, locking in frees the cursor (the shared `UiMode` flag). After
  stepping out, the browser needs one click to capture the mouse again, as
  after Escape.

## Files

`Scripts/LetterLensGame/`
- `LetterLensGame.cs` — bootstrap, spawning, lock in, trace, check, hints.
- `HiddenLetter.cs` — one hidden letter: building it, the alignment fit,
  vantage ring, found pop.
- `LensPropFactory.cs` — any prefab → one correctly sized, centered,
  collider-free, URP-safe dot.
- `LetterLensUI.cs` — hint line, found letters, viewfinder frame, ink, big
  letter, Lock in button.
- `LetterLensPalette.cs` — the prop lists asset.

## Not yet

- What receiving a letter does (inventory, backpack, feeding other games).
- Saving which letters were found.
- Occlusion: scattered trees can still stand in front of a letter after it
  is placed (the line of sight is only checked to the letter's middle).
