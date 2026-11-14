# Iso Puzzle (experimental, standalone)

A roguelike perspective puzzle, separate from the main wizard game. There is
no avatar, no inventory, and no link to the other mini-games' save data. The
player turns a small isometric room until scattered shapes line up into a
letter (the Viewfinder / Monument Valley "aha" moment), snaps a photo of it,
then traces the letter on the photo.

Lives on its own branch, `thread_iso_puzzle`.

## How a run plays

1. **Title card.** A sample room slowly turns behind it. Press **Play**.
2. **Turn the room.** Drag left and right to turn it. From room 4 on, drag up
   and down as well, because the letter may hide higher or lower. Arrow keys
   or WASD also work. A quick flick keeps the room spinning for a moment.
3. **Find the letter.** The viewfinder corners warm up as the view gets close
   and turn gold once it lines up. If you let go near the answer, the room
   clicks the rest of the way into place.
4. **Snap.** The camera flashes and the view freezes into a photo.
   - Snap from the wrong angle and you **lose a heart**.
5. **Trace** the letter on the photo with a finger or the mouse. The grade
   comes 1.4 s after the last stroke, so letters with several strokes have
   time to finish.
   - **Pass:** "That's the letter B!", rooms cleared +1, and the room sinks
     away while a new random one rises.
   - **Fail:** you **lose a heart**, the ink clears, and blue guide dots show
     how the letter is formed over the shapes.
   - **Turn again** goes back to turning the room for free.
6. **Potions.** Some rooms hold a floating pink potion (22% of rooms, or 45%
   when you're hurt; never the first room). Tap it to get one heart back.
   With full hearts it stays put, and it disappears with its room, so it
   can't be saved for later.
7. **Out of hearts:** the game-over card shows rooms cleared and your best run
   (stored in PlayerPrefs under `IsoPuzzle.BestRooms`). Press **Play again**.

## How the hidden letter works

The camera is **orthographic**, so sliding an object straight toward or away
from the camera does not move it on screen at all. `IsoRoomBuilder`:

1. Takes the letter's strokes from `LetterShapes`, the same stroke table the
   Creature Game's tracer uses, so the shapes and the grader always agree.
2. Cuts each stroke into short pieces and builds each one as a ProBuilder
   block, rod or wedge, with ball joints at sharp corners.
3. Pushes every piece a random distance along the target view direction,
   kept inside the room volume. From the target angle the pieces line up
   into the letter. From anywhere else they look like a scattered sculpture.
4. Adds decoy props (pillars, stairs, arches, cubes, tori, cones, balls), but
   only where they don't cover the letter's outline from the target view.

Every solid is generated at run time with ProBuilder's `ShapeGenerator`
(`IsoShapes` wraps the calls). No prefabs or authored meshes are needed.

## Difficulty curve (per room cleared)

| Knob | Start | Change | Limit |
|---|---|---|---|
| Decoy props | 5 | +2 per room | 20 |
| Tip up/down needed | no | from room `flatRooms + 1` (4) | always |
| Alignment tolerance | 7° | −0.4° per room | 4° |
| Letters | A–Z, random | no repeats within the last 8 | |

## Scene and files

- **Scene:** `Assets/Scenes/Iso Puzzle.unity` holds one GameObject, `Iso Puzzle`,
  with `IsoPuzzleGame`. The camera, sun light, UI, EventSystem and rooms are
  all created when play starts. The component also works if dropped into any
  empty scene.
- **Build settings:** the scene is added at the end, enabled. To test it on
  its own in a WebGL build, drag it to the top of the list (or untick the
  other scenes) for that build.
- **Inspector knobs on `IsoPuzzleGame`:** `seed` (0 means a new run every
  time; any other number repeats the same rooms, handy for testing), alignment
  tolerances, `flatRooms`, trace threshold (0.6, same as the Creature Game),
  `evaluateDelay`, whether wrong snaps or failed traces cost hearts, and the
  potion chances.

| Script | Job |
|---|---|
| `IsoPuzzleGame` | Run flow: hearts, score, snap, photo, tracing, potions, game over |
| `IsoRoomBuilder` | Builds a room: platform, hidden letter, decoys, potion spot, palette |
| `IsoShapes` | ProBuilder shape wrappers and shared URP materials |
| `IsoOrbitCamera` | Orthographic orbit camera; drag, keys, flick, taps, snap assist easing |
| `IsoHud` | All UI, built in code (hearts, score, hint, Snap, viewfinder, photo, cards) |
| `IsoPotion` | The bobbing potion and its drink animation |
| `IsoPointer` | Browser-accurate pointer position (via `Plugins/WebGL/WebPointer.jslib`) |

## Browser input

Turning, tapping potions and tracing all read the pointer through
`IsoPointer`, which uses the same `WebPointer.jslib` bridge as the main
game's `TruePointer`. That keeps ink under the finger on scaled browser
displays. UI buttons use the normal EventSystem.

## Known limits / next ideas

- The only link to the rest of the project is `LetterShapes` (a shared,
  read-only stroke table). Copy it into this folder if the games must be fully
  separate.
- Materials use `Shader.Find("Universal Render Pipeline/Lit")`. If shapes come
  out pink in a build, add that shader (and URP/Unlit for the potion) to
  *Project Settings > Graphics > Always Included Shaders*.
- No sound yet. Ideas for later: room themes, rooms where two letters hide
  (pick the right one), and a lowercase mode.
