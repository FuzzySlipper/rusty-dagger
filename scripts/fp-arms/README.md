# First-person arms

3D first-person arms for the held viewmodel (`viewmodel.style` `held` or `heldRetro`): Ironbelly's MedievalFPArms
(the AAA First Person Arm Pack's male leather armour arms) playing the FPP melee clips that came with the old Rift
Riders Unity game, both on the UE4 mannequin skeleton. The arms are clip-led: each weapon family loops its idle clip at
rest and plays a clip for every attack, fitted to the attack's timing in the rules so the hit lands on the clip's strike
mark. The item wielded chooses the family and the look, and its metal tints the look. The classic sprite style (`classic`) is untouched.

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
    holds and its hold, measured where the clips' `weapon_r` bone sits, `idle` the loop at rest and its length, `guard` the clips that raise, hold and lower the weapon, each over its seconds, `class`
    the weapon class the workbench times it by, `powerHold` what the arms do while a power attack stays charged (a
    `clip` looped every `seconds`, or held still `at` a fraction of it), and `strikes` an Engine `ArmClip` per attack id
    with its `marks` (start, strike, recover, end as fractions of the clip) and `fade`. `twoHanded` lets the off hand
    play every clip (it otherwise rests out of view); `leftHand` lists attacks whose clips move it.
  - `shield`: how a shield on the off arm draws. `family` is the family whose clips were made for one, `seat` where a
    shield sits on the off hand's joint, `carry` where the hand holds it in view, `guard` how the arms raise it, and
    `items` each shield's model by item template, with its own `seat` where it differs.
  - `items`: what an item template changes, keyed by template index: its `family`, and its `model`, `grip` and `hold`
    where they differ from that family's look. A template not listed draws its classic visual's family and look.
- **The container**, `local/first-person.rpak` (ignored): both arms GLBs, each carrying every clip the definition names
  (not clip packs: the Engine shares an opened model's packs between everything that opens it, so a second set of
  arms, a weapon swapped or another site's viewmodel, could not associate them again), and `provenance.json`.

`DaggerfallHeldArt` reads the definition when the container is installed beside the product. The rules time every attack
(`attacks` in the tuning: windup, commit and recovery per weapon class), and the wielded weapon's own class sets the
timing, so a mace plays the sword-and-shield clips at blunt timing. A clip plays through its marks over those phases on
world time: the strike mark lands where the windup ends, and the blow lands there whether or not anything draws it. A
chained attack is a new clip fading in over the recovery. An attack a family has no clip for shows its sprite.

### Families and items

| Family | Visuals | Fast attacks | Power attack | Cast | Hands |
|---|---|---|---|---|---|
| `dagger` | `weapon.dagger.steel` | `FPP_Dagger`: AttackR, AttackLSlash, AttackD, AttackF | AttackHeavyD, held at the end of its Start | Magic | one |
| `longsword` | `weapon.longblade`, `weapon.axe`, `weapon.flail`, `weapon.warhammer` | `FPP_Longsword`: Attack_R, _L, _D, _F | AttackHeavy_D | Magic | both |
| `sns` | `weapon.mace` | `FPP_SnS` (sword and shield; the shield hand rests out of view): Attack_RD, _LU, _RU, _F | AttackHeavyU | Magic1 | one |
| `halberd` | `weapon.staff` | `FPP_Halberd`: Attack_R, _LD, _D, _F | AttackHeavy_R | Magic | both |
| `unarmed` | `weapon.unarmed` | `CHANM_FPArms_Unarmed_Punch`, then the same `.mirrored`, turn about | the punch, held drawn back | `FPP_sns_Magic1` | both |

`fastA` to `fastD` are the four fast attacks in the order a chain cycles them, so a cut from one side is followed by one
from the other, then a downward blow and a thrust. Every plain clip starts and ends at its family's idle, so any can
follow any. A chain cuts a clip's long tail at its recovery. `powerIn` plays a heavy clip's Start, `powerHold` holds it and
`powerOut` plays its End; each family's heavy clip was chosen for meeting its own hold. The dagger's Start ends far from
its hold clip, a plain idle, so its hold stays on Start's last frame. The halberd's End waits still for over a second
before it swings, so its `start` mark skips the wait. `kick` plays the family's Kick, whose strike mark is where a foot reaches furthest. The bow and the werecreature have no
fitting clip and keep their sprites.

The pack has one punch, so a clip name ending `.mirrored` bakes it the other way round for the second fist. The bare hands
borrow the sword-and-shield set's guard, kick and cast.

`cast` is the left hand's, so every family lists it in `leftHand` and the weapon stays in the right hand. The rules time
it, and the spell leaves at the clip's strike mark, the palm thrown forward. Until then the element's missile burns on the
palm: `DaggerfallSiteAppearance` draws the same sprite the spell flies as, in the art chosen, on the viewmodel layer where
`DaggerfallHeldViewmodel.CastPalm` samples the left hand in the clip. With the weapon sheathed the `unarmed` arms come up
for the cast and go again. With no family drawn (a bow, a beast's claws) the element's classic casting hand draws instead.

A shield goes on the left hand with an Engine joint attachment, so the renderer carries it and nothing polls the joint.
The arms skeleton's root is scaled 0.01 and its joints are in centimetres, so a `seat` is in centimetres with `scale` 100.
With a shield up every one-handed weapon plays the `sns` clips, as they were made for it, and the weapon keeps its model.
Those clips hold the shield out of view at rest, so the hand keeps to `carry` through idle and every strike, and leaves
it only to raise the guard or cast, easing back after. `held.shield` moves a seat or carry in the running game to place
them by eye, and answers with what to save.

The rules' guard (the `block` intent) plays the family's `guard` clips, or the shield's with a shield up. They run faster
than authored, because the guard counts from the moment it is raised.

A classic visual covers weapons of both kinds: `weapon.longblade` draws every sword and `weapon.axe` both axes. So the
family follows the item's handedness in Daggerfall, and `items` moves the one-handed items off their visual's family:
the tanto (114) to `dagger`, and the shortsword, wakazashi, broadsword, saber, longsword, katana and battle axe (116 to
121, 127) and the club (288) to `sns`. The claymore, dai-katana and war axe stay on `longsword`, as do the flail and
warhammer, which are two-handed too; the mace is one-handed, so its visual is on `sns`. Every melee weapon but the dagger,
whose family's look is its own, is listed for its model. The sword-and-shield idle holds the weapon upright beside the face, so a
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

- grows a low leg and boot on each of the skeleton's legs and joins them to the arms' mesh: the pack has arms only, and a
  kick needs something to kick with. Rings run down the bones with small plain textures, so none of it is the pack's art;
- imports the male leather arms (LOD0) with their medium colour and normal maps at 1024 px; the retro arms carry the
  colour map alone at 256 px, drawn nearest, as Daggerfall draws its world;
- bakes each clip as its own camera bone sees it (the pack's own clips have only `camera_mount`, with other axes, and take
  the arms' skeleton as it is): every frame the whole body is moved so the camera bone sits at the
  origin looking down the view, so the arms are placed with their origin at the game's eye and the clip carries only
  what moves in view. The clips' skeleton (root, camera and weapon bones) differs from the arms' (no root,
  `camera_mount`, `weapon_mount`) but shares its rest joints, so each bone takes the clip's motion from its own rest;
- writes `local/fp-arms/measure.json`: the shoulder, the palm, each family's look grip and hold, its idle length, and
  per attack the blade's speed per frame and suggested marks. The strike mark is the frame of the swing's fastest burst
  whose blade (30 cm along the weapon bone's +Z, the pack's blade axis) lies nearest the centre of the view; the end is
  where the clip has come back to rest; the recovery lies three tenths of the way between, the commit's share of the
  time after the hit in the tuning, so the clip plays at one pace after the hit. A `cast` is measured on the left hand,
  not the blade, and its recovery lies much nearer the strike, because its commit is only the moment the spell leaves. A
  `powerIn` clip is all windup, so its strike mark is where its motion ends. Copy what the definition should take from
  it, then tune in the game.

The clips were animated for a 90° horizontal field of view (about 59° vertical at 16:9); Dagger's 65° vertical shows
them a little smaller than authored.

## Tuning: the workbench and contact sheets

In `rusty dev --output window --live-debug` with `held` or `heldRetro`, the developer tools' **Workbench** tab and the
`held.motion*` commands (`docs/playtesting.md`, "Developer commands") show a look with a chosen attack, hold each mark,
scrub to any moment and play at any speed, and tune the marks and fade, the idle loop, the look's grip and hold, an
item's own grip and hold, the shoulder and the palm. A look is a classic visual or `item.N` for template N. **Save** writes them into the held definition. `held.clip` reads what the arms play now, in play
or in the viewer.

`scripts/fp-arms/sheet.sh ORIGIN LOOK OUT_DIR [STEP] [ATTACK...]` drives the viewer over a running host and writes a
contact sheet per attack, every STEP seconds of the attack as the rules time it, its hit outlined: the quickest way to
place marks.

## Not yet

- The other arm sets (female leather, male and female cloth, male chainmail) are not built: a set chosen by the
  player's gender and armour needs its own bake where its skeleton's rest differs.
- The bows draw their sprites: there are no clips for them yet, so their models wait for those.

## Weapon models

`content/worldrpg/first-person/weapons/*.glb` are the game's own weapon models, committed beside the definition and
packed by `build.sh` at `first-person/weapons/`. They are made in `authoring/tools/weapons` from the refreshed
paper-doll icons (`authoring/docs/weapons.md`), so they carry no Asset Store art and are ordinary committed content.
Each is in the Engine's held-look frame (origin at the grip's centre, length along -Z, +Y one edge), so a family's
`look.grip` and `look.hold` seat it where the clips' `weapon_r` bone held the pack's weapon without retuning. Its
steel parts are their own `metal` material and the other parts are `trim`. The game tints the `metal` material with the
item's metal, from the same `dyeTints` the paper doll draws with; steel stays untinted.

An item's `chain` in the definition hangs the model's links and weight as an Engine ragdoll (`HeldChain`, in a Dynamics
world of its own): the handle joint follows the hand, and the rest swing below it. That world is in view space, so
turning the view does not swing the chain, and its `gravity` is ours to choose: the flail's leans right, so the ball
hangs off the edge of the view at rest and only a strike brings it in. The chain collides with itself and the haft, not
the hands. Held, `heldDamping` makes its links sluggish, so walking and the idle leave it still; a strike lets it go
until the strike ends. It never sleeps, since it hangs from a bone that follows the pose. `heldRetro` steps the chain
when the hand jumps and at least once per classic frame, because stepping between jumps tears the chain apart.

## Reused from rusty-riders

Riders was a one-time donor of its clip-led arms: its `ArmClipCatalog`/
`MotionClips`/`ArmsLead` became the families here, keyed by attack ids and timed by the rules rather than Riders'
action phases; its `HeldView` lead (the look riding the main hand, the empty off hand resting out of view
unless `leftHand` or held in both hands) became `DaggerfallHeldViewmodel`; its `leather` rig profile became `rig`; its
`riders.dev.held.blade` readout became `held.clip`; and its `scripts/arm-clips/in-game.sh` became `sheet.sh`. Riders'
retargeting (`retarget.py`) is not needed: these clips are first person already.
