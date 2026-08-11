# Letter Creatures — mini-game prototype

Catch letter-creatures by reading, calling, trapping and **writing**. Built for
players aged 6-8 who are practicing letter recognition and formation.

## The capture loop

1. **Find** — each creature type IS a letter (Creature W, Creature S, ...) and
   has a home terrain: Creature W lives in the water and sometimes strolls up
   the beach; Creature S roams the meadows.
2. **Call** (optional) — the Call button opens the calling screen and starts
   **listening: say the creature's letter out loud** — its name ("double u")
   or its sound ("wuh") — and every creature of that letter within earshot
   comes toward the player, including out of the water onto the shore. Letter
   buttons remain on the same screen as the everywhere-fallback (no mic, loud
   classroom, non-Windows build). *Letter naming and letter-sound production.*
3. **Trap** — the Trap button (or `T`) asks which creature to hunt, then deals
   three words: **tap the word with the most of that creature's letter**
   ("willow" beats "cat" for W). The right word becomes a paper sheet laid on
   the ground wherever the player taps. *Letter counting inside real words.*
4. **Snare** — the word decides who the paper can hold: **only creatures
   whose letter is tied for the most occurrences in it**. "willow" (two w's,
   two l's) can snare Creature W *or* Creature L — nobody else sticks. Both
   are drawn toward it, so which one you catch is hunting craft: lay the
   paper in the right habitat and call the letter you want. The rule is
   child-sayable — "the biggest letters in the word win the paper."
5. **Capture** — walk up to the stuck creature (`E` or tap it) and **trace its
   letter** over a dotted guide. Scoring is the worse of "ink on the letter"
   and "letter covered by ink", so neither scribbling nor half a letter
   passes, but wobbly lines do (threshold 60%, tunable). *Letter formation.*
6. **Collect** — captures land in the **booklet** (`B`, or the Book button on
   the left edge): caught counts and a friendly line per creature, persisted
   between sessions.

## Controls

| Action | Laptop | Touch |
| --- | --- | --- |
| Booklet | `B` | Book button (left edge) |
| Make a trap | `T` | Trap button (left edge) |
| Call a creature | Call button, then **speak the letter** | same (tap-a-letter fallback on screen) |
| Trace a stuck creature | `E` near it | tap the creature |
| Place / cancel a paper | click ground / `Esc` | tap ground / X |

The **Book / Trap / Call buttons live in the scene Hierarchy** (under
` Canvas Overlay` in PCG World) — restyle, resize and move them freely in the
editor. Each is an ordinary UI Button plus a `CreatureGameButton` component
(pick the action in its dropdown); the runtime only builds its own fallback
buttons in scenes that contain none. To add them to another scene: duplicate a
button, or make any Button + `CreatureGameButton`.

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
