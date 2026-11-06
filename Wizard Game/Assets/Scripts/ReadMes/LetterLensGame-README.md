# Letter Lens — find letters hidden in perspective

A standalone mini-game in the PCG World, inspired by *Viewfinder*. Letters are
hidden in the landscape by **forced perspective**: a letter is made of
ordinary scenery (pebbles, rocks, mushrooms, ferns, crystals, stumps, trees)
standing on the ground at different distances, and their tops only line up
into the letter's shape from **one standpoint**. Nothing floats. Walk
around, notice a shape coming together, press **C** to lock in the view,
then **trace the letter you see** on the screen. A trace that matches a
letter that really lines up from that view finds it.

*Letter recognition in the wild — spotting a letter shape among clutter,
then forming it with the hand.*

It does not feed the vial / paper economy yet. Finding a letter raises
`LetterLensGame.LetterFound` (a `char`) and adds it to the "Letters found"
line; what *receiving* a letter means is the next step.

## How it plays

1. **Wander**: hidden letters come in three sizes:

   | Size     | Where it lies                      | Spans    | Typical dots                       |
   |----------|------------------------------------|----------|------------------------------------|
   | Bush     | the ground 3–7 m ahead             | ~17°     | pebbles, mushrooms, little plants  |
   | House    | 12–28 m ahead, below a rise        | ~14°     | rocks, stumps, ferns, crystal clusters |
   | Building | 35–85 m ahead, seen from a hilltop | ~13°     | boulders and whole trees           |

   Every dot is something standing on the ground whose **top** (a cap, a
   rock's crown, a canopy) sits on the dot's sight line: a pebble where the
   line touches down, a taller thing nearer, a tree nearer still. From
   anywhere else it is just a meadow, a rock field or a copse.
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
forward = the sight line through the letter's middle, tilted down onto the
ground. For each dot of the letter's path (`LetterShapes.SamplePath`, the
same shapes the creature game traces), the sight ray from the eye is
followed to where it meets the ground. A random distance short of that is
chosen, and from the palette every prop is checked: scaled so its top
lands on the ray at that distance, is it a natural height for that prop
(its min/max height), and does its top look about dot-sized from the
vantage? One of the props that fit is stood there, upright on the terrain.
A ray that cannot be filled naturally is left as a small gap rather than
filled with something odd. Moving away makes near tops slide past far ones
(parallax), and the letter falls apart.

Because nothing may stand in the sky, every sight ray of a letter must come
down onto the ground. The spawner tries spots and headings until the whole
letter's view lands on dry ground in range: bush letters fit almost anywhere,
house and building letters end up on rises and hilltops looking out over
lower ground, or facing a slope.

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
first one is a bush letter whose spot is 6–18 m from the player, so the idea is
discovered quickly. Letters are picked from `Alphabet` without repeats.
`Seed` 0 = a new layout every play.

To tune it, add a `LetterLensGame` component to any object in the scene (a
scene-placed one wins over the bootstrap) and edit it in the Inspector.

**Hand-placed letters:** add `HiddenLetter` to an empty GameObject at eye
height (about 1.6 m above the ground) where the player should stand, aimed
(tilted down) at the ground where the letter should lie. Set the letter and size; it builds itself on
Start and the game picks it up alongside the spawned ones. The selected
gizmo shows the eye point and the viewing line.

**Props:** `Assets/Resources/LetterLensPalette.asset` lists the prefabs,
each with a believable height range (mushrooms 6–50 cm, stones up to 1.2 m,
rocks up to 4.5 m, stumps up to 1.3 m, trees 3–20 m...). Any prefab works and
needs no resizing: the factory measures its render bounds once, scales it,
stands it upright with its base slightly sunk into the terrain, and converts
built-in "Standard" materials to URP Lit so the FantasyEnvironments rocks
don't render pink. Small props lose their colliders; anything 2.5 m or
taller (trees, boulders) keeps them, like the scene's own trees. The trees
are the same FantasyEnvironments and Silver Cats ones the world already
uses; flowers and bushes from FantasyEnvironments are left out because their
cut-out leaf materials would need a URP conversion that may not survive a
WebGL build.

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
- `LensPropFactory.cs` — any prefab → one dot: measured, scaled, standing on the ground,
  URP-safe (keeps colliders only on trees and boulders).
- `LetterLensUI.cs` — hint line, found letters, viewfinder frame, ink, big
  letter, Lock in button.
- `LetterLensPalette.cs` — the prop lists asset.

## Not yet

- What receiving a letter does (inventory, backpack, feeding other games).
- Saving which letters were found.
- Occlusion: scattered trees can still stand in front of a letter after it
  is placed (the line of sight is only checked along the letter's middle),
  and a letter's own near trees can hide a few of its far dots.
