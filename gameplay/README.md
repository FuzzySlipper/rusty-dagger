# Dagger gameplay authoring

This workspace is the normal home for Dagger gameplay definitions: stats,
actors, actions, items, rules, and encounters. TypeScript here is an
authoring language only — it never evaluates. Rust
(`crates/dagger-rpg::resolution`) admits the materialized package, owns its
meaning, and is the only evaluator. See `docs/gameplay-resolution.md`.

## Layout

- `src/authoring/` — the grammar: expressions, programs, definition shapes,
  envelope composition. `mod.ts` is the single import surface for catalogs.
- `src/catalogs/` — the everyday editing surface, one file per domain
  (`stats`, `actors`, `monsters`, `actions`, `items`, `equipment`, `rules`,
  `encounters`, `derived`). Entries read as data with builder helpers, not
  control flow. `actors` holds the player and class-career enemies;
  `monsters` holds the full classic table-driven monster table; `derived`
  holds the named classic formula catalog; `items` holds the classic iron-tier
  item vocabulary (weapons with damage/handedness/skill, armor valued per
  material, shields, gold, arrows), `equipment` the classic slot and
  capacity-metric vocabulary items bind against, and `loot` the 22 classic
  letter loot tables (donor `LootTables.cs` `DefaultLootTables`).
- `src/packages/` — one entry per package composing catalogs into the
  deterministic envelope. Materialization walks this directory.

House rules:

- Catalogs import only from `../authoring/mod.js`.
- Items carry weight in the classic quarter-kg unit (`weightUnits`) and value
  in gold pieces. An item with a weapon/armor/shield block is a unique
  equippable entity; an item without one is a fungible stack. Actor
  definitions may declare a spawn `inventory` loadout (`item`, `quantity`,
  `equipSlot`); at spawn Rust binds it into upstream `InventoryComponent` /
  `EquipmentComponent` state through the Engine's inventory and equipment
  services, with the weight capacity limit derived from `max-encumbrance`
  (kg) in quarter-kg units.
- Monsters and class enemies may declare `lootTableKey` naming one of the 22
  classic loot tables. At session spawn Rust generates the table's contents
  into the actor's own inventory (the donor corpse-loot model), so looting a
  dead enemy transfers out of its inventory; the treasure containers placed
  from the dungeon's random-treasure markers (RDB archive 199 record 19)
  generate from the dungeon's loot key (Privateer's Hold: MAPS.BSA type 2,
  Human Stronghold → "N") into standalone container entities. Generation is
  deterministic per entity (the spawn evidence stream); pickups happen in
  the gameplay product with the F verb, and the lab lists every container and its
  generation receipt read-only.
- Adding content (an actor, action, item, rule, encounter) is a one-file
  catalog edit. Extending the grammar itself means editing `authoring/` and
  the Rust compiler in `crates/dagger-rpg/src/resolution/` in the same
  change — that coupling is intentional.
- All randomness is bounded named evidence (`dice`, `equippedWeaponDice`,
  `struckArmor`); the caller supplies roll values, so resolution is
  deterministic and replayable. `equippedWeaponDice` reads an explicit
  evidence id bounded at evaluation by the subject's currently equipped
  weapon (unarmed: the derived hand-to-hand range); `struckArmor` reads a
  0..19 struck-body-part roll and maps it through the classic table to the
  target's `armor-<part>` stat; `equippedWeaponSkill` reads the equipped
  weapon's skill (hand-to-hand when unarmed). Career/world facts
  (proficiency, racial bonuses, swing state, rapid-healing and no-regen
  flags) cross as bounded named evidence too, 0 until careers and swing
  states are modeled. Dice bounds may be negative (swing modifiers span
  -10..+10).
- `stats.ts` also declares the classic body parts (`armorParts`); each
  becomes an `armor-<part>` stat whose spawn base is the actor's flat
  `armorValue` and which equipped armor/shield items subtract from through
  upstream attributed sources.
- `stats.ts` also declares the progression stats (`progression`: `xp`,
  `level`) for the kill-XP experiment profile. They compile to wide-range
  mechanics stats (0..=1_000_000), and the Rust spawn authority attaches
  them to player-kind actors only (xp 0, level 1) — never as actor `stats`
  map keys. Monsters and class enemies declare `xpReward` (initial profile:
  classic level × 50); the player declares `hitPointsPerLevel`, the
  career-owned level-up roll bound [hitPointsPerLevel/2, hitPointsPerLevel]
  (donor `FormulaHelper.CalculateHitPointsPerLevelUp`). When a player kill
  lands, Rust awards the xp, crosses the authored `xp-level` curve
  (floor(live xp / 500) thresholds over the spawn base level 1), and per
  level gained evaluates `hit-points-per-level-up` with a bounded
  `<killer>.level-up.<level>.hp-roll` evidence roll, applying the result to
  health max AND current health. Only player kills award. `xp-level` reads
  live xp, so it evaluates only in live-state contexts (the progression
  award, the lab readout) — never against definition bases. Classic has no
  kill XP: the classic skill-use advancement (`player-level` +
  `skill-uses-for-advancement`) is the documented alternative profile, kept
  in the derived catalog for reference.
- Expressions can also read live track currents (`track`), spawn-derived
  track maxima (`trackMax`), and fixed-point powers (`powMilli` — base^exp
  scaled by 1000, floor at each step). Division comes in floor (`divFloor`)
  and truncating (`divTrunc`) forms; signed differentials use `divTrunc`,
  the donor's C# integer semantics.
- Content is inclusive classic Daggerfall data. Nothing here is scoped or
  validated down to "only what the current dungeon needs".
- Numbers: the package envelope is schema-2 canonical binary64, composed
  through the Engine's `authorBinary64RulePackage`. Approximate tuning
  values (speeds, ranges, cooldowns, multipliers, coefficients) are ordinary
  JSON numbers with explicit units in the field name — never ad-hoc
  encodings like milli-integers. Exact data (dice bounds, counters,
  identifiers, expression constants) stays integer-backed: the Rust
  admission accepts integral binary64 into integer fields and rejects
  non-integral values. The single `f64 -> f32` conversion boundary lives in
  the Rust compiler (`tuning_to_f32`).

## Commands

```bash
pnpm gameplay:build   # typecheck, compile, materialize data/gameplay/*.package.json
pnpm gameplay:check   # build + verify the committed package has no drift
cargo run -p dagger-runtime --bin dagger-gameplay-check
```
