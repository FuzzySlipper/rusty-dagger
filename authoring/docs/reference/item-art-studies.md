> Reference copy from asset-pipeline, kept for its measurements and rationale. Paths and commands in it name the asset-pipeline layout; the current workflow is in [item-icons.md](../item-icons.md) and is the one to follow.

# Source item art reinterpretation

This is a bounded image experiment for finding a consistent way to redraw tiny
item sprites with newly invented detail. It produces original-sized inputs,
normalised reference canvases, exact expanded prompts, native generated images,
ComfyUI graphs, generation records and labelled comparison sheets. It does not
replace game assets or establish a final art direction.

For the **actual prompt text**, start with the [literal prompt book](item-art-prompts.md).
It includes a complete generated-input example, the long Qwen style contracts
and the named-artist cues. Each prepared study now writes `PROMPTS.md` with
every complete expanded prompt, plus a plain `prompt.txt` beside each image.
Existing studies can export those readable files without rerunning inference:

```bash
python3 tools/item-art-study/run.py prompts /absolute/study-directory
```

Use `tools/item-art-study/run.py` to orchestrate the existing
[`tools/lora-training/generate.py`](../tools/lora-training/README.md) routes.
No producer imports a product repository. Inputs and outputs cross the boundary
as ordinary files; art for another product stays in that product's workspace.

## Run a study

Read [resource sharing](resource-board.md) and the
[host guide](asset-hosts.md) first. Inspect `art-tools status` and
`art-tools resources`; an empty native queue is only one signal. For den-nimo,
also follow [its operating guide](den-nimo-inference-operations.md). Check live
loader menus before using a route: model and LoRA availability differs by host.

```bash
source legacy/tools/text-to-3d/scripts/lan-env.sh

python3 tools/item-art-study/run.py prepare \
  --spec tools/item-art-study/items-r01.json \
  --source /home/agent/dev/rusty-dagger/local/exports/daggerfall-items-r01 \
  --out /home/agent/dev/rusty-dagger/local/image-gen-tests/items-r01-20261027

asset-resources run --host den-nimo --resource nimo-comfy \
  --owner item-art --project rusty-dagger --purpose 'Item art model comparison' -- \
  python3 tools/item-art-study/run.py generate \
    /home/agent/dev/rusty-dagger/local/image-gen-tests/items-r01-20261027 \
    --host den-nimo

# Run each other host separately, with its corresponding resource wrapper.
# A single host's jobs remain sequential; different idle hosts may overlap.
# This particular spec uses gpu-lab for its short Qwen sweep. Follow the lab's
# owner-desktop policy; copy the spec and change that route's host/url to den-m5
# in a NEW output directory when the lab is unavailable.

python3 tools/item-art-study/run.py sheets \
  /home/agent/dev/rusty-dagger/local/image-gen-tests/items-r01-20261027
```

`prepare` does no inference. Its plan records absolute source and output paths,
the complete spec, every command argument, reference path and endpoint. An
unchanged plan can be reopened. A changed plan needs a new output directory:
reusing old outputs under different prompts would silently mislabel evidence.
Completed jobs are skipped on repeat generation; failures retain a log and do
not prevent unrelated planned jobs from running. Generate with `--routes ID ...`
to retry or run only selected recipes. Use a new directory for a deliberately
new attempt, changed seed, changed input or changed prompt. Keep the prepared
inputs intact after submission; the generator records their identities in its
existing provenance contract.

The comparison sheets are inspection derivatives. The PNG in each job directory
is the actual generator output. Extracted game media and generated study images
stay under the product's ignored `local/`; commit the tool and authored spec,
not the game media. The completed images can separately follow the ordinary
[Workbench publication workflow](gallery.md).
Sheets page over at most four items and four recipe columns at a time, so a
larger comparison can be read without shrinking all ten items into one image.

## Build the item contract before the style prompt

The manifest chooses the exact role, archive, record, frame and colour variant.
Do not silently substitute a paper-doll item for a world sprite, mirror a weapon,
mix enchanted and mundane frames, or confuse a dye with a different design.
Animated and related-frame work needs a separate temporal consistency study.

For each item, record:

