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
side and back, with the other side mirrored. One rig whose parts swap to a turned drawing holds only
a small turn, because bones, draw order and foreshortening differ from view to view. A wanderer is
repainted in the stationary people's style rather than converted from its classic sprite.

1. **Turnaround:** the front still made as for any person, and the side and back made from it with
   the same Qwen setup as a rig source, stating which hip a one-sided item such as a pouch is on.
2. **Walk clips:** Pika H3 Flex FL2VA, one clip per view, with that view's still as first and last
   frame on the generation ground at half the rig painting's size. The prompt asks for a walk in
   place at a calm pace in that view, with a fixed camera. 107 frames give about three cycles.
   H3 draws perspective, so a foot coming toward the camera shortens and the track narrows under
   the body.
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
4. **Cycle:** RTMW on every frame. The period comes from the ankles. The cycle is cut where its
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
   straight further than the clip does.
6. **Placement:** each clip draws the figure at its own height and offset. After the retarget, the
   rig's root moves so the walk's planted foot stands on y = 0 and the rig source's neck on x = 0, so
   a change of view, mirrored or not, keeps the feet and head in place.
