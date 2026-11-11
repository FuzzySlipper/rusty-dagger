# Animated people

How a Daggerfall townsperson or static NPC becomes an animated figure in the game. Only the two ends
are fixed: the classic sprite and its frames, and what the game plays. Every stage in between can
change, including the accepted still finals in `gen-raw/people/`.

The route is a parts rig played at runtime:
1. A **rig source** painting gives the rig clean parts. Clothing that moves apart from the body,
   such as a skirt's back and front panel, is generated as **separate layers**.
2. **See-through** splits the body into layers.
3. **rig-c/parts** builds the rig, with the generated clothing as author layers.
4. A **video** of the figure supplies the motion and the key drawings for the frames a rig cannot bend into.

Fix a part that goes wrong by changing what is generated or how it's rigged, not by repainting the
part.

The parts of this route that carry to other projects, with the reasons behind them, are in
asset-pipeline's `docs/parts-rig-characters.md`. This page keeps what is Daggerfall's and every
number used for its figures.

## Rig sources

A rig source is the figure repainted in a stance that parts can be cut from. It keeps the figure's
identity, costume and look. The animated figure doesn't have to return to the classic pose: shift
its pose and design toward what the rig does well, such as an open stance or a panel that can stay
in front of the legs, rather than fighting the technique. Keep each change modest, since rigid
parts bend badly far from how they were drawn.

What a source should show:
- legs slightly apart, with each leg drawn whole;
- arms clear of the body;
- held objects clear of the body;
- clothing panels that overlap a limb visibly separate, such as a skirt's front panel between the legs.

How a rig source is made:
1. Lay the accepted final on its generation ground, RGB 48,52,59.
2. Edit it with Qwen-Image 2.1 through asset-pipeline's `tools/texture-edit/qwen_edit.py`:
   - weights: `qwen_image_2.1_int8_convrot.safetensors` with `qwen3vl_8b_int8_convrot.safetensors`;
   - sampler: 25 steps, cfg 1, euler, resolution 1024;
   - LoRAs: none;
   - host: Patch's 5090 ComfyUI.

   The prompt keeps everything but the stance and states the new stance part by part. It also
   restates the costume details the stance touches, such as a skirt's panel width and where its slits
   start: Qwen otherwise redesigns them to fit the new stance. Try a few seeds and keep the smallest
   change that opens the stance.
3. Don't run the Krea retro pass again. The final already carries that style, so a second pass
   repaints the figure: the face and held objects drift.
4. Cut the figure out with `tools/items/matte.py` (BiRefNet) joined with a ground-colour key, and lay
   it on white for See-through.

Each figure's source is recorded in `specs/people/rig/<figure>.json`: input, model files, sampler
settings, seeds tried, the chosen seed with the reason, and the full prompt.

## Clothing layers

See-through gives a skirt as one layer, shaped to the still, with fill painted where the legs hid it.
A garment that hangs behind or in front of the legs is generated whole instead, with the same Qwen
setup, each piece fitted to one body:
1. **Body:** the final with that clothing removed and plain shorts in its place, in the rig source's
   stance.
2. **Worn:** the body wearing one garment at a time, with the final as a second image for the cloth.
   A shape the prompt alone doesn't hold, such as a panel tapering from hip width to a narrow hem,
   takes a `--guide` outline. Don't pass the outline as the compositing mask too: the garment
   doesn't fill it, and what it leaves is repainted.
3. **Isolated:** a garment the body covers, such as a back skirt, comes from removing the woman from
   the worn image, which also paints its hidden inner face. A garment in front of everything is
   already whole in the worn image, so it is taken from there; asked to remove the woman, Qwen
   redraws it.
4. **Cut out:** BiRefNet alone for garments, since dark cloth sits too close to the slate ground for
   a key; BiRefNet joined with a ground key for the body, which brings back a staff.

The painting the rig is assembled against is the stack of all three, and See-through runs on the body
alone. A generated garment keeps its own art throughout: assembly would colour it from the worn
picture, where the arm lies over it at a slightly different place than in the body. What hangs on
it, such as a belt pouch, is cut from the worn picture as a part of its own on the hip, over the
garment and under the near arm.

