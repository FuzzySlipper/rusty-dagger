# Daggerfall task-creation packet

Prepared 2026-09-10. This is the coverage inventory and decomposition input for
the [coverage plan](daggerfall-coverage-plan.md), not a set of created Den tasks.
It makes the target finite enough to plan without requiring a full semantic audit
of the game before implementation. The original game remains the behavioral
target, with explicit Rusty architecture adaptations and recorded differences.

The approved packet has now been expanded into **255 planned children in Den
campaign #7922**. See the [task index and coverage mapping](coverage/task-index.md).
This packet remains the inventory baseline; Den is authoritative for live task
state. Task creation does not establish implementation coverage.

## Start here

| Document | What the task author gets |
| --- | --- |
| [Coverage plan](daggerfall-coverage-plan.md) | Twelve-area order, ownership, exclusions, execution and drift-review posture. |
| [Feature ledger](coverage/feature-ledger.md) | Stable F001–F141 IDs; every feature-map row has a disposition and named remaining behavior. Duplicate rows name their canonical owner. |
| [Supplemental behaviors](coverage/supplemental-behaviors.md) | Explicit SUP IDs for persistent world changes, dungeon actions, crime/services/time and other behavior implicit in the map; includes every registered dungeon-action flag. |
| [Magic inventory](coverage/magic-inventory.md) | Individually named candidate effects, helpers and behavior families behind broad magic rows. |
| [Quest inventory](coverage/quest-content-inventory.md) | Individually named action candidates and quest-source/catalog scope behind broad quest rows. |
| [Content scope](coverage/content-scope.md) | Source-family inventory, current content anchors and explicit authored-content target. |

Read the applicable rows and current source before drafting each task. Do not turn
each file, row or ID into exactly one ticket. A coherent behavior can involve
several IDs; a broad ID can require several tasks with stable child identifiers.

Preparation accounting: 141 feature-map rows (136 canonical rows after aliases),
18 supplemental behavior families, 26 registered dungeon-action flags, 153 effect
files with leaf IDs and parameter variants, 83 quest-action files, and 265 donor
quest-source files are inventoried. The content
manifest records 28 source families in 29 summary rows and all 1,680 supplied local
source files. These counts include explicitly excluded helpers/demos and are not
counts of required tasks, completed features, or usable runtime records.

## Scope contract

Baseline target: the classic game’s playable world, character development,
physical/social/magic systems, main and side quests, and content needed by those
behaviors. Use the local original data and the surveyed DFU implementation as
references. The target is not only the currently published Privateer's Hold pack.

Authored records are part of the backlog: importer support without published and
consumable records does not finish a content family. Conversely, not every raw
unused archive entry needs a new gameplay feature. Every discovered source record
gets an honest imported, required-pending, unused, duplicate, excluded, or unresolved
disposition when that family's importer/publication task enumerates it.

Retain the coverage plan's explicit exclusions: Unity/bootstrap/distribution
topology, runtime Arena2 setup, donor singleton orchestration, Unity serialization
and import bridges, widget/window implementation shapes, and MIDI playback.
Ordinary audio/music remains included, with its long-duration exercise separate.

Do not silently add DFU mods, mod message protocols, editor tools, replacement
graphics packages or optional convenience behavior to the baseline. Keep candidate
DFU-only records in the inventories so they can be assessed instead of disappearing.
Original-language text and adapted UI functionality are the initial baseline;
localization expansion and binary classic/DFU save compatibility are not assumed.

## Current-source corrections and reuse anchors

These are focused source findings from this preparation pass. They correct planning
assumptions; they are not interactive acceptance or proof of full behavior parity.
Paths below are repository-relative. Existing owners must be extended unless a
task explains a concrete reason to replace them.

