# Conversion plan for Den tasks 10494–10496

Counts are unique source frames (identical pixels shared by several items or elements are counted once) taken from `authoring/df-raw/`. Batch sizes follow what has worked so far: 24–50 frames per run, one host per batch, one review pass per batch in `authoring/review/`.

The recipe is the owner-accepted one (Qwen painted reconstruction, Krea retro at denoise 0.35, back-projection 0.5, transparent cut-out). It is proven for object sprites of roughly 10–90 px. Everything below the "Recipe fit" rows is a guess until its pilot batch has been reviewed.

## Order of work

1. Close out 10494 (small, unblocks the ledger).
2. Tooling pass T1, needed by both other tasks (see below).
3. Pilot batches (marked **P**) for each new art type. Review each before queueing the rest of its area.
4. Grind the remaining batches, one area at a time.

Decision gates (owner) are marked **G**. Nothing after a gate is queued until the answer is recorded here.

## Tooling pass T1 (before any 10495/10496 batch)

- `review_ledger.py` is world-sprite specific (it globs `df-raw/items/*/*/manifest.json`). Generalise it to areas: `world`, `paper-doll`, `spells`, `effects`, each with its own source glob, notes file (`ledger/<area>-notes.json`) and finals directory (`gen-raw/<area>/`).
- A spec scaffolder: given an area and a list of source keys, write `specs/<area>/<name>.json` plus `-retro.json` with the template keys and one stub item per source (id, image, name, size). The contract text still gets written per item by hand or by an agent. Today every batch spec is hand-assembled.
- `run_batch.sh` takes an area (source root and spec directory) instead of assuming items.
- Alpha policy: all areas keep the cut-out alpha (the same transparent RGBA finals as the item icons). No additive variant unless a pilot shows an effect needs it.

## 10494 — inventory icons and world sprites

Status: 147 of 147 frames have a final. See the ledger for status.

| Batch | Content | Count | Notes |
| --- | --- | --- | --- |
| 10494-R1 | Owner review pass of existing finals | 10 flagged guesses, tin, emerald, belt | Tanto, straps, totem, painting, wine-rack, lantern, torch, skeleton, black rose, fig, batch C guesses. Record accepted / redo / skipped in `ledger/items-notes.json`. |
| 10494-R2 | Redo anything R1 marks `redo` | tbd | One spec `redo-N` per round. |
| 10494-C | Count reconciliation | 0 images | Task says 173 sprites; the export holds 132 records / 147 frames. Find the 26 missing, or correct the task text. |

Redo-2 (the belt with two pouches, formerly "cloth amulet") is converted and accepted. Redo-3 (the painting, now a pumpkin still-life with leaves and dirt) follows the owner direction to pick a subject instead of an abstract blob; use the same approach for the other guesses.

Out of scope: first-person item art and casting hands. They become an animated hand mesh with special effects as needed (12,352 extracted first-person PNGs stay in `df-raw/items` as reference only).

## 10495 — paper doll bodies and worn items

Source: `df-raw/paper-doll/` (bodies, faces, creatures) and the `player/` folders of `df-raw/items/` (worn items).

**G1 (decided): 2D layered paper doll.**

| Batch | Content | Count | Recipe fit |
| --- | --- | --- | --- |
| 10495-P1 | Bodies, one race per gender | 4 | Large figures (about 66×143). New territory: pilot. |
| 10495-B1 | Remaining bodies | 28 | Same race/gender grid. Must stay mutually consistent, so one host and one seed policy. |
| 10495-C1 | Creature paper dolls (werewolf, wereboar, wolf) and vampire heads | 20 | Mixed sizes. |
| 10495-F1…F4 | Faces, 16 files × 10 heads | 160, four batches of 40 | Tiny (about 26×35). Likely needs a different recipe from item icons: pilot 10 first. |
| 10495-W0 | Identify the worn archives | 0 images | Done: 432 and 433 are the artifact paper-doll art, male and female (26 artifacts each; the donor also uses them as artifact world art). They are already in `df-raw/items/artifacts/*/player` and `world`. 201, 209, 213 (shared layers, 7 frames in all) are still unnamed. |
| 10495-W1 (**P**) | One body morph, clothing | about 100 | Archives 235–242 are clothing per body morph, about 100 garments each. Pilot one morph. |
| 10495-W2 | Same morph, armor | about 37 | Archives 245–252, about 37 pieces each. |
| 10495-W3 | Weapons and shields worn on the doll | 28 | Archive 234. |
| 10495-W4…W10 | Remaining seven morphs | about 137 each | **G2** (decided by the W1/W2 pilot): convert each morph separately, or derive from the pilot morph by body alignment. Per-morph conversion is about 960 frames and does not stay consistent between morphs. |