| Part | What to write |
| --- | --- |
| Identity | Item class and visible components, count, holes, attachments and silhouette |
| Composition | Viewpoint, rotation, bounding aspect, relative position of major features |
| Local colour | Large material regions and their light/middle/dark relationships |
| Uncertainty | What the pixels do not resolve; describe an interpretation as an interpretation |
| Allowed invention | Material grain, small wear, folds, joinery or edge thickness that fit the existing form |
| Forbidden invention | A new handle, symbol, gem, clasp, anatomy, magical effect or material change unsupported by the source |

A model can make a better illustration while making the wrong object. Score
identity before aesthetic polish. A sack with an attractive new upright
drawstring silhouette still fails a source that lies diagonally with a folded
neck. A bracelet must retain its broad hollow cuff rather than becoming a thin
bangle or a jewel-studded treasure.

The r01 pair is a reproducible stratified random sample: one mundane container
and one jewellery/religious/plant subject, with the selection procedure and
resolved IDs in the spec. It is a discovery pair, not evidence of corpus-wide
reliability. This pair's sack material and cuff surface pattern remain uncertain.

## Keep the style contract separate

The starting Rusty Dagger UI treatment is inked dark-fantasy illustration with
a tight palette. Its producer uses Krea 2 turbo, the Retro Dark Fantasy
Illustration LoRA at 0.8 and a 64-colour palette pass. That is useful context,
not a requirement to add brass or stone to every item. The item keeps its own
material and colour. The UI's green extraction ground and final palette
reduction are separate from the drawing style.

The initial study explores four descriptions: painted adventure, inked dark
fantasy adjacent to the UI, warm storybook gouache and tinted engraving.
Each style card specifies edge treatment, medium, shading, texture density,
colour relationships and exclusions. It is deliberately independent of the
item description so a collection does not depend on a freshly improvised prompt
for every object.

`items-strong-style.json` is a follow-up prompt experiment on the same two
subjects: flat ink with discrete value shapes versus angular cut-brush gouache,
each at two seeds. It strengthens both the medium instructions and their
matching presentation language. Those changes are one compound recipe revision,
not an isolated one-word attribution. Prepare it in a fresh output directory
and compare against the retained first pass.

`items-two-pass.json` tests a different route: Qwen's painted reconstruction,
then the retro Krea LoRA at two lower redraw strengths. This asks whether
identity recovery and collection style can be handled in successive passes.
Prepare it with `--upstream` pointing at the completed first study; the route's
`upstream_route`/`upstream_style` select the exact input by item and seed. The
saved command points to the native intermediate PNG and the generator records
it as its init image. Preserve that upstream directory with the two-pass study.

```bash
python3 tools/item-art-study/run.py prepare \
  --spec tools/item-art-study/items-two-pass.json \
  --source /home/agent/dev/rusty-dagger/local/exports/daggerfall-items-r01 \
  --out /home/agent/dev/rusty-dagger/local/image-gen-tests/items-two-pass-20261027 \
  --upstream /home/agent/dev/rusty-dagger/local/image-gen-tests/items-r01-20261027
```

This is a candidate pipeline, not an assumption that the second pass will
respect the first. Compare both original and intermediate with each result;
look for lost folds, changed openings, new attachments and increased noise.
The two-pass spec uses den-m5's existing Krea/LoRA install. Host differences
mean it is not a bit-for-bit den-nimo comparison.

`items-retro-seeds.json` checks that lower-strength retro style pass at two fresh
seeds per item. The first-stage Qwen image stays fixed; compare these with the
original two-pass seed. This measures variation in the style pass, not fresh
end-to-end reconstruction or consistency across unseen item categories.

`items-ten.json` expands to ten new subjects with varied materials, shapes and
source roles. It compares the same painted Qwen baseline with direct Krea retro
at 0.65 redraw strength. `items-ten-retro.json` consumes that Qwen route through
`--upstream` and applies retro at 0.35. `items-ten-qwen-long.json` isolates a
four-item subset for single-pass Qwen tests: very long painted-dark-fantasy and
old-RPG ink descriptions, then matched Gary Chalk / John Blanche cues on the
same ink description. It does not train or load a style LoRA in Qwen. Artist
reference links and the controlled differences are in the literal prompt book.

