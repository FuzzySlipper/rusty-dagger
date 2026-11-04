# First-person arms

3D first-person arms for the held viewmodel (`viewmodel.style` `held` or `heldRetro`): Ironbelly's MedievalFPArms
(the AAA First Person Arm Pack's male leather armour arms) playing the FPP melee clips that came with the old Rift
Riders Unity game, both on the UE4 mannequin skeleton. The arms are clip-led: each weapon family loops its idle clip at
rest and plays a clip for every classic strike, fitted to the strike's classic timing so the hit lands on the clip's
strike mark. The item wielded chooses the family and the look, and its metal tints the look. The classic sprite style (`classic`) is untouched.

## Licence: never commit the art

The meshes and clips are Unity Asset Store purchases. They may ship inside the game but the raw assets, and any GLB
converted from them, must never be committed: this repository is public. They live in the ignored `local/fp-melee/`
(sources) and `local/first-person.rpak` (the built container), and stay out of tracked content packs. The held
definition and the measured values in it (marks, holds, the shoulder) are the game's own data and are committed.

## The held definition and the container

The game draws the arms from two parts:

- **The held definition**, `content/worldrpg/first-person/daggerfall.held.json`: tracked, authored content.
  - `rig`: the arms model (`first-person/arms.glb`), the clip it is first measured on, the joints (forearm twist
    `lowerarm_twist_01_*`, share 0.5), the palm (where the dagger clips' weapon bone sits in the hand) and the grip
    calibration of the digits.
  - `retroModel`: the same arms with the retro look (`first-person/arms-retro.glb`), which `heldRetro` draws.
  - `arms`: the shoulder, where the clips' right upper arm sits from their camera bone at the start of the rig's clip,
    so the clips' eye is the game's; the elbow pole and the default hand closures.
  - `families`: each weapon family, by the clip set it plays. Its `weapons` are the classic weapon visuals it draws,
    `look` the held weapon (a family with no model has the hand play its clips holding nothing) with the point the hand
    holds and its hold, measured where the clips' `weapon_r` bone sits, `idle` the loop at rest and its length, `strikes` an
    Engine `ArmClip` per classic strike id with its `marks` (start, strike, recover, end as fractions of the clip) and
    `fade`. `twoHanded` lets the off hand play every clip (it otherwise rests out of view); `leftHand` lists strikes
    whose clips move it.
  - `items`: what an item template changes, keyed by template index: its `family`, and its `model`, `grip` and `hold`
    where they differ from that family's look. A template not listed draws its classic visual's family and look.
- **The container**, `local/first-person.rpak` (ignored): both arms GLBs, each carrying every clip the definition names
  (not clip packs: the Engine shares an opened model's packs between everything that opens it, so a second set of
  arms, a weapon swapped or another site's viewmodel, could not associate them again), and `provenance.json`.

`DaggerfallHeldArt` reads the definition when the container is installed beside the product. A strike's sprite still
plays hidden and stays the swing's clock: its windup spans the classic frames before the hit frame, its commit the
frames from the hit to the last and its recovery the last frame, and the clip plays through its marks over those
phases on the world time the sprite reads. A strike a family has no clip for shows its sprite.

### Families and items

| Family | Visuals | Clips | Hands |
|---|---|---|---|
| `dagger` | `weapon.dagger.steel` | `FPP_Dagger`: strikeDown AttackD, strikeDownLeft AttackL, strikeLeft AttackR, strikeRight AttackLSlash, strikeDownRight AttackDHook, strikeUp AttackF; idle `FPP_Dag_Idle` | one |
| `longsword` | `weapon.longblade`, `weapon.axe`, `weapon.flail`, `weapon.warhammer` | `FPP_Longsword`: Attack_D, _RD, _R, _L, _LD, _F; idle Longs_Idle | both |
| `sns` | `weapon.mace` | `FPP_SnS` (sword and shield; the shield hand rests out of view): AttackHeavyU, Attack_RD, _RU, _LU, AttackHeavyL, Attack_F; idle sns_Idle | one |
| `halberd` | `weapon.staff` | `FPP_Halberd`: Attack_D, _RDfast, _R, AttackHeavy_L, Attack_LD, _F; idle Halb_Idle | both |

A strike's clip was chosen by which way its blade moves where it is fastest: a classic `strikeLeft` swings toward the
left, so it plays the clip that cuts from the right. The bow, hand-to-hand and the werecreature have no fitting clip and
keep their sprites (the pack's unarmed punch may serve the fist later).

A classic visual covers weapons of both kinds: `weapon.longblade` draws every sword and `weapon.axe` both axes. So the
family follows the item's handedness in Daggerfall, and `items` moves the one-handed items off their visual's family:
the tanto (114) to `dagger`, and the shortsword, wakazashi, broadsword, saber, longsword, katana and battle axe (116 to
121, 127) and the club (288) to `sns`. The claymore, dai-katana and war axe stay on `longsword`, as do the flail and
warhammer, which are two-handed too; the mace is one-handed, so its visual is on `sns`, and `items` lists it (124) for
its model. The sword-and-shield idle holds the weapon upright beside the face, so a
mace's head sits above the frame at rest and comes into view as it swings.

## Building it

The sources are copied once from the old game into `local/fp-melee/`; nothing reads the old game's folder after that:

```sh
scripts/fp-arms/copy-sources.sh ../rusty-riders/old-game/Assets/MoreArt/FpMelee
scripts/fp-arms/build.sh
```

`build.sh` runs `build.py` under Blender 5 for every clip the held definition names, checks each GLB with
`rusty asset check` and packs `local/first-person.rpak`. A clip is named by its FBX file stem; add one to the definition
and build again. `build.py`:

- imports the male leather arms (LOD0) with their medium colour and normal maps at 1024 px; the retro arms carry the
  colour map alone at 256 px, drawn nearest, as Daggerfall draws its world;
- bakes each clip as its own camera bone sees it: every frame the whole body is moved so the camera bone sits at the
  origin looking down the view, so the arms are placed with their origin at the game's eye and the clip carries only
  what moves in view. The clips' skeleton (root, camera and weapon bones) differs from the arms' (no root,
  `camera_mount`, `weapon_mount`) but shares its rest joints, so each bone takes the clip's motion from its own rest;
- writes `local/fp-arms/measure.json`: the shoulder, the palm, each family's look grip and hold, its idle length, and
  per strike the blade's speed per frame and suggested marks. The strike mark is the frame of the swing's fastest burst
  whose blade (30 cm along the weapon bone's +Z, the pack's blade axis) lies nearest the centre of the view; the end is
  where the clip has come back to rest; the recovery lies two thirds of the way between, so the clip plays at one pace
  after the hit. Copy what the definition should take from it, then tune in the game.

The clips were animated for a 90° horizontal field of view (about 59° vertical at 16:9); Dagger's 65° vertical shows
them a little smaller than authored.

## Tuning: the workbench and contact sheets

In `rusty dev --output window --live-debug` with `held` or `heldRetro`, the developer tools' **Workbench** tab and the
`held.motion*` commands (`docs/playtesting.md`, "Developer commands") show a look with a chosen strike, hold each mark,
scrub to any moment and play at any speed, and tune the marks and fade, the idle loop, the look's grip and hold, an
item's own grip and hold, the shoulder and the palm. A look is a classic visual or `item.N` for template N. **Save** writes them into the held definition. `held.clip` reads what the arms play now, in play
or in the viewer.

`scripts/fp-arms/sheet.sh ORIGIN LOOK OUT_DIR [STEP] [STRIKE...]` drives the viewer over a running host and writes a
contact sheet per strike, every STEP seconds of the strike as play times it, its hit outlined: the quickest way to place
marks.

## Not yet

- The other arm sets (female leather, male and female cloth, male chainmail) are not built: a set chosen by the
  player's gender and armour needs its own bake where its skeleton's rest differs.
- Weapons without a committed model draw only the hand. Each gets its `model` under `items` as it is made.

## Weapon models

`content/worldrpg/first-person/weapons/*.glb` are the game's own weapon models, committed beside the definition and
packed by `build.sh` at `first-person/weapons/`. They are made in `authoring/tools/weapons` from the refreshed
paper-doll icons (`authoring/docs/weapons.md`), so they carry no Asset Store art and are ordinary committed content.
Each is in the Engine's held-look frame (origin at the grip's centre, length along -Z, +Y one edge), so a family's
`look.grip` and `look.hold` seat it where the clips' `weapon_r` bone held the pack's weapon without retuning. Its
steel parts are their own `metal` material and the other parts are `trim`. The game tints the `metal` material with the
item's metal, from the same `dyeTints` the paper doll draws with; steel stays untinted.

## Reused from rusty-riders

Riders was a one-time donor of its clip-led arms: its `ArmClipCatalog`/
`MotionClips`/`ArmsLead` became the families here, keyed by classic strike ids and timed by the classic sprite rather
than Riders' action phases; its `HeldView` lead (the look riding the main hand, the empty off hand resting out of view
unless `leftHand` or held in both hands) became `DaggerfallHeldViewmodel`; its `leather` rig profile became `rig`; its
`riders.dev.held.blade` readout became `held.clip`; and its `scripts/arm-clips/in-game.sh` became `sheet.sh`. Riders'
retargeting (`retarget.py`) is not needed: these clips are first person already.