## Parts

- **See-through runs:** one on the white-padded figure and one on a square crop around the head, at
  1280. The 5090's ComfyUI keeps most of the lab container's memory, so the runner must keep the model
  weights on the GPU.
- **Generated garments:** each goes into the full See-through run as an author layer
  (`author:<label>`: its PNG, depth image and `layers.json` entry on the run's canvas).
- **Draw order, back to front:**
  1. back hair, a back skirt, held objects;
  2. shorts, then the legs and feet, then a front panel;
  3. the bodice, then the neck, then the arms;
  4. the face, then ears, nose, mouth;
  5. eye whites, irises, lashes;
  6. front hair, then brows.

  rig-c's proposed plan can put the face over the eyes and leave the nose out; check it.
- **Front panel:** drawn in front of the legs. Its mesh is weighted to both leg chains as well as
  the waist, with a soft blend (`exponent` 0.5), so its hem narrows partway as the legs close.
  Weighted to the waist alone, it stays as wide as the stance it was painted in and covers the legs;
  with the default blend, it follows each leg fully and shrinks to a strip. Its turn depth sits just
  in front of the legs, which ride it. A part has one place in the draw order on any frame: a panel
  meant to pass between the legs toward its hem would have to be cut into pieces, so prefer a pose
  where it can stay in front.
- **Back skirt:** drawn behind everything and hung from the hip on three chains of its own, running
  from the waist to the hem, so it shows past the legs at the sides. The chains are on physics, not
  keyed sway, with rig-c's cloth recipe values: the edges softer than the centre, the centre damped
  apart so they fall out of phase, the response growing toward the hem. The recipe itself only drafts
  trees on See-through's skirt, so the constraints go on the author layer's chains directly.
  - Gravity is off: the skirt is painted flared, and gravity pulls its edges straight down.
  - The wind response is low, so the runtime's steady default wind only leans it.
  - A standing figure's hips barely move, so physics alone leaves the skirt still. What swings it is
    the wind the render is given: a gust that rises from calm and dies back before the loop's end,
    so the skirt is at rest on both ends and the loop closes. A render at 8 fps takes the gust on
    its own frames, since wind steps can't fall between them.
- **Outline:** BiRefNet's soft edge keeps the generation ground's colour, and See-through runs on the
  figure laid on white, so the assembled parts carry a grey and light fringe at the figure's outline.
  After assembly, wherever the cut-out's alpha is partial, a part takes the cut-out's own colour with
  the ground unmixed, and no more than its alpha. Each part reads the cut-out its art came from: the
  painting's, the body's for a split view's See-through layers, a generated garment's own. Clear
  pixels then take the nearest solid colour, so filtering at draw time blends no black into the edge.
- **Hidden back hair:** See-through paints back hair it could not see as a washed-out grey mass,
  which shows in the gaps beside the jaw and when the head tilts. After assembly, back-hair pixels
  are recoloured from the front hair's own colours, matched by brightness and a shade darker, since
  hair behind the head is in shadow. Only back hair that no other part covers and that is clearly
  hair-coloured keeps its pixels: the cut-out leaves the white backing in the gaps beside the jaw.
  In profile, where the head is cut whole from the painting, the full run's back hair is drawn
  behind everything as well: See-through paints it on behind the near arm, so a swinging sleeve
  bares hair rather than a gap. There it is recoloured darker still (0.6), in shadow behind her.
- **Hidden skin:** See-through bleeds cloth colour into skin it paints in. In bare-skin limb parts,
  a pixel greener than it is red is refilled from the skin around it, after assembly.
- **Held staff:** a bone from its planted foot to the grip, aimed by a one-bone IK at a grip bone on
  the forearm, so it pivots on the ground and stays in the hand. Riding the forearm alone swings a
  long staff wildly; riding the hip leaves the hand behind.
- **Eyes:** each iris on a bone of its own under its eye, for gaze. A profile's head is cut whole from
  the painting, so its near eye is cut again as a part of its own on an eye bone, from See-through's
  eye layers, and the head under it is painted with the skin around it. Squashed, the eye then
  shows a closed lid.
- **Stance:** the video's pose sequence is an input like any other. Where its legs close further
  than the rig source's stance, each knee and ankle is turned outward about its hip by a few
  degrees, which keeps leg lengths; the witch takes 4°.
- **Head turns:** a turned head is a key drawing, not a bend. Video frames from the turn are cut out,
  shifted so their neck lands on the rig's neck, and run through See-through (full and head) like the
  source. Their head parts become alternate sets on the head chain, bound to the frames of the turn,
  and the in-between drawing holds one 8 fps frame each way. Garment layers leave the head as it was,
  so head key frames and their runs carry over to a rig rebuilt with new clothing.

## Layers

A figure's clips play together at runtime on layered tracks, as the Engine's parts rigs play them;
nothing is baked into one clip. The figure's layer stack (`parts-rig-layers/1`) says how:

- **Body,** track 0: the clip the game chooses (idle, walk, a gesture). It may key anything, draw
  order included.
- **Attention,** track 2: the `attention` clip, looping under every body clip on its own clock. It
  owns the eye, iris and brow bones and the `head` bone, and body clips leave them unkeyed; the
  rig's own idle blink is taken out. Body clips move the head through `neck` and `head_ctl`, which
  `head` hangs below, so the head's own motion rides on theirs.
- **Attention override,** track 3: a body clip's companion `<clip>.attention`, played with it when
  the rig has one. A higher track replaces only what its clip keys, so a companion that moves only
  the irises keeps the blinks and the head's drift.

Track 1 is left for breathing added over a walk. A layer's clip keys bone transforms only.

The `attention` clip is about 10 s:
- **Blinks** at uneven gaps of 2 to 4 s, sometimes doubled, each shaped as rig-c's idle blink (the
  eyes squashed to 0.12 and back, the brows dipping).
