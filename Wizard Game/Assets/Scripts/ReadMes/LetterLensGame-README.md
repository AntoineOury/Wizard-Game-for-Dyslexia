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

**The story:** the world is a book, and an evil spell made its words go wild
and its letters escape into the scenery. The player tidies the page: every
letter caught leaves the world, and its pieces fly into the bag. The
top-left counter shows how many escaped letters are left; when it reaches
zero, the page is tidy again.

**Caught letters are kept.** Each catch adds one of that letter to
`LetterInventory` (saved, letter → count, the same pattern as
`VialInventory`), so other mini-games can spend them later with
`LetterInventory.TryConsume('W')`. The backpack shows them as a green count
in each slot's bottom-left corner and a "Letters" total.
`LetterLensGame.LetterFound` (a `char`) is raised too.

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
   - the shape must be a well-formed letter on this letter's path (the same
     grader as the creature capture, `LetterShapes.ScoreTrace`: on the path
     *and* covering it, forgiving of wobble, honest about b/d/p/q because it
     never flips or rotates), and
   - it must look more like **this** letter than any other of the 26
     (`LetterShapes.LooksMostLike`, average distance both ways, compared in
     the letter's own on-screen placement). The first check alone let a
     well-drawn wrong letter pass: an F over an E, an O over a Q, any
     neat shape roughly where the letter is. `Recognition Margin` (0.1)
     is how close a look-alike may come and still count as a tie.

   Offline test with simulated traces of every letter (20 each, tested as
   every one of the 26 targets): the right letter was accepted 99% of the
   time for careful or normal tracing and 94% for very wobbly tracing. A
   wrong letter was accepted 0.6% of the time (2% when very wobbly), down
   from 68% with the first check alone. The leftover mix-ups are the true
   look-alikes: O/Q, F/P, T/Y.
5. **Caught**: a big gold letter pops on screen, and the rocks,
   mushrooms and trees that formed it hop and fly one after another, in
   the order the letter is written, into the bag just below the view,
   shrinking as they go. They are gone from the world for good. The letter
   is added to the inventory and the view unlocks when the last piece lands.
   Otherwise the hint says why: "that looks like F", trace again, "almost
   lined up — take a tiny step", or "no letter lines up from here". **C** / **Step out** leaves at
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
`Bush Letters` + `House Letters` + `Building Letters` letters (10 + 8 + 4)
on dry, gentle ground inside the terrain, each with a clear line of sight.
The first `Near Player Letters` (3) are bush letters whose spots are 6–35 m
from the player, so there is always one close by. Letters are picked from `Alphabet` without repeats.
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

- Other mini-games spending caught letters (the inventory is ready).
- Saving which letters on the page were caught: the counts in the bag are
  saved, but every visit to the PCG World hides a fresh set of letters.
- Occlusion: scattered trees can still stand in front of a letter after it
  is placed (the line of sight is only checked along the letter's middle),
  and a letter's own near trees can hide a few of its far dots.
