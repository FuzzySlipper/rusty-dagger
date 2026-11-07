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
  1. back hair, a back skirt, held objects, neck;
  2. shorts, then the legs and feet, then a front panel;
  3. the bodice, then the arms;
  4. the face, then ears, nose, mouth;
  5. eye whites, irises, lashes;
  6. front hair, then brows.

  rig-c's proposed plan can put the face over the eyes and leave the nose out; check it.
- **Front panel:** drawn in front of the legs. Its mesh is weighted to both leg chains as well as
  the waist, with a soft blend (`exponent` 0.5), so its hem narrows partway as the legs close.
  Weighted to the waist alone, it stays as wide as the stance it was painted in and covers the legs;
  with the default blend, it follows each leg fully and shrinks to a strip. Its turn depth sits just
  in front of the legs, which ride it.
- **Back skirt:** drawn behind everything and hung from the hip on three sway chains of its own,
  running from the waist to the hem, so it shows past the legs at the sides and swings on its own.
- **Hidden skin:** See-through bleeds cloth colour into skin it paints in. In bare-skin limb parts,
  a pixel greener than it is red is refilled from the skin around it, after assembly.
- **Rigging:** a held staff rides the hip, not the forearm. Eyes get iris bones for gaze.
