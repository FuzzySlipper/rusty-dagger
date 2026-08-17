# Gameplay resolution in Rusty Dagger

Rusty Dagger authors Dagger-specific definitions in TypeScript, admits them in
Rust, and resolves them through Rusty Engine's host-neutral gameplay-resolution
kernel. TypeScript is an authoring language here, not runtime authority.

The boundary deliberately has four representations:

1. `gameplay/src/*.ts` supplies typed builders and Dagger-owned definitions.
2. `data/gameplay/*.package.json` is the deterministic package exchanged with
   Rust, including source provenance.
3. `dagger-rpg::compile_gameplay_package` validates the package and compiles its
   authoring grammar into the Engine's structural `Program` grammar.
4. `dagger-rpg::resolve_dagger_action` supplies Dagger intent, facts, checks,
   operations, interceptors, transaction behavior, events, and trace details to
   the Engine resolver.

The Engine knows sequencing, conditional execution, resolution phases,
preview/apply, ordered interception, child correlation,
staged commit, quotas, and receipt/trace collection. It does not know health,
magicka, spells, items, actors, damage, or Daggerfall formulas.

The initial authored slice crosses the important boundaries with real
content. `gameplay/src/catalogs/stats.ts` declares the classic attribute and
skill vocabulary; `actors.ts` defines the player plus table-driven classic
monsters (Rat, Skeletal Warrior) with derived track maximums and encounter
behavior tuning; `actions.ts` authors the shared melee hit-check shape
(d100 against skill plus target armor vulnerability, clamped 3..97) and each
actor's attack as resolution programs; `items.ts` carries weapon damage
ranges that `weaponDice` rolls read; `encounters.ts` owns named encounter
content. All rolls are bounded named evidence supplied by the caller, so
resolution is deterministic and replayable. Player and AI origins enter the
same policy path.

`dagger-gameplay-check` is the production Rust diagnostic. It admits the
committed package, resolves the same controlled action for player and AI
origins, verifies equivalent authoritative state, and prints the structured
resolution readout. The readout exposes package identity, status, commit mode,
effects, semantic events, and the phase trace. A future Dagger Explorer can
render these records alongside source definitions; it does not need a runtime
value editor or a second implementation of resolution logic.

Regenerate and check the package with:

```bash
pnpm gameplay:build
cargo run -p dagger-runtime --bin dagger-gameplay-check
```

New gameplay should normally start in the TS catalog and Dagger policy types.
Do not encode a spell, item, rule, or formula as a new branch in patrol,
presentation code, or browser UI. Extend the Engine only when the missing seam
is host-neutral structure that also makes sense for a mechanically different
consumer.
