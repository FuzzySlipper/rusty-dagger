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
effective because classic has no bare-hand material. And the runtime owns the
player's equipment verbs: the equip-cycle (KeyE in the native host) rotates
carried equippable items through their legal slots via `EquipmentService`
swap semantics, while the lab panel drives explicit `equip_item`,
`unequip_slot`, and `grant_item` verbs through the lab server
(equip/unequip/grant routes). `grant_item` is experiment instrumentation for
fungible stacks only (unique items equip; entity allocation stays with the
spawn loadout) and exists to exercise capacity-limit rejections live. Every
attempt — success or upstream rejection — appends an equipment-log record
with the operation, item/slot, and rejection reason; combat records report
the weapon used (or "unarmed"), the struck body part, and
material-ineffective outcomes.

Progression is the kill-XP experiment profile. The stats section declares a
`progression` category (`xp`, `level`) that compiles to wide-range mechanics
stats (0..=1_000_000 — xp accumulates past the classic attribute range);
`spawn_actor` attaches them to player-kind actors only (xp 0, level 1), never
from the actor stat maps, and monsters never carry them (their classic
`level` is unrelated definition data). Monster and class-enemy definitions
declare `xpReward` (initial profile: classic level × 50, authored in
`monsters.ts`/`actors.ts`); the player declares `hitPointsPerLevel: 8`, the
career-owned roll bound. When a player-origin resolution leaves an enemy
dead, the runtime calls `dagger-rpg`'s progression authority
(`award_kill_progression`): xp accumulates on the killer's `xp` stat base,
the derived `xp-level` rule — the experiment pacing curve, floor(live xp /
500), tunable as catalog authoring — maps the post-award total to thresholds
crossed, and each level gained evaluates the classic
`hit-points-per-level-up` rule with a bounded roll crossing as
`<killer>.level-up.<level>.hp-roll` evidence (rolled from the runtime's
salt-5 deterministic stream with a per-player level-up sequence) and applies
the result to the `health-max` stat base AND to current health, clamped to
the new maximum through the track service. The live level is the spawn base
(1) plus the thresholds crossed. Only player kills award; AI kills don't.
The derived rules evaluate against LIVE component stats (policy-style), never
definition bases — `xp-level` reads live xp and only exists in these
live-state contexts (award, lab readout). Stat-base mutations go through the
Engine's `StatService::set_base`, so catalog bounds and track reconciliation
stay upstream-owned. Every award returns a structured `DaggerProgressionRecord`
(victim, xp before/after, levels, per-level roll evidence and health-max
changes) appended to a capped progression history; the lab's read-only
Character panel renders live xp/level, xp-to-next from the curve's own
divisor, health, and the award history. Progression persistence is
session-scoped: the lab jump verb heals but preserves it, and
`reset_play_session` restores the spawn bases before track restoration.
Classic has no kill XP — the classic skill-use advancement (`player-level` +
`skill-uses-for-advancement`, both expressible in the derived catalog) is the
documented alternative profile, kept for reference, not what the live runtime
evaluates.

Loot follows the donor's corpse-container model. Actor definitions declare an
optional `lootTableKey` naming one of the 22 classic tables; at session spawn
the runtime draws the table's bounded roll contract (`loot_roll_evidence`)
from each entity's deterministic spawn stream and generates contents into the
actor's own inventory (`bind_actor_loot`) — looting a dead enemy transfers
out of its inventory, exactly as classic. The dungeon's random-treasure
markers arrive as treasure content entities (id band 3000+, `lootKey` on the
project entity) and spawn as standalone container entities
(`spawn_container`) with the dungeon-type loot key. The runtime's
interact/pickup verb (`interact_loot`, KeyF in the native host) aims a cone
query at dead enemies and treasure piles within 2.5 units and performs a
take-all transfer through `InventoryService::transfer` /
`EquipmentService::transfer_unique_item`, stopping at the first capacity
rejection; every transfer, the stopping rejection, and the empty-container
note are equipment-log records under `loot:<container>`. The lab's loot panel
lists every container with its live contents (upstream
`InventoryService::view`), the spawn-time generation receipt (including
unsupported-category coverage), and emptied state — read-only; pickup stays
on the native verb.

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
