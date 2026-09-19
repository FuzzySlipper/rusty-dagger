# Actors and live mechanics

`WorldRpg.Kit/Actors/ActorsState.cs` owns one session `EntityDirectory`, whose
`EntityStore` holds the attached class components. `DaggerActorFactory` is the
Daggerfall assembly seam: it creates the player and authored actors from admitted
definitions, then wires their inventories and equipment. `PlayerActorState` and
`ActorState` compose Engine `Actor`; wrapping an existing entity adds nothing and
does not own its lifetime.

Named properties (`Stats`, `Effects`, `Inventory`, `Equipment`, and player
`Progression`) read the actual attached objects. The generic actor factory
attaches stats, effects, targeting, attack state and defeat-track metadata; NPCs
also have a pose, and players have progression. Dagger behavior assembly adds
pursuit memory to its NPCs. Dagger's session factory explicitly registers and
attaches inventory/equipment for the player and every placed actor. A facade
property requires that its component has been attached; use Engine `TryGet<T>`
for an optional capability on a differently assembled entity.

## Identity

- `actor.DurableId` is the product actor instance key used by authored placements,
  saves, combat facts and presentation/perception observation keys.
- `actor.Actor.Entity` is the generated runtime `EntityId`. Do not cast a durable
  ID into it. Resolve through the session directory.
- `actor.Actor.TypeId` describes the definition/kind, not the instance.
- `DurableIdentityReference` includes a kind. An actor and its corpse container
  can have the same durable number and distinct runtime entities. Unique item
  references have their own kind, including generated loot identities.

The directory is the sole durable-to-runtime map. `IdentityOf` provides the
reverse lookup from attached metadata. Actor enumeration reads the store; there
is no second actor state dictionary. Destroying a directory entry removes that
entity and its attached components. Native resource owners still dispose their
resources explicitly. The current Dagger session owns the inventory store for
its lifetime; a future live despawn feature must decide disposition of contained
items rather than assuming entity destruction transfers or deletes them.

## Stats and recovery

`DaggerfallMechanicsState.CreateStats` builds an Engine `StatsComponent` directly.
Combat, HUD, rewards and stamina recovery use those same `Stat`/`Track` objects.
A track shares its maximum Stat; changing that stat reconciles the live track.
Player progression is attached, while Dagger retains XP and level-up policy.
`PassiveTrackRecovery` in Kit implements rate, quiet delay and fractional carry;
Dagger decides which admitted actions delay stamina recovery and when recovery
is allowed. `EffectsComponent` is attached for discoverable effect state; this
refactor does not implement the pending spell/effect gameplay backlog.

`DaggerSessionPersistence` captures and restores the current source-generated
payload. It rebuilds authored sources before applying saved track currents, so
aliases and shared maximums remain shared. The payload has no schema version,
migration negotiation, compatibility fingerprint, or unknown-field preservation.
It keeps meaningful state and relationships, including charged combat cooldowns;
held input, AI/perception work, native continuation, presentation, and an
in-flight attack are reconstructed or transient after restore.

## Inventory and equipment

Kit coordinators receive the attached `InventoryComponent`/`EquipmentComponent`
and the same session directory. They perform workflows over Engine's one
`InventoryStore`; they do not cache a second item ledger. Definitions and slot
policy are explicitly supplied by Dagger. Unique materialization takes a durable
item reference and returns the allocated runtime item identity. UI item keys are
session-local runtime IDs; saves map them back through `IdentityOf`.

Ordinary grant, consume, equip and unequip calls have no operation/source string
protocol. Multi-item grants, container transfers and equipment reassignment use
Engine's optional inventory edit where the gameplay operation must succeed as a
whole. A failed construction removes its newly allocated entities. Queries read
the live facades after publication; do not retain an `EquipmentState` snapshot
and expect later assignments to appear in it.

## Named gameplay services

`DaggerfallState.Kit` is the Dagger session's discoverable route to the named
Kit owners: actors, targeting, attack capabilities and execution, combat
resolution, inventory, and equipment. Kit's `Combat`, `Targeting`, `Ai`, and
`Loot` namespaces provide the reusable mechanisms; `DaggerCombatRules` supplies
Daggerfall eligibility, formulas, timing, and authored meaning. Direct reads and
actions use those owners. Typed facts and RuleEvents remain available where an
interaction has real contributors; ordinary gameplay does not require a
proposal/acceptance or replay protocol.