| Area / IDs | Existing owner or path | Consequence for task drafting |
| --- | --- | --- |
| Composition, packs, F108–F110 | `src/WorldRpg.Kit/GameComposition.cs`; `src/WorldRpg.Host/WorldRpgProduct.cs`; `src/WorldRpg.Rulesets.Daggerfall/DaggerfallSession.cs` | Reuse composition/admission/update. No new global product manager, pack loader, registry or parallel host. |
| Stats/tracks, F001–F004 | `src/WorldRpg.Kit/Actors/ActorsState.cs`; `src/WorldRpg.Rulesets.Daggerfall/Content/DaggerfallMechanicsState.cs` | General modifier behavior extends the existing actor Mechanics owner. Inspect paired stat/track handling and ordinary stat reads separately. |
| Progression, F016 | `src/WorldRpg.Rulesets.Daggerfall/Policies/DaggerfallFormulaPolicy.cs`; `DaggerfallRewardReactions.cs` in the ruleset root | Classic formulas exist, but `PlanProgression` currently invokes `ExperimentalXpLevel` on kill XP. Plan classic skill-use/level policy and its consumers, not just more formula helpers. |
| Damage/targeting, F011–F021 | `src/WorldRpg.Rulesets.Daggerfall/Modules/Combat/CombatModule.cs`; `DaggerfallMeleeTargeting.cs` in that directory | Extend current equipment, targeting, random, cooldown and guarded mutation paths; no second combat result authority. |
| Stamina/recovery, F010 | `src/WorldRpg.Rulesets.Daggerfall/Modules/Combat/DaggerfallStaminaRecoveryModule.cs` | Ordinary stamina recovery exists. Rest/travel recovery must share applicable state/formulas rather than supplanting or double-applying it. |
| Weapon presentation, F018/F019/F045 | `src/WorldRpg.Rulesets.Daggerfall/Presentation/PrivateersHoldAppearance.cs`: `UpdateRightHandEquipment`, `CreateViewmodel`, `StartWeaponStrike`, `ToggleWeaponDrawn` | Viewmodel and equipped-item selection already exist despite Absent survey notes. Extend their supported mappings/actions and ordinary callers. |
| Blood/appearance, F066 | Same appearance owner: `SpawnBlood`, attack/death fact consumption | Blood presentation exists; a comment reserves magic sparkle for a future spell fact. Do not call the whole feedback system absent or count reserved behavior as implemented. |
| Inventory/containers, F037–F044 | `src/WorldRpg.Kit/Inventory/MechanicsInventoryCoordinator.cs`; `MechanicsInventoryContainerCoordinator.cs` | Use the current item/container/equipment lifecycle; quest items, shop stock, wagon storage and enchanting must not introduce competing inventories. |
| Corpse loot, F043/F067 | `src/WorldRpg.Rulesets.Daggerfall/Modules/Loot/DaggerfallCorpseLootModule.cs` | Corpse registration, seeding, transfer and restore paths exist. Extend dynamic actor and world lifecycle; no replacement corpse system. |
| Actor behavior and movement, F022/F063 | `src/WorldRpg.Rulesets.Daggerfall/Modules/Behavior/DaggerfallEnemyBehaviorModule.cs`; `src/WorldRpg.Kit/Actors/ActorNavigationCoordinator.cs`; `src/WorldRpg.Kit/Controls/` | Existing controls/navigation/perception integration supplies the starting owner; policy extensions do not justify downstream spatial machinery. |
| Save, F112 | `src/WorldRpg.Rulesets.Daggerfall/DaggerfallSavePayload.cs`: `ValidateForRestore`; `src/WorldRpg.Host/WorldRpgSaveStore.cs` | Current restore matches authored actor IDs and Privateer's Hold inputs. Dynamic/world/effect/quest state needs a planned extension to the same save path. |
| Dungeon textures, F060 | `src/Daggerfall.Import/Arena2/DungeonTextureTableTransform.cs` | Classic per-location texture table conversion already exists. Schedule broader corpus use and any actually missing seasonal policy, not another table transform. |
| World geometry, F055/F057/F058 | `src/Daggerfall.Import/Arena2/RdbDecoder.cs`; `MapsDecoder.cs`; `Normalization/DungeonNormalizer.cs` | Existing dungeon geometry does not establish outdoor RMB or terrain coverage. Keep these import/runtime responsibilities separate. |
| Thin UI, F044/F095–F107 | `src/WorldRpg.Rulesets.Daggerfall/Presentation/`; `src/ui/` | Extend projections and semantic actions per owning behavior. Do not add a new UI state authority or recreate DFU's widget framework. |

The original feature-map notes remain available in the CSV; do not erase their
provenance to make the current picture look cleaner. Reconcile further stale
claims locally while drafting the affected tasks.

## Capability order for task dependencies

These identifiers name contracts to define or extend, not proposed classes or
packages. Split contracts into smaller tasks when useful. “Needs” means the
specific capability used, not completion of an entire numbered area. Existing
implementations can satisfy a prerequisite once their relevant behavior is checked.

