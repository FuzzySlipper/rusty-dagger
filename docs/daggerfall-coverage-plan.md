# Daggerfall coverage plan

Status: planning baseline, 2026-09-10. This defines scope, ownership, and work
ordering; it is not an executable Den backlog or a claim of completed coverage.
The [task-creation packet](daggerfall-task-preparation.md) now supplies the
feature dispositions, behavior inventories, content scope and dependency inputs
for expanding these families into explicit tasks. The current user request and
owning task override older guidance.

Task creation is complete: **Den campaign #7922**, with **255 planned children**.
Use the [task index](coverage/task-index.md) for area navigation, concrete IDs and
coverage mapping. Den owns live status and dependency scheduling; this document
remains the scope and architectural baseline, not an implementation-complete claim.

## Purpose and references

Bring rusty-dagger to mostly full Daggerfall behavior and content coverage through
deliberate, primitive-first implementation. The original game supplies the product
requirements; DFU supplies a substantial reverse-engineered behavioral reference.
This is an experiment in detailed planning and long autonomous implementation runs,
with narrow drift checks and later reconciliation. It does not use vertical slices,
proof threads, or interactive demonstrations as the organizing unit of delivery.

- [Feature map](daggerfall-feature-map.md): source references and point-in-time
  implementation inventory. Its rows are not automatically implementation tasks.
- [AGENTS.md](../AGENTS.md): active ownership and execution rules.
- [Migration map](code-migration-map.md): current product graph and retired paths.
- Den campaigns #7322 and #7533: completed foundation and migration history.
- DFU donor: `/home/research/daggerfall-unity`, surveyed at
  `81e89e90c27bc3c1a7a61871e545fad129174dec`. Recheck relevant source when planning
  each concrete task; the survey is not a permanent API or coverage guarantee.

## Behavioral fidelity and architectural adaptation

Preserve Daggerfall rules, formulas, state transitions, content meaning, and
interactions. DFU is a model of these behaviors, not a coding style to emulate.
Its Unity architecture reflects its platform and distribution requirements.
Use ordinary explicit C# composition and existing Rusty owners here.

When original behavior and DFU differ, record the actual difference and selected
behavior in the owning task. Do not silently turn a DFU enhancement, workaround,
or incidental bug into a classic requirement. Unresolved differences remain
explicit planning decisions, not permission for a worker to invent semantics.

| Donor area | Disposition for this campaign |
| --- | --- |
| Content-reader singleton bootstrap and Arena2 application paths/setup | Exclude the runtime topology and distribution/setup workflow. Decode offline into normalized Rusty-friendly formats; consume admitted packs at runtime. |
| Top-level GameManager orchestration | Exclude singleton/MonoBehaviour wiring and global service access. Extend the existing Host/ruleset composition and Engine-admitted update. |
| DFU game-state machine | Preserve needed game modes, pause/input consequences, and transitions; do not reproduce its state stack or architecture by default. |
| Unity-specific serialization | Exclude component/object-graph serialization. Persist product meaning through existing Rusty save ownership and Engine persistence. Classic/DFU save-file compatibility is not a default task requirement. |
| Unity import and rendering bridges | Exclude Unity materials, meshes, Texture2D, scene/prefab construction, Resources loading, and reader-to-Unity adapters. Retain source-format decoding, conversion semantics, and needed asset data in Import; use Engine presentation. |
| UI | Adapt information, choices, workflows, and semantic actions to thin DOM UI. Do not emulate Unity widgets, window classes, pixel geometry, or component hierarchy. Gameplay authority remains in C#. |
| MIDI playback | Excluded. Contextual music and long-running loop playback from an ordinary imported audio file remain in scope. |
| Other DFU infrastructure, mod protocols, editor/demo helpers | Not automatically required by a survey row. Classify explicitly before scheduling; do not recreate it just to turn every row into “Covered.” |

Excluding Unity import code does not exclude textures, models, books, sounds, or
other game content. Excluding DFU orchestration does not exclude actual game
behavior such as pausing or new-game flow.

## Ownership and scheduling rules

**Engine guarantees. Kit shapes. Ruleset decides. Bundle assembles. Host launches.**

