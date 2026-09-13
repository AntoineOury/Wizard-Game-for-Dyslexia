# Potion Brewing — the item-making mini-game

The alchemist store's crafting loop, from the LM-GM flowchart: **hear a
letter-sound sequence, rebuild it with potion ingredients, and the cauldron
brews a word-trap paper** for the creature hunts. Phonological awareness is
the learning mechanic; the recipe words come from the same `WordBank` the
traps use, so everything brewed is a word the hunting game understands.

## The brewing loop

1. **Listen** — the spell book on the table says the letters of a secret
   word, one sound at a time ("wuh... ah... guh..."). Walk close and it
   reads automatically; **tap the book any time to hear it again** (the
   flowchart's replay branch).
2. **Pick** — letter potions float on the shelves, each with a big letter
   over the cork (and a letter-tinted bottle). The word's letters are all
   there, shuffled, plus a couple of decoys.
3. **Drop** — get them into the cauldron **in the order the book said**:
   *tap* a potion and it flies in by itself, or *drag* it and let go over
   the pot (drop it anywhere else and it glides back to its shelf). Both
   inputs, per the design sheet — mouse and touchscreen alike.
4. **Puff** — the moment a potion sinks in, the cauldron **puffs the letter
   back out** as rising smoke, and says it too, so the player always sees
   (and hears) the sequence they are actually building. The hint line keeps
   the running progress: `W A _ _`.
5. **Brew** — when as many potions are in as the word has letters:
   - **Right order** → golden burst, the whole word rises from the brew, a
     paper sheet flies out into the player's satchel, and the book picks a
     new word. Words grow longer after every couple of successes
     (`Starting Length` → `Max Length`).
   - **Wrong order** → gray fizzle, the shelves refill with the same word's
     potions, and the book reads the sequence again. No penalty — just
     another listen.

Brewed papers land in **`PaperInventory`** (word → count, persisted like the
capture journal). The creature game does not consume them YET — its trap
supply is still infinite — so brewing is purely additive today. When hunts
should start spending papers, `PaperInventory.TryConsume(word)` is the one
call the trap flow needs, and the booklet can show the satchel through
`PaperInventory.All`.

## The scene setup (Alchemist Store)

One GameObject — **`Brewing Mini-Game`** — carries `BrewingGameController` +
`LetterSpeaker`. The controller's Inspector points at the store pieces you
placed: the **locked_book** (Book), the **boiler** (Cauldron), and the two
**shelf** instances. Move or restyle the room freely; everything re-measures
itself from the models' real bounds at play time (shelf rows, cauldron
mouth, tap zones). If a reference is ever lost, the controller falls back to
finding pieces by name ("book", "boiler", "shelf").

The four Aletheia potion prefabs are the ingredient bottles (variety is
cosmetic — swap in any models you like). Colliders, floating letters, the
tap zones on book and shelves, the solid body on the cauldron, and the
one-line hint UI are all added at runtime: **no third-party prefab was
modified**, and there are no panels — the whole game happens in the world.

## The book's voice (per platform, best available wins)

1. **Recorded clips** — the `LetterSpeaker` component has 26 AudioClip
   slots (A=0 ... Z=25). Record real phonics whenever you're ready and drop
   them in; they take over on every platform, no code changes.
2. **Browser builds** — with empty slots, WebGL uses the browser's built-in
   text-to-speech (the same Web Speech API the voice-calling already uses,
   synthesis side), speaking each letter's phonic form ("wuh", "sss") from
   the shared spoken-forms table. Real audio on itch.io out of the box.
3. **Editor / everywhere else** — a per-letter musical chime plays and the
   letter briefly appears over the book, so the sequence is still fully
   learnable while play-testing. (`Always Show Letters` forces the visual
   even when real audio plays, if you ever want both.)

The cauldron's letter echo on each pour uses the same voice.

## Tuning (all on the controller)

| Field | What it does |
| --- | --- |
| Starting/Max Length, Brews Per Length Up | how fast recipes grow |
| Decoy Count | extra wrong-letter potions on the shelves |
| Gap Between Letters | breathing room in the spoken sequence |
| Auto Play Radius | how close the player must be for the book to self-read (0 = tap only) |
| Interact Range | max distance for tapping potions/the book |
| Tint Potions | letter-colored bottles on/off |
| Puff Color | the cauldron's letter-smoke color |

## Code map (Assets/Scripts/BrewingGame/)

| File | Role |
| --- | --- |
| `BrewingGameController` | The one scene component: rounds, input, shelves, evaluation, hint line. |
| `PotionIngredient` | A bottle with a letter: label, bob, follow-the-finger, arc into the pot. |
| `BrewingCauldron` | Pour intake, letter puffs, success burst, fail fizzle, solid collider. |
| `LetterSpeaker` | The voice: clips → browser TTS → chime, in that order. |

Plus `PaperInventory` (in CreatureGame — the satchel is the hunting game's
domain) and two tiny additive touches to shared code: `WordBank.PickWord`
(length-ranged recipe words) and `VoiceLetterListener.SpokenFormsFor` (the
book speaks from the recognizer's own table). No terrain, water, controller
or toggle code touched.

## Deliberate prototype edges

- The **decoding step** from the flowchart's dashed box — *say* the letters
  aloud to activate the finished potion — is not wired yet; when wanted, it
  is a `VoiceLetterListener` session pointed at the brewed word's letters
  before `PaperInventory.Add` runs.
- Sequence evaluation happens after ALL slots are filled (per the
  flowchart), not on the first wrong drop.
- No inventory UI yet beyond the toast (satchel counts persist and are
  query-able); the booklet is the natural future home.