| Capability | Concrete scope to settle | Needs | Main owners / plan areas |
| --- | --- | --- | --- |
| CAP-ID | Stable authored/dynamic identity, references and lifetime across load/unload; no serialized Engine handles | Existing composition | Kit + Daggerfall, 2 |
| CAP-SAVE | Per-owner capture/reconstruction, dynamic collections, content identity and consistent restore ordering | CAP-ID, existing save envelope | Daggerfall + Host, 2 |
| CAP-TIME | Game/calendar time, admitted advancement, rest/travel/prison elapsed intervals, ordered timed consequences | CAP-SAVE | Daggerfall; reusable Kit coordination only where useful, 2 |
| CAP-DATA | Normalized referenced catalogs/text/placements and admitted pack resolution | CAP-ID, existing Import/composition | Import + packs + Daggerfall, 3 |
| CAP-ACTOR | Actor creation/destruction, stat/track bindings, disposition and save identity | CAP-ID, CAP-SAVE, required CAP-DATA records | Kit + Daggerfall, 5 |
| CAP-ITEM | Instance/stack/container/equipment operations, condition and persistent ownership | CAP-ID, CAP-SAVE, required CAP-DATA records | Kit + Daggerfall, 5 |
| CAP-SITE | Location/building context, enter/exit, anchors, admission/unload and persisted world changes | CAP-ID, CAP-SAVE, relevant CAP-DATA; verified Engine capabilities | Kit + Daggerfall + Import, 4 |
| CAP-INTERACT | Stable target/context, activation modes, locks/doors/containers and named outcomes | CAP-SITE, relevant CAP-ACTOR/CAP-ITEM | Kit + Daggerfall, 4/6 |
| CAP-MOVE | Movement modes/costs/support transitions over Engine stepping | CAP-ACTOR, relevant CAP-SITE | Kit + Daggerfall, 6 |
| CAP-HIT | Shared targeting/hit/damage/condition/death operations and outcomes | CAP-ACTOR, CAP-ITEM, existing targeting; relevant CAP-MOVE | Daggerfall + Kit mechanisms, 6 |
| CAP-EFFECT | Active effect identity, sources, stacking/rounds, target flags, cancellation and save behavior | CAP-ACTOR, CAP-TIME, CAP-SAVE, verified Engine modifiers/effects | Kit coordination + Daggerfall, 7 |
| CAP-CAST | Spell bundle admission, costs, delivery, chance/resistance/reflection and result | CAP-EFFECT, targeting and relevant CAP-HIT; CAP-DATA spell records | Daggerfall, 7 |
| CAP-NPC | Static/civilian/questor identity, site binding, interaction and faction references | CAP-ACTOR, CAP-SITE, CAP-DATA | Daggerfall + Kit lifetime, 8 |
| CAP-SOCIAL | Reputation/membership/rank/crime records and named mutations | CAP-ID, CAP-SAVE, CAP-DATA; NPC identity where used | Daggerfall, 9 |
| CAP-TEXT | Text/book lookup, context values, global and scoped macros, choices | CAP-DATA; CAP-TIME/CAP-SOCIAL only for corresponding substitutions | Daggerfall + Import + UI, 3/9 |
| CAP-TRAVEL | Rest/travel/transport costs, time advancement and encounter consequences | CAP-TIME, CAP-SITE, CAP-ACTOR; CAP-ITEM/CAP-EFFECT where used | Daggerfall, 8 |
| CAP-SERVICE | Real purchase/repair/training/cure/bank/guild transactions and eligibility | CAP-ITEM, CAP-SOCIAL, CAP-TIME; CAP-EFFECT for cures | Daggerfall, 9 |
| CAP-QSTATE | Quest identity, tasks/resources/symbols, start/end and persistent records | CAP-ID, CAP-SAVE, CAP-DATA | Daggerfall; Import quest records, 11 early |
| CAP-QTIME | Quest clocks, ordering, cancellation and task transitions | CAP-QSTATE, CAP-TIME | Daggerfall, 11 |
| CAP-QBIND | Person/place/foe/item binding, spawn queues and cleanup | CAP-QSTATE, CAP-SITE, CAP-NPC, CAP-ACTOR, CAP-ITEM | Daggerfall, 11 |
| CAP-QACTION | Individually listed actions using existing domain operations | CAP-QSTATE plus each action's actual needs; not all of magic/services | Daggerfall, 11 |
| CAP-SPECIAL | Artifact/transformation/summoning behaviors | CAP-EFFECT/CAST plus selective CAP-QSTATE/QACTION, world/social/item operations | Daggerfall, 10 late |
| CAP-PRESENT | Per-subsystem projections, semantic UI actions, appearance/audio policy | The particular domain operation; existing Engine/DOM path | Daggerfall + UI + Engine, across areas |

