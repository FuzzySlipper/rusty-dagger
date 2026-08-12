# Daggerfall Unity donor code map

Use this map to seed searches in the frozen Daggerfall Unity corpus. It is a
navigation aid, not a substitute for reading source or verifying parser and
extraction claims against the real Arena2 files.

## Corpus

- Source: `/home/research/daggerfall-unity`
- Codebase Memory project: `daggerfall-unity`
- Declared revision: `81e89e90c27bc3c1a7a61871e545fad129174dec`
- Revision caveat: the checkout has no `.git` metadata; do not treat an index
  branch label as provenance.

## Query seeds

| Concept | DFU names and starting paths |
|---|---|
| Archive and record parsing | `BsaFile`, `MapsFile`, `BlocksFile`, `Arch3dFile`, `Assets/Scripts/API/` |
| Mesh decoding and triangulation | `DFMesh`, `MeshReader`, `Arch3dFile` |
| Dungeon block assembly | `RDBLayout`, `DaggerfallDungeon`, `Assets/Scripts/Internal/` |
| Formulas and classic calculations | `FormulaHelper`, `Assets/Scripts/Game/Formulas/` |
| Weapon input and attack flow | `WeaponManager`, `FPSWeapon`, `WeaponBasics` |
| Damage, fatigue, blood, and effects | `WeaponDamage`, `DecreaseFatigue`, `EnemyBlood`, `EntityEffectManager` |
| Enemy identity and mobile tables | `EnemyBasics`, `MobileTypes`, `MobileEnemy` |
| Enemy state, animation, and facing | `DaggerfallMobileUnit`, `ApplyEnemyState`, `UpdateOrientation`, `OrientEnemy`, `AnimateEnemy` |
| Player and entity behavior | `PlayerEntity`, `DaggerfallEntity`, `DaggerfallEntityBehaviour` |
| Items and equipment | `DaggerfallUnityItem`, `ItemBuilder`, `Assets/Scripts/Game/Items/` |
| Magic and status effects | `Assets/Scripts/Game/MagicAndEffects/`, `EntityEffectManager` |
| Save semantics | `Assets/Scripts/API/Save/`, `Assets/Scripts/Game/Serialization/` |

## Consultation depth

For a narrow value or record interpretation, inspect its definition and the
consumer that gives it meaning. For behavior, inspect the entry point, state or
data model, core implementation, and meaningful callers/callees. For a proposed
subsystem, follow the connected donor flow far enough to explain where state is
owned and when effects occur.

Record whether the model was adopted, adapted, rejected, or not found. Explain
intentional differences from DFU rather than silently replacing a working
classic model with an original one.
