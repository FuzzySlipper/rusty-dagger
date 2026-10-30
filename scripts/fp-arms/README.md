# First-person arms and dagger

3D first-person arms and the item-113 dagger for the held viewmodel, the capture config that asset-pipeline's
`tools/fp-capture` reads to recover the dagger's motion from H3 clips of the classic WEAPON02 animations, and the build
of the container the game reads them from.

## The container

`scripts/fp-arms/build.sh` builds `local/first-person.rpak`: `first-person/arms.glb`, `first-person/dagger.glb`,
`first-person/arms-provenance.json` and `first-person/held.json`, which `assemble.py` writes from the authored
`rig.json` (the arms' joints, forearm twist shares and grip calibration, as rusty-riders' `shared` profile) and
`arms.json` (shoulder, elbow pole, hold, hand closures), the hand's hold on the dagger that fp-capture's `describe.py`
measures on these arms, and a capture's strike bakes (default `local/fp-capture/r01`: one `bake-<action>.json` per
captured strike, its action the classic strike id such as `strikeDown`). The dagger is held as the capture held it:
`held.json`'s look hold is the seated hand's frame, and its grip the point that puts the wrist where the capture had it.

The game opens the container beside itself when present (`DaggerfallHeldArt`). Set `viewmodel.style` in the tuning
payload to `held` to draw the 3D arms and dagger for the weapons the container names (`weapon.dagger.steel`), or
`heldRetro` to play the same motion sampled once per classic frame and held. The sprite playback still times every
swing, so the hit lands as it does with the sprites; a strike with no captured motion shows its sprite.

## The models

Both models are reproducible and land in the ignored `local/fp-arms/`:

```sh
blender -b --factory-startup --python ../asset-pipeline/tools/fps-arms/generate.py -- --params ../asset-pipeline/tools/fps-arms/sets/dagger.json --out local/fp-arms/arms.glb
blender -b --factory-startup --python scripts/fp-arms/build-dagger.py -- local/fp-arms/dagger.glb
```

- The arms are asset-pipeline's shared first-person arm set (`tools/fps-arms`) in its Daggerfall look
  (`sets/dagger.json`): an MPFB hand with forearm twist joints, a teal sleeve and a slim bronze gauntlet and glove,
  shaped against the classic WEAPON02 and WEAPON10 frames. The generator, the look and the MPFB hand are CC0 or our
  own. `arms-provenance.json` records the asset-pipeline commit the build used (and any uncommitted changes under its
  arms and capture tools) and the GLB's sha256. Change the look in asset-pipeline, not here.
- `build-dagger.py` builds the dagger procedurally in the Engine's held-look frame (origin at the grip, length along
  -Z, up +Y), proportioned from the WEAPON02 frames.
- `capture.json` names the arms rig, the dagger's keypoints and the classic camera: the 320x200 canvas shown at 4:3
  with a 65 degree vertical field of view, checked by fitting and overlaying the idle and strike-down frames.