Quest/effect dependency rule: implement quest start/end and common effect admission
before special effects that start quests or quest actions that cast spells. Do not
make “all magic” and “all quests” depend on one another. The same applies to early
NPC identity versus later civilian wandering, and faction state versus guild quests.

World action ordering needs explicit task attention: persistent door/lock state,
trigger/action links, quest placement and subsequent unload/reload must refer to
the same identities. Capturing a visible door's state is not enough if the next
load recreates it from unchanged authored data.

## Behavioral splits that must survive task creation

This is an enumeration checklist, not a prescription for one ticket per bullet.
The feature and specialist inventories provide donor references for these splits.

- **Character:** race/gender/name/face, career/custom class and advantages, reflexes,
  background answers, starting values/items/spells, permanent/live values, skill-use
  attribution, advancement eligibility, level-up choices and gains. Preserve the
  distinction between a formula and every event that invokes it.
- **Combat:** weapon versus unarmed selection, swing/proficiency/race/backstab and
  enemy-type factors, body part, material gates, hit chance, enemy multi-attacks,
  damage and equipment wear, poison/on-hit effects, death/corpse/rewards, ranged
  ammunition/delivery, equipped presentation and attack timing. Effects and items
  share accepted hit results instead of recomputing damage independently.
- **Items:** all selected template categories, material/variant, stackability,
  uniqueness, condition/repair, identification, equip restrictions, weight/money,
  use/consume/drop/transfer, loot generation, stolen/quest/enchantment meaning and
  held/strike/used effects. State follows items across player, corpse, shop and wagon.
- **Movement/world:** normal locomotion modes, fatigue/breath/fall consequences,
  activation modes, locks/doors/bashing, interior/exterior/dungeon transitions,
  terrain/city/dungeon data, water, moving actions/triggers, discovery, time/weather,
  NPC presence and encounter selection. Mechanical support belongs to Engine.
- **Society:** reaction/reputation and faction relations, membership/rank/expulsion,
  service eligibility, theft/witness/guard consequences, arrest/court/prison/fines,
  trading/repair/identification/training, tavern lodging, account/credit/loan/default,
  property/transport purchase, dialogue/directions/rumors and guild quest offers.
- **Magic:** use every leaf of the magic inventory, including duration/chance,
  magnitude, targeting/element, saves/resistance, caster/target lifetime and cleanup;
  recipes and spell/item construction are distinct from casting/using the result.
- **Quests:** use every retained resource/action leaf, including pre-spawn queued
  operations, deadlines during time skips, resource relocation, lifecycle cleanup,
  save/load and main/guild/misc content binding. No unimplemented action is success.
- **UI/audio:** information, choices and consequences belong to the respective
  subsystem task. Name required actions/projections, not DFU window classes. Music
  selection/loop lifetime and the long-duration Engine exercise remain separate.

## Decision register for task authors

Scope decisions below are sufficient to begin drafting. A local unresolved detail
blocks its affected task specification, not all task creation. Record a specific
source comparison and decision as that task is drafted; do not issue vague research
tickets for every formula or silently choose whichever implementation is easier.