- **Glances** of the irises, about a seventh of the eye's width, held a second or more.
- **Head:** a degree or two of drift on slow waves whose periods divide the loop, and a lean toward
  each glance of up to 1.6°, starting 0.12 s after the eyes and eased in over 0.4 s.

Its length divides none of the body clips', so the two never repeat in step. Its times come from
the figure, not the view, so every view blinks, glances and turns together and a change of view
never cuts one. A view lacking a part simply does not draw that part's motion: from behind only the
head moves.

## Idle video

A standing NPC's idle is small movement on a long loop: shifting weight in place, moving a held
object, small head adjustments and blinks, with no big gestures or head turns. A head turn needs key
drawings and suits a one-off gesture, not an idle.

- **Video:** Pika H3 Flex FL2VA on Patch's 5090, through asset-pipeline's `h3_anim` graph. The first
  and last frame are the rig's own render in its stance, on the generation ground. That closes the
  loop and puts the clip in the rig's coordinates. Clips run 17k+5 frames at 24 fps; 192 frames
  gives an 8 s loop. Each figure's prompt, seeds and choice go in its spec under `idle`. Pick the
  seed whose feet stay planted.
- **Poses:** RTMW whole-body joints on each frame. The cycle is the frames before the last, which
  repeats the first, retimed to whole seconds so 8 fps samples it evenly.
- **Closing the loop:**
  - The two reads of the one first and last picture differ, and a pixel on an eye is degrees of
    head tilt, so the gap is spread back over the cycle.
  - The retarget plants each support span on the floor apart and doesn't blend a change of
    support across the wrap, so the cycle starts inside one span.
  - `rig-pose clean` runs with its stillness hold off: the hold doesn't wrap, so held spans at the
    two ends can settle on different poses.
  - The rig's own idle, carried into the clip for breathing, hair and blinks, gets a cycle that
    divides the loop. The wind gust dies back before the end.

## Walking townspeople

