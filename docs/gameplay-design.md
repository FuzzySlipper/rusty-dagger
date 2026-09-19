# WorldRpg gameplay design

This is the current intended design, promoted from rusty-dagger Board post
**146** and updated to the implemented shape. The post preserves the discussion;
its wait-for-Engine sequencing and pre-refactor survey are historical. Read this
document when an older task names a removed owner or assumes the old mutation
or save model. Explicit current user instructions and owning task decisions
remain authoritative; preserve the task's gameplay requirements while applying
the current architecture.

## Build a substantial Kit

> Engine guarantees. Kit shapes. Ruleset decides. Bundle assembles. Host launches.

Building the reusable WorldRpg Kit is a primary goal. Daggerfall is its proving
ruleset, not a reason to leave most reusable gameplay machinery in Dagger modules.
Kit owns actor conventions, targeting, inventory/equipment workflows, attack
execution, reusable AI and loot coordination, and typed interaction resolution.
Effects, progression and future world mechanisms should follow the same division
as their real behavior is implemented.

Dagger supplies formulas, eligibility, capacities, timing policy, content meaning
and special rules. Use small explicitly composed policies, not one giant
Dagger-shaped interface. Engine owns host/update/input, rendering/resources,
spatial mechanisms and persistence primitives. C# owns gameplay authority; there
is no Rust-authority or Rust/TypeScript replay boundary to preserve. TypeScript
is DOM presentation and semantic input, never a second gameplay implementation.

## Actors expose attached state

Engine `Actor` wraps an `EntityStore` entity. Kit's `PlayerActorState` and
`ActorState` compose that wrapper and expose named properties over attached
class components. `DaggerActorFactory` constructs the Dagger actors explicitly;
wrapping an existing entity adds nothing and does not own its lifetime.

Use components for state that shares an entity's identity and lifetime: stats,
effects, inventory/equipment, targeting, attack readiness/pending impact, pursuit
memory and corpse state. Services, definitions, native resource owners and UI
projections retain their own appropriate ownership. Do not make everything a
component or keep a second actor dictionary to mirror attached state. Avoid
reflection discovery, inheritance chains and historical Unity caching machinery.

Keep three identities distinct:

- `EntityId`: current Engine runtime instance.
- `EntityTypeId`: creation metadata describing kind/origin, separate from components.
- `DurableIdentityReference`: product instance identity, including its kind.

Kit `EntityDirectory` owns the durable-to-runtime map. A durable actor and its
corpse container can share a number while having different kinds and runtime
entities. Never manufacture an Engine ID from a saved number. A new lifecycle
feature must explicitly decide what happens to owned items and native resources.

The same attached objects serve gameplay, wrappers and save capture. Engine
`Stat` is double-backed with integer/float accessors; `Track` shares its maximum
`Stat`. `StatsComponent` owns those references. Do not recreate separate exact
and continuous stat families or bake temporary contributions into permanent bases.

See [Actors and live mechanics](actors-and-mechanics.md) for component ownership.

## Three ordinary interaction paths

1. **Read or act directly** through named properties/services for simple work.
2. **Resolve typed interactions** when independently authored rules contribute.
3. **Publish completed changes** as typed notifications for observers/reactions.

`DaggerfallState.Kit` exposes `GameplayServices<TFact>`: `Actors`, `Targeting`,
`Attacks`, `AttackExecution`, `Rules`, `Inventory`, and `Equipment`. This is
explicit composition, not ambient `Resolve<T>()` discovery. Kit's `Ai` and `Loot`
mechanisms are composed separately; they are not currently aggregate properties.

Input and AI use neutral `IAttackCapabilities<TFact>`; they do not calculate
Dagger hit formulas. `TargetingService` uses Engine perception with an explicit
`DaggerTargetingPolicy`. `AttackExecution<TFact>` owns readiness, pending impact,
interruption and cooldown state through the attached `AttackState`.
`DaggerCombatRules` supplies costs, formulas and application policy.

The implemented RuleEvent path is `CombatResolution`, with `TryHitEvent`,
`DamageEvent` and `ApplyHitEvent`. Base hit/damage calculations precede ordered
contributions; application contributions run before the supplied application
handler. Gathering visits attacker and target contributions and their distinct
equipped items, then explicitly registered action contributions. A multislot
item contributes once. Effect owners can add/remove `CombatContributions`;
the full spell/effect backlog is not implemented by this extension point.

Keep contexts typed and participants explicit. Read existing authoritative stat
modifiers rather than applying their bonuses again. Application handlers may
mutate canonical state synchronously. Distinguish calculated damage from actual
health lost. Essential application must not depend on eventual subscribers.
Do not add a universal bus, handler dependency scheduler, reflection scan,
string/object context bag or mandatory per-handler audit trail.