The broader study keeps both retro recipes useful. Check the reconstruction
before selecting the next stage: the sapphire's Qwen result retained pixel
blocks, and its low-strength style pass retained that structure; direct Krea
resolved facets more successfully. The bottle and loose bandage benefited
from the reconstructed shape. The long Qwen prompts changed fine lines and
surface marks without reproducing Krea's bold value treatment on the tested
four subjects. These are recipe observations, not universal model limits.

### Source-locked post-process (no inference)

`postprocess.py` applies deterministic treatments to one route of a finished
study and writes a new directory of images, per-item records, metrics and
sheets. It never edits the study it reads and refuses an existing output.

```bash
T=/home/agent/dev/rusty-dagger/local/image-gen-tests
python3 tools/item-art-study/postprocess.py run $T/items-ten-retro-20261027 \
  --route qwen-retro --palette /home/agent/research/daggerfall-files/ART_PAL.COL \
  --out $T/items-ten-postprocess-20261028 --detail small-chest red-rose clay-jar

# Selected variants only, e.g. half and full broad back-projection of direct Krea:
python3 tools/item-art-study/postprocess.py run $T/items-ten-20261027 \
  --route krea-retro --palette /home/agent/research/daggerfall-files/ART_PAL.COL \
  --out $T/items-ten-bp-krea-retro-20261028 --variants original bp-broad-half bp-broad

# Side-by-side sheets across runs (LABEL=RUN_DIR:VARIANT, source column added):
python3 tools/item-art-study/postprocess.py compare --study $T/items-ten-retro-20261027 \
  --out $T/items-ten-bp-compare-20261028 --title '...' \
  --column "Direct Krea=$T/items-ten-bp-krea-retro-20261028:original" \
  --column "Krea + BP full=$T/items-ten-bp-krea-retro-20261028:bp-broad"
```

The object mask takes the ground colour from the image border, not the spec:
direct Krea draws a darker navy than the requested slate, and a spec-coloured
mask treated its whole ground as object (rectangular correction patches).

**Back-projection** shrinks the generated image onto the source grid recorded
in `input.json`, compares it with the native sprite in OKLab and adds the
enlarged difference back, five times. "Broad" blurs both at sigma 1 source
pixel before comparing, so only colour/value masses are enforced; "strict"
enforces every source pixel. A guided filter on the generated lightness shapes
the correction so it stays inside ink-bounded shapes rather than glowing
across contours. **Palette grading** pulls object pixels toward their nearest
palette colour, mostly in hue/chroma, with the shift blurred: either the full
`ART_PAL.COL` (which contains every colour of all ten sources) or only the
entries the sprite itself uses.

On the ten Qwen→retro outputs (2026-10-28), broad back-projection cut the
broad source-match error about 6–15× while keeping the drawing, and visibly
restored source colour decisions: the book returns to tan, the lantern to an
evenly bright glow, the chest gains the source's darker right side and bluer
bands. Strict back-projection reintroduces source dither as mottling (dagger
blade, clay jar) and is not recommended. Full-palette grading is close to a
no-op because the 255-colour palette is dense; the item-ramp grade is subtle
and adds little after back-projection. It does not fix a failed reconstruction:
the sapphire remains block-derived.

The owner judged the back-projected colours closer to Daggerfall. The
follow-up (`items-ten-bp-compare-20261028`) adds a half-strength variant,
which blends half of the converged correction in OKLab, and applies both
strengths to direct Krea retro. Half strength roughly halves the error and
keeps more of the retro LoRA's contrast and saturation while shifting hue
toward the source. Full strength is the most Daggerfall-like but slightly
flatter and muddier in value. Qwen→retro is the better base: its geometry
follows the source grid, so the correction lands on the right parts. Direct
Krea's broadened or rearranged shapes take the source colour in the wrong
places, giving blotches inside the bottle body, a mottled dagger blade and a
greyed lower shield at full strength. Back-projection still corrects direct
Krea's colour, notably the bandage and rose, and gives the sapphire its
source blue while keeping Krea's facets. Strength remains an owner choice.

