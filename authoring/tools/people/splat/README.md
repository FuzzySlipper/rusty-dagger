# Splat people: the bound route

How a Dagger character with an approved concept becomes a rigged Gaussian-splat figure for the game. Townspeople who walk
and static NPCs who stand in one place both go this way. The finished body is an Engine skinned-splat GLB, listed in
`ready.json`, which the authoring CMS shows as the character's final and `../splat-people.py` packs for the game.

These tools came from asset-pipeline on 2026-11-22 (Patch: one place, less sprawl, a clearer process). Their history and
measurements stay in asset-pipeline's `docs/splat-scaffold.md`. This page is the process.

Only the settled route lives here. New or experimental splat work is tried in asset-pipeline (Patch 2026-11-22), and a step
moves here once it is accepted and runs as a named script, with this page updated in the same change. Attempt scripts never
go in this folder.

```
CMS concept (front, back optional)  ──keys, clay, Meshy mesh──▶  02-guide/scaffold/scaffold.glb (rigged clay)
            │                                                          │
            └──stick-figure guide + H3 REF2VA orbit clip──▶  frames ──▶ bake (cut-outs, cameras, round 0, repaint rings, r1 data)
                                                                       │
                       bound bake held to the scaffold, distilled to the runtime size ──▶ 09-game/bound/final/ID.glb
                                                                       │
                     Workbench playable set (Patch's look) ──▶ choose (his words) ──▶ ready.json, CMS final, splat-people.py
```

## Where things are

| Folder | What |
| --- | --- |
| `static-npc/` | `static-npc STEP ID`: the whole route for a figure that never walks, one named step at a time |
| `character/` | `dagger-char`: the character's stage folders, its record (`character.json`), pages, `ready.json`; `bound.py` adopt, playable, publish, choose, glb |
| `scaffold/` | the rigged clay scaffold, the stick-figure orbit guide and clip (`guided_clip.sh`), the bound bake (`bound_run.sh`), rigs and clips (`static_rig.py`, `static_idle.py`, `static_clips.py`, `skirt_bones.py`), the Engine GLB (`bound_to_glb.py`), checks (`face_seethrough.py`) |
| `bake/` | `bake.sh` and `pipeline.sh`: a bake from the orbit clip, stage by stage, on the GPU lab and a Halo |
| `mesh-splat/`, `solv3r/`, `gsplat/` | the bake's own tools: cut-outs and mattes, camera solve and fuse, the splat trainer, VACE rings, the `.vsplat` and GLB writers |
| `mesh/`, `image/` | Meshy multi-image-to-3D; Qwen image edit (the clay) |
| `hosts/` | running steps on the Strix Halos: `strix-cpu/` (CPU steps and Blender), `strix-gpu/` (ROCm gsplat) |

Every character's files live in `local/art/generated/people/dagger-characters/ID/`, the tree the CMS reads:
- `00-brief` through `09-game`, the stage folders;
- `work/`, the bake's run folder (`work/NNN/A`);
- the record, `character.json`.

Characters are named by their CMS id, `npc-NNN` (Patch 2026-11-22). The run inside `work/` keeps the bare number, because
the bake scripts read a run id as `NNN-X`. The villagers baked before the move keep their numbers, and their run folders
stay in asset-pipeline's `live-evidence/`.

## Run a static NPC

From the Rusty Dagger root, one step at a time. Each step checks that the one before it has run.

```sh
S=authoring/tools/people/splat/static-npc/static-npc
$S init npc-023            # the approved front (and back, if the CMS has one) and npc.json; edit 00-brief/*.txt, rings.json
$S keys npc-023; $S clay npc-023; $S mesh npc-023; $S rig npc-023
$S guide npc-023           # look at a few guide frames: H3 poses the arms by the stick figure
$S clip npc-023            # look at a few clip frames yourself; no review gate (Patch 2026-11-22)
$S bake npc-023 --to r1    # --to r1 keeps the free round-1 fit for the graft (or $S r1 npc-023 after a plain bake)
$S bound npc-023
$S graft npc-023           # the free fit grafted on the face and hands: size 32k-graft beside 32k-face-share-cleaned
$S publish npc-023         # Patch looks at both sizes in the Workbench
$S ship npc-023 --size 32k-graft --said "Patch's words"   # final, Engine GLB with eyes and clips, inset core, page, CMS link
```

