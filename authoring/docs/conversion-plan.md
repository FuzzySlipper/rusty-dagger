# Conversion plan for the 2D refresh areas

How the `items`, `paper-doll`, `worn`, `spells` and `effects` areas are divided into batches, and what the pilots
taught about the recipe. Task status, review state and Patch's decisions live on the Den tasks (#10494 items,
#10495 paper doll, #10496 spells and effects) and in `ledger/<area>-notes.json`, not here.

Counts are unique source frames (identical pixels shared by several items or elements are counted once) taken
from `authoring/df-raw/`. A batch is 24–50 frames, one host per batch, one review pass per batch in
`authoring/review/`.

The recipe is the one Patch accepted (Qwen painted reconstruction, Krea retro at denoise 0.35, back-projection
0.5, transparent cut-out). A new art type gets a pilot batch (**P**), reviewed before its area is queued in bulk.
If a pilot fails, change the recipe, note it in `docs/item-icons.md`, and rerun it.

## Design

- **First-person art is not converted.** Item frames in first person and the casting hands become the animated
  hand mesh with effects as needed (#10493). The 12,352 first-person PNGs in `df-raw/items` are reference only.
- **The paper doll stays a 2D layered doll.** Every race of a body morph shares one generated shape (`bodies.py`),
  and worn items are dressed on that shape (`dress.py`), so garments fit every race of the morph.
- **Dyes and metals are tints.** Clothing is painted neutral grey and armour and weapons in bare steel; the game
  multiplies a mask by each classic dye's or metal's tint (`promote.py`).

## Items (#10494)

Source: the `world` role of `df-raw/items/`: 132 records, 147 frames.

| Batch | Content | Count | Notes |
| --- | --- | --- | --- |
| first-ten, A, B, C, D1, D2, F1, F2 | World item sprites by family | 147 | Object sprites of about 10–90 px: the recipe's home ground. |
| frames-E1, E2 | Animated items (lit lights) | | Only the flame is composited onto frame 0 (`composite.py`). |
| redo-N | Redos after review | | One spec per round. A guessed subject is drawn as a definite thing (a pumpkin still-life, a belt with pouches), not an abstract blob. |

## Paper doll and worn items (#10495)

Source: `df-raw/paper-doll/` (bodies, faces, creatures) and the `player/` folders of `df-raw/items/`.

| Batch | Content | Count | Notes |
| --- | --- | --- | --- |
| P1, B1 | Bodies | 32 | `bodies-detailed` template; one host and seed policy so the set stays consistent. |
| F1 (a–d) | Faces, 16 files × 10 heads | 160 | `faces-detailed` template; trimmed to the classic silhouette (`heads.py`) and skin-matched (`skin.py`). |
| C1 | Vampire heads, beast heads, beast-form dolls | 20 | The beast dolls are framed paintings, fitted by `pictures.py`. |
| W1, W2 | Clothing (235–242) and armour (245–252) per morph | about 137 per morph | Dressed on the morph's base body (`dress.py`). |
| W3 | Held weapons (234) | 28 | Trimmed to the classic silhouette so the grip gap stays open. |
| artifacts | Artifact layers (432 male, 433 female) | 29 unique of 52 | 23 female records are the male one pixel for pixel. |

Not doll art: archives 201, 209 and 213 (the `player` images of the horse, the book, and the cart and ships) and
`FACES.CIF` (the faction faces of the character sheet and escort HUD, #10503).

## Spells and effects (#10496)

Source: `df-raw/spells/` and `df-raw/effects/`.

| Batch | Content | Count | Notes |
| --- | --- | --- | --- |
| pilot-icons, I1, I2 | Spell icons | 69 | 16×16 selectors: painted energy blooms; I2 uses the `icons-shapes` template. |
| T1 | Targeting icons and element icons | 9 | Framed UI tiles with a hand or skull: their own contract and pilot. |
| pilot-fire, E2, E3 | Projectile and impact frames per element | about 90 | Frame 0 stable, only what moves varies. Late fading impact frames sometimes gain a speck; check in review. |
| X1, X2 | TEXTURE.380/434 effects and the magic sparkle | about 54 | 434 copies 380 apart from record 0, so its duplicate keys take the 380 finals; 380 record 7 is blank and skipped. |

## Recipe findings from the pilots

- **Bodies:** headless figures, neck stump, undergarments, tail and scales survive. The `bodies-detailed`
  template (pulp-fantasy paperback painting, defined anatomy) is far more detailed than the first try.
- **Spell icons:** abstract bursts become painted energy blooms; a full-square tile keeps its square shape with
  a transparent surround.
- **Fire effect:** impact and projectile frames keep their relative sizes and fade/flicker progression when drawn
  independently.
- **Clothing:** the placeholder blue is neutralised to pale grey in a source copy (`gen-raw/source-edits/`) before
  conversion, so the result tints cleanly; fixed-colour parts such as a vest or sash stay out of the dye mask.
- **Worn pieces:** faint cloak drapes and lace-only boots or sandals tend to be redrawn as whole garments, and
  some brown cloth comes out paler than the source.

## Generation speed (worn pieces, 6-piece test on den-nimo)

| Variant | Qwen steps / retro steps | Canvas | Qwen+retro time | Result vs baseline |
| --- | --- | --- | --- | --- |
| v0 | 25 / 8 | 1024 | 1187 s | baseline |
| v1 | 15 / 8 | 1024 | 1011 s | same |
| v2 | 10 / 5 | 1024 | 759 s | same |
| v3 | 25 / 8 | 768 | 665 s | shapes drift (greaves, pauldron gaps) |
| v4 | 12 / 5 | 768 | 451 s | shapes drift |

Steps can drop to 10 / 5 at 1024 without a visible loss (`routes[].steps` in the spec); a 768 canvas changes the
construction of the piece, so it is not used. Patch's RTX 5090 (`--host den-5090`, no resource wrapper) ran the
same six pieces in 75 s, about 10x faster than den-nimo, with indistinguishable output. It is Patch's on-demand
machine: ask before queueing long batches there.
