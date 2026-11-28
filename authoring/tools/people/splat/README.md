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

npc-468's second round (Patch 2026-11-23: the legs disconnect at speed, goofy hands, a staff and staff clips):
- `$S hands npc-468` after `graft`: turn_hands.py re-rests painted open hands along the forearm, thumb forward. The retarget's
  hands follow their forearm, so a palm-forward painted hand stayed palm-forward through every clip.
- `"skirt_opts": "--solid 3 --fill-legs --fill-top 0.10"`: the bake never saw the shins under a robe, so legging rings are
  added from the shoe to 10 cm above the hem, in the colour and at the radius of the legging seen below it. Higher up, a robe
  front stretched thin at full stride shows them through.
- Walkers take the full walk; `"walk_clip": "gentle"` brings back the villagers' softer one for a long skirt.
- `"extra_clips": {name: [clip JSON, Mixamo GLB]}`: a character's own motions in its playable set and Engine clip fit. 468's
  staff walk, strike and cast are PINOC generations (`fbx_clip_dump.mjs` for the JSON); the GLBs stay on the NAS
  (`pinoc-clips/mixamo/staff-468`). `splat-people.py` does not pack them for the game yet.
- A held prop is an experiment in asset-pipeline `tools/splat-prop/`: `prop_splat.py` paints it from its concept onto a Meshy
  shape, `attach_prop.py` puts it in the re-rested hand along the thumb as size NAME-prop, which publish and ship take.

npc-468's third round (Patch 2026-11-25, r02 in the Workbench: the bag rides his hand, the staff is too thick and loosely held,
the PINOC staff walk turns the feet and doesn't loop). These are still experiments in asset-pipeline:
- **Staff walk:** `tools/splat-rig/layer_clip.py WALK STAFF-CLIP OUT`, once for the clip JSON and once for the Mixamo GLB. It
  takes the known walk and holds the right arm at the staff clip's average pose, with 0.6 at the shoulder and 20% of the
  walk's own swing. This is rusty-engine's joint-weighted clip layer (`AnimationLayerSet.Override`) baked into one clip,
  because the Workbench plays one clip at a time. The game could layer it live instead.
- **Bag on the hand:** `tools/splat-rig/hand_islands.py BODY RIG TRANSFORM` on the run's sizes and full bake, before
  `dagger-char bound --step adopt`. A relaxed hand hangs beside the belt, and its binding took a pouch strip, 208 Gaussians in
  the 16k body. Any part of a hand's binding that isn't connected to the hand itself takes its neighbour's binding.
- **Staff:** `tools/splat-prop/prop_mesh.py ... --girth 0.5 --grip-from BODY --transform T`. The shaft goes through the middle
  of the bake's hand Gaussians. The old X Bot palm guess only fits hands that turn_hands re-rested.

