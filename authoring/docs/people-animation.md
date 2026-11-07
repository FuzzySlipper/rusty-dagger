# Animated people

How a Daggerfall townsperson or static NPC becomes an animated figure in the game. Only the two ends
are fixed: the classic sprite and its frames, and what the game plays. Every stage in between can
change, including the accepted still finals in `gen-raw/people/`.

The route is a parts rig played at runtime:
1. A **rig source** painting gives the rig clean parts.
2. **See-through** splits it into layers.
3. **rig-c/parts** builds the rig.
4. A **video** of the figure supplies the motion and the key drawings for the frames a rig cannot bend into.

## Rig sources

A rig source is the figure repainted in a stance that parts can be cut from. It keeps the figure's
identity, costume and look. Bones move it back into the classic pose, so the change stays small:
rigid parts bend badly far from how they were drawn.

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

## Parts

- **See-through runs:** one on the white-padded figure and one on a square crop around the head, at
  1280. The 5090's ComfyUI keeps most of the lab container's memory, so the runner must keep the model
  weights on the GPU.
- **Draw order, back to front:**
  1. back hair, held objects, neck;
  2. the skirt, then the legs and feet;
  3. the bodice, then the arms;
  4. the face, then ears, nose, mouth;
  5. eye whites, irises, lashes;
  6. front hair, then brows.

  rig-c's proposed plan can put the face over the eyes and leave the nose out; check it.
- **Skirt front panel:** in a legs-apart source, the panel is the skirt showing between the legs.
  It's cut out from that gap and drawn in front of the legs, so legs moving back together pass
  behind it.
- **Hidden skin:** See-through bleeds cloth colour into skin it paints in. In bare-skin limb parts,
  a pixel greener than it is red is refilled from the skin around it, after assembly.
- **Rigging:** a held staff rides the hip, not the forearm. Eyes get iris bones for gaze.
