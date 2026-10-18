# Gem Stencils — the letter-vial mini-game

The third station in the Alchemist Store. A **stencil scroll** unrolls on the
gem table with a letter inked on it; the player sets a gem on every vertex of
the letter, the gems glow and say the letter's sound, and fuse into a
**glowing letter**. Carry it to the **mortar** on the other table, drop it
in, and **say the letter's name** ("double u") — said right, a **letter vial**
flies onto the potion shelf, ready for brewing.

**The economy chain:** gem stencils make VIALS (letters) → the cauldron brews
vials into PAPERS (words) → papers are spent as traps on hunting trips in the
PCG World. Each arrow is one mini-game.

*Letter shape, letter sound, letter name — see it, hear it, say it.*

## How a round plays

1. **Unroll** — a parchment scroll rolls open on the gem table with one
   letter inked on it, and an empty dot on each of the letter's vertices
   (W has 5, A has 5, L has 3; round letters like O get a ring of dots
   around the curve). A row of gems — all one color this round, exactly one
   per dot — waits at the table's edge.
2. **Place** — three ways, all mouse and touch alike:
   - **drag** a gem onto a dot (it snaps in when close enough),
   - **tap a dot** and the next gem hops onto it,
   - **tap a gem** (it lifts and sparkles), then tap the dot it should go to.

   Tap a placed gem to send it back to the row. The hint line counts dots
   filled ("3 of 5 dots have a gem").
3. **Glow** — the moment the last dot is filled, the gems glow and pulse, the
   ink turns gold, and the letter's **sound** plays ("wuh").
4. **Fuse** — the gems fly together into a floating, glowing letter (golden
   glyph, halo, a little light). **Tap it** to pick it up: it rides in front
   of the camera, lower right, like a held lantern.
5. **Mortar** — walk to the mortar on the other table and **tap it**: the
   letter arcs in and sinks. The tap is forgiving: it looks past the
   player's own body (third person puts it between camera and mortar) and
   counts a tap that lands just beside the mortar. The hint asks the player to **say the letter's
   name out loud** ("double u", "ess", "bee").
6. **Vial** — heard right, the mortar grinds, the letter puffs out of it, and
   a letter-tinted **vial** arcs onto the potion shelf. It sits there for a
   moment, then joins the shelf's stock (`VialInventory`, persisted), and the
   scroll rolls up for the next letter.

The creature letters (`Priority Letters`, default S and W) come first while
the player has no vials of them; after that letters are random, never the
same twice in a row.

## The speaking step (and its fallback)

The mortar listens with the same `VoiceLetterListener` the creature Call
screen uses: the browser's built-in speech recognition in WebGL builds
(Chrome, Edge, Safari, Android), Windows speech in the editor on Windows. It
listens only for the round's letter, and accepts its name or its sound.
The voice status line under the hint shows what was heard, so a child can
see the ears working.

When there is no microphone path (Firefox, the editor on a Mac) — or after
`Tap Fallback Delay` seconds of listening (loud classroom, shy speaker) —
three letter buttons appear: the right one plus two look-alikes (W shows
next to M and V, B next to D and P). Tapping the right one also makes the
vial. Turn `Allow Tap Fallback` off to require speech.

## How vials feed the cauldron

The brewing game treats vials as its ingredients: recipe words are chosen
the vial stock can afford whenever possible, and **every potion poured into
the cauldron spends one vial of that letter** (when one exists). With no
vials the cauldron still brews for free, so a fresh save never dead-ends.
Vial counts show in the backpack grid as the light-blue number in each
slot's top-left corner.

## Scene wiring (Alchemist Store)

One GameObject — **`Gem Mini-Game`** — carries `GemStencilController`, its
own `LetterSpeaker` (26 clip slots, browser TTS, chime fallback — the same
voice stack as the spell book) and a `VoiceLetterListener`. The Inspector
points at the placed pieces: **table (1)** (the gem table) and the
**mortar**. `Vial Shelf` can be left empty: it falls back to the brewing
game's second shelf, then to anything named "shelf". Gems spawn from the Blue
Olive crystal prefabs (one model per round = one color), the vial is the
store's potion_01 model.

The scroll is built from primitives at runtime (parchment, two rollers, ink
lines, dots) and carries no colliders. Table/mortar colliders, gem
colliders, halos and the overlay are all added at runtime — no prefab was
modified, and the table can be moved or turned freely (`Letter Up World`
sets which way the letter's top points across the tabletop).

## Tuning (on the controller)

| Field | What it does |
| --- | --- |
| Letter Up World | which way the letter's top points across the table |
| Table Area Scale | how much of the table's short side the letter uses |
| Gem Size | gem size in meters (its largest side) |
| Snap Radius | how close a dropped gem must land to a dot, in letter widths |
| Interact Range | how close the player must be to touch gems, the letter, the mortar |
| Priority Letters | letters offered first while the player has no vials of them |
| Allow Tap Fallback / Tap Fallback Delay | the letter buttons at the mortar |

## Where the dots come from

The ink and the dots both come from `LetterShapes` — the same stroke table
the creature tracing grades against, so every mini-game agrees on what a W
looks like. Each stroke is simplified (Douglas–Peucker) so straight letters
keep exactly their corners and coarse arcs keep only the points that shape
them; dots closer than a small gap merge, so a shared corner gets one dot.
Every letter lands between 3 and 10 dots (L 3, W 5, O 8, G and Q 10).

## Code map (Assets/Scripts/GemGame/)

| File | Role |
| --- | --- |
| `GemStencilController` | The one scene component: rounds, input, the mortar, speech, vial, overlay. |
| `StencilScroll` | The parchment: unroll/roll-up, ink, vertex dots, `VerticesFor(letter)`. |
| `StencilGem` | One gem: row bob, drag, snap hop, select sparkle, glow, fuse flight. |
| `GlowingLetter` | The fused letter: float, carry in front of the camera, drop into the mortar. |

Plus `VialInventory` (in CreatureGame, next to `PaperInventory`), a vial
badge in `BackpackUI`, the vial-spending lines in `BrewingGameController`,
and `PotionIngredient.HaloSprite()` made public so the gems share the
potions' glow texture.

## Deliberate prototype edges

- The vial joins the stock after a short stay on the shelf rather than
  staying there as a model: the brewing game rebuilds its shelf bottles every
  round, so a permanent vial rack is a next step if wanted.
- A gem can only go on a dot, so a round can't be "wrong" — the learning is
  in finding the vertices and in the say-it step.
