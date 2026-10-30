> Reference copy from asset-pipeline, kept for its measurements and rationale. Paths and commands in it name the asset-pipeline layout; the current workflow is in [item-icons.md](../item-icons.md) and is the one to follow.

# Dagger sprites: retro paint and assisted animation

Status: **measured texture experiment; historical unfinished upper-body rig study**.
Tested 2026-10-17 after the four-view sprite reconstruction. The user accepted
four views as the continuing route; no new front-only controls were generated.

The [mage walk and attack follow-up](dagger-mage-animation.md) repairs the
shoulder weights and contacts, adds lower-body controls, and tests automatic
colored skeleton guides. The ten-bone proof described below is retained as
earlier evidence, not the current animation result.

## Paint on the accepted smooth mage

The mage already has a useful smooth silhouette, so this test keeps its
29,934-triangle geometry. The existing [detail-separated workflow](detail-separated-prop.md)
remains appropriate when the mesh itself has unwanted relief; that character
reconstruction step was not repeated here. The mummy is a stronger candidate
for that later test.

The original four idle sprite crops were registered to actual orthographic
unlit mesh views using authored matching landmarks. Existing `view_paint.py`
performs the warp and projects the edits into the UV atlas with visibility
checks and facing power 8. Optional `sampling: "nearest"` in registration JSON
preserves source colour clusters during registration; default sampling remains
bilinear. PNG alpha excludes background. No image model generates or edits
the references in this paint pass.

The existing material-adoption helper copies only the new colour image into
the original GLB. Its original BIN prefix, triangle/UV/normal/index definitions,
normal/MR/AO maps and sampling settings are retained. Atlas storage stays 2048²
with ordinary filtering; the stepped appearance comes from the artwork.

The result restores the cyan hood stripe, warm sprite accents and structured
teal/black robe shading while the silhouette stays smooth. It is not a finished
seamless texture: the face is dark, inherited painted shading creates strong
dark patches, and some sleeve/back joins remain visible. Fully painted pixels
in all four registered view images stay within their original sprite palette.
The later view blending, atlas projection and runtime filtering mean the final
render is not claimed to preserve a literal indexed palette.

Matched Engine rotations use 120 original frames each, 20 fps, six seconds,
512×640, orthographic height 1.25, and identical camera/light/material settings.
An independent reviewer inspected front/profile/rear samples and neighboring
frames around joins and the loop boundary. The visible joins stayed attached
to the surface in those samples; no popping was found there. This is a bounded
static-turntable observation, not certification at all distances or through
deformation.

- [Retro mage model and comparison video](http://192.168.1.10:8960/#?root=active-evidence&id=import%3Adagger-mage-retro-texture-20261017)
- Local comparison: `live-evidence/dagger-retro-20261017/renders/mage-texture-comparison.mp4`.

## Authored rig in the existing stance

A T pose is not required for an authored bind pose. The proof binds ten named
bones in the imported mage's current stance, retains Blender staff/wrist/elbow
controls and bakes Blender's IK solution into an ordinary skeletal GLB. Existing
Engine bake reviews exact clip samples; no new skeleton evaluator or renderer
was introduced.

The game supplies four move frames and six primary-attack frames per direction.
The experiment extracts them from the normalized atlas with orientation,
mirroring, frame rectangles, FPS and action-sequence metadata. Six manually
authored staff targets roughly follow the attack's diagonal → horizontal →
diagonal phases. They are visual guides, not calibrated 3D measurements or an
automatically recovered performance. The study visits stored frames 0–5 once
at 10 fps (0.6 s), with 13 baked keys at 20 fps. It does **not** reproduce the
game's primary sequence `[0,1,-1,3,2,1,0,-1,5,4,0]`; `-1` is a damage-event
marker, not another pose. That sequence remains in the reference packet.
Legs and lower robe retain their bind
stance, so the result is not a complete recreation of the source action.

**The rig is not animation-ready.** Initial region weights stretched the staff
bulb and sleeves; those runs remain rejected evidence. The final bounded
revision keeps the staff rigid, but still has visible shoulder/sleeve tearing,
ragged surfaces and approximate wrist/hand contact. Independent review confirms
those defects in front, profile and back captures. Successful export and capture
do not establish usable skinning.

The durable deliverable is an inspectable authoring proof: endpoints and region
choices in `rig.json`, native editable controls in
`authoring/mage-staff-controls.blend`, the exported clip, and source-vs-pose sheets.
A schema-valid `MotionPlan` describes the resulting keys; it does not drive
Blender. Its pivots are normalized. The companion rig report records bone-local
frames; the plan itself has no binding entries. There is no general
MotionPlan-to-rig adapter yet.

- [Unfinished staff-animation authoring study](http://192.168.1.10:8960/#?root=active-evidence&id=import%3Adagger-mage-staff-authoring-study-20261017)
- Local comparison: `renders/staff-sweep-study.mp4` in the experiment root,
  explicitly labelled half speed, fixed lower body and unfinished weighting.

## Practical assisted workflow

The useful loop is to select one source action and bind frame, place joint and
prop/contact landmarks in front and profile, edit named bone chains and weight
regions in a native master, solve a small set of pose targets, then inspect exact
Engine samples beside the available sprite directions. Keep shape and texture
fixed across frames. The next concrete gap is better part selection and weight
painting around shoulders, cuffs and props, followed by grip/foot constraints.

Source directions include mirroring and drawings with inconsistent folds or
poses. Treat them as weighted references with authored phase alignment; forcing
every pixel to be an exact projection would produce bad joints or changing bone
lengths. This prototype authors 3D targets directly; it has not built a
2D-landmark-to-IK fitting tool.

For non-bipeds, retain the review loop and author a creature-specific skeleton:
spine/limb/tail chains for quadrupeds, wing chains and membrane topology for bats,
or separate leg chains/contact phases for spiders. The current local MIA helper
predicts a humanoid skeleton and is not that general authoring interface.
Engine can review imported non-biped clips through the existing GLB path; this
study has not rigged or animated a non-biped.

Replay: [specimen README](../legacy/experiments/dagger-retro/README.md).
Review: [three-lane packet](agent-review/dagger-retro/review.md).
Both publications and all 147 members passed deployed catalog readback.