`items-bp-strength-compare-20261028` adds 0.7 (`bp-broad-07`) on the ten
Qwen→retro items and on the sack/bracelet style-pass seeds 27101–27103. Use
`run --seed` to select one seed of a multi-seed study. The effect is the same
on every seed: errors per item agree within about 0.5. Back-projection also
shrinks the colour spread between seeds in proportion to strength, about 5×
at 1.0. At 1.0 it adds soft airbrushed gradients inside flat painted shapes.
At 0.7 most of the colour correction remains and the flat value shapes
survive. **The owner selected 0.5 (2026-10-28)** as the compromise between
source colour and the retro pass's own paint character, and judged the result
much better than earlier attempts. The provisional recipe is therefore Qwen
painted reconstruction → Krea retro 0.35 → broad back-projection at 0.5
(`bp-broad-half`).

`items-equip-world.json` and `items-equip-world-retro.json` apply that recipe
to the 16 unique world sprites of the weapon and armour templates (several
templates share one sprite; the spec names each sharing). Both stages ran on
den-m5 at seed 27301 (`items-equip-world-compare-20261028`). Weapons hold up
well: silhouettes, views and materials survive, and the set is coherent.
Dark-metal armour is weaker. Qwen reconstructs its colour faithfully, but the
retro LoRA adds a purple-blue cast with copper accents and multicoloured
mottling (mail, boots), which 0.5 back-projection only halves. The retro pass
also drifted a few identities: the lying boot became a tube, the axe edge
serrated, and the dagger grip gained a red motif. Dark armour may need higher
back-projection or a lower redraw strength. The owner accepted the icons and
world textures as broadly acceptable at 0.5, small drifts included.

**First-person frames** (`items-first-person.json`, `-retro.json`) use two
optional spec keys. `anchor: "bottom"` places the enlarged sprite on the
canvas's lower edge, where the screen cuts it. `task` replaces the default
item-redraw task sentence. The collection and presentation text allow the
player's single hand and forbid drawing the forearm's end. On the unarmed
idle fist and the dagger idle frame (`items-first-person-compare-20261028`),
the crop held and the dagger came through well. The fist's skin came out pale
yellow from Qwen and pinker with ink from retro. The main new issue is scale:
first-person sprites fill about the same screen area as the 1024² output, so
the retro pass's brush marks, tuned on small icons, read as coarse blotches
over broad skin. Test a higher generation resolution or lower redraw before
animation work, and check temporal consistency on a short sequence.

The follow-up (`items-first-person-res-compare-20261028`) kept the Qwen 1024
intermediate. It reran retro at 0.25, and at 0.35 and 0.25 at 2048
(`items-first-person-retro-025.json`, `items-first-person-retro-2048.json`).
When the style-pass canvas is larger than the upstream image, `run.py prepare`
now writes a Lanczos-enlarged `upstream-ROUTE-SIZE.png` into the item's
inputs, because image-to-image keeps the init image's size. 2048 is chosen so
the integer canvas scale doubles exactly and back-projection placement still
matches. `postprocess.py compare --screen-height 1080 1440` lays each object
box out 1:1 at the size a 320×200 frame shows it on that screen. At 1080p,
**2048 / 0.25** removed the coarse blotches and drifted least (dagger guard
closest to Qwen). 2048 / 0.35 gave finer but busier hatching and a more
ornate guard, at about four times the Krea time (≈265 s per frame on den-m5).

The unarmed fist is a **leather-gloved fist**, not a bare hand: owner
correction, 2026-10-28. The first contract had wrongly told the model the
source read as bare skin. `items-first-person-glove.json` and
`items-first-person-glove-retro.json` (2048 / 0.25) describe the glove at
length and forbid skin anywhere. The model followed it at every stage
(`items-first-person-glove-compare-20261028`): it drew a stitched brown glove
with a cuff, and the broad error fell from 17.2 to 5.5 after Qwen and to 2.8
after back-projection at 0.5. When a source's material is ambiguous, ask the
owner rather than guessing in the contract.

The same long glove wording on the dagger idle frame
(`items-first-person-glove-dagger*.json`,
`items-first-person-glove-dagger-compare-20261028`) produced a convincing
glove, but the hand grew. Instead of a narrow band at the bottom edge, the
fist and wrist filled about a third of the frame, the guard moved up to
mid-frame, and Qwen's broad error rose from 4.3 to 11.4. Length in a contract
acts as emphasis. Where a component occupies only a few source pixels, state
its frame proportion explicitly and keep its description short.