| ID | Decision / working baseline | Remaining task-local work |
| --- | --- | --- |
| DEC-01 | Classic behavior is the baseline; DFU supplies reverse-engineering evidence and explicitly selected fixes. | For any known difference, identify source behavior and select it explicitly. Unrelated parts can be drafted immediately. |
| DEC-02 | Classic skill-use progression is required; existing experimental kill-XP leveling is not its substitute. | Define skill-use events, advancement and rest/level-up integration in F016 tasks; preserve unrelated experimental tooling unless removal is scoped. |
| DEC-03 | Donor enemy damage selection intentionally differs from classic: `FormulaHelper.CalculateAttackDamage` may prefer stronger unarmed damage for armed enemies. Follow classic equipped-weapon semantics for baseline coverage. | Verify each enemy/career's authored attack data while drafting F011/F020. Do not copy this DFU choice accidentally. |
| DEC-04 | Rusty save semantics are included; reading/writing binary classic/DFU saves is outside the default scope. | Define product schema changes and same-product save behavior; don't implement donor serialization classes. |
| DEC-05 | Base-language text and functional DOM workflows are included. Exact Unity UI layout, widget system, retro postprocess mode, localization expansion and mod protocols are outside baseline. | Preserve gameplay information/actions. Any optional extension receives its own explicit scope, not a hidden prerequisite. |
| DEC-06 | Classic quests are required. DFU-only sources remain individually visible as candidates; inclusion follows behavior/content provenance, not filename availability alone. | Use quest inventory/catalog membership when grouping tasks. Classify helpers needed by retained classic behavior separately from optional added quests. |
| DEC-07 | Source-format quirks and static deterministic generation belong in Import where possible. Runtime randomness uses Engine. | If a dynamic classic sequence is required, specify the exact observable property and verify safe Engine support; no general downstream RNG port. |
| DEC-08 | Existing Engine capabilities are reuse candidates, not hypothetical blockers. | Verify required property in safe packaged API during task drafting; create a narrow upstream task only for a confirmed gap. |
| DEC-09 | Ordinary music looping remains included; MIDI excluded. Local MP3 is an input candidate, not a promised supported runtime format. | Pick a user-provided track, supported admission/conversion path and bounded duration when drafting the audio experiment. Its input is not needed for other tasks. |
| DEC-10 | Full original content coverage is the target; unused/duplicate/malformed records need individual dispositions. | Import/publication tasks enumerate record identities and resolve exceptions; filesystem counts alone do not establish usable content or parity. |
| DEC-11 | When original semantics remain unknown but DFU implements a concrete behavior, use that behavior as an explicitly provisional donor baseline unless it conflicts with a settled decision. | Record the uncertainty and the actual chosen value/rule in the owning task; do not invent a value, claim exact classic fidelity, or require an open-ended reverse-engineering exercise before implementation. Reconcile later if better evidence appears. |

No new user decision is needed to create the baseline task graph. If a future
source comparison exposes a material scope choice not settled here, isolate and
describe that choice while continuing unrelated drafting.

## Drafting procedure and readiness

1. Select canonical F IDs and any SUP/FORM/MAG/QST/CNT leaves. Name the behavior precisely;
   carry dispositions forward, including exclusions and unresolved subcases.
2. Inspect the listed current owner and safe Engine capabilities. State what is
   reused, extended or added, and the persistent-state owner. New code must not
   become a second implementation of an existing operation.
3. Resolve required capability contracts and task-local donor differences. Specify
   inputs/results, transitions, ordering and interoperability; choose actual task
   dependencies, not a dependency on every earlier plan area.
4. Include authored records and real callers where this task owns them. If a later
   task owns the consumer, name that dependency without manufacturing a demo. A
   primitive still implements its whole contract, including specified state changes.
5. Define focused semantic/interoperability checks and assign useful narrow drift
   review scopes. No default interactive deliverable gate or “working combat” proxy.
6. Keep a many-to-many mapping from feature/content IDs to created task IDs. Every
   retained leaf must be covered by some task; aliases and excluded rows must not
   generate duplicate or pointless work. Reconcile this mapping before dispatch.

The preparation packet accounts for the current feature-map rows, expands the
largest behavior families, states content scope and identifies current reuse paths.
It is ready for task drafting, with exact formulas/API contracts/source-record
exceptions resolved within that drafting work. It does not claim every proposed
API exists, every original behavior has been audited, or every source record imports.

## Evidence limits

Den project guidance and completed campaign #7533 were refreshed during preparation;
the downstream brief revision remains the one read during the planning discussion.
Local source checks are explicitly listed above. DFU was consulted through the
`daggerfall-unity` code index and exact source, including
`Game/Formulas/FormulaHelper.cs` (`CalculateAttackDamage`, its weapon-selection
difference, modifier calls and hit consequences). Outcome: adapt semantics to the
existing Rusty owners; exclude donor singleton, override-hook and Unity topology.
Specialist inventories record their own source scopes and limitations. The
preparation pass performed no runtime implementation, task creation, deployment
or interactive tests. The subsequent task-creation pass is recorded separately in
the [task index](coverage/task-index.md), including its checks and evidence limits.