Kit owns reusable or placement-uncertain world-RPG mechanisms over verified Engine
APIs. Daggerfall owns its identities, formulas, interpretation, and policy; packs
own authored records, and typed tuning owns adjustable policy. Import owns source
formats and normalization. Host owns selection/lifecycle; TypeScript presents UI.
Do not build a universal RPG framework, reflective registry, generic command bus,
new gameplay DSL, second simulation loop, or duplicate Engine mechanism.

The numbered areas below give preferred order, not twelve all-or-nothing barriers.
Tasks depend on concrete capabilities. Import/catalog work can proceed alongside
runtime work, and an Engine blocker only stops its dependent tasks. Reuse existing
coverage after inspecting it; do not rewrite a subsystem because it appears below.

Shared identity, state ownership, time advancement, and content contracts deserve
precise early planning. Local organization and refactoring can be reconciled in
larger batches. Do not defer interoperability decisions that many tasks depend on.

## 1. Coverage target and task inventory

Preparation output: [task-creation packet](daggerfall-task-preparation.md), including
the complete feature-map disposition ledger and specialist behavior/content
inventories. Use those stable IDs when creating tasks; maintain their mapping to
task IDs rather than restarting the survey. Exact formulas and API contracts are
resolved while drafting the affected tasks, not through another global planning gate.

Breakdown:

- Assign stable feature identifiers and map each to source behavior, current
  implementation, planned family, and disposition: implement, extend/reuse,
  adapt, exclude, or unresolved.
- Separate behavior coverage from authored-content coverage. Inventory supported
  locations, actor/item catalogs, spells, quest corpus, books, and media; the
  feature map intentionally does not enumerate these payloads.
- Record exact exclusions above, selected DFU differences, and explicit remaining
  questions. Deduplicate overlapping map rows rather than scheduling them twice.
- For partial coverage, enumerate missing behavior and current limitations before
  defining tasks. “Covered” in a structural survey is not full semantic parity.
- Expand families into bounded tasks with concrete prerequisites and named owners.

Ownership: planning document and Den tasks; no new runtime framework. This area
organizes the other eleven and continues as discoveries refine the inventory.

## 2. Durable identity, state, and game time

Breakdown:

- Stable world/location/building/placement identities and dynamic actor/item
  identities, distinguished from transient Engine resource handles.
- Loaded and unloaded world changes, actor creation/removal, ownership of save
  records, and reconstruction after location changes or load.
- Extend current fixed-scene save assumptions before dynamic spawns: restoration
  currently matches saved actors to the authored Privateer's Hold actor set.
- One product game-time model advanced inside admitted updates, with explicit
  elapsed-time operations for rest, travel, and prison. Specify ordering and
  catch-up behavior for deadlines and periodic consequences; no wall-clock loop.
- Daggerfall calendar, seasons, timescale, reset rules, and persistent time state.

Ownership: Kit reusable identity/state coordination; Daggerfall time/calendar and
reset policy and save meaning; Host existing envelope/lifecycle; Engine persistence.
Requires: existing composition. Enables world changes, timed effects, services,
and quests. Each later family extends its own save meaning as it lands.

## 3. Normalized catalogs, source data, and text

Breakdown:

- Races, careers, attributes/skills/resistances, advantages/disadvantages, enemies,
  item groups/templates/materials, factions, regions/locations/buildings.
- Text tables, names, books, classic spell records, and source references needed
  for later quest and dialogue work.
- Stable cross-record references and normalized pack publication; distinguish
  authored values from algorithmic constants and adjustable policy.
- Preserve source identity/provenance and useful differential fixtures. Expand
  each corpus alongside its consumers; do not require all assets imported first.

Ownership: Import decoders/normalizers, content packs, Daggerfall interpretation;
reuse Kit composition. Requires identity conventions from area 2 for relevant
records. No runtime Arena2 setup, source reader singleton, or Unity importer.

## 4. General world and persistent interactions

Breakdown:

- Multiple RDB dungeons, building interiors, RMB cities, exterior terrain and
  location data, and Engine-backed admission/unloading/presentation.
- Enter/exit context, return positions, building directory, persistent placements,
  and world changes across unloading/reloading.