Not covered: `FACES.CIF` (the importer decoder rejects its compression). Confirm whether anything uses it before spending effort.

## 10496 — spell icons, casting hands, missiles and effects

Source: `df-raw/spells/` and `df-raw/effects/`.

| Batch | Content | Count | Recipe fit |
| --- | --- | --- | --- |
| 10496-P1 | Spell icons, pilot | 8 | 16×16 selector icons. Far smaller than anything converted so far: pilot, and expect a different recipe (symbolic reading rather than a painted object). |
| 10496-I1, I2 | Remaining spell icons | 59, two batches | |
| 10496-T1 | Targeting icons | 5 | 24×16. Could ride with I2. |
| 10496-E1 (**P**) | Fire projectile and impact | 19 | Animated. Reuse the frames approach: keep frame 0 stable, vary only what moves. |
| 10496-E2…E5 | Cold, poison, shock, magic | about 15–20 each | After E1 is accepted. Some frames are shared between elements (90 unique across 5). |
| 10496-X1 | Blood splash, corpse, remaining TEXTURE.380 | about 30 | Alpha-blended effects, same cut-out as the icons. |
| 10496-X2 | Magic sparkle (20) and inventory glints (TEXTURE.434, 7 not already in 380) | about 27 | Same. |
| 10496-H | Casting hands | 0 | **G3 (decided): not converted.** All first-person animation sprites become an animated hand mesh with special effects as needed. |

## Pilot results

All four pilots ran with the unchanged recipe and read well.

- **Bodies (4, done):** male human, female human, male Khajiit, female Argonian. Headless figures, neck stump, undergarments, tail and scales all survive.
- **Spell icons (8, done):** the abstract bursts become painted energy blooms; the full-square tile (icon-001) keeps its square shape with a transparent surround.
- **Fire effect (19, done):** impact and projectile frames keep their relative sizes and fade/flicker progression when drawn independently.
- **Bodies, detailed prompt (4, done):** the `bodies-detailed` template (pulp-fantasy paperback painting style, defined anatomy) replaced the first bodies result and is far more detailed. It is also more realistic than the item art, so check it beside the armor before using it for faces and the other bodies.
- **Clothing, neutral base (4, done):** the placeholder blue is neutralised to pale grey in a copy of the source (`gen-raw/source-edits/worn-neutral`) before conversion, and the result tints cleanly (shown multiplied by blue, gold, red and green). Fixed-colour parts such as the vest and sash would be left out of the tint by the game's dye mask.
- **Worn equipment (12, male human, done):** armor, shields, boots and trousers are good. The clothing sprites are drawn in placeholder blue (the game tints them per dye); decided: neutral base plus tint, as in the neutral-base pilot above. Procedure for the rest of clothing: neutralise the blue in a source copy, then convert from it.

## Suggested next batches

Pilots are done. Next: 10494-R1 review, then bodies B1 (28), creatures C1, faces F1, spell icons I1/I2 and T1, fire E2–E5, effects X1/X2, and the rest of worn W1–W3 using the neutral-base procedure.

## Original first six batches

1. 10494-R1 (owner review of what already exists, plus redo-2).
2. T1 tooling.
3. 10495-P1 (bodies pilot), after G1.
4. 10496-P1 (spell icons pilot).
5. 10496-E1 (fire pilot).
6. 10495-W1 (clothing pilot for one morph).

Each pilot answers whether the recipe fits its art type before any area is queued in bulk. If a pilot fails, change the recipe, note it in `docs/item-icons.md`, and rerun it before queueing anything else.
