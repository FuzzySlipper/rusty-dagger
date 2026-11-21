# Sprite-anchored concept art (portrait wide-A turnaround route for dungeon enemies & creatures)

**Status:** Validated on enemy `128` (Mage, 2026-11-20) and ready for dungeon denizens & non-humanoid creatures (where classic silhouettes and colour blocking matter more than townsperson robes, and where the slightly grittier pen-and-ink + matte gouache finish keeps enemies visually cohesive beside non-human monsters).

Townspeople remain on the text-only New B route ([`../README.md`](../README.md), `front.py`).

## Proven `832×1216` Portrait Wide-A 2-Stage Method (`sprite_front.py`)

Early 2026-11-08 tests ran at `1024×1024` square and inherited the sprite's combat/walking stance and pitch-black hood cavities ("Nazgûl void"). Validated fixes from enemy `128` (`outputs/dagger-characters/128/01-concept/candidates/sprite-anchored-portrait/`):

1. **Portrait `832×1216` sprite canvas:** Crop the classic front sprite's alpha bbox, optionally mask out held weapons/staves so hands stay open and empty, and enlarge nearest-neighbour to ~`1060 px` tall centered on an `832×1216` `#30343b` slate canvas (matches `front.py` pixel density instead of wasting horizontal slate in a `1024²` square).
2. **Stage 1 — Qwen-Image 2.1 InkWash at `832×1216` (25 steps, cfg 1.0):**
   - Explicitly requests a **symmetrical 30° wide-A turnaround pose** with empty open hands and wide clear background gaps between arms/hands and hips/pouches (eliminates webbing/ghosting in H3 orbits and splat bakes).
   - For hooded characters: explicitly describes a **real shadowed human face** (brow/eyes recessed in deep dark-umber shadow; nose, cheekbones, lips/beard sculpted in muted warm skin tones) and forbids an empty black void.
3. **Stage 2 — Krea2-Turbo New B image-to-image (`denoise = 0.38–0.46`, 8 steps, cfg 1.0):**
   - LoRAs: `Brom 0.35 + Elmore 0.45 + RetroInk 0.45`.
   - `d = 0.38` preserves crisp Vagrant-Story ink contour structure and sprite-accurate colour blocking while adding matte gouache richness (`128` approved front: `SB2-s202-d38`); `d = 0.46` leans slightly closer to pure Brom/Elmore painting.
   - Batching all Stage 1 seeds before all Stage 2 seeds keeps each UNET warm (~`6 s` per stage per image on RTX 5090).

## Usage

Run from the Rusty Dagger repository root. The recipe reads the workflow JSON beside the script and writes to
`local/art/generated/studies/people-concept/128-sa/` by default.

```sh
python3 authoring/tools/people/concept/sprite-anchored-wip/sprite_front.py 128 \
  --sprite local/art/reference/daggerfall-mobile-idle-unmirrored-r01/mobile-128/front.png \
  --seeds 42201,42202,42203 --denoise 0.38
```

## Matching Back View (`../back_ph3.py` — MiniMax H3 Image `p-h3-image_w4a8`)

Use **MiniMax H3 Image (`p-h3-image_w4a8`)** directly from the approved `front.png` with a short 4-sentence prompt (~`4.6 s` warm, single-stage, zero style drift):
- Avoid over-specifying rear pouches or running a Qwen-Image post-edit (Qwen tends to teleport front-side pouches onto the back and subtly shifts the overall painting finish).
- Include one calm geometric sentence asking for the smooth backs of both hands (palms facing away), five clear naturally proportioned fingers, and thumbs along the outer upper edge on each hand.
- Generate 6 seeds and inspect the automatic 2× hand zooms (`back_sheet.jpg`). Validated on `128` (`ph3v2-back-s5.png`).

```sh
python3 authoring/cms/concept_cli.py approved mobile-128 --image /tmp/mobile-128-front.png
python3 authoring/tools/people/concept/back_ph3.py \
  --front /tmp/mobile-128-front.png \
  --extra "The short teal hood covers the back of his head with no cape." \
  --out local/art/generated/studies/people-concept/128-back
```

## Earlier History (2026-11-08)

- Crew thread `den-01M6ZZ734GS6PM7AM72KF87451`, section 3 (`mobile-30` Vampire Ancient, `mobile-140` Monk, `mobile-1` Imp, `mobile-2` Spriggan).
- Original combined 2-stage workflow: `live-evidence/humanoid-concept-matrix-20261108/stage4-brom-elm-ink/workflow-new-b-sprite-to-concept-2stage.json`.
- Enemy `128` validation & approved front (`SB2-s202-d38`): `outputs/dagger-characters/128/01-concept/candidates/sprite-anchored-portrait/`.