That was then tested (`items-first-person-glove-dagger-prop*.json`): one glove
sentence plus a stated layout (blade in the upper two-thirds, guard three
quarters down, fist in the bottom tenth). It did not work. Qwen zoomed in
instead: the blade widened, its point left the top edge, and the fist still
filled about a quarter of the frame (broad error 10.2, against 4.3 for the
bare-hand contract). Text proportions did not override the enlargement. For
material changes to a small component, keep the faithful reconstruction and
edit that component only, by instruction edit or masked inpaint, rather than
re-describing it in the reconstruction prompt.

The owner then spotted the deeper fault: the gloved runs hold the dagger with
the back of the hand to the camera, like a left hand. The source and the
bare-hand reconstruction show the right hand from its finger side. Our
contracts' "seen from behind" and "seam across the back of the hand" wording
likely encouraged this. `items-first-person-glove-dagger-grip*.json` ran two
seeds with an explicit right-hand, finger-side grip, a back-of-hand
prohibition and no proportion wording. Neither seed complied: both still show
a large back-of-hand view (`items-first-person-glove-dagger-grip-compare-20261028`).
Multi-seed two-pass specs now take each job's own seed's upstream image
(`routes[].seeds`). The owner considers exact first-person framing and pivots
negotiable, so they can be fixed by our own crop. Grip orientation is not.

The fix that worked was to change the material after reconstruction, not in
its prompt. `tools/texture-edit/qwen_edit.py` was run on the bare-hand Qwen
reconstruction with "make the hand a plain brown leather glove … keep exactly
the same hand pose, grip, finger positions, size and position; change nothing
else". This is Qwen-Image-2.1 int8 on the owner's 5090, about 8 s warm.

- **Seed 27401:** failed; the background turned to noise.
- **Seeds 27402 and 27403:** kept the blade, guard and background (mean change
  outside the hand 2.9 and 0.9 of 255) and the small fist at the bottom edge.
  27402 keeps the bare hand's finger-side grip with its folded fingers toward
  the camera. 27403 smooths the fist into a rounder dome.

The edited images feed `items-first-person-glove-edit-retro.json` (retro
2048 / 0.25 on the 5090, 25–50 s) as the upstream study.
`items-first-person-glove-edit-20261028/plan.json` records the edits in the
runner's upstream format. Broad error is 7.0 after retro and 3.6 after
back-projection at 0.5. That compares with 4.6 → 2.4 for the bare hand and
13.2 → 6.7 for the grip prompt.

Back-projection pulls the dark-brown leather back toward the source's tan and
bronze. With retro 0.25 marks, the glove then looks mottled
(`items-first-person-glove-edit-compare-20261028`). The 5090 LoRA
`Retro_Dark_Fantasy_Illustration_krea2_3352765_epoch_8.safetensors` is the same
file as den-m5's `civitai_retro_dark_fantasy_krea2.safetensors` (sha256
25adc8ae…).

The shared detail hierarchy is silhouette, a few large form masses, then sparse
material marks. Keep lit regions quiet. Do not ask every item for maximal
detail, cinematic lighting or intricate ornament: that invites unrelated
decoration and noisy sets. A fixed 64-colour reduction is not part of this
discovery run; test it later as an independent treatment if it helps the chosen
style. High resolution does not require photographic detail.

The expanded prompt has these independently editable parts:

1. Task and input role: redraw with new detail, using the source for content.
2. Subject and composition: the per-item identity contract.
3. Uncertainty: the unresolved source features and the chosen hypothesis.
4. Permitted invention: specific types of new detail.
5. Style: one shared style card.
6. Collection rules: palette discipline, detail hierarchy and shared exclusions.
7. Presentation: fixed canvas, occupancy, background, light and absence of props.

Retain the literal expanded prompt, not merely a template name. A required LoRA
trigger is prepended by its route and saved in that prompt. At guidance 1,
negative prompts are generally ineffective; put essential instructions in the
positive prompt. The LoRA files are existing shared installs, not trained by
this study. Do not treat a filename as proof of its style or quality.

