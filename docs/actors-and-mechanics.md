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
also have a pose, and players have progression. `DaggerActorFactory` creates
every non-player actor through one path that also attaches pursuit memory and
the enemy-senses memory the behavior policy reads. It explicitly registers and
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
resources explicitly. The Dagger session owns the inventory store for its
lifetime. `DaggerfallActorRoster` owns which definition each live actor was
registered from and which actors were spawned; its spawn, retirement and site
unload paths explicitly end affected effects and destroy owned items before
removing the actor; directory destruction alone does not provide that gameplay
policy.

## Stats and recovery

`DaggerfallMechanicsState.CreateStats` builds an Engine `StatsComponent` directly.
Combat, HUD, rewards and stamina recovery use those same `Stat`/`Track` objects.
A track shares its maximum Stat; changing that stat reconciles the live track.
Player progression is attached, while Dagger retains XP and level-up policy.
`PassiveTrackRecovery` in Kit implements rate, quiet delay and fractional carry;
Dagger decides which admitted actions delay stamina recovery and when recovery
is allowed. The active-effect lifecycle is described below; individual spell and
effect families remain separate gameplay work.

`DaggerSessionPersistence` captures and restores the current source-generated
payload. It rebuilds authored sources before applying saved track currents, so
aliases and shared maximums remain shared. The payload has no schema version,
migration negotiation, compatibility fingerprint, or unknown-field preservation.
It keeps meaningful state and relationships, including charged combat cooldowns;
held input, AI/perception work, native continuation, presentation, and an
in-flight attack are reconstructed or transient after restore.

## Active effects

Kit `ActiveEffectLifecycle` coordinates stable instance context, round counters
and reversible cleanup over the actor's Engine `EffectsComponent`. Dagger's
`State.Effects` supplies compiled definitions, like-kind policy, effect-specific
state and outcomes. Start applies an initial magic round; resume does not.
The session advances rounds from admitted game-calendar minutes. Explicit elapsed
catch-up follows Dagger's two-day bound and creates no second clock.

`RefreshDuration` retains the incumbent's source, settings, stacks, state and
contributions; it changes only remaining rounds. Replacement, cancellation,
expiry, actor/item retirement and session disposal use the cleanup owner.
Current saves retain durable references, never runtime entity handles.

Capture is read-only. Stat sources carrying effect provenance are rebuilt with
fresh actor identities before tracks, through Engine's existing capture/rebuild
helper. A compiled definition that applies contributions supplies a separate
resume callback to bind cleanup to restored state without applying a second
contribution or replaying the initial round. The session composes one
`DaggerfallEffectCatalog` from the disease (`DaggerfallDiseasePolicy.Definitions`)
and poison (`DaggerfallPoisonEffects.Definitions`) families, so a saved effect
names the definition that interprets it; unknown effect definitions fail clearly.

## Ranged delivery

Kit attack execution releases a delayed impact to Dagger's flight policy.
The session records release origin/aim and advances travel inside admitted
updates; arrival checks the target's current position for a dodge. Flight is
transient across saves and discarded when its generation or combatants expire.
The current delivery still prerolls hit/damage and never collides with an
intervening actor's body. Admitted static geometry on the release line stops a
shot through the Engine's spatial query. The published classic arrow mesh follows
the transient flight through the ordinary appearance snapshot.

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

Worn social enchantments contribute temporary sources through `State.Social`:
GoodRepWith adds ten and BadRepWith subtracts ten from the selected social group;
All means the five named groups, while the Masque affects all eleven groups using
live Personality divided by five. Contributions sum across equipped items and
never change permanent standing. Faction and NPC reactions read those same sources.
Equipment changes, retirement and disposal remove the item's sources; restore
rebuilds them from saved item metadata and equipment.

BadReactionsFrom applies only while the matching humanoid, animal or Daedra group
is strictly within eight metres. Each matching worn source subtracts five from
player attack chance. Its armor contribution follows the donor's literal minus
five in the nonstacking decreased-armor slot, including the sign difference from
the donor comment; StrengthensArmor remains a separate slot. The current enchant
action accepts one setting or one loaded bundle per item. Mixed construction,
same-item parameter exclusivity and drawback budgets belong to item-making;
within one construction, All excludes other options of its own family and a
Good/Bad pair with the same group excludes its opposite. BadReactionsFrom adds
no custom parameter exclusivity. Equipped items may
independently carry opposing social sources.

`DaggerfallState` names the session's owners. The session constructs it once,
from `DaggerActorFactory`'s `DaggerActorAssembly` and the services built over
it, so no member is null or replaced; `DaggerfallActorInventories` gives any
live actor's inventory and equipment coordinators. `DaggerfallState.Kit` is the
Dagger session's discoverable route to the named Kit owners: actors, targeting, attack capabilities and execution, combat
resolution, inventory, and equipment. Kit's `Combat`, `Targeting`, `Ai`, and
`Loot` namespaces provide the reusable mechanisms; `DaggerCombatRules` supplies
Daggerfall eligibility, formulas, timing, and authored meaning. Direct reads and
actions use those owners. Typed facts and RuleEvents remain available where an
interaction has real contributors; ordinary gameplay does not require a
proposal/acceptance or replay protocol.
