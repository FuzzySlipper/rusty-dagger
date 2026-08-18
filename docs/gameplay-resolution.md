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
   operations, transaction behavior, events, and trace details to
   the Engine resolver.

The Engine knows sequencing, conditional execution, resolution phases,
preview/apply, ordered interception, child correlation,
staged commit, quotas, and receipt/trace collection. It does not know health,
magicka, spells, items, actors, damage, or Daggerfall formulas.

Durable stat and track state is mechanics-backed. Package admission also
builds an Engine `MechanicsCatalog` from the declared vocabulary: each
attribute and skill is a stat (classic 0..=100), and each track gets a
synthetic `{track}-max` stat so its maximum is stat-derived. The item
vocabulary binds into the same catalog: compiled items become upstream item
definitions (fungible stacks vs unique entities, `weightUnits` as a `weight`
capacity cost, equipment policy with the `hands` exclusivity group for
two-handed weapons and shields), and the optional `equipment` payload section
becomes the upstream capacity metrics and equipment slots. The Dagger
expression evaluator computes track maxima at spawn (derived rules are
arbitrary Dagger-owned expressions the neutral catalog does not model) and
stores them as the entity's stat bases. `spawn_actor` is the single spawn
authority; it attaches upstream inventory and equipment components to every
actor and binds the authored spawn loadout through `InventoryService::grant`
and `EquipmentService::equip` (unique items are contained item entities).
Resolution reads live stats through `StatService`, and effects
commit through `TrackService` inside the kernel's staged transaction. The
live runtime (`DaggerRuntime`) holds the same mechanics-backed
`DaggerGameplayState`, so the player and every combatant enemy resolve
through the same binding in the product. The committed package is the only
source of gameplay truth; Dagger Lab is a read-only explorer over the
admitted definitions, live state, and resolution explanation.

The initial authored slice crosses the important boundaries with real
content. `gameplay/src/catalogs/stats.ts` declares the classic attribute and
skill vocabulary; `actors.ts` defines the player plus table-driven classic
monsters (Rat, Skeletal Warrior) with derived track maximums and encounter
behavior tuning; `actions.ts` authors the shared melee hit-check shape (d100
against skill plus target armor vulnerability, luck/agility differentials,
and the target's dodging penalty, clamped 3..97 — player melee adds the
swing/proficiency/racial and adrenaline classic terms) and each actor's
attack as resolution programs; `items.ts` carries weapon damage ranges that
`weaponDice` rolls read; `equipment.ts` carries the classic slot/capacity
vocabulary; `encounters.ts` owns named encounter content. All
rolls are bounded named evidence supplied by the caller (dice bounds may be
negative — swing modifiers span -10..+10), and career/world facts
(proficiency, racial bonuses, rapid-healing and no-regen flags) cross the
same way, 0 until careers are modeled, so resolution is deterministic and
replayable. Beyond stats and evidence, expressions read live track currents
(`track`), spawn-derived track maxima (`trackMax`, the `{track}-max` stat),
and fixed-point powers (`powMilli`, base^exponent scaled by 1000 with floor
at each step). Player and AI origins enter the same policy path.

`dagger-gameplay-check` is the production Rust diagnostic. It admits the
committed package, resolves the same controlled action for player and AI
origins, verifies equivalent authoritative state, prints the structured
resolution readout, and prints the spawned player's upstream inventory view
(capacity usage, stacks, and equipped item entities). The readout exposes
package identity, status, commit mode,
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
