# First-person arms and dagger

3D first-person arms and the item-113 dagger for the held-motion viewmodel (Den task #10457), and the capture config
that asset-pipeline's `tools/fp-capture` reads to recover the dagger's motion from H3 clips of the classic WEAPON02
animations.

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
