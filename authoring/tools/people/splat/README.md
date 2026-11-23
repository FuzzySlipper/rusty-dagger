# Splat people: the bound route

How a Dagger character with an approved concept becomes a rigged Gaussian-splat figure for the game. Townspeople who walk
and static NPCs who stand in one place both go this way. The finished body is an Engine skinned-splat GLB, listed in
`ready.json`, which the authoring CMS shows as the character's final and `../splat-people.py` packs for the game.

These tools came from asset-pipeline on 2026-11-22 (Patch: one place, less sprawl, a clearer process). Their history and
measurements stay in asset-pipeline's `docs/splat-scaffold.md`. This page is the process.

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
$S bake npc-023; $S bound npc-023; $S publish npc-023
```

Settings live in `00-brief/npc.json`:

| Key | What it does |
| --- | --- |
| `view_yaw` | The paintings' angle, measured from the outlines when not set. |
| `hands` | `front` (the default) when the hands are held in front, as on a staff; `sides` when the arms hang. |
| `head_axis` | The head's turning axis, for a far-forward head. |
| `loose`, `loose_factor`, `shell_m` | How much room the bound bake gives the scaffold. |
| `sizes` | The runtime sizes to distil. |
| `front_only` | No back painting. The orbit's back is H3's own. Patch prefers this to drift from an angled back painting. |

After Patch looks at the published set in the Workbench, choose the size with his words:

```sh
authoring/tools/people/splat/character/dagger-char bound npc-023 --step choose --size 32k-face-share-cleaned --patch-said "..."
```

That writes `ID.glb` and the static NPC's own clips, `ID-idle.glb` and `ID-look.glb`, and lists the character in
`ready.json` as `static`. Link it to its CMS record with **Link pipeline**, or:

```sh
curl -X POST -H 'Content-Type: application/json' -d '{"addPipeline":"npc-023"}' http://192.168.1.10:8784/api/characters/npc-023/links
```

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