## First-person animation through keyframe-anchored H3 (measured 2026-10-28)

**Status: measured experiment.** One weapon (the dagger, all six normal
strikes, 30 drawn frames) was run once at seed 28101. Nothing has been styled
or published.

[`h3_anim.py`](../tools/item-art-study/h3_anim.py) applies the sprite-anchored
orbit recipe ([video.md](video.md#sprite-anchored-orbits-drawn-keyframes-and-addguide-pins-2026-10-22))
to time instead of yaw. Each strike record is one stock FL2VA clip:

- **Keys:** the first drawn frame is the first video frame and the last drawn
  frame the last. The frames between are pinned with `MiniMaxH3AddGuide`
  at evenly spaced indices.
- **Clip settings:** 73 frames at 24 fps, 28 steps, no LoRA, and a prompt
  that describes only the content and the motion.
- **Canvas:** whole 320×200 game screens with each frame at its CIF offset,
  enlarged 3× to 960×608 (the screen at 960×600, padded at the top).

```bash
# Low-res keys straight from the export
python3 tools/item-art-study/h3_anim.py keyframes OUT_SRC --manifest EXPORT/weapons/item-113-dagger/manifest.json
# Up-res keys: tight crops, then Qwen, then back-projection 0.5, cropped back inside the drawn silhouette
python3 tools/item-art-study/h3_anim.py tight TIGHT_EXPORT --manifest EXPORT/weapons/item-113-dagger/manifest.json
python3 tools/item-art-study/run.py prepare --spec tools/item-art-study/items-first-person-dagger-anim-tight.json \
  --source TIGHT_EXPORT --out STUDY && python3 tools/item-art-study/run.py generate STUDY
python3 tools/item-art-study/postprocess.py run STUDY --route qwen-base --palette ART_PAL.COL --out BP --variants original bp-broad-half
python3 tools/item-art-study/h3_anim.py keyframes OUT_UP --manifest TIGHT_EXPORT/weapons/item-113-dagger/manifest.json --upres BP
# Clips (owner 5090), sheets, and side-by-side MP4s
COMFY_URL=http://192.168.1.20:8188 python3 tools/item-art-study/h3_anim.py generate OUT_SRC
python3 tools/item-art-study/h3_anim.py sheet OUT_SRC
python3 tools/item-art-study/h3_anim.py compare CMP --run 'Low-res keys=OUT_SRC' --run 'Up-res keys=OUT_UP'
# Revision for one record: room around the screen, uncut keys, softened keys, minimal prompt
python3 tools/item-art-study/h3_anim.py keyframes OUT --manifest ... --canvas 1344 832 --records strike-down
python3 tools/item-art-study/h3_anim.py keyframes OUT --manifest ... --canvas 1344 832 --records idle   # optional bookend key
python3 tools/item-art-study/h3_anim.py uncut OUT            # geometric blade completion (default)
python3 tools/item-art-study/h3_anim.py smooth OUT OUT_SOFT
COMFY_URL=http://192.168.1.20:8188 python3 tools/item-art-study/h3_anim.py generate OUT_SOFT --prompt minimal --seeds 28301 28302
# Open a clip in ComfyUI: laid-out workflow with groups and notes; --soften shows the softening as
# ImageScale nodes on the raw keys; --install adds it to the server's Workflows list
COMFY_URL=http://192.168.1.20:8188 python3 tools/item-art-study/h3_anim.py workflow OUT_SOFT/clips/NAME --soften --install NAME
```

`h3_anim.py workflow` uses [`comfy_ui.py`](../tools/item-art-study/comfy_ui.py), which converts any
API-format `prompt.json` into the frontend's workflow format using the server's `/object_info`
for slot and widget order. Without a layout it places nodes in columns by graph depth. The
strike-down clip with softened keys (seed 28302) is installed on the owner's 5090 as
**h3-anim-dagger-strike-down-softened** (softening as nodes) and
**h3-anim-dagger-strike-down-exact** (the submitted graph, which converts back to it with no
differences). The ComfyUI softening (area, then bicubic) matches the PIL keys to within
0.42/255 on average.

Results (rusty-dagger `local/image-gen-tests/dagger-anim-h3-compare-20261028/`):

- **Timing:** 116–236 s per clip on the 5090.
- **Pins:** every pinned frame reproduces its key in position, angle and size,
  and framing never drifts.
- **Low-res keys:** smooth, believable in-betweens in all six strikes (a
  wrist roll through the horizontal slash, the fist turning into the thrust),
  rendered in the original low-resolution look. One clip briefly invents a
  green sleeve.
- **Up-res keys:** equally smooth painted motion wherever the keys are
  consistent. Two kinds of weak Qwen key break the clips:
  - On strike-up, a pulled-back guard redrawn as a small hand makes H3 hold
    poses and cut.
  - The blade-tip-only final frames end each clip on a smear.

**Strike-down revision** (owner direction 2026-10-28, rusty-dagger
`local/image-gen-tests/dagger-anim-h3-strike-down-revision-20261028/`). The
goals were a minimal prompt and nothing cut off except at the bottom. The
canvas is 1344×832, with room above and beside the 3× screen.

- **Uncut keys:** `uncut` completes keys cut at the screen's top or sides.
  The default `--method geometric` continues the drawn blade's own edges past
  the cut and closes them to a leaf point, filled with the blade's own
  cross-section, in source pixels. The tip length is set so the widest-to-tip
  stretch is 0.31 of the guard-to-tip blade, as on the fully visible frame 2.
  `--method qwen` (Qwen edit with a mask outside the screen plus a 30 px band;
  `--pick IDENT=SEED`) was the first route and is rejected: its blades kept
  the full width and then stepped down to a thin spike or a dark translucent
  block, and their lengths differed between frames.
- **Measurement:** glove boiling is measured as the residual after optical-
  flow compensation.
- **Prompt:** minimal and firm-grip prompts leave boiling unchanged, at about
  10.
- **Softened keys** (`smooth`, box-reduced and bicubically enlarged): boiling
  falls to 6.6 on two seeds, and the fist turns over several frames instead
  of dissolving and re-forming at each pin.
- **Fewer pins** (`--pins 0 2 4`): H3 invents its own motion. Rejected.
- **Wind-up blades:** the Qwen completions disagreed and the blade morphed
  through stepped "paddle" shapes. With the geometric completion, the blade
  stays one pointed leaf blade through the whole wind-up (idle-bookend clip,
  seeds 28301/28302). Motion timing and glove boiling are unchanged, as
  expected, since only the blade pixels changed.
- **Idle as first and last frame** (`keyframes --records idle` adds the idle
  key to an existing set; `generate --bookend idle-0 --length 90`): the
  invented wind-up out of idle is believable, but H3 holds each pinned pose
  and moves in bursts. Half or more of the steps are near-still, and half the
  motion falls in 10% of the steps, against 15–21% and 22% without the
  bookend. Evenly spaced pins over very uneven pose changes look like the
  cause; spacing pins by pose change is the next thing to try.

**Direction (owner, 2026-10-29):** first-person weapons move to a 3D arm
with regular animations rather than regenerated sprite frames. These H3 clips
become the motion source: track the dagger in the clip, then bake the path
into Engine held motion. Den task rusty-dagger #10457 has the plan.

Reconstruct per-frame keys from tight crops (`h3_anim.py tight`). On whole
CIF record rectangles, Qwen moves small hands toward the middle of the canvas
and fills the empty space. The clips are slow motion compared with the game;
retime by subsampling.

## Inventory icon conversion: batches, finals and the review ledger

The accepted recipe (Qwen painted reconstruction, Krea 2 turbo retro at 0.35, broad back-projection at 0.5)
is run in batches of about 12 to 28 unique world sprites. Each sprite is generated once; templates that share
it are listed in its contract's uncertainty text.

- **Specs:** `items-icons-<batch>.json` (Qwen) and `-retro.json` copy the template's style card and replace
  only `items`. Flame animation frames use `items-icons-frames-*.json`; only the flame differs between frames,
  so the finals keep frame 0's converted body and take just the flame region from each frame's conversion.
- **Transparency:** `postprocess.py cutout STUDY RUN --variant bp-broad-half --out DIR` turns the flat background
  into alpha (enclosed ground is kept or removed by the source sprite's own alpha) and decontaminates the edge.
- **Hosts:** Qwen and retro both run on den-m5 and den-nimo (den-nimo gained the int8 Qwen-Image-2.1 edit weights).
  Different hosts are not bit-identical; record which host ran a batch.
- **Finals** go to the product's `local/assets/items/` as `texture-<archive>-record-<record>-frame-<frame>.png`
  (1024x1024 RGBA). Working studies stay under `local/image-gen-tests/`. A redone sprite overwrites its final.
- **Ledger and review page:** `review_ledger.py` joins the item export, the specs, the post-process metrics and
  the finals into one row per sprite frame and writes `local/review/index.html` (cards, filters, detail),
  `items-ledger.csv` and thumbnails. The only hand-edited input is `ledger/items-world-notes.json` (status and
  notes per `archive-record-frame` key). Status is derived (`unconverted`, `converted`, or `needs-owner-review`
  when the contract says it is a guess) unless the notes override it with `accepted`, `redo` or `skipped`.

```sh
python3 tools/item-art-study/review_ledger.py --export local/exports/daggerfall-items-r01 \
  --studies local/image-gen-tests --finals local/assets/items --review local/review
```

## Interpret the comparisons correctly

| Comparison | Held fixed | What it can tell us |
| --- | --- | --- |
| Qwen style sweep | Model, source reference, seed, item, presentation | Whether the visual-language words create useful distinct directions |
| Krea plain / retro / darkbrush | Model, init canvas, redraw strength, seed, core prompt | Whether each LoRA helps at the tested strength; retro also receives its required trigger |
| Krea retro redraw strength | Model, LoRA, seed, prompt | The tested tradeoff between added detail and source preservation |
| Other model recipes | Item contract, core style, canvas, seed number | Practical route behaviour, with conditioning differences explicitly visible |

Krea starts from the scaled source image. Qwen receives it as an instruction
reference. Z-Image receives only an outline plus the text description. The FLUX
route here receives text alone; this is a deliberately weak identity control,
not a claim about every FLUX image-editing capability. These are recipe
comparisons, not a controlled architecture benchmark. Equal seed numbers do
not give different models equivalent noise or equal exploration budgets.

Nearest-neighbour enlargement is used only to make the tiny input readable
without inventing interpolation details. It is labelled as a source in the
sheets. The outline is derived from source alpha and carries no internal
colour or material evidence. Neither preprocessing result counts as generated
art. Raw generations use one flat slate ground; they are not yet production
RGBA assets. A cutout made from the original silhouette could discard valid new
edges, so final alpha needs its own inspection after choosing a recipe.

## Review and narrow the recipe

Look at every result at native resolution and at intended display sizes. Keep
the original alongside it. Use a small record with separate fields rather than
one attractive-image score:

| Field | Decision |
| --- | --- |
| Identity | Preserved / drift / ambiguous; name the changed feature |
| New detail | Plausible / merely smoothed pixels / unsupported decoration |
| Style | The actual medium, edge treatment and palette; does the pair belong together? |
| Readability | What remains at 64, 128 and 256 pixels? |
| Presentation | Crop, background, framing, extraneous object or cast shadow |
| Disposition | Explore / retry with one change / reject / owner-selected direction |

Once the owner chooses a direction, freeze a candidate recipe with its style
card, graph, model, LoRAs, strengths, sampler, steps, reference preprocessing and
output policy. Test at least two fresh seeds per item before making a
consistency claim. Then try held-out subjects with different materials and
shapes, such as a weapon, glass vessel, book, plant ingredient and clothing.
The two discovery items cannot establish generalisation to those categories.

Keep a small set of owner-approved anchors and explicit anti-examples. Future
items are judged against that set at the same display size, without replacing
anchors with unreviewed new generations. An anchor image used as a second style
reference is a separate experiment: preserve the first reference's content and
measure whether the model copies the anchor's objects or colours. Record that
reference role and include it in provenance.

If prompt and reference control remain insufficient after a direction is
selected, follow the [LoRA curation and held-out evaluation workflow](../tools/lora-training/README.md).
Train from reviewed examples of the chosen direction, not the mixed exploration
sheet. Training now would encode an unresolved preference. Do not run the whole
item corpus until both identity and cross-subject style have been checked.
