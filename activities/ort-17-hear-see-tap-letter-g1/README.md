# Hear the Letter, Tap the Letter

**Activity Corpus:** ORT-17 *Hear Letter Name, See Letter, Tap Letter* — family *Hear, See Tap - Target +
Distractors* (Orthographic Knowledge › Tactile, Audio), guided and independent phases. Its model phase is
ORT-15 *Hear Letter Name, See Letter, Tap Letter* (family *Hear, See, Tap - Single Target*), which the corpus
reserves for exactly this use.
**Target Units:** Visual Letter Unit, Group 1 — lower-case a–z, with the letter names and key words as written there.
**Phase × skill × units:** one phase per launch, letter recognition from the spoken name, 3–7 letters (`AC-1`).

Launch: `index.html?phase=guided&units=a,b,c,d,e` (all parameters in `manifests/*.json`). Catalog and test
launcher: `../index.html`.

## The tutor's exercise, and the fidelity check (`FI-1`, `FI-4`, `FI-6`)

A tutor lays a few letter cards on the table and says *"Point to the letter m."* The child points. On a wrong
card the tutor names it (*"That's n"*), contrasts it with the target by its shape or, when the two names sound
alike, by its name, points to the right card, has the child point to it, and brings the letter back later in the
drill. On the first introduction of a letter the tutor simply shows one card and names it while the child touches it.

The digital version keeps the act (tap the card named) and the whole move:

