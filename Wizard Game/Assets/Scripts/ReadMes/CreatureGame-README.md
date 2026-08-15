# Letter Creatures — mini-game prototype

Catch letter-creatures by reading, calling, trapping and **writing**. Built for
players aged 6-8 who are practicing letter recognition and formation.

## The capture loop

1. **Find** — each creature type IS a letter (Creature W, Creature S, ...) and
   has a home terrain: Creature W lives in the water and sometimes strolls up
   the beach; Creature S roams the meadows.
2. **Call** (optional) — the Call screen (`Q`) starts **listening: say the
   creature's letter out loud** — its name ("double u") or its sound ("wuh")
   — and every creature of that letter within earshot heads for the player's
   area, including out of the water onto the shore. Calling DRAWS OUT; it
   does not catch: a called creature that comes across a word paper it could
   stick to redirects to the paper. Letter buttons remain on the same screen
   as the everywhere-fallback (no mic, loud classroom, non-Windows build).
   *Letter naming and letter-sound production.*
3. **Trap** — the Trap button (or `T`) asks which creature to hunt, then deals
   three words: **tap the word with the most of that creature's letter**
   ("willow" beats "cat" for W). The right word becomes a paper sheet laid on
   the ground wherever the player taps. *Letter counting inside real words.*
4. **Snare** — the word is the trap's whole character. A creature cares only
   if its letter is IN the word, and the count is the grip: **"willow" (two
   w's) glues Creature W down for good; "water" (one w) attracts and holds it
   loosely, with a real chance it wriggles free**; a word with no w at all is
   simply not Creature W's trap (but still works for its own letters). Pull
   toward the paper scales with the same count. Every offered word "works" —
   the better-read choice just hunts better. Creatures are also **shy**:
   while the player stands within Player Shy Radius of a paper they won't
   approach it, so hunters learn to lay the trap and step back. Shyness is
   visible in the wild too: walk up to any roaming creature and it startles
   and scurries away (called creatures trust the voice and skip the fear).
   Wild creatures ALWAYS behave this way — by design, catching some of a
   letter never calms the species. When trained companions arrive later,
   tameness will belong to the captured individual (its own saved state),
   not to the wild population.
5. **Capture** — walk up to the stuck creature and press `E` (or the Capture
   button). The camera glides from third person down into the player's eyes,
   and a dotted letter hangs **in the air above the creature**: trace it by
   drawing in the world — no panel, no canvas, just the hint bar. Multi-
   stroke letters work naturally (grading waits a moment after each stroke
   for the next one). Correct → the creature celebrates, joins the booklet,
   and the camera glides back out to third person. Wrong → the creature
   TAUNTS, the ink clears, and the player tries again; `Esc` (or the Capture
   button again) steps back out without catching. Scoring is the worse of
   "ink on the letter" and "letter covered by ink" (threshold 60%, tunable).
   This is the moment that will later be skinned as the **butterfly-net
   swing**. *Letter formation.*
6. **Collect** — captures land in the **booklet** (`B`, or the Book button on
   the left edge): caught counts and a friendly line per creature, persisted
   between sessions.

## Controls

| Action | Laptop | Touch |
| --- | --- | --- |
| Booklet | `B` | Book button (left edge) |
| Make a trap | `T` | Trap button (left edge) |
| Call a creature | `Q`, then **speak the letter** | Call button (tap-a-letter fallback on screen) |
| Capture a stuck creature | `E` near it | Capture button, or tap the creature |
| Place / cancel a paper | click ground / `Esc` | tap ground / X |

The **Book / Trap / Call buttons live in the scene Hierarchy** (under
` Canvas Overlay` in PCG World) — restyle, resize and move them freely in the
editor. Each is an ordinary UI Button plus a `CreatureGameButton` component
(pick the action in its dropdown); the runtime only builds its own fallback
buttons in scenes that contain none. To add them to another scene: duplicate a
button, or make any Button + `CreatureGameButton`.

**The booklet is scene-authored too**: `Booklet_Panel` under ` Canvas
Overlay` (kept inactive; the game activates it). Restyle the window, title
and close button directly; style the **Row Template** child and every
creature's entry clones it — `CreatureBookletPanel` only fills in the words
(letter, name, count, blurb) at runtime, never the layout. `Row Spacing` on
the panel sets the distance between rows. Delete the panel from a scene and
the game falls back to its code-built booklet.

### The butterfly net

Add a net model anywhere under the Player (any name containing "net", e.g.
the Island Pack's `SM_Net`) and it becomes the capture tool automatically:
`CaptureNet` drives it in camera space like an FPS-held item — visible in
first person and during the capture moment, hidden in third person — and
while tracing it leans and points toward the player's drawing, sweeping the
letter's shape through the air. Add the `CaptureNet` component to the net by
hand to tune its rest position/tilt, follow lag and sway in the Inspector
(the auto-attach only runs when no CaptureNet exists in the scene).

### The capture camera (no controller changes)

`CaptureSequence` runs at script execution order 200 — after both player
controllers — so each frame it overwrites the camera pose they wrote:
blending from "whatever the controller wants this frame" into the head view
and back again is what makes both ends seamless in either view mode, without
touching a line of controller code. Gameplay input freezes through the same
UiMode flag the panels use; the touch joystick (which UiMode does not gate)
is put to sleep by deactivating the TouchControls object for the duration.

### Call and Trap have no required order

Calling lures creatures toward the PLAYER; papers pull eligible creatures
toward the PAPER, on their own timers — the two systems never wait on each
other. Call first and drop a paper where the crowd gathers, or lay papers
first and call a creature across them: both are equally valid hunts, and the
shyness rule adds the third move (step away so they dare approach). `Call
Radius` on the controller sets how far a call carries; each creature type can
override it with its own `Call Response Radius` in the creatures list.

### Landscape builds

The project is set to Auto Rotation with portrait disabled (Player settings),
so Android/iOS builds run landscape in both directions and never flip
vertical. WebGL ignores those flags — the page decides: the itch.io/embed
canvas size should be wide (e.g. 1280x720), and for a hard lock call the
browser's `screen.orientation.lock('landscape')` from the page after a
user gesture / fullscreen.

## Voice calling

`VoiceLetterListener` listens for each active letter's spoken NAME ("ess",
"double u") and, where a word engine can plausibly match one, its phonic
SOUND ("sss", "wuh"). Backend today: Unity's built-in Windows speech keyword
recognizer — offline, keyless, works in the editor. On other platforms it
reports unsupported and the call screen leans on the letter buttons.

Swapping in a cloud engine (e.g. Google Cloud Speech-to-Text) touches ONE
file: replace the platform block in `VoiceLetterListener` — start streaming in
`StartListening`, and pass each transcript to `ReportPhrase()`, which already
maps spoken forms to letters. Confidence is deliberately lenient: a misheard
letter merely calls a different creature, which is a shrug, not a failure.

### Troubleshooting voice (Windows)

The Call screen shows a live grey status line fed by `VoiceLetterListener`
(also logged to the Console as `[VoiceLetterListener] ...`). What it says is
the diagnosis:

- **"Listening! ..."** — the recognizer is running. Speak clearly, close to
  the microphone; letter NAMES ("double u", "ess") match far more reliably
  than phonic sounds.
- **"Windows says speech recognition is unavailable"** — this is about the
  WINDOWS system settings app, not Unity's Project Settings: press
  `Win + I` → **Privacy & security → Microphone** → enable *"Let desktop
  apps access your microphone"* (the Unity editor is a desktop app). Then
  check **Time & language → Language & region**: the Windows display
  language needs its speech pack installed (English works out of the box on
  most machines).
- **"Speech failed to start: ..."** / **"Windows speech error: ..."** — the
  exact engine error, usually a missing language pack or the microphone in
  use by another app.
- **"Voice needs the Windows editor or a Windows build"** — you are on
  macOS/Linux/mobile, where the built-in backend does not exist; the letter
  buttons carry the flow until a cloud backend is plugged in.

While any mini-game panel is open the cursor is freed and gameplay input
pauses via `PlayerControlScheme.UiMode` — the same flag the controllers
already use for Escape, and the mini-game's **only** touch-point with the
player-control code.

## Architecture (Assets/Scripts/CreatureGame/)

| File | Role |
| --- | --- |
| `CreatureGameController` | The one scene component: spawns creatures, runs call/trap/capture, owns everything below. |
| `CreatureDefinition` | One creature type: letter, prefab, habitat, blurb. Add a list entry = add a creature. |
| `LetterCreature` | Wander / lured / stuck / captured state machine, added to spawned models at runtime. |
| `WordTrapPaper` | The word sheet: built in code, trigger box, snares one creature. |
| `WordBank` | Kid-friendly word pool + "most of this letter" challenge builder. Covers all 26 letters. |
| `LetterShapes` | Traceable stroke paths for A-Z; feeds both the dotted guide and the grader. |
| `CaptureJournal` | Caught counts, PlayerPrefs-persisted, drives the booklet. |
| `CreatureGameUI` | All panels and buttons, built at runtime (the TouchControls recipe — nothing to author). |

**Deliberately no dependencies** on the terrain, water, controller or toggle
scripts: the world is read through physics raycasts, the water line through
one look-up of the `-- Water --` object's mesh, and the player through the
built-in `CharacterController`. Drop the component in any walkable scene and
it runs; delete the folder and nothing else breaks.

## Extending

- **New creature**: add a `CreatureDefinition` entry on the controller —
  letter, prefab, habitat. Words and trace shapes for all 26 letters already
  exist.
- **Better words**: grow `WordBank.Words`; the challenge builder adapts.
- **Narrative properties**: `blurb` is the seed; the booklet is the place.

## Prototype simplifications (flagged for later)

- Voice runs on the Windows keyword recognizer: great in the editor and on
  Windows builds, unavailable on tablets until a cloud/native backend is
  plugged into `VoiceLetterListener` (single swap point, see above). Phonic
  sounds match best-effort — a phoneme-level engine would grade them properly.
- Tracing accepts any stroke order/direction — formation order would need
  per-stroke sequencing on top of `LetterShapes`.
- No capture inventory items (paper supply is infinite), no sounds yet.