Weapons painted in hand, from four-view sheets (npc-471 sword, npc-467 staff; Patch 2026-11-25: "weapons on characters should
become the new normal"). This is the standard for armed characters. The weapon is in the paintings, carved into the clay mesh
and baked with the hand, so there is no prop to fit (the separate held-prop route above is for when a character must drop or swap it):
- **Four views.** `static-npc init npc-NNN --walker --from DIR` with `front.png`, `back.png`, `right.png` and `left.png` (her
  right and left; the CMS's `turnaround-4view-*` candidates) sets npc.json `"sides"`. Keys, clay (`clay-right.txt`,
  `clay-left.txt`), Meshy (four images) and the orbit (`<Picture 3>` right, `<Picture 4>` left; name them in orbit.txt) all
  take them. Pinning them as guide frames (`"side_guide": true`: right at 18, left at 54) made no visible difference on
  npc-471, so it is off by default. At full size four references don't fit the 5090 at 1920 px, so the clip scales them to
  1280 px (`REF_SIZE`, npc.json `ref_size`): 9 min on the GPU lab, peak 31.7 GB, no visible loss against full-size references
  on a Halo (2 h 51 min; npc-467, 2026-11-25). The CMS views are 832x1216, so 1280 carries all their detail. A three-view sheet (`side.png`, one side) worked too, but the sword swung through the
  front frames where the front painting drew it pointing down.
- **Clay of a foreshortened weapon.** Qwen draws a sword that points at the viewer as lying sideways in the front and back
  clay. Say in clay-front/back.txt that only the fist, guard and pommel show, then check: the back clay still needed the blade
  masked out (every pixel outside the painting's own outline, dilated 8 px, set to the background).
- **Rig.** A Meshy rig weighted npc-471's blade wholly to the hand. When Meshy refuses the clay (a content block: npc-467),
  the walker rig now falls back to `fit_rig.py`, and npc.json `"rigid_prop": "Right"` passes `--rigid-prop`: the staff is left
  out of the arm trace (which otherwise ran down the staff and put the hand at mid-thigh), the arm is set by its grip, and
  every vertex of the staff is bound wholly to that hand.
- **Walk.** The retarget swings a painted bent arm onto X Bot's arm direction, which turned npc-471's blade up across her
  chest. `layer_clip.py CLIP.json - OUT.json --hold rig --rig SCAFFOLD.glb --weights 'RightShoulder=0.6,RightArm=1,RightForeArm=1,RightHand=1'`
  gives the held bones the X Bot rotations that the retarget turns into the chest's own turn, so the weapon arm rides the
  chest exactly as painted. Do walking, around and idle into `09-game/wip/clips-held/` and set npc.json `"clips_dir"`: the
  walk step and the playable sets then use them.
- **Bake.** A weapon held out in front moves the bake's centre forward, so the face region now uses `bone_region.py --front
  auto` (the head's own median depth; with 0, npc-471's whole head sat behind it). The rings draw an overhead blade long:
  that is foreshortening of the body, not an error.

Two walker settings npc-468 needed:
- `"skirt_opts": "--solid 3 --inside-legs"` for a robe that ends above the ankle. The skirt step drops the legs inside the
  robe's bell (on skirt bones they rode the cloth). For a mid-shin robe, that made the shin vanish when a stride swung it out
  in front, so the shoe floated. `--inside-legs` keeps the ones coloured like the legs below the hem, and only within `--inside-band` (12 cm) above it,
  on their leg bones. The rest is still dropped: kept on the legs, the robe's inner cloth swung out through the robe as pale
  haze, and its dark folds higher up stuck out at the knee as shards (npc-468's two failed tries).
- `clean_opts` for a teal or green hood, as for green hair. The bound step now warns when the face or crown rule takes much.

A long cape or cloak behind the legs: `"skirt": true, "skirt_bones": 6, "skirt_rig_opts": "--cloak"` (npc-472, 2026-11-26,
the first long cape that walks). `skirt_bones.py --cloak` takes the Gaussians below the hips, behind the body and 2 cm clear
of the leg capsules, and hangs them on an open XPBD sheet over the cloak's own arc: bone columns at both edges, no wrap, and
nothing dropped as inside. 472's walk keeps the cloak whole, opens it at the stride with the boots under the hem, and no leg
comes through, at 400k and at 16k. The cape-layer route (bake bare, cut the cape) is no longer needed for a cape that hangs
behind; npc-461 and npc-462 use this too.

With a `backdrop`, the bound step's clean also drops Gaussians coloured like the key (`clean_bound.py --key`): 4,700 magenta
specks inside npc-468's robe, 1.2% of the 400k.

A walker binds as the villagers did: 2.5 cm of shell, 16k for a crowd, with the hands and a hooded head loose at 2.4 times
the shell. Without a Meshy mesh task (a Hunyuan3D-2mv mesh after a content block) the rig step fits the target skeleton
instead (`fit_rig.py`). Use the back painting when the
pair is solid: same pose, items on the right sides seen from behind, outlines that agree. A back that drifts from the front
makes H3 morph during the orbit, and front-only is better then (Patch 2026-11-23).

For cards beside the Gaussians (an experiment, in asset-pipeline): `tools/bbsplat-gsplat/character_cards.py ID` bakes 6,144 cards
from the walked full bake on the GPU lab, binds each card as the Gaussians around it are bound (skirt bones, loose hands), and
publishes a Workbench set with the playable clips. For the Engine (Patch 2026-11-26, all 15 shipped enemies), after `ship`:
`python3 -m bbsplat_gsplat.export_glb ATLAS final/ID.glb final/ID-cards.glb --binding ATLAS/binding.npz --rig playable/scaffold.glb`
(and again with `--core final/ID.core.glb` for `ID-cards.core.glb`). `--binding` puts the cards on the same rebinding as the
Workbench set, skirt joints by name; without it they take the scaffold mesh's skin and a cloak follows the legs. ready.json
lists them as `cards_glb` and `cards_core_glb`. The card bake's face seed uses `bone_region.py --front auto` too (ten of the
fifteen enemies hold a weapon in front, and `--front 0` found no face).

### Repairs without a rebake

Edits to a finished bound body (Patch 2026-11-26). They change the run's body folders in `work/NNN/A/bound/`: find the one a
published size is hard-linked from by inode (`find work -inum $(stat -c %i 09-game/bound/sizes/SIZE/splat.npz)`), write the
fix to a new folder, keep the original in `bound/pre-rigid` or `bound/pre-blade`, move the fix into place, then `publish` (and
`ship` again if shipped). The tools are in asset-pipeline:
- **A weapon that wiggles in the walk** (399, 459, 460, 469): Meshy's rig traced the arm along the haft and put the hand bone at
  the weapon's head, so the haft was shared with the forearm, hips and legs. `tools/splat-prop/rigid_weapon.py BODY OUT --rig
  RIG --transform T --arm-z Z --y-range 0.45,1.03 --plot SEL.png` binds every Gaussian of the weapon, and the gripping hand,
  wholly to the hand. Look at `--plot` before using it; `--arm-z` keeps the arm itself out of the box. 470's axe was already
  rigid.
- **A blade that baked short or streaked** (461, 472: painted pointing at the viewer in the front views):
  `tools/splat-prop/blade_patch.py BODY OUT --rig RIG --transform T --base X,Y,Z --dir DX,DY,DZ --length 0.46` clears the stub
  and tiles a flat steel blade of Gaussians on the hand. The colour takes the stub's brightness on a neutral steel tint (472's
  stub held magenta key spill). Patch: good, with some style mismatch; judge in game. 462's own 16k blade is as sparse, so
  transplanting a blade from another character does not help.
- **A staff on a Meshy rig** (458, 468): Meshy gave the lower staff to the nearest leg, so it broke at the knee. The walker rig
  now runs `tools/splat-rig/prop_weights.py` when npc.json has `rigid_prop` (robust shaft axis, 6 cm radius), before the bake.

### Creatures

`static-npc init creature-NNN --creature quadruped|spider|ghost|flyer|biped` (2026-11-26). A creature runs as `cNNN`
(`work/cNNN/A`; `bound.py run_num`) and publishes under its own id. A humanoid creature (orc, zombie, daedra, lich) is a walker
(`--walker`) and goes the whole route above; the spriggan (creature-002) did. For the others the steps are `keys`, `clay`,
`mesh`, `guide`, `clip`, `bake` and `r1` on Colab; the bound route's Mixamo scaffold does not fit them, so binding and motion
are asset-pipeline experiments (`docs/creature-splats.md` there): a Tripo rig from the Tripo site, the round-1 free fit bound
to it after the fact, and motion from Tripo's presets, a retarget, a scripted gait or anymove.
- **Back clay prompts say the face is hidden** ("the face is on the far side: no face, no eyes and no teeth are visible").
  Listing the face made Qwen turn the head round (rat, ghost).
- **`"no_rings": true`** for quadrupeds, spiders and flyers: the repaint rings are human VACE prompts and repainted the
  sabertooth as a sitting tiger (41 of 81 frames dropped at match). r1-data is then the orbit alone.
- **`"key_fill": T`** when the key colour is painted into the figure (the ghost's magenta robe tears, the imp's wing
  membranes): `orbit_views.py --key-fill` greys key-coloured pixels inside the matte.
- **BiRefNet is required:** `static-npc bake` sets `BIREFNET_REQUIRED=1`, and the stage retries once and then stops instead of
  falling back to the colour key (the ghost's rebake lost BiRefNet and kept the pink room).
- Every creature record in the CMS has an approved four-view turnaround (Patch 2026-11-26; creature-010 is being redesigned).
- **Check the side views against the front and back** before using them: on batch 1 (2026-11-27) creature-012's and
  017's sides carried the weapon or the gear differently, so their npc.json drops `sides` and the orbit is front/back only.
- **One-handed weapons in the clay front:** Qwen drew 007's scimitar lying across the thigh. Describe what the painting
  shows, literally ("below the vambrace only the fist, the guard and a short blade tip pointing down beside the right
  thigh show, exactly as painted. No blade crosses in front of the body"), with a negative ("sword lying sideways, blade
  across the body"), and try a few seeds (`02-guide/wip/clay-try/`). Note the weapon in `weapon_note`, leave `rigid_prop`
  out (staffs only), and bind it after `bound` with asset-pipeline `tools/splat-prop/rigid_weapon.py`.
  creature-007 (2026-11-27): the orc is broad, so the default box (`--beyond 0.26`) took the forearm and the outer thigh;
  `--beyond 0.45 --wrist-y 0.84 --arm-clear 2 --y-range 0.55,0.84` took the fist and scimitar alone (check `--plot`). The
  scimitar, held level and pointing forward, still baked as a 5 cm stub (the front views foreshorten it), so `blade_patch.py
  --base -0.58,0.73,-0.21 --dir 0,0.12,1 --length 0.58 --width 0.07 --clear 0.08 --colour 0.62,0.64,0.68` replaced it on every
  body (one fixed `--colour`: left to itself it took 0.31-0.54 grey from each body's stub).
- **Batch 2 (2026-11-27/28: 015 skeletal warrior, 021 orc shaman, 024 orc warlord, 030 vampire ancient, 032 lich).**
  - **Meshy ran out of credits** (402 "Insufficient funds"): 021, 024, 030 and 032 took npc.json `"mesher": "hy2mv"` (local
    Hunyuan3D-2mv) and every one `"rigger": "fit"`. Do not buy credits.
  - **`fit_rig.py` template arm.** An arm the slices cannot follow (015's left behind the shield, 024's right with the axe,
    both of 030's under the cloak) now takes the template's elbow, wrist and fingertips from the fitted shoulder, with a
    WARNING and `fit.json` `"template": true`, instead of stopping. An arm that traces short (015's right stopped at the
    axe, 0.72 of the template) is forced with npc.json `"fit_opts": "--template-arm Right,Left"`. Check the joint chains
    against the clay before the clip.
  - **Staffs (021, 032):** `rigid_prop` plus held clips, as for npc-467: `layer_clip.py` 385's walking/around/idle `--hold rig
    --weights 'RightShoulder=0.6,RightArm=1,RightForeArm=1,RightHand=1'` into `09-game/wip/clips-held`, npc.json `clips_dir`.
  - **Repaint check false alarm (024):** VACE repainted the torn cloak and pitted helmet clean, `repaint_match.py` read that as
    new content and dropped 30 of 81 head-high frames (over 33%: bake stops). npc.json `"match_opts": "--max-ignore 0.5"` (as
    453) and `static-npc bake creature-024 --resume match` dropped none. Look at the control/VACE pairs first: a real
    content change (a different figure) is a reason to stop.
  - **Axes (015, 024):** `rigid_weapon.py` after bound on every body (`bound-cleaned*-tidy`, `16k-face-share-cleaned`,
    `16k-face-share-cleaned*-tidy`, `16k-graft*-tidy`; originals in `bound/pre-rigid`). The default box took the thigh
    and, on 024, the cloak edge: 015 `--beyond 0.45 --wrist-y 1.06`, 024 `--beyond 0.44 --z-range -0.12,0.7`.
- **Stick guides centre on the hips** (`stick_fit.py`) and the guide's head sits over the neck (`blender_path.py`), both
  since 2026-11-27: a big forward-leaning orc (007) was centred on its bounding box, and its head floated 35 cm forward.
- H3 drew batch 1's magenta key as a pale lilac studio with floor shadows (as on 457); BiRefNet cut it clean (007's mattes).

Settings live in `00-brief/npc.json`:

| Key | What it does |
| --- | --- |
| `view_yaw` | The paintings' angle, measured from the outlines when not set. With one painting the measure can be fooled: npc-024's ragged hem gave -35 degrees for a square-on front (outline IoU 0.763 against 0.742 at 0), so compare the mesh's outline at 0 before the guide and set 0 when the painting is square. |
| `backdrop` | `"R,G,B"`: a chroma-key clip. The keyframes and the lit guide sit on that colour (say it in orbit.txt too), and the bake's mattes open every enclosed patch near it and take its spill off the edge (`key_holes`, default 60; `despill`, default 2 px). Pick the colour furthest from the painting's palette in OKLab. npc-024 used magenta (his hair is green); H3 drew it as a flat (247,94,184), spread about 2, and the cuts opened between claws and hem tatters with no pink rim. **Green does not hold:** npc-026's keyframes and guide were bright green and H3 painted a white studio with floor shadows (the mattes still cut her clean, 2026-11-24); use magenta. The clips do not always come back on the key (457 pale grey, 459-461 pinks), so a key-coloured patch now opens only where BiRefNet too sees background: before that, pale skin and armour highlights were opened and baked as holes (457's arms and face, 2026-11-25). |
| `clean_opts` | Options for `clean_bound.py`: `"--no-face --no-crown"` for green hair or a crown. Its face rule removes greenish Gaussians on the head and its crown rule recolours the top of the head to the hair, both made for 381's strays (npc-024: they took his hair and his gold crown). |
| `hands` | `front` (the default) when the hands are held in front, as on a staff; `sides` when the arms hang. |
| `head_axis` | The head's turning axis, for a far-forward head. |
| `loose`, `loose_factor`, `shell_m` | How much room the bound bake gives the scaffold. A bone name takes its descendants; `Hips=` is that bone alone (458's kilt panel below the belt; plain `Hips` is the whole body). |
| `sizes` | The runtime sizes to distil. |
| `front_only` | No back painting. The orbit's back is H3's own. Patch prefers this to drift from an angled back painting. |
| `sides` | `["right", "left"]`: side paintings from a four-view sheet (set by `init --from`). `side_guide` also pins them in the orbit. |
| `rigid_prop` | `Right` or `Left`: a staff painted in that hand, bound wholly to it by `fit_rig.py --rigid-prop` (or, on a Meshy rig, `prop_weights.py`). Staffs only, floor to head: `prop_weights.py` refuses a one-handed weapon (under half its pick on the hand already; creature-007's scimitar, 012's mace) and the rig keeps Meshy's weights; bind such a weapon after `bound` with `rigid_weapon.py`. |
| `clips_dir` | The character's own walking/around/idle clip JSONs (e.g. the weapon arm held as painted, `layer_clip.py --hold rig`). |
| `rigger` | `fit` to fit the target skeleton instead of Meshy's rig (automatic without a Meshy task). |
| `fit_opts` | Extra `fit_rig.py` options, e.g. `"--template-arm Right,Left"` for arms the trace cannot follow (creature-015). |
| `match_opts` | `repaint_match.py` options for the bake's match stage, e.g. `"--max-ignore 0.5"` when VACE cleans up a torn garment (creature-024, 453). |
| `creature` | `quadruped`, `spider`, `ghost`, `flyer` or `biped` (set by `init --creature`): the creature prompts and no walker steps. |
| `no_rings` | Skip the repaint rings, refs, VACE and match: r1-data is the orbit alone (non-human creatures). |
| `key_fill` | Grey key-coloured pixels inside the matte (`orbit_views.py --key-fill T`). |
| `skirt_rig_opts` | Options for `skirt_bones.py rig`: `"--cloak"` for a long cape behind the legs (with `skirt_bones: 6`), `"--slit"` for a skirt that parts at the front. |
| `gpu_host` | Where the bake's GPU stages run: `gpu-lab`, or a Colab ssh alias (`colab-bake`); the Colab lane sets and resets it. |

`ship` runs `dagger-char bound ID --step choose --size NAME --patch-said "..."`, which writes `ID.glb` (with the eye mask
when `character/eye_picks.json` has the character's eye centres) and the static NPC's own clips, `ID-idle.glb` and
`ID-look.glb`, and lists the character in `ready.json` as `static`. It then runs `scaffold/core_extract.py` for the core
and `dagger-char page`, and posts the CMS link (`{"addPipeline": ID}` to `/api/characters/ID/links`). Commit
`authoring/cms/links.json` afterwards.

Eye picks are by hand: `scaffold/eye_mask.py ID.glb OUT.glb REPORT.png` runs the search, which often finds brow shadow
instead. The report crops the 512 px render to x 128-384, around the eyes' mean height, at twice the size, so convert from
report pixels before writing `--eyes` and the picks (npc-023, 2026-11-23). A bare skull (npc-024) gets none.

**Where each character is:** the CMS's character stages page (http://192.168.1.10:8784/#pipelines) reads every character.json, and
`static-npc` keeps those current: each step marks its stage running (step, host, time), then done, on hold with what comes next,
or failed with the error, and publish links the Workbench sets (Patch 2026-11-25). `static-npc sync NPC` rewrites them from what
is on disk, after manual work or for an older character.

## Hosts and shared resources

- **GPU lab (the 5090):** training, VACE and the H3 clip run there. The bake stages its trainer and tools to
  `/workspace/RUN/tools` under `/tmp/gpu-lab-claude.lock`.
- **Strix Halos (den-m5, den-nimo):** the bake's CPU stages run there. Run `hosts/strix-cpu/setup.sh den-m5 den-nimo` once
  after the tools change. It syncs them to `~/.local/state/asset-cpu/rusty-dagger`, apart from asset-pipeline's copy.
- **Colab (Patch's compute units):** the splat bakes (`r1`, `bound`, `graft`, `walk`) run on Colab L4s through the `colab-bake`
  ssh aliases (`hosts/colab/README.md`); ComfyUI work stays on the 5090. Two runtimes at most. After `colab stop -s NAME` run
  `ssh -O exit ALIAS`, or the next lane rides the dead master. A refused allocation right after a stop clears in a few minutes;
  the lane stops on a failed setup instead of baking without tools. About 3.9 units per walker on an L4.
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