| Tutor's move | Here |
|---|---|
| Lays out cards, says the name | Letter cards appear; narration *"Tap the letter b."* (the only on-screen text is the caption of what is being said) |
| Waits | No countdown, no auto-advance; after 6 s a neutral re-prompt, after 12 s a no-response, then the demonstration and waiting as long as it takes (`FI-5`, PEDAGOGY `R-9`) |
| Names the wrong card | *"That's d."* (verification, PEDAGOGY `F-1`) — the card gets a quiet dashed outline, never red, never a buzzer (`TN-1`) |
| Contrasts | Visually confusable → *"b has a tall line, then a ball."* · Name-confusable → *"Listen: b."* (one elaboration, `F-1`; the corpus family's two error patterns) |
| Points to the right card | The target card lifts with a pointing hand and *"This is b."* |
| Has the child repeat | *"Tap the letter b."* again; the child reproduces the correct response (`F-12`). Not credited (E-SELECT: first response only) |
| Second miss: does it with the child | *"Let's do it together."* — the other cards fade, the target stays pointed out, the child taps it (PEDAGOGY `M-13`, Vision language bank) |
| Brings the card back | The item returns later in the activity, after at least one other item (`F-12`) |
| Introduces a new card | Model phase (ORT-15): one card, *"This is g."*, *"Tap the letter g."*, the name repeated as it is tapped |

*Would a skilled dyslexia tutor recognize the exercise?* Yes: it is letter-card identification with the correction
routine intact. The one thing missing from the table is the tutor's hand guiding the child's finger, which the
"together" step approximates by leaving only the target card tappable.

## What the child sees and hears

- **Start.** A single large *Tap to start* button (the one gesture tablets require before audio can play). All
  narration for the run is fetched and decoded while it waits (`NA-13`, `PL-7`); narration begins the moment it is
  tapped (`NA-11`).
- **Intro.** *"Listen, then tap the letter."* — once per run, skippable by tapping a card (barge-in, `NA-5`).
- **Items.** 2 cards (guided), 3–4 cards (independent) or 1 card (model), on a cream ground with nothing else in
  the working area (`VZ-2`, `VZ-3`). Each card shows the letter in Andika (single-story a and g, `VZ-4`) sitting on a
  faint baseline with mid- and ascender lines, as on a lined letter card, so where a letter sits (b vs p) is visible.
  Cards are ≥ 132 px (≥ 60 px required, `VZ-9`) and ≥ 16 px apart.
- **Chrome.** Progress markers top-left: one dot per planned item, filling as items are done — effort, not score
  (`GM-7`, `TN-1`). Sound-effects toggle top-right (narration cannot be muted, `NA-4`). Replay control bottom-left,
  speaker icon, no label, unlimited, never counted as a hint (`NA-2`). `?hand=left` mirrors the chrome (`TR-14`).
- **Correct.** The card settles green-grey, a soft two-note tone, the letter name spoken as it is tapped (the
  name paired with the symbol *in time*, `MM-5`), next item. No praise (`TN-2`); pace is the reinforcement (`F-17`).
- **Completion.** *"That's all five."* (`TN-3`), the five letters settle into a row (≤ 3 s, skippable, `GM-3`), one
  *Done* button. The activity emits its results and exits; what comes next is the session's decision (`RP-5`).

Each unit is presented twice per run by default (`passes=2`), so a 5-unit guided run is 10 items ≈ 80 s, well
inside the 2-minute target; a 3-minute ceiling of learner time (idle excluded) ends the run gracefully with
partial results (`AC-3`, PEDAGOGY `R-11`).

## Items, distractors, order

- Item order is a fresh shuffle every run and every pass, never the same letter twice in a row; target position is
  random (`AC-6`, PEDAGOGY `P-14`). Runs are reproducible from the `seed` recorded in telemetry.
- Distractors come from the unit set (plus any `pool` letters the host allows) and are **diagnostic** (`F-9`):
  a letter confusable with the target by shape (b/d/p/q, m/n/h, i/l/j/t, c/e/o/a, u/v/w/y …) weighs 3, by name
  (the "ee" names b c d e g p t v z; a/j/k; i/y; l/m/n; f/s/x; u/q/w) weighs 2, any other pool letter 1 — so the
  contrast the skill is about appears often without the set becoming predictable. Both lists live in
  `shared/data/letters-group1.json` and mirror the `common_confusions` field of the Vision `TR-7` stroke schema.
- Guided = 2 options (chance 50%), independent = 3–4 options. Because chance is high, E-SELECT mastery needs
  ≥ 90% over ≥ 10 items across two sessions (PEDAGOGY `P-2`); that decision is the session layer's.

## Error taxonomy and responses (PEDAGOGY `F-8`, `F-11`, `M-13`)

| What happened | Logged as | Response |
|---|---|---|
| Tapped a letter that looks like the target (or both looks and sounds alike) | `incorrect`, `visual` / `visual_name` | *"That's d."* → shape line for the target → demonstration → reproduce → requeue |
| Tapped a letter whose name sounds like the target's | `incorrect`, `name` | *"That's d."* → *"Listen: b."* → demonstration → reproduce → requeue |
| Tapped an unrelated letter | `incorrect`, `other` | *"That's x."* → demonstration → reproduce → requeue |
| Tapped before the name could have registered (< 0.9 s from prompt onset) | `incorrect`, `fast_wrong: true` | Verification and demonstration only, then a slower pace into the next item; no reteaching (`F-11`: guessing) |
| Wrong again on the reproduction | `repair.together: true` | *"Let's do it together."* — only the target is tappable |
| Two consecutive first-tap errors across items | next item `scaffold: "model_support"` | The next item opens with *"This is m."* and the pointer before the prompt (`M-13`: return to modeling) |
| Silence for 6 s after the prompt | `reprompts: 1` | Neutral re-prompt, identical wording (`R-9`, `A-8`) |
| Silence for another 6 s | `no_response` (not an error, not in the error run, `R-9`) | Demonstration, prompt, wait indefinitely; the eventual tap is logged as a repair; requeued |

Hint count is always 0: this activity offers no hint control, because the replay control and the demonstration are
the scaffolds a tutor uses here. Requeued items are logged `attempt: 2` and are never mastery-eligible (`M-3`).

## Timing (measured, never displayed — PEDAGOGY `L-16`, Vision `TN-7`)

| Constant | Value | Why |
|---|---|---|
| Acceptable letter-name latency | 3 s | PEDAGOGY `M-6` 2 s × 1.5 for this population (Vision `AC-11`) |
| Timeout before re-prompt | 6 s after the prompt ends | `R-9`: 2 × acceptable, no visible countdown |
| Pause after the prompt | 0.8 s | `NA-12` |
| Fast-wrong threshold | 0.9 s from prompt onset | a tap before the name could be processed is a guess (`F-11`) |
| Learner-time ceiling | 180 s active | `AC-3`; idle beyond 20 s per item is not counted (`R-10`) |

## Narration (Vision §5)

All 164 utterances live in `narration/manifest.json` with stable keys (`NA-10`): `intro`, `together`, `stop_now`,
`done.3`–`done.7`, and per letter `prompt.x` *"Tap the letter x."*, `name.x` *"x."*, `this_is.x` *"This is x."*,
`thats.x` *"That's x."*, `listen.x` *"Listen: x."*, `shape.x` (the shape line). Every line is one clause, ≤ 8 words,
and the prompt wording is identical for every letter and every re-prompt (`A-8`, `TN-9`). Multi-sentence feedback is
played as separate complete utterances with a natural gap, never assembled inside a sentence (`NA-7`).

Audio: Kokoro-82M v1.0, voice `af_heart`, speed 0.9 (≈ 10% slower than conversational, falling intonation,
one adult voice — `NA-8`), generated offline by `tools/generate-narration.mjs`, 64 kbps mono MP3, ≈ 1–3 s each.
`narration/generated.json` lists the phoneme string spoken for each clip; the letter names were checked against the
expected name for all 26 letters (e.g. the shape line for *a* is given to the engine as "Eigh is …" because a bare
"A" is read as the article). This is the `NA-9` discipline applied to letter names.

If a clip fails to load or decode on a device, the player falls back to browser speech synthesis, or to the caption
alone, and the run's telemetry says so (`narration_mode: tts | text | mixed`). This is a declared deviation
(`manifests/*.json → corpus_deviations`), never the intended path.

## Telemetry (Vision `RP-3`, PEDAGOGY `R-12`)

`activity:start` carries the manifest-level context (phase, units, pool, seed, band K-1, latency multiplier,
narration mode, missing assets). `activity:item`, per item: `unit, phase, attempt, pass, response_mode, options,
target_position, outcome, first_tap, latency_ms, error_class, fast_wrong, hint_count, scaffold, reprompts,
no_response_first, repair{reproduced, taps, together, latency_ms}, requeued, capture_confidence,
eligible_for_mastery, elapsed_ms, narration_mode`. `activity:complete` carries the full record with
`status: completed | ceiling` (`interrupted` is written if the page is left mid-run; reloading the same link
resumes it, `AC-5`).

## Build checklist (Vision §11) — answered

- **Fidelity.** Maps to ORT-17 (and ORT-15 for the model phase) ✔ · response act is the tutor's (tap the named
  card) ✔ · no modality substitution ✔ · models, corrects, requires repetition and requeues ✔ · a tutor would recognize it ✔
