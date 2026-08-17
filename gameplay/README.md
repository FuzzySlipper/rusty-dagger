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
  (`stats`, `actors`, `actions`, `items`, `rules`, `encounters`). Entries
  read as data with builder helpers, not control flow.
- `src/packages/` — one entry per package composing catalogs into the
  deterministic envelope. Materialization walks this directory.

House rules:

- Catalogs import only from `../authoring/mod.js`.
- Adding content (an actor, action, item, rule, encounter) is a one-file
  catalog edit. Extending the grammar itself means editing `authoring/` and
  the Rust compiler in `crates/dagger-rpg/src/resolution/` in the same
  change — that coupling is intentional.
- All randomness is bounded named evidence (`dice`, `weaponDice`); the
  caller supplies roll values, so resolution is deterministic and
  replayable.
- Content is inclusive classic Daggerfall data. Nothing here is scoped or
  validated down to "only what the current dungeon needs".

## Commands

```bash
pnpm gameplay:build   # typecheck, compile, materialize data/gameplay/*.package.json
pnpm gameplay:check   # build + verify the committed package has no drift
cargo run -p dagger-runtime --bin dagger-gameplay-check
```
