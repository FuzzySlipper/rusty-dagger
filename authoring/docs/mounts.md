# Mounts

The horse and cart a rider sees in the held styles. They're our own art, committed in
`content/worldrpg/first-person/mounts/` and packed by `scripts/fp-arms/build.sh`. The game's side is in
`scripts/fp-arms/README.md`.

## The horse

1. Generate the body from the refreshed inventory art with TRELLIS.2 (asset-pipeline `image-to-mesh-local`), on the cutout
   `authoring/gen-raw/studies/items-icons-player-icons-bp-20261103/cutout/horse.png`, with `--mask-source alpha`, into
   the ignored `authoring/gen-raw/mounts/horse-generated.glb`.
2. Run `authoring/tools/mounts/horse.py` under Blender. It stands the horse at full size, reduces it, finds a skeleton from
   the mesh (each foot's cluster, the column of each leg above it, then the spine, neck and tail), weighs the vertices to
   their nearest bones, repaints the soft colours in the art's palette as flat dithered texels, drapes a saddle and blanket
   on the back from the horse's own surface, hangs the reins, and makes the clips.
3. Check it with `rusty asset check`, and in the game with `player.ride horse`. It also writes `horse-harness.glb`, the horse
   with no saddle, for a cart's shafts.

The clips are made from each gait's footfalls (`GAITS`): the loop's seconds, the share of it a foot is down, each leg's phase,
how far the legs swing and the body bobs. A new gait is one row. `REINS_AHEAD` and its neighbours place the reins' end where
`RIDE_HAND` in `scripts/fp-arms/build.py` puts the hands, so change them together.

## The cart

`authoring/tools/mounts/cart.py` builds it under Blender from boxes and cylinders, with the seat over the origin and the
shafts along -Z. The game draws the horse ahead of it, at `mount.cartHorse` in the held definition. The cart carries reins
from the driver's fists to where the horse's own end, overlapping them so the horse can move. Moving the seat, the horse or
`RIDE_HAND` means moving `FIST` and `REINS_END` too.