Patch shipped npc-023 grafted (2026-11-23), and npc-024 has a grafted size waiting for his look: the bound bake blurs where Meshy's clay is a
poor stand-in (mitten hands, a too-thick hooded head), and the free fit is sharp there. Skip `r1` and `graft` for a figure
whose clay is close. The core's size is `core_tris` in npc.json (2,400 as on npc-023; rusty-engine measured CPU skinning at
about 40 us a figure a frame at 2,400 and 14 us at 800; Patch is comparing the two on npc-024). The core comes beside the
Engine GLB as `ID.core.glb`, never in its place, and ready.json lists it as `core_glb`.

### A walker

`static-npc` also takes a figure that walks (npc-468, 2026-11-23: Patch asked for the animated NPC process as solid scripts):

```sh
$S init npc-468 --walker [--from DIR]   # --from: front.png/back.png given outside the CMS approval
$S keys npc-468; $S clay npc-468; $S mesh npc-468
$S rig npc-468             # Meshy's remesh + API rig (10 credits) and WAM r05 donor hands at the cuffs; check 02-guide/wip/check
$S guide npc-468; $S clip npc-468
$S bake npc-468 --to r1; $S bound npc-468; $S graft npc-468
$S walk npc-468            # skirt bones for a robe (npc.json "skirt": true), Mixamo Walking and Around, tidy, review stills
$S publish npc-468         # sizes 16k-tidy and 16k-graft-tidy, the robe on its skirt rig, the gentler walk for a long skirt
$S ship npc-468 --size 16k-graft-tidy --said "..."   # Engine GLB on the 22 Mixamo joints (+ skirt joints), clip fit, core, CMS link
```

Two walker settings npc-468 needed:
- `"skirt_opts": "--solid 3 --inside-legs"` for a robe that ends above the ankle. The skirt step drops the legs inside the
  robe's bell (on skirt bones they rode the cloth). For a mid-shin robe, that made the shin vanish when a stride swung it out
  in front, so the shoe floated. `--inside-legs` keeps the ones coloured like the legs below the hem on their leg bones; the rest, the robe's own inner
  cloth, is still dropped (kept on the legs, it swung out through the robe as pale haze).
- `clean_opts` for a teal or green hood, as for green hair. The bound step now warns when the face or crown rule takes much.

With a `backdrop`, the bound step's clean also drops Gaussians coloured like the key (`clean_bound.py --key`): 4,700 magenta
specks inside npc-468's robe, 1.2% of the 400k.

A walker binds as the villagers did: 2.5 cm of shell, 16k for a crowd, with the hands and a hooded head loose at 2.4 times
the shell. The rig step needs a Meshy mesh task: a Hunyuan3D-2mv mesh has no API rig here. Use the back painting when the
pair is solid: same pose, items on the right sides seen from behind, outlines that agree. A back that drifts from the front
makes H3 morph during the orbit, and front-only is better then (Patch 2026-11-23).

For cards beside the Gaussians (an experiment, in asset-pipeline): `tools/bbsplat-gsplat/character_cards.py ID` bakes 6,144 cards
from the walked full bake on the GPU lab, binds each card as the Gaussians around it are bound (skirt bones, loose hands), and
publishes a Workbench set with the playable clips.

Settings live in `00-brief/npc.json`:

| Key | What it does |
| --- | --- |
| `view_yaw` | The paintings' angle, measured from the outlines when not set. With one painting the measure can be fooled: npc-024's ragged hem gave -35 degrees for a square-on front (outline IoU 0.763 against 0.742 at 0), so compare the mesh's outline at 0 before the guide and set 0 when the painting is square. |
| `backdrop` | `"R,G,B"`: a chroma-key clip. The keyframes and the lit guide sit on that colour (say it in orbit.txt too), and the bake's mattes open every enclosed patch near it and take its spill off the edge (`key_holes`, default 60; `despill`, default 2 px). Pick the colour furthest from the painting's palette in OKLab. npc-024 used magenta (his hair is green); H3 drew it as a flat (247,94,184), spread about 2, and the cuts opened between claws and hem tatters with no pink rim. |
| `clean_opts` | Options for `clean_bound.py`: `"--no-face --no-crown"` for green hair or a crown. Its face rule removes greenish Gaussians on the head and its crown rule recolours the top of the head to the hair, both made for 381's strays (npc-024: they took his hair and his gold crown). |
| `hands` | `front` (the default) when the hands are held in front, as on a staff; `sides` when the arms hang. |
| `head_axis` | The head's turning axis, for a far-forward head. |
| `loose`, `loose_factor`, `shell_m` | How much room the bound bake gives the scaffold. |
| `sizes` | The runtime sizes to distil. |
| `front_only` | No back painting. The orbit's back is H3's own. Patch prefers this to drift from an angled back painting. |

`ship` runs `dagger-char bound ID --step choose --size NAME --patch-said "..."`, which writes `ID.glb` (with the eye mask
when `character/eye_picks.json` has the character's eye centres) and the static NPC's own clips, `ID-idle.glb` and
`ID-look.glb`, and lists the character in `ready.json` as `static`. It then runs `scaffold/core_extract.py` for the core
and `dagger-char page`, and posts the CMS link (`{"addPipeline": ID}` to `/api/characters/ID/links`). Commit
`authoring/cms/links.json` afterwards.

Eye picks are by hand: `scaffold/eye_mask.py ID.glb OUT.glb REPORT.png` runs the search, which often finds brow shadow
instead. The report crops the 512 px render to x 128-384, around the eyes' mean height, at twice the size, so convert from
report pixels before writing `--eyes` and the picks (npc-023, 2026-11-23). A bare skull (npc-024) gets none.

## Hosts and shared resources

- **GPU lab (the 5090):** training, VACE and the H3 clip run there. The bake stages its trainer and tools to
  `/workspace/RUN/tools` under `/tmp/gpu-lab-claude.lock`.
- **Strix Halos (den-m5, den-nimo):** the bake's CPU stages run there. Run `hosts/strix-cpu/setup.sh den-m5 den-nimo` once
  after the tools change. It syncs them to `~/.local/state/asset-cpu/rusty-dagger`, apart from asset-pipeline's copy.
- **Before heavy work:** run `art-tools status`, and wrap long jobs in `asset-resources run`.
- **Long steps** (`clip`, `bake`, `bound`): run them as background jobs. Never edit a script a running chain is reading:
  write a new file and move it into place.
- **Things that stay in asset-pipeline** (`ASSET_PIPELINE` overrides `~/dev/asset-pipeline`):
  - the Workbench gallery and its importer (publish);
  - `.api.env`, which holds `MESHY_TOKEN`;
  - the older runs' `live-evidence/`;
  - X Bot;
  - `tools/fast-bound` (the Hunyuan3D-2mv mesh fallback on a Meshy content block).

## Checks worth keeping

- **The rig:** `head_zone_on_head` should be near 1.0. Lower means the face tears in the look clip (017).
- **Before publishing:** run `face_seethrough.py --box whole --yaw N --png`. It finds see-through patches on the face and
  the robe.
- **`ready.json`:** `dagger-char page` rebuilds it with each character's `eyes` (blink mask) and `static` flags. Check the
  diff before committing anything that depends on it.
