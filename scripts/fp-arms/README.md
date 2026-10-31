# First-person arms

3D first-person arms for the held viewmodel, the capture config that asset-pipeline's `tools/fp-capture` reads to
recover the dagger's motion from H3 clips of the classic WEAPON02 animations, and the build of the container the game
reads the arms from. The dagger has no model in the game yet (#10511): the arms hold and swing the steel dagger's
place empty.

## The held definition and the container

The game draws the art from two parts:

- **The held definition**, `content/worldrpg/first-person/daggerfall.held.json`: tracked, authored content. It names
  the classic weapon visuals the look replaces (`weapon.dagger.steel`), the arms rig (joints, forearm twist shares,
  turn, scale, palm and grip calibration), the arms (shoulder, elbow pole, hold,
  hand closures), the look (its model, `null` while it has none, the stance that seats it at rest, the hand's grip
  and hold on it) and the held motion, its tracks keyed by the classic strike ids such as `strikeDown`. Its models are
  container paths. The rest stance is the arms' idle: the gloved fist low and right of centre, seen from its thumb
  side, after the classic unarmed idle (tuned in the workbench).
- **The container**, `local/first-person.rpak` (ignored): the adopted arms `first-person/arms.glb` and
  `first-person/arms-provenance.json`. `scripts/fp-arms/build.sh` builds it.

`DaggerfallHeldArt` reads the definition when the container is installed beside the product. Set `viewmodel.style` in
the tuning payload to `held` to draw the 3D arms for the weapons the definition names, or `heldRetro` to
play the same motion sampled once per classic frame and held. The sprite playback still times every swing, so the hit
lands as it does with the sprites; a strike with no motion shows its sprite.

## Tuning and new motion: the workbench

Motion is designed in the game. In `rusty dev --output window --live-debug` with `held` or `heldRetro`, the developer
tools' **Workbench** tab (the **`** key) and the `held.motion*` commands (`docs/playtesting.md`, "Developer
commands") show the dagger look (its place, while it has no model) in either hand with a chosen strike, hold each key, scrub to any moment and play at
any speed, and tune the stance, the hand's grip and hold, each key's phase, moment and curve, the held key's pose, the
arms' reach and the rig's palm. Each change shows at once; **Save** writes it into the held definition, keeping its
formatting, and the change is committed like any content. Keys and poses set on the page stay on it, marked edited,
after the viewer moves to another key or strike, so Save still writes them. A new strike's track and poses are added to the definition
by hand (a track for a strike that is not a classic strike id is refused) and then tuned in the workbench.

## Seeding from a capture

The definition's stance, the hand's grip and hold and the `strikeDown` track were seeded from capture `r01`
(`local/fp-capture/r01`: one `bake-<action>.json` per captured strike). To seed again from a capture, measure the hand's
hold on the current arms and write the capture into the definition:

```sh
blender -b --factory-startup --python ../asset-pipeline/tools/fp-capture/describe.py -- scripts/fp-arms/capture.json local/fp-arms/model.json
python3 scripts/fp-arms/seed.py local/fp-arms/model.json local/fp-capture/r01/bake-*.json
```

`seed.py` replaces the look's stance (the first bake's), hold (the capture's) and grip (the point that puts the wrist
where the capture had it, with the definition's palm) and each baked strike's track and poses, so it overwrites what
the workbench tuned there; every other value is kept. Review the diff before committing.

## The models

The arms are reproducible and land in the ignored `local/fp-arms/`; the procedural dagger is built only for seeding
a capture (`capture.json` names it), not into the container:

```sh
blender -b --factory-startup --python ../asset-pipeline/tools/fps-arms/adopt.py -- --params ../asset-pipeline/tools/fps-arms/sets/psx-dagger.json --out local/fp-arms/arms.glb
blender -b --factory-startup --python scripts/fp-arms/build-dagger.py -- local/fp-arms/dagger.glb
```

- The arms are Drillimpact's [PSX First Person Arms](https://drillimpact.itch.io/psx-first-person-arms-free) 1.1.0
  (CC0 1.0), kept in asset-pipeline (`tools/fps-arms/packs/psx-drillimpact/`) and adopted by its `tools/fps-arms/adopt.py`
  in the Daggerfall look (`sets/psx-dagger.json`): the pack's low-poly mesh, skeleton and clips with forearm twist
  joints added, the glove's edge cut into the mesh at the wrist, and a hand-painted texture after the classic unarmed
  WEAPON10 frames (`sets/psx-dagger-paint.png`: a brown leather glove with drawn seams and creases and a grey sleeve
  with drawn folds, 256², drawn nearest). The rig holds with the pack's own `knife_idle` clip, so its grip
  calibration is zero, and its palm offset is where that clip holds its knife. `arms-provenance.json` records the
  asset-pipeline commit the build used (and any uncommitted changes under its arms and capture tools) and the GLB's
  sha256. Change the look in asset-pipeline, not here.
- `build-dagger.py` builds the placeholder dagger procedurally in the Engine's held-look frame (origin at the grip,
  length along -Z, up +Y), proportioned from the WEAPON02 frames. It is not drawn; #10511 replaces it.
- `capture.json` names the arms rig, the dagger's keypoints and the classic camera: the 320x200 canvas shown at 4:3
  with a 65 degree vertical field of view, checked by fitting and overlaying the idle and strike-down frames.
