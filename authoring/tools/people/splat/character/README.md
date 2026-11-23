# dagger-character: the dagger character factory

The route a character takes through these stages is documented on the wiki: [Rusty Dagger splat characters](https://wiki.dragonden.stream/wiki/rusty-dagger/splat-characters).

One folder per character, one numbered folder per stage, one status file, one page. Patch (2026-11-15): "each character has a directory,
source images, wip raw assets, prompts, approved concept, approved video, etc all the way to final splat, final splat bake ... a static
page with a script refresh showing the roughly 8 steps".

- Root: `config.json` `root` (default `local/art/generated/people/dagger-characters` in this repository, git-ignored; Patch browses it over his share of
  den-agents' home). `DAGGER_CHAR_ROOT` overrides. Open `ROOT/index.html` in a browser; it needs no server.

**Which characters are ready for game testing:** `ROOT/ready.json` (written with the page) and the "Ready for game testing" box
at the top of `ROOT/index.html`. A character is ready when its 09-game stage is approved, which means Patch passed its playable set
in the Workbench, recorded with his words by `dagger-char bound ID --step choose --size NAME --patch-said TEXT`. Each entry
names the runtime body and its folder `ID/09-game/bound/final/` (splat.npz, binding.npz, the .vsplat, and `character.json` naming
the body, rig and clips). `not_ready` lists every other character and the furthest stage with work in it (Patch 2026-11-19).
`engine_glb` is the same body as the Engine's skinned splat GLB (`ID/09-game/bound/final/ID.glb`, written by
`dagger-char bound ID --step glb` and by every `choose`): `authoring/tools/people/splat/scaffold/bound_to_glb.py` maps the scaffold's 36 joints
onto the 22 Mixamo body bones the Engine already admits for the free bake (fingers merge into the hands), so Mixamo clips play
by name. Beside it: `ID.glb.json` (vsplat_to_glb.py's checks) and `ID-clip-fit.json` (PINOC's Mixamo Walking and Idle). Fixed 2026-11-19: the right toe stood 1-7 cm off the floor in the Mixamo walk on 11 of the 12 because PINOC's walking.glb
carried float noise (up to 5.6e-6) on its still bone translations, over the Engine's 1e-6, so the Engine gave some leg bones
X Bot's lengths; walking.glb is now cleaned (`authoring/tools/people/splat/scaffold/clean_clip_noise.py`, the original kept as walking-raw.glb)
and both feet plant. A new Mixamo clip should go through the same cleaner before the Engine plays it on a bound body.
- Status: `ROOT/ID/character.json`, changed only through this tool. Per stage it holds the state (empty, running, failed, needs-check,
  needs-Patch, approved, rejected, on-hold, skipped), the attempts and the selected one(s), inputs and outputs with hashes (a changed
  input makes the stage stale), check numbers, Patch's marks and the Workbench batch, who approved and when, the reason a stage is not
  approved, provenance, and the history.
- Ids are archive-record-variant (`383-A`); `name` is a short description from the describing text (`dagger-char name ID TEXT`).
- Recipe: `bound-scaffold` is the default (Patch, 2026-11-15: "I'm pretty happy with this workflow so the bound bake can now become
  the default"): the same 2176² pinned FL2VA clip, cut-outs, cameras, first fit and VACE repaint round, then the full-size bake is
  held to a rigged clay scaffold made from the approved paintings, and the runtime body is distilled from it - 16,000 Gaussians with
  a quarter of the seed on the face unless another size is chosen (`bound.py` `FINISH`). `accepted-2176-repaint` is the free bake
  the earlier characters were made with (their label stays); `experimental-sweep-head` is the Blender-guided sweep, an alternative
  clip route.
- Patch's gates: 01-concept, 03-clip, 08-final (his look before 09-game, "at least until we've got things on a steady roll").
  04-cutouts is a glance.

## Commands

    dagger-char init ID [--name NAME]
    dagger-char status [ID]                    (warns under 100 GB free)
    dagger-char set ID STAGE STATE [--note T] [--why T]
    dagger-char name ID TEXT | recipe ID RECIPE
    dagger-char adopt [ID ...]                 existing work from live-evidence/, by hard link
    dagger-char review ID 08-final             levelled review sheets on the lab + one Workbench batch, recorded
    dagger-char approve ID STAGE [--by WHO]    a gate takes Patch's marks from its recorded batch (any Bad -> rejected, a Good ->
                                               approved by him with batch and date, else still needs-Patch); refuses a gate with no batch
    dagger-char bound ID [--step adopt|playable|publish|choose --size NAME --patch-said TEXT]
                                               the bound-scaffold route: file a scaffold run under the stages with roles (adopt, the
                                               default), write the playable sets, publish them to the Workbench, record the size
                                               that is the runtime body (it becomes the final, the others alternates)
    dagger-char marks [ID ...]                 re-read Patch's marks on the shared village-fronts batch for characters still at the
                                               concept: Good -> the refined front is approved (the back is made when the character
                                               starts), Bad -> rejected with his note
    dagger-char page | refresh                 the overview and character pages; fails on any broken image or video

## Routes and roles

Patch (2026-11-15): "I do lose track sometimes of what are intermediate and finals ... we need to use the structure established in
outputs\dagger-characters\ to try to keep track of everything a bit better since that ends up being a problem for splat characters
with all their infinite steps and options."

- The bound route's files sit in a named folder of the stage they belong to. A character baked free before it keeps those files
  beside them; on a bound character's page the free bake's items, working files and superseded attempts fold away, and the
  stages' states are the bound route's (02 Scaffold, 08 Final bake, 09 Game).
- The bound route (`bound.py`, [docs/splat-scaffold.md](../../docs/splat-scaffold.md)): `02-guide/scaffold/` (the rigged clay
  scaffold), `08-final/bound/` (the full-size bound bake, `playable/`), `09-game/bound/` (`final/` - the runtime body, `sizes/NAME/`, `clips/`,
  `playable/`, `scores.json`, `review/`). `bound.py` `RUNS` lists the run folder and files per character; townsman 385 is the only one so far.
- Every stage has a ledger of items (`dc.record_item`), each with a role, and the character page shows it as "What is here":
  **final** (what the game or the next stage uses), **alternate** (a finished option that was not chosen; can be switched to),
  **candidate** (a finished option awaiting a choice),
  **source** (a full-size result others are made from; kept, not a game file), **evidence** (sheets, videos, scores),
  **intermediate** (working files), **superseded** (replaced by a later attempt). Working and superseded files stay in the run
  folder and are listed by path. New stage tools should record their items the same way.
- A size is a runtime body as soon as it is distilled to its count and bound to the rig; `playable` and any game export change
  its file format, not its Gaussians. `bound --step choose` records the choice (385: 16k face share, Patch 2026-11-15). The Engine's skinned-splat GLB of a bound
  body is not written by this tool yet.

## What each stage wraps

The tool runs the existing scripts; it does not rewrite them. Paths are in this repository.

| stage | existing scripts | check (with its known case) |
| --- | --- | --- |
| 00-brief | recorded graph from `live-evidence/townsperson-all24-ab-20261108/new-b/ID/`; describing texts (`clothing.txt`, `clothing-back.txt`, `back-prompt.txt`) | - |
| 01-concept | New B text-to-image (the batch's `t2i.py`, from `newb_t2i.py`), Qwen back `authoring/tools/people/splat/image/qwen_edit.py`, New B `--back` (`i2i.py`); keyframes `authoring/tools/people/splat/mesh-splat/flood_cut.py --equal-height`; pair check `authoring/tools/people/splat/mesh-splat/pair_mirror.py`, `pair_hang.py` | pair_mirror arm/leg numbers; Patch's mark |
| 02-guide | `tools/blender_path.py` through `halo_guide.sh` (h3-orbit-control run folder; experimental route only) | frame counts of clay / depth / shaded |
| 03-clip | `authoring/tools/people/splat/bake/orbit.sh` (FL2VA pinned, accepted recipe); `ref2va_orbit.py` (experimental route); `clipcheck.sh` (stability, legmotion, legs, hips, bgclean, turn_profile, pair_mirror); `review.sh` (frame-counted review video) | frame count of every video; clipcheck numbers; Patch's mark |
| 04-cutouts | `bake.sh mattes`: `birefnet_masks.py` (Halos' ComfyUI) + `orbit_views.py --birefnet` + `orbit_inputs.py`; `matte_review.py` | matte review: edge, area jumps, flips; the 23-gap / sash / 384-C stripe known cases |
| 05-cameras | `bake.sh cams` (Solv3R, lab), `bake.sh fuse` (`fuse.py --up figure`, `loop_data.py`); `clip_agree.py` for several clips | cloud IoU; clip_agree even/odd floor |
| 06-first-fit | `bake.sh r0` (`train_loop_splat.py`), `bake.sh resid` (`residual_by_view.py`), `level_splat.py` | held-out subject PSNR; view band |
| 07-refine | `bake.sh rings refs vace match r1data r1` (VACE repaint, `repaint_match.py`, ignore masks) | match flags; r1 scores |
| 08-final | `bake.sh soles final` (`cap_splat.py`, `splat_to_cloud.py`), `level_splat.py`; `review.py` (levelled sheets, Workbench batch) | frame lean; Patch's mark |
| 09-game | `game.py` (level, distil on the lab), `rig.py` (rig, checks, glbs, evidence; `sheet.py` centred tiles), `review.py` (faces per tier, Workbench batch with known faults); extra evidence: `armsdown.py` / `arm_angles.py` (upper-arm angles), `mocap.py` (a Mixamo FBX clip, retargeted, on the Mixamo-rig GLB, shown in the page as layers) | hem check, clip fit, shoe track, weights, GLB validator, frame counts; known cases of each extra path |

Guards every run carries (from 2026-11-14/15): the staged-tool hash check in `bake.sh` (re-stage, or refuse under `STRICT_TOOLS=1`),
`train_loop_splat.py` refusing views.json fields it does not read, frame counts on every video, `watchdog.sh` on every chain, the lab
lock and `asset-resources run`, planned lab ComfyUI restarts (`lab-comfy-restart.sh`), `vram_fast.sh`'s lab-side timeout.

## Halo orbit batch from approved fronts (2026-11-17)

Patch asked for clips for the villagers with approved fronts and no existing orbit. The batch
`live-evidence/dagger-halo-orbits-20261117-r01/` has 14 villagers in `plan.json`. It skips the
seven approved orbits and the existing candidates for 384-C and 390-B. Patch approved villager 455's
new front, and den-m5 rendered her clip after 453. [455 still approval](http://192.168.1.10:8960/reviews/review-7d41319f58a4)

Each Halo worker sends one 1920², 73-frame, four-step H3 REF2VA Turbo prompt at a time through
`authoring/tools/people/splat/scaffold/ref2va_orbit.py`. It passes the approved front unchanged as the sole appearance
reference and reuses an existing lit stick guide and its depth video at strength 0.5. The guide
supplies camera and lighting. H3 infers the unseen back from the front and the costume description.

The worker checks the current stage and approved front before submitting each job. It saves the native
frames, exact graph and prompt in `03-clip/candidates/` along with a 24 fps review MP4, records the
attempt in the character page, then moves to the next villager. All 15 clips finished and are
filed in their character folders. Each has 73 native frames and a 1920² review MP4 at 24 fps.
Patch marked all 15 orbits Good in the shared review. Each clip is filed in its character's
`03-clip/approved/orbit/` and shown as approved in the index. [Workbench review](http://192.168.1.10:8960/reviews/review-104976994ee4)

Patch requested fresh orbits for 384-C and 390-B. The old candidate MP4s were deleted from their
character folders. The new jobs use their approved front and back paintings on den-m5, with
384-C first and 390-B second. The run plan is in `live-evidence/dagger-halo-retakes-20261117-r01/`.

## Open: Mixamo-rig GLBs and X Bot clips (2026-11-15)

The Mixamo-rig GLBs that `vsplat_to_glb.py --rig mixamo` writes (09-game/game/mixamo/, and townsman 385's) do not carry X Bot's bind
frames: each joint's axes come from the character's own joints, and the arms are bound in the character's A-pose (about 37 deg out)
against X Bot's T. Measured on 386-C against `legacy/experiments/animations/mixamo/X Bot.fbx`: neck and head 26 deg off, spine 14,
feet 15, shoulders 8-15, arms 53-60. Playing an X Bot clip by its joint rotations (glTF skinning by name, as the GLB checks and the
Engine do) therefore throws his head back about 25 deg and pushes the chest out. `mocap.py` retargets in its offline posing instead:
X Bot's change from its own bind, applied to our bind (the arm chain first swung to X Bot's T bone directions). Whether the Engine
route should bake that retarget into converted clips or re-bind the GLBs to X Bot's frames is open, and has to be checked against
PINOC's mixamo-glb clips too (the GLBs were framed on PINOC's mixamo Walking). Neither the writer nor the published-format GLBs are
changed until the lead decides.

## Not yet

`run` and `check` per stage (run wraps the scripts above with the guards and records attempts, inputs and outputs). Concept and clip
review batches come with `run`.
