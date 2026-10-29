# First-person arms and dagger

3D first-person arms and the item-113 dagger for the held viewmodel, the capture config that asset-pipeline's
`tools/fp-capture` reads to recover the dagger's motion from H3 clips of the classic WEAPON02 animations, and the build
of the container the game reads them from.

## The container

`scripts/fp-arms/build.sh` builds `local/first-person.rpak`: `first-person/arms.glb`, `first-person/dagger.glb` and
`first-person/held.json`, which `assemble.py` writes from the authored `rig.json` (the arms' joints and grip
calibration, from rusty-riders' `painted` profile) and `arms.json` (shoulder, elbow pole, hold, hand closures) and a
capture's outputs (default `local/fp-capture/r01`: `model.json` from fp-capture's `describe.py`, and one
`bake-<action>.json` per captured strike, its action the classic strike id such as `strikeDown`). The dagger is held as
the capture held it: `held.json`'s look hold is the capture's hand frame, and its grip the point that puts the wrist
where the capture had it.

The game opens the container beside itself when present (`DaggerfallHeldArt`). Set `viewmodel.style` in the tuning
payload to `held` to draw the 3D arms and dagger for the weapons the container names (`weapon.dagger.steel`), or
`heldRetro` to play the same motion sampled once per classic frame and held. The sprite playback still times every
swing, so the hit lands as it does with the sprites; a strike with no captured motion shows its sprite.

## The models

Both models are reproducible and land in the ignored `local/fp-arms/`:

```sh
blender -b --factory-startup --python scripts/fp-arms/build-arms.py -- ../rusty-riders/content/art/fp-arms/painted.glb local/fp-arms/arms.glb
blender -b --factory-startup --python scripts/fp-arms/build-dagger.py -- local/fp-arms/dagger.glb
```

- `build-arms.py` recolours rusty-riders' `painted` arms (the Hands pack's rigged arms with the old project's
  hand-painted texture, built by riders' `scripts/import-fp-arms.sh`; also on the den NAS under
  `rusty-riders/art/fp-arms/`) to a teal sleeve and bronze gauntlet. The pack is a purchased asset, so neither it nor
  the result is committed.
- `build-dagger.py` builds the dagger procedurally in the Engine's held-look frame (origin at the grip, length along
  -Z, up +Y), proportioned from the WEAPON02 frames.
- `capture.json` names the arms rig, the dagger's keypoints and the classic camera: the 320x200 canvas shown at 4:3
  with a 65 degree vertical field of view, checked by fitting and overlaying the idle and strike-down frames.
