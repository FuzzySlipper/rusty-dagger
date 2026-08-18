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
attribute and skill is a stat (classic 0..=100), each declared armor part is
a signed `armor-<part>` stat (classic sbyte range −128..=127; good gear
drives armor negative), and each track gets a synthetic `{track}-max` stat so
its maximum is stat-derived. The item
vocabulary binds into the same catalog: compiled items become upstream item
definitions (fungible stacks vs unique entities, `weightUnits` as a `weight`
capacity cost, equipment policy with the `hands` exclusivity group for
two-handed weapons and shields), the optional `equipment` payload section
becomes the upstream capacity metrics and equipment slots, and each
armor/shield item carries an attributed source (donor
`UpdateEquippedArmorValues`: its value × 5 subtracted per covered body part)
so equipping armor lowers the matching `armor-<part>` stats while equipped.
The Dagger
expression evaluator computes track maxima at spawn (derived rules are
arbitrary Dagger-owned expressions the neutral catalog does not model) and
stores them as the entity's stat bases. `spawn_actor` is the single spawn
authority; it attaches upstream inventory and equipment components to every
actor, replicates the actor's flat `armorValue` into each `armor-<part>`
base, binds the authored spawn loadout through `InventoryService::grant`
and `EquipmentService::equip` (unique items are contained item entities), and
rejects a loadout over the actor's capacity limit.
Resolution reads live stats through `StatService`, and effects
commit through `TrackService` inside the kernel's staged transaction. The
live runtime (`DaggerRuntime`) holds the same mechanics-backed
`DaggerGameplayState`, so the player and every combatant enemy resolve
through the same binding in the product. The committed package is the only
source of gameplay truth; Dagger Lab is a read-only explorer over the
admitted definitions, live state, and resolution explanation.

The initial authored slice crosses the important boundaries with real
content. `gameplay/src/catalogs/stats.ts` declares the classic attribute,
skill, and body-part vocabulary; `actors.ts` defines the player plus
table-driven classic
monsters (Rat, Skeletal Warrior) with derived track maximums and encounter
behavior tuning; `actions.ts` authors the shared melee hit-check shape (d100
against the equipped weapon's skill plus the struck body part's armor,
luck/agility differentials,
and the target's dodging penalty, clamped 3..97 — player melee adds the
swing/proficiency/racial and adrenaline classic terms) and each actor's
attack as resolution programs; `items.ts` carries weapon damage ranges that
`equippedWeaponDice` rolls read live from the acting subject's equipment
(unarmed attacks read the derived hand-to-hand range); `equipment.ts` carries
the classic slot/capacity vocabulary; `encounters.ts` owns named encounter
content. All
rolls are bounded named evidence supplied by the caller (dice bounds may be
negative — swing modifiers span -10..+10). The hit check's struck body part
crosses as `{action}.struck-body-part` (0..19, mapped through the donor's
`CalculateStruckBodyPart` table to the target's `armor-<part>` stat), and the
weapon damage roll as `{action}.equipped-weapon-damage`; the runtime rolls
both deterministically (salts 3 and 2) against live equipment so evaluation
bounds never spuriously reject. Career/world facts
(proficiency, racial bonuses, rapid-healing and no-regen flags) cross the
same way, 0 until careers are modeled, so resolution is deterministic and
replayable. Beyond stats and evidence, expressions read live track currents
(`track`), spawn-derived track maxima (`trackMax`, the `{track}-max` stat),
and fixed-point powers (`powMilli`, base^exponent scaled by 1000 with floor
at each step). Division has floor (`divFloor`) and truncating (`divTrunc`)
forms; signed differentials use `divTrunc`, the donor's C# integer
semantics. Player and AI origins enter the same policy path.

Two combat rules live in the Rust authority rather than the authored
expressions, like the remaining-health clamp. The classic material gate
(donor: `target.MinMetalToHit > weapon material` means 0 damage, surfaced as
materialIneffective) clamps a Damage plan to 0 with a `MaterialIneffective`
trace detail when the target's `minMetalToHit` outranks the attacker weapon's
material (iron < steel < silver < … < daedric); unarmed attacks are always
effective because classic has no bare-hand material. And the runtime's
equip-cycle verb (KeyE in the native host) rotates the player's carried
equippable items through their legal slots via `EquipmentService` swap
semantics, logging every equip/unequip/swap receipt into the readout's
`equipmentLog`; combat records report the weapon used (or "unarmed"), the
struck body part, and material-ineffective outcomes.

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