A wanderer walks in every direction the camera can see it from, so it has one rig per view: front,
side and back, with the other side mirrored. Eight directions add a front and a rear three-quarter,
each also mirrored. One rig whose parts swap to a turned drawing holds only a small turn, because
bones, draw order and foreshortening differ from view to view. The 2.5D turn (a depth per part,
the parts sliding against each other) holds about 15 to 20°. At 45° the far side of the face and
body has to be drawn, so a three-quarter is a rig of its own. A wanderer is repainted in the
stationary people's style rather than converted from its classic sprite.

**Shared route:** one set of scripts serves every figure. Each figure's root holds a `figure.json`:
- its id;
- the description and pronoun its clip and still prompts use;
- the clip each view's cycle is read from, and that view's retarget flags;
- the profile's foot-roll frame;
- `hidden_legs` when a floor-length skirt hides her legs.

The scripts find that file from the folder they run in, so a figure's own folders hold only its data, and a fix proven on one figure
reaches the rest. Figures 453, 391, 385, 384 and 397 run on it; 453 rebuilt on it unchanged.

Faults the route now handles itself, found on the three men, who wear tunics with their legs showing:
- **A belt in no layer:** See-through can put a waistband in no layer, which leaves a hole between the top and the skirt. The build
  patches the painting's own pixels over it, on the hip, and builds again.
- **Specks:** a rigid part keeps only its pieces of at least 5 % of its largest. A cut can catch a sliver of the other shoe in a
  foot, and rigid, it travels with the limb.
- **Hidden back hair:**
  - It is recoloured from the front hair once that has 150 px.
  - With no front hair, as in profile and from behind, its colour comes from the head cut whole from the painting.
  - Hair that is short leaves the full run no back hair, and none is drawn.
- **Tall boots:** See-through gives them as footwear up to the knee, which leaves the ankles on no legwear. They are merged into the
  legwear so each leg runs from hip to sole.
- **Empty layers:** a part whose See-through layer is empty is left out of the plan.
- **Bob:** a view whose own bob is under half the front's takes only the share it lacks.
- **Profiles:** a tunic's hem gets springs. A rig without foot bones gets no heel-to-toe roll.
- **A tunic given whole:** when See-through gives a tunic as topwear alone, the build cuts it at the belt so the skirt rides the hips.
  The cut skirt then hangs on three chains spread along its flare, like a split skirt, and swings with springs and the clip's edges.
- **Walking shoes** replace the whole shin drawing, so they suit legs hidden under a skirt only.

**Static figures:** a standing NPC has no walk clip, so each view's rig source is that view's still itself. `route/prep_still.py`
cuts it out and lays it at the walkers' rig geometry (1600 px tall, soles at y = 1700 on 1024×1792), so the rest of the route runs
unchanged. A shirt See-through gives whole as topwear is cut at the belt in every view, front included. Its design comes from the
classic sprite through the New B loop: a Qwen edit of the upscaled sprite, then Krea image-to-image at denoise 0.32 with the
Brom, Elmore and RetroInk adapters.

**Idles across views:** a walk lines its views up on a shared footfall, but an idle has no such event. One 3D idle seen from each
view's heading gives every view the same motion at the same times. A free PINOC library idle is dumped to joints with Blender and
projected orthographically at 0°, 45°, 90°, 135° and 180°. Each view keeps its own still's stance and takes only the clip's
displacement from its mean pose, enlarged 2.5 times, since the library's calm idles read as near still at game size. `route/idle.sh`
then cleans and retargets it onto each rig and runs the springs, the attention layer and placement. On 184-20 the worst bone jump
across a view switch is about 1.7 px with the figure 354 px tall.

**Seams under motion:**
- A shirt cut at the belt overlaps 8 px, so the chest and hip meshes don't part.
- Two limbs cut from one layer, both legs from one legwear layer, meet on a bare seam that any motion opens. Each limb part grows
  16 px under the same-layer limb parts drawn over it (`cast/underfill.py`). Grown under cloth or another layer's limb, the band
  would ride out with the part instead.

**Judging a rig:** `out/recomposite_rig.png` and `out/idle.gif` are drawn before the route's passes, so a hair streak or a seam seen
there may not ship. Judge a render of the built rig. `rp rig` can report a refusal and still exit 0, leaving the last rig in
place, so the build stops on any FAIL in `rig.log`.

