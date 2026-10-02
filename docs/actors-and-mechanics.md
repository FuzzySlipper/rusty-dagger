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
effect families remain separate gameplay work. Attribute drains keep one permanent, target-owned
incumbent per attribute and an additive Engine stat source, with a floor of one relative to
the permanent base. Matching partial healing updates that source; complete healing removes
it through the effect lifecycle. Historical cast provenance survives source retirement
without retaining a living caster or item dependency. Disease and poison use separate cure scopes.

`DaggerSessionPersistence` captures and restores the current source-generated
payload. It rebuilds authored sources before applying saved track currents, so
aliases and shared maximums remain shared. The payload has no schema version,
migration negotiation, compatibility fingerprint, or unknown-field preservation.
It keeps meaningful state and relationships, including charged combat cooldowns;
held input, AI/perception work, native continuation, presentation, and an
in-flight attack are reconstructed or transient after restore.

`DaggerfallLodgingState` retains room expiry against the placed tavern and the shared
calendar. The ordinary rest caller reads that privilege and stops when it expires.
Room quotes reuse classic trade pricing and guild privileges; booking spends the
canonical carried coins or letters of credit through the currency owner. Saves retain the booking across
site transitions and reject a room whose tavern no longer exists in the admitted directory.

## Spell casting

`DaggerfallSession.Casting` resolves known/ready spell keys against normalized
records and explicit compiled `(Type, SubType)` bindings in `DaggerfallEffectCatalog`.
Readiness is attached to the canonical actor, retains its quoted cost, and clears
on cancel, release, site transition, save or disposal. Released bundles retain
source/item identity, target shape, element, settings and per-effect outcomes.
Touch uses an Engine capsule sweep; ranged flight consumers submit their actual
segment to `DeliverSpellImpact`; blast candidates come from paged Engine perception.
Admission uses the existing cost/save policies, live actor and biography inputs,
and active defense projections before the lifecycle attaches effects. Release
and terminal delivery each emit one fact; repeated callbacks apply nothing.

The shipped catalog composes disease, poison, five elemental resistance variants,
Shield, paralysis, Free Action, Regenerate, Spell Absorption, immediate
and continuous health/fatigue/spell-point damage, Disintegrate, and disease, poison
and paralysis cures.
Additional spell families add their bindings through that same composition seam;
unmapped effects refuse before payment or skill use. UI selection, spell flight
presentation and item-trigger policy remain separate consumers.

Resistance keeps each admitted source's chance and lifetime in active-effect state.
The live projection sums matching chances up to full resistance, retaining the
distinction between no marker and a zero-chance marker. Casting, disease exposure
and Razor read that projection. Shield keeps one starting/remaining pool, extends
its incumbent rounds and tops up within the original starting cap. Its participant
contribution reduces accepted damage before the canonical health mutation;
depletion and expiry remove the contribution, and saves retain the remaining pool.

Immediate destruction consumes the casting owner's admitted magnitude or chance
once, then expires in the initial round. Health loss and terminal Disintegrate
use the same accepted health owner and death notifications; Disintegrate bypasses
Shield without spending its pool. Fatigue uses classic fatigue units and protects
peaceful non-player targets. Fatigue and spell-point outcomes report bounded live
loss, while saves retain the resulting tracks and affected enemy hostility.

Health and fatigue transfers apply direct target loss before bounded caster recovery.
The classic admitted magnitude determines recovery even when Shield or a track bound
reduces actual loss; fatigue uses the same 64-unit scale at both ends. Transfers
require an actual living caster and target, expire after initial delivery, and
report loss, recovery and terminal outcomes through ordinary facts. Saves retain
the resulting tracks without a transfer to replay. A reflected terminal self-hit
retains accepted death instead of restoring its dead caster.

Continuous destruction shares those health/track consequences and source-scoped
incumbents. An initial payload and each admitted ordinary or elapsed round use
the saved cast settings and next draw identity. Same-kind effects from the same
caster/item extend duration without an extra tick; other sources keep their own
lifetime. Current saves resume without repeating the initial payload. Expiry,
cure, target death and source retirement stop future ticks. Peaceful non-player
fatigue targets remain protected.

Paralysis retains independent source-scoped durations. Its live control projection
blocks movement, jump and physical attacks without suppressing look, casting or
Engine gravity. Pending strikes and arrow releases stop; already released arrows
keep their existing flight. Expiry, cure, death and source retirement release the
restriction. Current saves restore the restriction and hostile response without
replaying the initial magic round. Site perception resets retain saved hostility.
Nonzero non-magnitude saving throws retain the admitted duration; full resistance
prevents attachment. Cure spells compose the same lifecycle cleanup in their own
spell family.

