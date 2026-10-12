# Gem Constellations — the letter-forging mini-game

The third station in the Alchemist Store: lay small crystals on the gem
table so that, connected by lines **in the order they were placed**, they
draw a letter — a constellation. Grind a finished constellation in the
mortar and it becomes a **letter-potion vial**, the ingredient the cauldron
spends when brewing trap words.

**The economy chain:** gem constellations make VIALS (letters) → the
cauldron brews vials into PAPERS (words) → papers are spent as traps on
hunting trips in the PCG World. Each arrow is one mini-game.

## How a round plays

1. A big target letter floats over the gem table (tap it to get a
   different letter). Only **single-stroke letters** are offered — one
   unbroken chain of lines can draw them — and the creature letters (S, W)
   come first while the player has no vials of them.
2. A pile of crystals waits at the table's edge — one color per round.
   Faint **numbered spots** on the tabletop suggest one good layout
   (`Show Guide Sockets` turns them off for a harder game).
3. **Drag** stones anywhere on the table — or **tap** a pile stone and it
   hops to the next numbered spot by itself. Tap a placed stone to send it
   back. Lines connect the stones in placement order, live.
4. Each placed stone lights one channel of the learning loop, in rotation:
   **READ** (the glyph pops up), **HEAR** (the book-voice says it),
   **SAY** (the hint invites saying it aloud), **WRITE** (the lines draw
   it). Gentle flavor, never a gate.
5. When the shape reads as the letter — the same best-fit, shape-based
   grader the tracing game uses (`Accuracy Threshold`, default 0.5) — the
   whole constellation glows gold. Placement is free-form: any layout that
   draws the letter counts, not just the suggested spots.
6. **Tap the mortar**: the stones arc in, the mortar grinds, the letter
   puffs out, and a tinted **vial** pops into the satchel
   (`VialInventory`, persisted; visible as the light-blue number in the
   backpack grid's top-left corners).

## How vials feed the cauldron

The brewing game now treats vials as its ingredients: recipe words are
chosen the satchel can afford whenever possible, and **every potion poured
into the cauldron spends one vial of that letter** (when one exists). With
an empty vial satchel the cauldron still brews for free, so the loop never
dead-ends on a fresh save — but the intended circle is:

> gems → vials → brewed word papers → traps → captured creatures

## Scene wiring (Alchemist Store)

One GameObject — **`Gem Mini-Game`** — carries `GemConstellationController`
plus its own `LetterSpeaker` (26 clip slots, browser TTS, chime fallback —
same voice stack as the book). The Inspector points at the placed pieces:
the second **table (1)** and the **mortar**. Stones spawn from the Blue
Olive crystal prefabs (one model per round = one color), the vial is the
store's potion_01 model. Colliders, sizing, lines, labels and sockets are
all runtime-built from renderer bounds — no prefab was modified, and the
table can be moved or re-rotated freely (`Letter Up World` sets which way
the letter's top points across the tabletop).

## Trap visuals are vials now (and the hologram hook)

Placed traps in the PCG World now LOOK like letter-potion vials: the
creature controller's new `Trap Visual Prefab` slot (wired to potion_01)
replaces the flat paper sheet with a letter-tinted vial and the word
floating above it. Leave the slot empty to get the classic paper back.

This same slot is the **Looking Glass hook**: Looking Glass Blocks is a
platform for sharing holograms viewed on Looking Glass displays or as web
embeds — it cannot render *inside* a Unity WebGL world, so a true
holographic trap needs either their Unity plugin on holographic hardware,
or a hologram-styled prefab (quilt-textured quad stack / depth-offset
layers) dropped into this slot. The game-side contract is ready: any
prefab handed to `Trap Visual Prefab` becomes the trap's look, no other
code involved.