Faults found on 397, a hooded woman in a floor-length skirt:
- **Hidden paint:** assembly colours a part from the painting only where it counts the part visible. Under a hood, a three-quarter
  face was counted mostly hidden and kept See-through's pale fill, and the hood kept grey streaks. After assembly, every part but
  the rigid limbs takes the painting's colour wherever it is the topmost part at rest. The limbs keep assembly's joint cuts.
- **Hoods read as hair:** See-through gives a hood's crown as back hair. Hair recolouring keeps the painting's coloured visible
  pixels (saturation at least 0.2) and recolours only what lies behind.
- **Hidden legs:** RTMW guesses the hips and ankles under a floor-length skirt. Walking toward the camera, the forward foot also
  sinks down the picture with the stride's depth. Planting those guesses bobbed her 54–64 units and sank her shoes below the
  ground. For a `hidden_legs` figure the cleaned cycle is rewritten before the retarget:
  - the hips ride the neck, which the clip shows, at their median distance below it;
  - the lower ankle stands on one floor and the other never goes below it;
  - each knee moves by half its ankle's change.

  Her profile then also shortens its legs to 0.55, like the front.
- **Legs past the hem:** any skirt ending within 60 rig px of the ankles has its thighs cleared, and each shin is cut to a stub
  above its shoe, as for a split bell skirt.
- **Shoes as feet:** a profile needs the shoes as their own layer to get foot bones and the heel-to-toe roll. When the legs were
  drawn under the skirt with the shoes merged into them, the shoes come back out as footwear. The legwear keeps 30 px around each
  ankle so the proposer still finds the legs. The plan is proposed again whenever the layers change.

**Three-quarters:**
- **Stills:** P-H3-Image (asset-pipeline `docs/pruna-ph3-image.md`) edits a cardinal key into the
  turned view in seconds, keeping the pose, scale, frame and ground.
  - The rear three-quarter takes the back and profile keys as references.
  - The front three-quarter takes the front key alone, since given the profile beside it, it copies
    the profile. It settles at a 30–40° turn however far it is asked to turn, so the seed that
    turns furthest is kept.
- **Clips:** H3 walks a three-quarter at about half the cardinals' cadence, a stride every 48–52
  frames. Each cycle is retimed to the shared length like any other. A rear three-quarter drifts
  toward profile as it walks, so its cycle is read where the angle still holds.
- **Cycle:** a three-quarter's legs really cross in the picture, so the cycle is read without the
  profile's leg-swap repair.
- **Legs:** See-through may give a three-quarter no legwear, only the shoes. The legs are then drawn
  as slim stockings along the body's keypoints, with the shoes over them, into the run's legwear
  layer. The proposer finds legs by that role, and the skirt hides all but the shoes.
- **Branches:** the front three-quarter is built as the front and the rear three-quarter as the back.
  - Sway and roll: 0.7 of the cardinals', the share of a sideways motion seen at 45°.
  - Arms: they trail as in profile, since their swing still reads mostly as a turn.
  - The rear three-quarter takes the front's bob and the back's calmer feet. It keeps its standing
    shoes, which already point along the walk.

1. **Turnaround:** the front still made as for any person, and the side and back made from it with
   the same Qwen setup as a rig source, stating which hip a one-sided item such as a pouch is on.
2. **Walk clips:** Pika H3 Flex FL2VA, one clip per view, with that view's still as first and last
   frame on the generation ground at half the rig painting's size. The prompt asks for a walk in
   place at a calm pace in that view, with a fixed camera. 107 frames give about three cycles.
   H3 draws perspective, so a foot coming toward the camera shortens and the track narrows under
   the body. A clip opened and closed on the still starts and ends standing, so the walk is then
   generated again as a loop. Its first and last frame is a mid-stride frame of that clip, with
   one foot down under the body. The prompt has her mid-stride from the first frame to the last,
   never stopping or standing with both feet side by side. The idle and the walk are separate
   clips, and a change between them snaps rather than blends.
