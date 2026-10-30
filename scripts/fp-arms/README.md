# First-person arms and dagger

3D first-person arms and the item-113 dagger for the held viewmodel, the capture config that asset-pipeline's
`tools/fp-capture` reads to recover the dagger's motion from H3 clips of the classic WEAPON02 animations, and the build
of the container the game reads the models from.

## The held definition and the container

The game draws the art from two parts:

- **The held definition**, `content/worldrpg/first-person/daggerfall.held.json`: tracked, authored content. It names
  the classic weapon visuals the look replaces (`weapon.dagger.steel`), the arms rig (joints, forearm twist shares,
  turn, scale, palm and grip calibration, as rusty-riders' `shared` profile), the arms (shoulder, elbow pole, hold,
  hand closures), the look (its model, the stance that seats it at rest, the hand's grip and hold on it) and the held
  motion, its tracks keyed by the classic strike ids such as `strikeDown`. Its models are container paths.
- **The container**, `local/first-person.rpak` (ignored): the generated models `first-person/arms.glb` and
  `first-person/dagger.glb` and `first-person/arms-provenance.json`. `scripts/fp-arms/build.sh` builds it.

`DaggerfallHeldArt` reads the definition when the container is installed beside the product. Set `viewmodel.style` in
the tuning payload to `held` to draw the 3D arms and dagger for the weapons the definition names, or `heldRetro` to
play the same motion sampled once per classic frame and held. The sprite playback still times every swing, so the hit
lands as it does with the sprites; a strike with no motion shows its sprite.

## Tuning and new motion: the workbench

Motion is designed in the game. In `rusty dev --output window --live-debug` with `held` or `heldRetro`, the developer
tools' **Workbench** tab (the **`** key) and the `held.motion*` commands (`docs/playtesting.md`, "Developer
commands") show the dagger look in either hand with a chosen strike, hold each key, scrub to any moment and play at
any speed, and tune the stance, the hand's grip and hold, each key's phase, moment and curve, the held key's pose, the
arms' reach and the rig's palm. Each change shows at once; **Save** writes it into the held definition, keeping its
formatting, and the change is committed like any content. A new strike's track and poses are added to the definition
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

Both models are reproducible and land in the ignored `local/fp-arms/`:

```sh
blender -b --factory-startup --python ../asset-pipeline/tools/fps-arms/generate.py -- --params ../asset-pipeline/tools/fps-arms/sets/dagger.json --out local/fp-arms/arms.glb
blender -b --factory-startup --python scripts/fp-arms/build-dagger.py -- local/fp-arms/dagger.glb
```

- The arms are asset-pipeline's shared first-person arm set (`tools/fps-arms`) in its Daggerfall look
  (`sets/dagger.json`): an MPFB hand with forearm twist joints, a grey sleeve and a brown leather glove with a short
  cuff, shaped and coloured against the classic unarmed WEAPON10 frames; the weapon frames' own painted gloves are not
  followed. The generator, the look and the MPFB hand are CC0 or our
  own. `arms-provenance.json` records the asset-pipeline commit the build used (and any uncommitted changes under its
  arms and capture tools) and the GLB's sha256. Change the look in asset-pipeline, not here.
- `build-dagger.py` builds the dagger procedurally in the Engine's held-look frame (origin at the grip, length along
  -Z, up +Y), proportioned from the WEAPON02 frames.
- `capture.json` names the arms rig, the dagger's keypoints and the classic camera: the 320x200 canvas shown at 4:3
  with a 65 degree vertical field of view, checked by fitting and overlaying the idle and strike-down frames.