- Doors, locks, containers, dungeon actions/triggers/links, moving-world behavior,
  and discovery data, with source semantics normalized offline.
- Quest markers and placement anchors with stable identities, even before quest
  actions consume them. Do not bind quest resources to transient renderer objects.
- Verify Engine capabilities for terrain, world origin, spatial changes, and
  presentation as their concrete tasks are planned; route genuine gaps upstream.

Ownership: Kit location/lifetime/interaction coordination; Daggerfall layout,
placement, action and reset policy; Import source records; Engine mechanisms.
Requires: relevant area 2 state and area 3 records. Enables world-aware gameplay.

## 5. Character and item construction

Breakdown:

- Complete actor creation from race/career/content, permanent and modified stats,
  skills/resistances, derived values, advancement, and character creation choices.
- Item instances, variants/materials, condition, identification state, equipment
  restrictions, weight/encumbrance, currency, and complete ordinary loot categories.
- Expose usable operations for later combat, services, effects, and quests;
  persist dynamic state and preserve item identity through transfers.
- Character creation, sheet, inventory, and equipment semantic UI actions and
  projections accompany their owning behavior tasks, using the existing UI path.

Ownership: Kit Engine-backed actor/inventory/stat coordination; Daggerfall rules,
definitions, creation policy, and presentation meaning; packs authored records.
Requires: areas 2–3. Effect-driven modification builds further in area 7.

## 6. Ordinary physical gameplay

Breakdown:

- Activation modes; taking/dropping/using items; lockpicking, bashing, theft and
  pickpocket operations; concrete outcomes available to later crime policy.
- Walk/run/crouch/jump, climbing, swimming and other movement capabilities with
  explicit Daggerfall eligibility, costs, and consequences over Engine spatial APIs.
- Full melee factors, weapon timing, equipment wear, ranged attacks/projectiles,
  attack-to-item binding, first-person weapon presentation, death and corpse flow.
- Enemy construction, ordinary senses, pursuit/attack/retreat policy, sounds and
  feedback using existing actor, targeting, navigation and presentation owners.

Ownership: Kit reusable coordination, Daggerfall physical rules and AI policy,
Engine spatial/perception/presentation. Requires relevant areas 4–5. Later magic
extends these same operations; do not create separate magical movers or hit paths.

## 7. Effects and casting foundations

Breakdown:

- Verify and use existing Engine stat contributions and effect mechanisms;
  coordinate source identity, modifier removal, conditions, duration and stacking.
- Daggerfall spell/effect keys, bundle records, caster/target identity, ready/cast
  state, cost, targeting, chance, saving throws, immunity, absorption/reflection.
- Persist active effects and define normal-time versus elapsed-time behavior,
  periodic application, cure/expiry, and restoration without duplicate application.
- Ordinary damage/heal/drain/fortify/resistance effects, then poison and disease
  mechanics using the common time/lifecycle path.

Ownership: Kit reusable Engine-backed coordination; compiled Daggerfall effect
semantics and loaded spell records. Requires areas 2, 3, 5 and relevant targeting
operations from 6. No reflective broker or copied MonoBehaviour effect manager.

## 8. Living world, travel, and maps

Breakdown:

- Rest/loiter and travel advance game time with healing, interruption, encounter,
  cost and deadline consequences; transport ownership/modes and route calculation.
- Climate/weather, seasonal appearance, shelter, ambient audio and opening hours.
- Static NPCs and mobile civilians, identities, population/encounter selection,
  spawning/removal, stealth-aware senses, and interactions with world persistence.
- Interior and exterior automaps, discovery persistence, region/travel selection,
  names and markers rendered through the appropriate Engine/UI surface.

Ownership: Kit reusable world/actor coordination; Daggerfall simulation rules;
Engine rendering/spatial/audio. Requires relevant areas 2–7, not every effect.
Static NPC identity and faction records can start earlier than civilian movement.

## 9. Society, dialogue, and services

Breakdown:

- Faction relationships, reputation, guild membership/rank/eligibility and changes.
- Crime attribution, guards, arrest/court/prison/fines and elapsed-time consequences.
- Dialogue context, topics/reactions, rumors, macro expansion and NPC bindings.
- Merchant trade, repair, identification, training, taverns, banking, loans/default,
  property and guild/temple services with real inventory/time/reputation effects.