3. **Rig sources:** each view's rig is cut from a frame of its own clip, doubled, in place, so the
   clip's poses land on the rig unchanged. The frame should have both feet down with the soles
   hidden, and every limb in sight: a profile's far arm swung clear of the back, since See-through
   paints a hidden arm badly. A sole or a turned-out foot in the source travels with the rigid
   foot through the whole walk. From behind, See-through invents a face and gives each bare arm
   as a hand layer, so the back's head is cut from the painting whole, as in profile, and its arms
   come from the hand layers. The head box is set around the hair, and the whole head is cut down to
   the end of the hair See-through drew, so long hair turns with the head instead of staying painted
   on the back. The whole head keeps only the pixels where See-through's frontmost layer belongs to
   the head, so a sleeve cap or the bodice inside the box stays with its own part. Under the head,
   those parts take See-through's own paint rather than the painting's, which there shows the hair.
   A belt rides the hips. The bodice is weighted along a chest segment and a hip segment, and both
   run down the body's middle, so a vertex at the belt's end sits about as far from either and takes
   half of each. The walk turns the chest against the hips, so the belt would shear apart at the
   buckle. The belt is found in See-through's bodice layer, the band near its bottom in the belt's
   dark, cool colour. The hip segment is laid along the belt's middle from end to end, the chest
   segment ends a belt's height above it, and the falloff exponent is 4. The belt is then at least
   94 % hip in every view, and the waist above it blends.
   Hands sit on their own bones. The proposer's arm is two links ending at the wrist, with the hand
   painted on the forearm, because the body keypoints stop at the wrist. RTMW whole-body also reads
   21 points per hand on the view's body image. The middle fingertip, seen from the wrist, gives each
   arm a third link, and the hand is cut off the forearm at the wrist as its own rigid part on it.
   The idle sways the hand as much as the forearm, since the chain's sway may not shrink toward its
   tip.
4. **Cycle:** RTMW on every frame. The period comes from the ankles: the shortest whose
   correlation is within 0.03 of the best, since two cycles correlate as well as one. The cycle is cut where its
   two ends agree best and turned to start where the left foot pushes off, the same event in
   every view, so a view change keeps the stride. In profile RTMW swaps the legs on the odd frame,
   which continuity undoes, and loses the hips and shoulders for stretches, so their midpoints
   keep only the cycle's two lowest harmonics. Every view is retimed to one cycle length that 8 fps
   divides.
