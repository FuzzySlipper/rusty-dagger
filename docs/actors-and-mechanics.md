# Actors and live mechanics

`WorldRpg.Kit/Actors/ActorsState.cs` is the actor entry point. It owns one session
`EntityDirectory`, whose `EntityStore` holds the attached class components.
`CreatePlayer` and `CreateActor` construct entities explicitly. `PlayerActorState`
and `ActorState` compose Engine `Actor`; wrapping an existing entity adds nothing
and does not own its lifetime.

Named properties (`Stats`, `Effects`, `Inventory`, `Equipment`, and player
`Progression`) read the actual attached objects. The generic actor factory
attaches stats, effects and defeat-track metadata; NPCs also have a pose, and
players have progression. Dagger's session factory explicitly registers and
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

For explicit save capture use Engine `StatsComponentCapture`. Rebuild authored
sources with the new runtime owner in its callback **before tracks are rebuilt**;
retain returned modifier handles when later removal is required. Aliases and
shared maximums must remain shared. Current Dagger restore already reconstructs
level-up sources before applying saved track currents; the broader current-schema
save rewrite remains #8339.

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