`FactBuffer.Deliver` detaches a stable batch. Facts appended during reactions wait
for the next delivery. A throwing reaction does not requeue already applied
changes or undo its predecessors; it is not a transaction or retry journal.

## Direct mutation, concrete safeguards

Ordinary gameplay uses trusted live references and direct mutation. Input uses
`PlayerInputSystem.Apply`; spatial movement uses `SpatialMovementSystem.Step`.
Appearance no longer copies its graph to claim rollback after a terminal callback
failure. Preserve native release/retirement, update ordering and Engine spatial
authority. Explicit load reconstruction is separate from ordinary frame edits.

Keep checks that protect an actual requirement: gameplay eligibility, track
bounds, valid current content references, coherent save relationships, ABI and
native lifetime. Optional inventory edits are appropriate when a multi-item grant
or payment/transfer must succeed as a whole. A quote may need its funds or item
checked when accepted. These do not establish a baseline proposal/accept/commit
protocol, revision guard or whole-state snapshot for every action.

Timing remains deliberate product policy. Player attacks currently apply
immediately; enemy attacks roll at start and apply at the authored impact.
Interrupting delivery retains charged costs/cooldown. Pending strikes are
transient on load; remaining cooldown is restored onto the new timeline. Keep
keyed gameplay RNG and admitted-step catch-up semantics. Consult the
[combat baseline](combat-behavior-baseline.md) before changing these decisions.

## Current schema, meaningful reconstruction

Only the current development schema exists. Breaking development saves is
acceptable. No product schema versions, migration branches, historical readers,
unknown-section preservation or content-fingerprint compatibility gates.
Optional upstream migration utilities are not required product patterns.

`WorldRpgSaveStore` uses Engine `ProductStateStore` and source-generated
`JsonProductStateCodec`. `DaggerSessionPersistence` owns capture/restore of
meaningful Dagger state; `DaggerfallSavePayload` is the current DTO contract.
Restore creates fresh runtime entities and rebuilds shared stats/track maxima
and authored sources through Engine capture helpers before restoring currents.
Preserve distinct items, equipment assignments, progression, world/corpse state
and durable relationships. Saved live items must agree with the durable allocator
so future allocation cannot reuse them. Malformed current state or missing
definitions must produce an understandable failure rather than partial success.

Do not serialize native handles. Held input, AI/perception work, presentation,
native continuation and in-flight attacks are transient or rebuilt. Native
continuation helpers remain optional, not an ordinary save requirement.

## Admit content once; give caches a real identity

`ResolvedGameComposition` retains Engine's immutable `ProductContent` and payload
memories. `DaggerfallRuleset` caches decoded definitions, inputs and tuning per
resolved composition. Share admitted definitions; do not repeatedly copy,
reparse, rehash or reverify cached resources as a defense against trusted code.

A cache still needs to distinguish publications. `DaggerfallUiArt` assigns a new
token to each admitted image block so the DOM requests/redraws it after a runtime
replacement. The token is not a byte hash or integrity protocol. Offline importer
bounds/decoding, source provenance and build-time artifact digests remain useful;
they do not imply runtime compatibility negotiation. Lazy bundle admission is a
separate loading concern and must use the actual packaged Engine capability.

## Discovery and remaining work

Start new work from the current owners, not a stale filename in a task:

| Concern | Current entry point |
| --- | --- |
| Actor construction | `DaggerActorFactory`, Kit `ActorsState` / `EntityDirectory` |
| Live gameplay services | `DaggerfallState.Kit`, `GameplayServices<TFact>` |
| Target selection | Kit `TargetingService`, `DaggerTargetingPolicy` |
| Attack lifecycle and rules | Kit `AttackExecution` / `CombatResolution`, `DaggerCombatRules` |
| Pursuit / corpse loot | Kit `PursuitCoordinator` / `CorpseLootCoordinator`, Dagger policy modules |
| Saves | `DaggerSessionPersistence`, `DaggerfallSavePayload`, Host `WorldRpgSaveStore` |
| Content admission | `GameCompositionResolver`, `DaggerfallRuleset` |
| Host / UI actions | Host lifecycle and selection; ruleset `IEntryScreenSession` interpretation |

The [code and migration map](code-migration-map.md) describes the broader graph.
This document is design guidance, not a claim that all RPG features or visible
acceptance are complete. Current followups include ordinary save/load controls,
connected gameplay verification and UI-art delivery warnings (#8342–8344).
Den owns their live status. Existing coverage tasks still carry their gameplay
requirements; update obsolete implementation assumptions without inventing new
features, duplicating owners or requiring proof-only gameplay scaffolding.