- Thin UI for these operations and persisted service state; quest offers can be
  attached later through the same NPC/service identity rather than a second system.

Ownership: Kit reusable relationship/transaction coordination where appropriate;
Daggerfall social rules, text interpretation and presentation; packs faction/text
records. Requires relevant identity, actor/item, world and time operations.

## 10. Magic breadth and special abilities

Breakdown:

- Remaining effect families grouped by prerequisites, including locomotion,
  door/world changes, detection, invisibility, charm, teleportation and summoning.
- Spellbook/maker, potion recipes/alchemy, enchanting and item-triggered effects;
  reuse equipment/strike/use lifecycle rather than introducing parallel dispatch.
- Artifacts, vampirism, lycanthropy and other special behavior with explicit
  interactions with faction, disease, time, world and quest state.
- Each family includes all assigned magnitude/duration/chance and target behavior,
  persistence, cleanup and caller integration, not one demonstrable example.

Ownership: Daggerfall semantics/content over Kit and Engine capabilities. Requires
area 7 plus each effect's actual domain operation. Some transformation behavior
needs quest invocation from area 11: do not make all of 10 a prerequisite for 11.

## 11. Quest machinery and action families

Breakdown:

- Normalize existing classic quest data/source semantics offline into Rusty-friendly
  records; compiled Daggerfall code interprets supported operations. No new generic
  gameplay language or donor reflection/registration topology.
- Persistent quest instances, symbols, typed resources, task conditions/transitions,
  deadlines, messages/macros and journal state. Establish quest start/end operations
  early enough to support dependent special abilities from area 10.
- Bind people/places/foes/items to stable world identities and normalized markers;
  support placement/relocation/removal and operations queued before a foe is spawned.
- Implement action families against actual item, actor, effect, faction, dialogue
  and world operations: rewards, spawns, reputation, spells, movement, messages,
  timers, completion and failure. Preserve lifecycle and cleanup semantics.
- Import and enumerate supported quest content, unresolved opcodes/resources and
  deliberately excluded behavior. Unsupported work remains explicit, never a no-op.

Ownership: Daggerfall quest semantics, Import source conversion, packs authored
quests; Kit reusable identity/facts/state coordination only. Data and instance
foundations can begin after areas 2–3. World binding needs 4/8; individual actions
depend on 5–10 selectively. This breaks the quest/effect cycle without stubs.

## 12. Content completion and reconciliation

Breakdown:

- Complete the supported world, catalogs, books/media, miscellaneous/guild quests,
  main quest and special locations. Content work progresses throughout earlier
  areas; this closes the inventory instead of starting bulk import at the end.
- Finish remaining UI workflows, controls/settings, contextual sound/music and
  presentation details selected by the coverage target.
- Schedule reconciliation after substantial implementation batches and at campaign
  completion: repair cross-system mismatches, consolidate accidental duplicates,
  refactor awkward ownership and update honest coverage/disposition records.
- Broader play sessions can discover integration defects here. They do not replace
  behavioral requirements or retroactively turn every task into a demo gate.

Ownership: existing domain owners; no separate completion runtime. Requires each
content family's actual capabilities and records remaining gaps explicitly.

### Music and long-running audio

No MIDI decoder, synthesizer, MIDI.BSA playback requirement, or Unity SongManager
port. Plan ordinary music selection/looping over the verified Engine Audio API.
A user-supplied MP3 under `local/` can be an offline input, converted if necessary
to a supported admitted format; do not assume direct MP3 support or bypass Engine
content admission with a browser audio player. Keep local media out of commits.

Separate the product music behavior task from a bounded long-duration Engine audio
exercise. That exercise should specify input, duration, loop/voice lifecycle,
stop/dispose behavior and available observations of errors/resource growth. Report
the elapsed duration and observations honestly; hearing one loop is not evidence
of long-run stability. This targeted experiment does not gate unrelated coverage
tasks. No media file or audio run is required to complete this planning document.

## Turning families into autonomous tasks

Each task should state:

1. Feature IDs and exact behavioral scope, donor files/symbols, selected semantics
   and explicit exclusions. Consult source rather than porting a class by name.