Compiled cures run once through casting and the initial effect round, then expire.
They use Magic-only chance admission and all target shapes. Each condition owner
selects its matching disease, poison/drug or paralysis instances on that target
and invokes canonical cleanup, preserving other conditions and other targets.
No match has a distinct cast result and completed-change signal. Cleanup removes
condition modifiers and restrictions without healing previously spent vitals;
current saves retain the post-cure state.

Free Action releases active paralysis restrictions while its immunity lasts and
rejects new paralysis; it does not erase another source's remaining condition.
Regenerate heals through bounded health tracks on ordinary and elapsed magic
rounds. Equivalent settings from the same caster/item extend one incumbent without another initial heal;
different settings coexist. Current saves retain the next round's random-draw
identity and never repeat the initial heal. Spell Absorption retains incumbent
settings and extends duration; chance uses the receiver's current level. Casting
reserves combined refund capacity and reports one terminal delivery result before
applying the aggregate refund. Distinct caster/item sources retain their own cleanup and lifetime. Source
retirement, cure and expiry use the same
active-effect cleanup. Free Action's donor cost row is published even though the
stock spell table contains no spell using it.

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

Effect capture is read-only; a full session save clears transient spell readiness
and pending ranged casts. Stat sources carrying effect provenance are rebuilt with
fresh actor identities before tracks, through Engine's existing capture/rebuild
helper. A compiled definition that applies contributions supplies a separate
resume callback to bind cleanup to restored state without applying a second
contribution or replaying the initial round. The session composes one
`DaggerfallEffectCatalog` from the disease (`DaggerfallDiseasePolicy.Definitions`)
and poison (`DaggerfallPoisonEffects.Definitions`) families, so a saved effect
names the definition that interprets it; unknown effect definitions fail clearly.

Held skill and spell-point sources retain the equipped item's durable provenance.
Save admission resolves their cleanup owner through the saved equipment and its
published enchantment, and the existing stat rebuild restores them before tracks.
The held owner recomputes those sources once from equipment. Unequip, break and
wearer destruction remove them and clear carry, talent and magic-round values.

## Ranged delivery

The Ring of Namira reflects an accepted enemy physical hit on the player once,
using the current ring slots and loaded artifact payload. Animal and spriggan
teams are excluded; daedra receive half and undead twice the incoming damage
after application defenses. Reflection uses the shared health application and
ordinary damage/death notifications, then charges that ring's condition through
the item-condition owner. Two rings do not double the effect. Equipment and item
metadata alone determine its state after unequip, break, retirement or restore.

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

Immediate healing uses matching permanent drain sources separately from disease or poison cure. The compiled eight attribute variants heal only their own drain; health and fatigue restore the canonical bounded tracks, with classic fatigue scaling. Potion-only spell-point healing is a named self-targeted compiled payload without a fabricated classic spell identity. Healing expires after its initial round, cannot resurrect an accepted death, and current saves retain the resulting stats and tracks without replay.

Temporary fortification contributes through effect-owned sources to all eight canonical attributes. Equivalent settings extend the incumbent duration without rerolling or adding its magnitude; different settings coexist. Expiry, cancellation and target/source lifetime cleanup remove only the corresponding source. The canonical attribute Stat applies the classic live cap, so combat, movement, skills, casting and UI read the same bounded result. Saves rebuild sources and resume their cleanup ownership without reapplying contributions.

Magical concealment composes six normal/true duration-only variants in the existing effect graph. Enemy senses read the same invisibility/blending/shade projection; normal power ends on an accepted destructive spell attack or positive physical contact while true power remains. Invisibility feeds the existing Engine appearance visibility fact, and HUD status rows describe normal/true power. Expiry, death and actor/item cleanup remove the owning effects and their projections without a concealment cache.

Mysticism composes language comprehension into the current perception and dialogue projections. Dispel Magic holds a paid, saveable player choice and cancels the selected active bundle through the effect lifecycle. Creature dispel uses Engine distance queries and ruleset enemy groups; roster banishment removes actors without death or loot, and authored absence survives save and site return.

Lock and Open remain ready in the effect lifecycle until dungeon-door activation consumes them. Lock precedes Open and ordinary lockpick/bash; current actor level controls lock strength and opening, with the Skeleton Key's interior exemption. The canonical door owner projects motion, collision and lock state, and the existing site/save owners retain both doors and ready effects. Exterior building-door admission and its Open caller remain coverage work.