5. **Retarget:** `rig-pose clean --loop --depth walk --hold off`, then retarget with the feet
   planted. Front and back let the shin shorten to 0.55, and the front orders the legs by depth.
   **Arms:** a townsperson walks calmly, legs moving more than arms, so every arm motion keeps 55 %
   of the clip's about its own mean. The retarget turns each segment to its picture angle only. From
   the front and behind, an arm swinging toward and away from the camera shows as shortening, and what
   the retarget makes of it is the arms swinging out and in. There the sideways turn keeps 20 % of its
   swing, and each arm segment shortens along itself by 55 % of what `clean --depth` measured, its
   picture length over its longest in the cycle. In profile the forearm trails the shoulder: it turns
   by 0.6 of how far the upper arm moved over the last 0.1 s, the other way, never bending back past
   straight further than the clip does. The hand trails the forearm the same way: it turns by 1.2
   times how far the forearm turned over the last 0.15 s, the other way, at most 12°. From the
   front and behind a hand swings toward the camera, so there it stays with the forearm.
   **Secondary motion:** the body and what hangs on it carry the walk with the arms, and it is
   derived offline and kept as keys, so nothing simulates at runtime and every player shows the
   same closed loop. From behind, a stride's depth draws as height: the legs shorten in the picture
   at every hand-over while the hips barely drop, so with the feet held on one floor the retarget
   keeps none of the bob. The back therefore takes its hip height from the front. Every view's cycle
   starts on the same footfall and lasts as long, so the front's hip height carries over unchanged. The
   back's thighs shorten along themselves by what the hips dropped (the shins take the thigh's scale),
   so the feet stay down. A foot seen from behind moves mostly away from the camera and back, which the
   retarget turns into the legs swinging out and in. As with the arms, the back's legs keep 30 % of
   their sideways turn about its mean. Each foot rises by half the height the clip draws it above its
   lowest, so a far step draws higher and the hem hides the feet more than it shows them. From the
   front and behind, the hips sway 5 units toward the standing foot and
   roll 2.5° down on the swinging leg's side. The chest turns back 1.6 times that roll against the
   hips, and the neck turns 0.5 of it, leaving the head nearly level. The thighs turn back by the roll
   and the sway, so a planted foot stays where the clip put it. A profile shows neither.
   **Smoothing:** the body's tracks keep their cycle's six lowest harmonics: hip position and
   roll, chest, neck, legs and feet. The retarget can hold a leg for a few frames while the clip's
   moves on, then snap it 30° in one key, and the hips it places through that leg zigzag with it.
   A change of support also takes the hips' new x at once, and the sway flips sides with the
   stance. Smoothing spreads each snap over a few frames before the springs act, halving the hips'
   worst jerk in every view. The standing feet slide no more than before, within about 1.5 units.
   **Springs:** the hanging chains each view draws carry springs: the front's back hair, bangs and
   skirt panels, the back's skirt panels, and the profile's hem and the pouch, which hangs from its
   belt on a bone of its own. There is one recipe per kind of part (strength, damping, inertia,
   mass and mix), shared by every figure with that part. Each view's rig config declares the springs
   as rig-c physics constraints, one per chain link. The walk plays four times on the rig with its
   springs so they settle. Each link's turn against its parent, less the same turn without springs,
   is read off the last cycle, smoothed and keyed into the walk on the chain's control bone. The
   back's hips move too little to swing its skirt as the front's does, so its recorded swing is
   scaled by 3. A front or back skirt's three chains follow its flare rather than the proposer's,
   which roots them a hand's width apart at the middle of the waist and leaves the outer thirds to
   sway as one block. Each chain sits at a sixth, a half and five sixths of the skirt's width on
   each row of See-through's bottomwear layer, down to just above the hem's point. The outer chains
   also swing as the clip's own skirt edges do, read on a row 85 % of the way down. Only the swing
   about each edge's mean is kept, since the clip's skirt hangs narrower than the painted one. The
   middle chain takes the mean of the two edges, and the springs add their lag on top. The rig
   ships without its springs, so every player shows the same closed loop. Hair
   and bangs cut with a profile's or the back's head can't swing. The figure walks on the spot, so a
   stop or a turn has no response.
   **Tuning:** each view also gets a tuning build in the Workbench set (the `<view>-tune` member).
   It is the producer's rig as its config makes it, with the springs live, playing the walk before
   its swing is recorded. Its `review.json` names that config and parts as the producer handoff.
   Open Rig Review on that member and use Animate's Environment (wind, gusts, gravity, a jolt) and
   Physics tuning panels. Apply rebuilds through the producer and checks the result. Tuned values
   come back from the submitted review and go into the recipe, or into an override for one spring.
   **Walking shoes:** a front or back rig is cut from the still, where she stands with her feet
   turned out. Walking, a foot seen from in front or behind points along the walk, so it shows
   toe-on or heel-on and narrow. The loop's mid-stride frame holds that shoe. It is cut there and
   added to each shin slot as a second drawing: its sole as low as the standing shoe's, under the
   shin's tip, upright at the setup pose and mirrored for the other foot. Its top runs on under the
   hem. The walk keys the walking shoes on its first key and every other body clip keys the
   standing ones. A profile's rig is already cut from a walking frame.
6. **Placement:** each clip draws the figure at its own height and offset. After the retarget, the
   rig's root moves so the walk's planted foot stands on y = 0 and the rig source's neck on x = 0, so
   a change of view, mirrored or not, keeps the feet and head in place. The ground is measured on a render with 300 rows
   below y = 0, so a view whose feet start below it is measured rather than clipped.
