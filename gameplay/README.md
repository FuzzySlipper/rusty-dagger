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
  material, shields, gold, arrows) and `equipment` the classic slot and
  capacity-metric vocabulary items bind against.
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
- Adding content (an actor, action, item, rule, encounter) is a one-file
  catalog edit. Extending the grammar itself means editing `authoring/` and
  the Rust compiler in `crates/dagger-rpg/src/resolution/` in the same
  change — that coupling is intentional.
- All randomness is bounded named evidence (`dice`, `weaponDice`); the
  caller supplies roll values, so resolution is deterministic and
  replayable. Career/world facts (proficiency, racial bonuses, swing state,
  rapid-healing and no-regen flags) cross as bounded named evidence too,
  0 until careers and swing states are modeled. Dice bounds may be
  negative (swing modifiers span -10..+10).
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
