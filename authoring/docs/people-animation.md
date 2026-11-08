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
alone.

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
- **Hidden back hair:** See-through paints back hair it could not see as a washed-out grey mass,
  which shows in the gaps beside the jaw and when the head tilts. After assembly, back-hair pixels
  are recoloured from the front hair's own colours, matched by brightness and a shade darker, since
  hair behind the head is in shadow. Only back hair that no other part covers and that is clearly
  hair-coloured keeps its pixels: the cut-out leaves the white backing in the gaps beside the jaw.
- **Hidden skin:** See-through bleeds cloth colour into skin it paints in. In bare-skin limb parts,
  a pixel greener than it is red is refilled from the skin around it, after assembly.
- **Held staff:** a bone from its planted foot to the grip, aimed by a one-bone IK at a grip bone on
  the forearm, so it pivots on the ground and stays in the hand. Riding the forearm alone swings a
  long staff wildly; riding the hip leaves the hand behind.
- **Eyes:** iris bones for gaze.
- **Stance:** the video's pose sequence is an input like any other. Where its legs close further
  than the rig source's stance, each knee and ankle is turned outward about its hip by a few
  degrees, which keeps leg lengths; the witch takes 4°.
- **Head turns:** a turned head is a key drawing, not a bend. Video frames from the turn are cut out,
  shifted so their neck lands on the rig's neck, and run through See-through (full and head) like the
  source. Their head parts become alternate sets on the head chain, bound to the frames of the turn,
  and the in-between drawing holds one 8 fps frame each way. Garment layers leave the head as it was,
  so head key frames and their runs carry over to a rig rebuilt with new clothing.

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