- **Contract.** One phase, one skill, 3–7 units, ≤ 2 min ✔ · stateless across sessions, decides nothing about what
  comes next ✔ · interruptible/resumable, varied order ✔ · manifests per phase ✔ · telemetry v1 ✔
- **Audio.** Every line narrated ✔ · replay always visible, unlimited, uncounted ✔ · completable without reading
  (the caption duplicates the audio and nothing else) ✔ · pre-generated assets, not browser TTS ✔ · no stitched
  fragments ✔ · letter-name pronunciation reviewed ✔ · all strings in the manifest, `narration:check` fails the build
  on missing or orphan assets ✔ · speaking within 300 ms of the start tap ✔
- **Speech / tracing.** Not applicable (tap activity); `uncertain` exists in the outcome enum for the speech
  activities that will share this telemetry.
- **Tone.** No buzzer, red, "wrong" or lost points ✔ · no person praise ✔ · every error carries the path forward in
  the same breath ✔ · no timers ✔ · lines from the shared bank ✔ · completion animation ≤ 3 s, skippable, only on
  completion ✔
- **Visual / platform.** Silent working area, nothing moving near the cards during a response ✔ · single-story
  a/g ✔ · spacing, cream/warm-grey contrast (≈ 10.9:1 body, 5.3:1 secondary), ≥ 60 px targets ✔ · shared
  library ✔ · assets prefetched; runs through a connection drop after start ✔ · no keyboard entry ✔ · no audio captured,
  no third-party scripts, minimum data ✔

## Workaround audit (Vision §12, PEDAGOGY §14) — tried

| Cheat | Result |
|---|---|
| Tap every card until one is accepted | Only the first tap is scored; the item is requeued and loses mastery eligibility (`E-SELECT`, `M-3`) |
| Wait it out | Nothing advances on idle; after two timeouts the demonstration shows the answer *and the item is logged no-response and requeued*, so waiting earns nothing |
| Replay to the answer | Replay repeats the prompt only |
| Exploit position or length | Target position random; all cards identical in size |
| Memorize the set | Order, distractors and positions re-randomize every run; 26 letters × confusion sets keep the item space large |
| Farm the reward | The completion line and animation only follow the last item; no per-item reward exists |
| Guess from a picture or context | There are none: the card shows the letter only |

## Decisions that need the owner's eye

1. **Shape lines** (`shared/data/letters-group1.json → shape_line`) were authored here in the corpus's style
   (*"b starts with the tall line, then the ball"*). They should be reviewed against the handwriting program the
   corpus adopts (`TR-9`), since the words will later have to match the stroke models.
2. **Confusion lists** (visual and name) were authored here; they drive both distractor weighting and feedback
   routing. Worth a pass by the corpus owner.
3. **Distractor pool.** Distractors are drawn from the unit set by default (letters the child has met), with an
   optional `pool`. The alternative, drawing from all of Group 1, gives more varied discrimination but can show a
   child letters not yet introduced (`L-1`).
4. **Voice.** Kokoro `af_heart` at 0.9 speed was chosen for warmth and clarity; the Vision's narration-production
   decision (human voice-over vs neural TTS) is still open (§14). The manifest and generator make a swap a one-command job.
5. **Progress dots** show planned items, not units or correct answers. If the session layer prefers "You know
   18 of 26 letters" style progress, that belongs at the session level (`GM-7`).
6. The **together** step treats the untapped cards as untappable (they fade). A tutor would physically guide the
   hand; this is the closest a touchscreen gets. Logged as `repair.together`.

## Files

```
index.html               shell page (viewport, fonts, modules)
activity.js              the activity: queue, options, prompt/response window, feedback loop, requeue, telemetry
manifests/{model,guided,independent}.json   Vision RP-2 manifests
narration/manifest.json  every spoken line (key, text, text given to the engine, asset path, contexts)
narration/audio/*.mp3    164 pre-generated clips
narration/generated.json per-clip phonemes and durations for review
```
