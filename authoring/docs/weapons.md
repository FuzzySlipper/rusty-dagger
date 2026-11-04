# Weapon models

Makes the 3D weapon models the held viewmodel draws (`viewmodel.style` `held` and `heldRetro`), one per weapon item.
Each weapon's design is its accepted refreshed paper-doll layer (the `worn` area's archive 234 finals), so the
inventory and the hand agree. The classic first-person frames are shared by whole families (WEAPON04 is a plain
blade for every sword) and add construction detail only where they show something the icon does not. The models
are crunchy, not modern PBR: a few thousand triangles, painted in the icon's own colours on 2 mm world cells, nearest
sampled and matte.

## Where things live

| Path | What | Git |
| --- | --- | --- |
| `authoring/specs/weapons/weapons.json` | Every weapon: its records and icon, length, hands, what the classic first-person art adds, its design (`subject`, `invent`) and its parts; the shared style, pose, orbit and `crunch` | tracked |
| `authoring/tools/weapons/` | `h3.py` (turnaround request), `build.py` (the mesh, under Blender), `paint.py` (the crunch), `sheet.py` (review sheets) | tracked |
| `authoring/gen-raw/weapons/<name>/` | `h3-<rev>/` (the upright icon, request, attempts, chosen `views/`), `build-<rev>/`, `paint-*/`, `final/`, `review-*.png` | ignored |
| `content/worldrpg/first-person/weapons/<name>.glb` | The adopted models, packed by `scripts/fp-arms/build.sh` | tracked |

## Steps

1. **Design.** Write the weapon's `subject` (what it is, from its icon) and `invent` (what the icon cannot show:
   sections, depths, the grip under the hand) in the spec. A conflict between the icon and the classic first-person
   frame goes to the icon.
2. **Turnaround.** The paper doll draws most weapons in pieces, with gaps where the hand holds them. A straight grip's
   gap can be filled row by row; anything else (a haft, a bow, a chain) goes through
   `python3 authoring/tools/weapons/h3.py join NAME`, where Qwen rejoins the icon into `joined.png`. Look at it: a join can
   leave a gap or redraw a fitting. A weapon with no paper-doll layer copies its accepted item icon to `joined.png`.
   `python3 authoring/tools/weapons/h3.py prepare NAME r01` then stands the icon upright (`axis: haft` stands it by its
   wood, so a big head does not tilt it) and writes an H3 REF2VA request. Run it on the lab 5090 with
   asset-pipeline's `legacy/experiments/h3-turnaround/run_turnaround.py` inside `asset-resources run --host gpu-lab
   --resource lab-comfy` (two seeds, 25 steps), from the asset-pipeline root. Look at every attempt; a changed prompt
   is a new rev. Copy eight frames of the chosen orbit to `h3-<rev>/views/` for the review sheet. The turnaround is
   the reference for what the icon hides: guard depth, pommel shape, how spikes sit round a head.
3. **Parts.** Give the weapon its `grip` and `parts` in the spec. `build.py` splits the icon into colour pieces
   (steel, brass, leather) and builds each part as `flat` (blades, guards, axe blades: two sheets meeting at the
   outline), `lathe` (grips, pommels, hafts: round in section; `ball` and `spikes` for a spiked head, `flanges` for a
   flanged one), `disc` (a tsuba or a saber's bell), `chain` (a flail's links and ball) or `string` (a bowstring). Touching
   lathe parts share one straight axis and one profile; `bridge` joins a part to the run above across a gap in the icon.
   A head needs something holding it to its haft: give an axe a lathe socket running up into the head. Its `build.json`
   lists every piece per class to pick from. Build into a new folder for each rev, so a failed build cannot leave an
   older model to be mistaken for the new one.

   ```sh
   blender -b --factory-startup --python authoring/tools/weapons/build.py -- NAME \
     authoring/gen-raw/weapons/NAME/h3-r01/icon.png authoring/gen-raw/weapons/NAME/build-r01
   ```
4. **Crunch.** `python3 authoring/tools/weapons/paint.py BUILD_DIR ICON OUT_DIR` repaints each part from its own
   icon colours through asset-pipeline's `tools/texture-edit/palette_cells.py`, at the spec's `crunch`.
5. **Review.** `python3 authoring/tools/weapons/sheet.py OUT.png --views H3_VIEWS LABEL=MODEL.glb ...` shows the
   model's eight sides under the turnaround's. Look at the joints close up, and from the side as well as the front. Then copy the painted `model.glb` to
   `content/worldrpg/first-person/weapons/NAME.glb`, run `rusty asset check` on it, rebuild the container with
   `scripts/fp-arms/build.sh`, and check it in the hand with `scripts/fp-arms/sheet.sh` in a 1920×1080 window run.

The models are in the Engine's held-look frame: origin at the grip's centre, length along -Z with the tip forward,
+Y one edge. A family's measured `look.grip` and `look.hold` then seat them where the clips held the pack's weapon.
The steel parts form the `metal` material, which the game tints per metal (iron … daedric), as the paper doll tints
its bare-steel layers; everything else is `trim`.
