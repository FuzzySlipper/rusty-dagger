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

For static NPCs or non-rigged sprites (e.g. Daedric Princes or static court NPCs), pass `--style-only` (and `--width 1024 --height 1024` for square sprites) to skip the wide-A turnaround stance and overhead lighting prep while keeping the flat `#30343b` slate ground for later cutout. When a classic 3/4 sprite hides one arm behind the torso, add `--show-both-arms` so the shoulders open slightly and both arms/hands stay visible for back views and splats:

```sh
python3 authoring/tools/people/concept/sprite-anchored-wip/sprite_front.py npc-001 \
  --sprite local/art/reference/npc/art/sprite/texture-175-record-002-frame-001-source.png \
  --style-only --width 1024 --height 1024 --seeds 101,102,103,104,105,106 --denoise 0.38 --brief "..."
```

For named NPCs whose reused world sprite disagrees with their dialogue portrait (`FACES.CIF`), pass `--portrait PATH` (implies `--portrait-only`; trims the classic 3 px gold border automatically) to anchor the face, hair, and collar cues on the portrait while generating a full-length body from the brief. Combine `--portrait` with `--style-only` for static NPCs to get a natural head-to-toe front portrait without wide-A turnaround pose constraints:

```sh
python3 authoring/tools/people/concept/sprite-anchored-wip/sprite_front.py npc-022 \
  --portrait local/art/reference/npc/art/portrait/faces.cif-006.png \
  --seeds 22101,22102,22103,22104,22105,22106 --denoise 0.38 --brief "..."
python3 authoring/tools/people/concept/sprite-anchored-wip/sprite_front.py npc-023 \
  --portrait local/art/reference/npc/art/portrait/faces.cif-007.png --style-only \
  --seeds 23101,23102,23103,23104,23105,23106 --denoise 0.38 --brief "..."
```

### Exacting Pose-Locked A-Pose (`--pose-lock`)

For animated characters that need exact rest-pose lock across seeds (canonical Mixamo 22° arm spread, 18° forward elbow bend, palm-inward/thumb-forward 25°-curled hands, straight legs, and 20% hip-width parallel feet), pass `--pose-lock`. By default (`--pose-mode stick`, `--style-variant clean`) Stage 1 applies `qwen_image_2.1_fun_controlnet_union_int8_convrot.safetensors` with the universal full-spec DWPose stick figure (`pose-lock/dwpose-832x1216.png`, `s=1.00, end=0.95`) and built-in `#30343b` slate-border lock, and Stage 2 finishes in plain Krea2-Turbo New B (`d=0.38`) — zero body-proportion distortion across male, female, stocky, robed, dress, and armored builds. Three alternative `--style-variant` finishes are also available (all release Stage 1 ControlNet at `end=0.60`): `crunchy` (`d=0.46`, cooler Vagrant-Story/tabletop-manual pen-and-ink crosshatching matching original `128`), `crunchy-warm` (`d=0.42`, warm ochre-and-umber book-cover gouache), and `painterly-vivid` (`d=0.42`, warm-umber contours and lush gouache with clean vivid local pigments under neutral light so characters take both amber tavern light and cold cobalt dungeon light cleanly). Optional `--pose-mode depth-female` (`xbot-depth-832x1216.png`) and `--pose-mode depth-male` (`ybot-depth-832x1216.png`) presets are bundled alongside it when stricter `0°` foot-angle depth lock is needed:

```sh
python3 authoring/tools/people/concept/sprite-anchored-wip/sprite_front.py npc-107 \
  --portrait local/art/reference/npc/art/portrait/tfac00i0.rci-396.png \
  --pose-lock --pronoun She --seeds 7101,7102,7103,7104,7105,7106 --brief "..."
```

The same graph is also available as a 1-click stock-ComfyUI browser workflow in [`ui-workflows/rusty-concept-apose-poselock-2stage.json`](ui-workflows/rusty-concept-apose-poselock-2stage.json) (and saved in the `gpu-lab` ComfyUI sidebar).

## Matching Back View (`../back_ph3.py` — MiniMax H3 Image `p-h3-image_w4a8`)

Use **MiniMax H3 Image (`p-h3-image_w4a8`)** directly from the approved `front.png` with a short 4-sentence prompt (~`4.6 s` warm, single-stage, zero style drift):
- Avoid over-specifying rear pouches or running a Qwen-Image post-edit (Qwen tends to teleport front-side pouches onto the back and subtly shifts the overall painting finish).
- Include one calm geometric hand sentence matching the front pose (default `--pose rest` describes the reverse of the canonical 22° curled palm-inward rest pose and centers the `back_sheet.jpg` hand zooms there; pass `--pose wide-a` for legacy 30° open-palm fronts or `--omit-hands` for cloaked/static poses).
- Generate 6 seeds and inspect the automatic 2× hand zooms (`back_sheet.jpg`). Validated on `128` (`ph3v2-back-s5.png`).

```sh
python3 authoring/cms/concept_cli.py approved mobile-128 --image /tmp/mobile-128-front.png
python3 authoring/tools/people/concept/back_ph3.py \
  --front /tmp/mobile-128-front.png \
  --extra "The short teal hood covers the back of his head with no cape." \
  --out local/art/generated/studies/people-concept/128-back
```

Submit the back candidates to the same [CMS concept stage](../../../../cms/README.md#concept-art) and select
**Approve as back**. Retrieve it with `concept_cli.py approved mobile-128 --view back`. The same single-stage MiniMax H3 back-view graph is also exported as a 1-click browser workflow in [`ui-workflows/rusty-concept-back-ph3.json`](ui-workflows/rusty-concept-back-ph3.json) (and saved in the `gpu-lab` ComfyUI sidebar).

## Earlier History (2026-11-08)

- Crew thread `den-01M6ZZ734GS6PM7AM72KF87451`, section 3 (`mobile-30` Vampire Ancient, `mobile-140` Monk, `mobile-1` Imp, `mobile-2` Spriggan).
- Original combined 2-stage workflow: `live-evidence/humanoid-concept-matrix-20261108/stage4-brom-elm-ink/workflow-new-b-sprite-to-concept-2stage.json`.
- Enemy `128` validation & approved front (`SB2-s202-d38`): `outputs/dagger-characters/128/01-concept/candidates/sprite-anchored-portrait/`.