2. Existing Engine and local owners to reuse/extend, proposed mutable-state owner,
   and concrete required capabilities. Record why a new mechanism is necessary.
3. Inputs, outputs, state transitions, ordering, and relevant failure/cleanup rules.
4. Required interoperability: real callers, content references, save/restore and UI
   actions/projections where applicable. Identify separately scheduled consumers
   honestly; a primitive need not manufacture a demo consumer to be complete.
5. Hard dependencies, independent work, and exactly which behavior is blocked if a
   prerequisite is absent. Prefer ready tasks over queue-wide waiting.
6. Focused checks of semantic cases and interoperability, plus coverage updates.
   Tests should expose meaningful mistakes, not mirror implementation branches.

Prefer “implement equipment condition loss for the specified hit outcomes and
persist it through the existing item owner” to “deliver working combat.” Do not
mandate one class per task or scatter one coherent operation across tiny tickets.
A task implementing a prerequisite contract may be complete before its later
consumer exists; it must actually implement its full assigned contract. A stub,
unconditional success, hardcoded demonstration, or unsupported no-op cannot count.

## Narrow drift review lanes

Use separately scoped reviewers when they add value. The lane definitions below
are available to the orchestrator; they do not require four reviewers for every
task or create a new approval system. Review a task's change against its stated
contract and relevant existing code, not the entire repository each time.

The first two lanes run on every task; the remaining lanes are selected per task
to a total of two to four reviewers. The reusable reviewer packets live in
[`docs/agent-review/`](agent-review/README.md), and the Den document
`rusty-dagger/agent-review-workflow` owns the persistence and disagreement rules.

| Lane | One question | Required basis for an actionable finding |
| --- | --- | --- |
| Engine reuse (always on) | Does this change recreate a mechanism already safely available upstream? | Name the current safe API, local duplicate and concrete replacement/adoption path; distinguish product policy from Engine guarantees. |
| Existing product reuse (always on) | Does this change create a competing mechanism instead of extending rusty-dagger's existing owner? | Name both owners and their overlapping state/behavior, relevant callers, and consequence. A new file or similar name alone is not a defect. |
| Ownership and values | Does this change leak Daggerfall policy into Kit/Host, source quirks into runtime, or authored/tunable values into incidental code? | Identify the actual assumption/value, current and correct owner, and affected use. Do not demand a universal abstraction or a constant for every literal. |
| Behavior and interoperability | Does this implement the task's full specified behavior through the required shared operations? | Show a concrete missing branch, no-op, ignored input, disconnected caller, incompatible state contract, or donor-semantic mismatch. A passing demonstration does not close the finding. |

Findings should be short and source-backed, distinguish confirmed defects from
uncertainty, and stay within the lane. Do not introduce interactive gates, broad
redesigns, stylistic demands, or invented acceptance criteria. The root consolidates
overlap and judges whether to fix, adapt, or decline findings; reviewer verdicts do
not amend user intent. Deferrable cleanup is explicit, not falsely marked complete.

## Donor dependency evidence behind this order

At the surveyed DFU revision, exact-source consultation found:

- `Game/Questing/Place.cs`: location/building selection and RMB/RDB marker binding
  underpin resource placement; world identity and normalized anchors precede actions.
- `Game/Questing/Actions/GiveItem.cs` and `CastSpellOnFoe.cs`: operations can be
  retained before foes spawn; resource identity/lifecycle precede action breadth.
- `Game/Questing/Clock.cs`, `Effects/Diseases/DiseaseEffect.cs` and
  `Effects/Poisons/PoisonEffect.cs` under `Game/MagicAndEffects/`: elapsed game time
  drives deadlines and periodic effects. They must share product time semantics.
- `Game/MagicAndEffects/EntityEffectBroker.cs`, `EntityEffectManager.cs`,
  `EntityEffect.cs`: bundle definitions and active-effect lifecycle precede bulk
  effects. Adopt behavior while adapting away from discovery/singleton topology.
- Special transformation effects and quest-triggered spells cross-reference quest
  invocation and effects; schedule their foundations before dependent families.

These are planning dependencies, not an instruction to copy the donor classes.
