# First-person arms

Builds `local/first-person.rpak`, the 3D arms, weapons and mounts the held viewmodel draws (`viewmodel.style` `held`
or `heldRetro`). The design is on the wiki page `wiki/rusty-dagger/first-person-held-art`.

## Licence: never commit the art

The meshes and clips are Unity Asset Store purchases. They may ship inside the game, but the raw assets, and any GLB
converted from them, must never be committed: this repository is public. They live in the ignored `local/fp-melee/`
(sources) and `local/first-person.rpak` (the built container), and stay out of tracked content packs. That covers the
arms' pixel paint and the claw arms' GLBs too: they derive from the pack or carry its clips. The held definition
(`content/worldrpg/first-person/daggerfall.held.json`) and the game's own weapon and mount models are committed.

## Building

```sh
scripts/fp-arms/copy-sources.sh ../rusty-riders/old-game/Assets/MoreArt/FpMelee    # once
scripts/fp-arms/build.sh
```

- Needs Blender 5 and the `rusty` CLI; about two minutes for each set of arms. If `build.sh` reports that
  `build.py` failed, rerun `build.py` by hand to see Blender's output.
- A clip is named by its FBX file stem. To add one, name it in the definition and build again. A name ending
  `.mirrored` bakes the clip the other way round.
- The build writes `local/fp-arms/measure.json` (shoulder, palm, grips, holds, `bowSeat`, suggested marks). Copy
  what the definition should take from it, then tune in the game. Changing where the bow clips aim means copying
  `bowSeat` to `bow.seat` again.
- A shield `seat` is in centimetres with `scale` 100: the arms' skeleton root is scaled 0.01.
- Average any map that goes into the paint with `paint.area`; Blender's own scaling aliases the cloth's weave.
- `RIDE_HAND` in `build.py` moves with `REINS_AHEAD` in `authoring/tools/mounts/horse.py` and `FIST` and `REINS_END`
  in `cart.py`.

## Tuning

Run `rusty dev --output window --live-debug` at 1920×1080 or more, with `held` or `heldRetro`.

- The developer tools' **Workbench** tab and the `held.motion*` commands show a look with a chosen attack and tune
  its marks and fade, the idle loop, grips and holds, the shoulder and the palm. A look is a classic visual
  (`weapon.dagger.steel`) or `item.N` for template N. **Save** writes into the held definition: commit it.
- `held.clip` reads what the arms play now; `held.shield` moves a shield's seat or carry and answers what to save.
- `scripts/fp-arms/sheet.sh ORIGIN LOOK OUT_DIR [STEP] [ATTACK...]` writes a contact sheet per attack from a running
  host, its hit outlined: the quickest way to place marks. It jumps between moments, so film a flail's chain with
  `held.motion.play` and `engine.time.advance` instead.
- Without the container, or under `classic`, each command answers why and how to fix it.
