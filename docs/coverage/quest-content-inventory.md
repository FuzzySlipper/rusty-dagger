# Quest content inventory

This is a bounded quest content and source inventory for the
[Daggerfall coverage plan](../daggerfall-coverage-plan.md). This file is a
directory inventory plus a small semantic orientation survey. It is not a
claim that the donor quest runtime has been behaviorally audited, and it is
not a task list. Task drafting should turn the records below into bounded
work only after each source-backed behavior and its local owner is resolved.

The source reference is the frozen Daggerfall Unity donor checkout at
`/home/research/daggerfall-unity`; its exact revision is logged in the Den
coverage campaign. The Codebase Memory index of that checkout is a navigation
aid; the paths and line anchors below were checked against the checkout itself.
The counts in this file are measurements of that donor revision.

## Reading rules and bounded scope

The classic candidate is the source-backed original/CompUSA corpus represented
by `QuestList-Classic.txt`, the 36 main-story/tutorial files, and the two cure
files. The list also retains broken, disabled, and unused records so that
coverage work can make an explicit disposition. `QuestList-DFU.txt` and the
21 `__DEMO` scripts are kept in a separate DFU/test category. A quest pack
extracted into `StreamingAssets/QuestPacks` is an extension point, not part of
the shipped baseline measured here.

The donor's comments claim 249 classic quests, with 211 in the list. The file
at this revision contains 210 named list rows: 187 active and 23 rows prefixed
with `-`. Adding the 36 main/tutorial files and two cures to the 210 list rows
gives 248 named records. This one-record discrepancy is retained as an
inventory issue; it is not silently rounded to 249 or treated as completed
content.

The `QST-*` identifiers below are planning identifiers for source rows. They
are stable by relative path, using a bytewise lexical sort of the donor path;
they do not imply a local implementation or replace the source filename. Do
not renumber an ID when regrouping the table. Add a new ID when a later source
file is found, and preserve the source path and donor revision in the record.

## Local anchors and ownership

The [feature ledger](feature-ledger.md) (F079–F087) carries the current
dispositions of the quest machine, instance, parser, tasks/clock,
places/persons, foes/items, messages/symbols, action library, and macro
expansion. The plan's
[quest machinery section](../daggerfall-coverage-plan.md#11-quest-machinery-and-action-families)
requires normalized classic source data, persistent instances and symbols,
task/deadline/message behavior, stable world bindings, actual operations for
actions, and explicit unsupported behavior.

Current product owners:

* `Daggerfall.Import` converts quest sources (`Arena2/QuestSourceReader.cs`,
  `Normalized/DaggerfallQuests.cs` and the quest table readers) and publishes the
  quest corpora (`Publication/ClassicQuestCorpusPublication.cs`,
  `FightersGuildQuestCorpusPublication.cs` and `NamedQuestCorpusPublication.cs`,
  with excluded source lines in `QuestCorpusExclusions.cs`).
* `content/worldrpg/content-packs/` holds the published `daggerfall.quests.*`
  packs beside the base and location packs.
* The ruleset interprets them: `Content/DaggerfallQuests.cs` and the corpus
  content readers (`DaggerfallClassicQuestCorpusContent`,
  `DaggerfallFightersGuildQuestCorpusContent` and
  `DaggerfallNamedQuestCorpusContent`); `DaggerfallQuestInstances.cs` (durable
  instance, resource and symbol state), `DaggerfallQuestTasks.cs` (compiler and
  runner), `DaggerfallQuestClocks.cs`, `DaggerfallQuestMessages.cs` and
  `DaggerfallQuestRuntime.cs` (the session adapter to player, progression and
  calendar owners). Action families have their own owners, among them
  `DaggerfallQuestItems`, `DaggerfallQuestPlaces`, `DaggerfallQuestPeople`,
  `DaggerfallQuestPlacements`, `DaggerfallQuestSpawning`, `DaggerfallQuestSocial`,
  `DaggerfallQuestMagic`, `DaggerfallQuestEnvironment`, `DaggerfallQuestJournal`
  and `DaggerfallQuestOffers`, with `DaggerfallSession.Quest*.cs` session
  adapters. `DaggerfallState.Quests` holds the instances and
  `DaggerfallSavePayload` persists them.
* Reusable identity/state coordination may belong to Kit. The Host must not
  acquire Daggerfall quest semantics.

## Donor source inventory

There are 15 top-level C# files under
`Assets/Scripts/Game/Questing/`, 82 action files directly under
`Assets/Scripts/Game/Questing/Actions/`, and one action file under its `Demo/`
subdirectory. The 15 core files have stable IDs here so prerequisite records
can point to a source owner:

| ID | Donor file | Source role | Local disposition |
| --- | --- | --- | --- |
| QST-CORE-001 | `Game/Questing/Clock.cs` | Quest-clock resource, alarms, ranges, flags, game-time countdown | Runtime Daggerfall quest/calendar state inside Engine admission; `DaggerfallQuestClocks` implements the duration forms, flags, ranges and in-order deadline catch-up |
| QST-CORE-002 | `Game/Questing/Foe.cs` | Symbolic quest foe and party/resource resolution | Runtime; `DaggerfallQuestResourceAllocator`, `DaggerfallQuestSpawning` and `DaggerfallQuestFoeLifecycle` own selection, spawn and durable actor identity |
| QST-CORE-003 | `Game/Questing/Item.cs` | Symbolic quest item, artifact and placement resolution | Runtime; `DaggerfallQuestItems` binds quest items over the Kit/Engine inventory substrate with Daggerfall definitions |
| QST-CORE-004 | `Game/Questing/Message.cs` | Popup, journal, letter, rumor text and variants | Runtime; `DaggerfallQuestMessages` owns variants, macros and delivery to thin DOM projections; base language only (DEC-05) |
| QST-CORE-005 | `Game/Questing/Parser.cs` | QRC/QBN source parser and resource/task construction | Offline Import (`QuestSourceReader`); every published source compiles; no runtime donor parser topology |
| QST-CORE-006 | `Game/Questing/Person.cs` | Symbolic NPC and faction/person resolution | Runtime; `DaggerfallQuestPeople` and `DaggerfallQuestNpcOverlays` bind stable world/NPC identity |
| QST-CORE-007 | `Game/Questing/Place.cs` | Permanent, remote, local, and random quest locations | Offline catalog plus runtime world binding; `DaggerfallQuestPlaces` allocates and `DaggerfallQuestPlacements` places against normalized quest markers. Remaining: destinations outside published site profiles (see quest destinations) |
| QST-CORE-008 | `Game/Questing/Quest.cs` | Live quest instance, lifecycle, tasks, resources, save state | Runtime; `DaggerfallQuestInstances` owns the live instance, tombstone and save state, persisted through `DaggerfallSavePayload` |
| QST-CORE-009 | `Game/Questing/QuestAction.cs` | Action interface/template, trigger flags, update/check/save contract | Runtime; compiled to typed ruleset operations in `DaggerfallQuestTasks`, no reflection registry |
| QST-CORE-010 | `Game/Questing/QuestListsManager.cs` | Classic/DFU list loading, guild/social selection, pack discovery | Offline catalog/import plus ruleset selection policy (`DaggerfallQuestOffers`) |
| QST-CORE-011 | `Game/Questing/QuestMCP.cs` | Quest macro/context data source | Runtime; explicit quest text context in `DaggerfallQuestMessages`, no global singleton |
| QST-CORE-012 | `Game/Questing/QuestMachine.cs` | Quest loading, action registration, scheduling, ticking, protected quests | Adapted lifecycle inside existing Engine admission (`DaggerfallQuestInstances`); the three protected quests are a published source decision; no donor scheduler/singleton |
| QST-CORE-013 | `Game/Questing/QuestResource.cs` | Shared resource parent/symbol/message/click/hidden behavior | Runtime; `DaggerfallQuestResourceAllocator` owns the stable resource model |
| QST-CORE-014 | `Game/Questing/Symbol.cs` | Named quest symbol and value/resource references | Runtime; typed resource symbols persisted in the canonical instance save |
| QST-CORE-015 | `Game/Questing/Task.cs` | Task conditions, action sequence, trigger/repeating state | Runtime; typed task kinds with ordering, rearm and persistence rules in `DaggerfallQuestTasks` |

The helper source used by quest messages is outside `Game/Questing` and is
listed separately because it is a prerequisite rather than an action file:

| ID | Donor file | Disposition |
| --- | --- | --- |
| QST-HELPER-001 | `Assets/Scripts/Utility/QuestMacroHelper.cs` | Runtime; `DaggerfallQuestMessages` implements the context, binding, detail, faction, resource and `%vcn` token forms |
| QST-HELPER-002 | `Assets/Scripts/Utility/MacroHelper.cs` | Donor macro catalog; context macros resolve through the shared text resolver, and their caller-context values follow F091; do not port the static Unity helper |

## Prerequisite groups

The action inventory is grouped by the capability it needs first. Every
production action compiles to a typed operation in `DaggerfallQuestTasks`; an
unmatched source line becomes `Unsupported` and fails its instance rather than
succeeding, and the published corpus has none. A row marked `R` is implemented
by the named owner. A row marked `U` is implemented with an open detail named in
its **Remaining** clause. Ownership follows the coverage plan; these flags do not
reopen settled Engine/Kit/ruleset boundaries. `H` is a donor helper/demo and is
not a classic runtime parity promise. "No classic usage" marks a form that the
published classic corpus never uses.

The grouping is dependency guidance, not an implementation sequence. For
example, a quest action can be parsed before the actual world operation exists,
but it must remain diagnosed/pending rather than becoming a no-op.

### Quest instance, tasks, and time

Prerequisites: `QST-CORE-005`, `QST-CORE-006`, `QST-CORE-008` through
`QST-CORE-013`; Daggerfall calendar and durable save state.

| ID | File | Semantic role | Disposition |
| --- | --- | --- | --- |
| QST-ACT-010 | `ClearTask.cs` | Clear a named task | R — `DaggerfallQuestTasks` runner |
| QST-ACT-019 | `DailyFrom.cs` | Always-on daily time-window condition | R — inclusive daily window on the admitted calendar, as in the donor |
| QST-ACT-026 | `EndQuest.cs` | End a quest, optionally showing text | R — ends the instance through pending-end passes |
| QST-ACT-046 | `PickOneOf.cs` | Select one task/resource from alternatives | R — `DaggerfallQuestInstances` picks through Engine keyed random and persists the result |
| QST-ACT-062 | `RunQuest.cs` | Start a child quest and branch on result | R — child start with cycle rejection and result branch; no classic usage |
| QST-ACT-067 | `StartQuest.cs` | Start a named/indexed quest | R — scheduled start; the `S0000999` references to sources 997/998, absent from the donor, are diagnosed |
| QST-ACT-068 | `StartStopTimer.cs` | Start or stop a `Clock` resource | R — `DaggerfallQuestClocks`; a missing clock fails the quest explicitly |
| QST-ACT-069 | `StartTask.cs` | Start/set a named task | R — `DaggerfallQuestTasks` runner |
| QST-ACT-075 | `UnsetTask.cs` | Unset a named task | R — `DaggerfallQuestTasks` runner |
| QST-ACT-082 | `WhenTask.cs` | Condition on task state, including `and`/`and not` forms | R — `TryCompileWhen`/`CheckCondition` preserve repeating/always-on ordering |

### World places, markers, and movement

Prerequisites: `QST-CORE-007`, stable world/building/dungeon identities,
normalized markers, and the existing spatial/session owner.

| ID | File | Semantic role | Disposition |
| --- | --- | --- | --- |
| QST-ACT-017 | `CreateNpcAt.cs` | Create an NPC at a named place | R — reserves the place and places the resource through `DaggerfallQuestPlacements` |
| QST-ACT-025 | `DroppedItemAtPlace.cs` | Trigger when an item is dropped at a place | R — `DaggerfallQuestWorldActions` checks the bound item and place and latches once |
| QST-ACT-045 | `PcAt.cs` | Condition on the player reaching a place; donor accepts `set` and `do` forms | R — `DaggerfallQuestWorldTriggers` accepts both `set` and `do`; an unknown `any` discriminator is diagnosed |
| QST-ACT-047 | `PlaceFoe.cs` | Place a foe at a place or marker | R — `DaggerfallQuestPlacements` queues placement until the destination is visited |
| QST-ACT-048 | `PlaceItem.cs` | Place an item at a place, quest marker, or any marker | R — `DaggerfallQuestPlacements` queues placement until the destination is visited |
| QST-ACT-049 | `PlaceNpc.cs` | Place an NPC at a place or marker | R — `DaggerfallQuestPlacements` queues placement until the destination is visited |
| QST-ACT-060 | `RevealLocation.cs` | Reveal a quest location/map target | R — `DaggerfallSession.QuestWorldActions` discovers the site and records the map note |
| QST-ACT-071 | `TeleportPc.cs` | Transfer the player to a place/marker | U — `DaggerfallSession.QuestWorldActions` moves the player to an admitted profile's quest marker. Remaining: a destination without a published profile is diagnosed (see quest destinations) |
| QST-ACT-079 | `WhenPcEntersExits.cs` | Trigger on exterior entry/exit type | R — `DaggerfallQuestWorldTriggers` enforces the exterior `p1=2` requirement |
| QST-ACT-083 | `WorldUpdate.cs` | Mutate world/block/building variant | U — the `location` form executes. Remaining: `locationnew`, `block`, `building`, `blockAll` and `buildingAll` compile but are diagnosed and stall their task; no classic usage |

### Actors, questors, foes, and presentation identity

Prerequisites: actor lifetime, stable dynamic IDs, actor placement, foe
catalogs, combat/effect admission, and thin presentation projections.

| ID | File | Semantic role | Disposition |
| --- | --- | --- | --- |
| QST-ACT-001 | `AddAsQuestor.cs` | Register a resource as a questor | R — `DaggerfallQuestNpcOverlays` |
| QST-ACT-003 | `AddFace.cs` | Add a face/presentation identity to an NPC or foe | R — `DaggerfallQuestNpcOverlays`, with portrait order and visible slots persisted |
| QST-ACT-007 | `ChangeFoeInfighting.cs` | Change foe attackability/infighting state | R — `DaggerfallQuestFoeRelations`; no classic usage |
| QST-ACT-008 | `ChangeFoeTeam.cs` | Change foe team by numeric or symbolic value | R — `DaggerfallQuestFoeRelations`; no classic usage |
| QST-ACT-015 | `CreateFoe.cs` | Create/spawn foe parties, including periodic/percent forms | R — `DaggerfallQuestSpawning`, including the `indefinitely` and `send` forms |
| QST-ACT-016 | `CreateNpc.cs` | Create a quest NPC | R — `DaggerfallQuestNpcLifecycle` |
| QST-ACT-021 | `DestroyNpc.cs` | Destroy a quest NPC | R — `DaggerfallQuestNpcLifecycle`; a destroyed resource cannot be restored or recreated |
| QST-ACT-023 | `DropAsQuestor.cs` | Remove a resource from questor registration | R — `DaggerfallQuestNpcOverlays` |
| QST-ACT-024 | `DropFace.cs` | Remove a face/presentation identity | R — `DaggerfallQuestNpcOverlays` |
| QST-ACT-027 | `Enemies.cs` | Make foes hostile or clear enemy state | R — `DaggerfallQuestFoeRelations`; no classic usage |
| QST-ACT-032 | `HideNpc.cs` | Hide an NPC | R — `DaggerfallQuestNpcLifecycle` keeps identity across hide and restore |
| QST-ACT-033 | `InjuredFoe.cs` | Trigger on foe injury | R — `DaggerfallQuestFoeLifecycle` records actual damage |
| QST-ACT-036 | `KillFoe.cs` | Command a foe death | R — `DaggerfallQuestFoeLifecycle` uses the accepted death and corpse path |
| QST-ACT-037 | `KilledFoe.cs` | Trigger on foe death/count | R — `DaggerfallQuestFoeLifecycle` counts actual group deaths, persisted |
| QST-ACT-043 | `MuteNpc.cs` | Mute NPC dialogue | R — `DaggerfallQuestNpcOverlays`, with mute rearm |
| QST-ACT-055 | `RemoveFoe.cs` | Remove a foe | R — `DaggerfallQuestFoeLifecycle`; removal before placement creates no death or corpse |
| QST-ACT-058 | `RestoreNpc.cs` | Restore a hidden/destroyed NPC | R — `DaggerfallQuestNpcLifecycle` |
| QST-ACT-059 | `RestrainFoe.cs` | Restrain a foe | R — `DaggerfallQuestFoeRelations`; an accepted player strike breaks only that foe's restraint |
| QST-ACT-066 | `SpawnCityGuards.cs` | Spawn city guards immediately or normally | R — `DaggerfallQuestGuardSpawning`; no classic usage |
| QST-ACT-074 | `UnrestrainFoe.cs` | Release a restrained foe | R — `DaggerfallQuestFoeRelations` |
| QST-ACT-078 | `WhenNpcIsAvailable.cs` | Always-on NPC availability condition | R — `DaggerfallQuestNpcLifecycle` reads static-NPC availability and reservation |

### Items, rewards, and economy

Prerequisites: item definitions and stable instances, the existing Mechanics
inventory/equipment substrate, quest binding, and Daggerfall reward/economy
policy.

| ID | File | Semantic role | Disposition |
| --- | --- | --- | --- |
| QST-ACT-012 | `ClickedItem.cs` | Trigger when a quest item is clicked | R — `DaggerfallQuestItemInteractions` on actual item selection |
| QST-ACT-028 | `GetItem.cs` | Acquire a quest item | R — `DaggerfallQuestItems` grants bound quest items |
| QST-ACT-029 | `GiveItem.cs` | Give a quest item to a resource | R — `DaggerfallQuestItemInteractions` gives once to current and future recipient inventories |
| QST-ACT-030 | `GivePc.cs` | Give the player reward/notification and mark quest success | R — `DaggerfallQuestRewards`: real loot, success after admission, town delivery delay |
| QST-ACT-031 | `HaveItem.cs` | Condition on item possession | R — actual inventory reads with quest binding |
| QST-ACT-034 | `ItemUsedDo.cs` | Trigger a task when an item is used | R — `DaggerfallQuestItemInteractions` on actual inventory use |
| QST-ACT-042 | `MakePermanent.cs` | Make a quest item permanent | R — `DaggerfallQuestItems` |
| QST-ACT-044 | `PayMoney.cs` | Payment branch to paid/unpaid task | R — `DaggerfallQuestItemInteractions` atomic paid/unpaid branch; no classic usage |
| QST-ACT-070 | `TakeItem.cs` | Remove an item from the player | R — guarded removal with no partial mutation |
| QST-ACT-072 | `TotingItemAndClickedNpc.cs` | Trigger while carrying an item and clicking an NPC | R — `DaggerfallQuestItemInteractions` NPC handoff of the bound item |

### Dialogue, messages, rumors, and journal

Prerequisites: `QST-CORE-004`, `QST-CORE-011`, stable person/place/item
references, message localization, journal persistence, and semantic UI
actions.

| ID | File | Semantic role | Disposition |
| --- | --- | --- | --- |
| QST-ACT-002 | `AddDialog.cs` | Add dialogue for location/person/item combinations | R — `DaggerfallQuestDialogueLinks` |
| QST-ACT-011 | `ClickedFoe.cs` | Trigger when a foe is clicked; may say or branch | R — `DaggerfallQuestActorClicks`; no classic usage |
| QST-ACT-013 | `ClickedNpc.cs` | Trigger when an NPC is clicked; may say or branch | R — `DaggerfallQuestActorClicks` |
| QST-ACT-022 | `DialogLink.cs` | Link dialogue to location/person/item | R — `DaggerfallQuestDialogueLinks`; a stale link is diagnosed |
| QST-ACT-035 | `JournalNote.cs` | Write a journal note | R — writes the personal notebook; no classic usage |
| QST-ACT-040 | `LogMessage.cs` | Add a quest log step/message | R — `DaggerfallQuestJournal` preserves order across restore |
| QST-ACT-053 | `Prompt.cs` | Show a yes/no prompt and branch | R — compiled prompt with persisted result |
| QST-ACT-054 | `PromptMulti.cs` | Show a multi-choice prompt and branch | R — compiled choices with persisted result; no classic usage |
| QST-ACT-056 | `RemoveLogMessage.cs` | Remove a journal/log step | R — `DaggerfallQuestJournal` |
| QST-ACT-061 | `RumorMill.cs` | Add quest rumor to social dialogue | R — quest rumors through `DaggerfallQuestMessages` |
| QST-ACT-063 | `Say.cs` | Show quest message; donor action is one-shot | R — once per action; an unresolved alias fails the quest explicitly |

### Factions, progression, conditions, and disease

Prerequisites: Daggerfall attributes/skills/reputation/crime/faction state,
actual disease/effect operations, progression events, and quest persistence.

| ID | File | Semantic role | Disposition |
| --- | --- | --- | --- |
| QST-ACT-009 | `ChangeReputeWith.cs` | Change reputation with a faction/resource | R — `DaggerfallQuestSocial` |
| QST-ACT-018 | `CurePcDisease.cs` | Cure disease, vampirism, or lycanthropy | R — `DaggerfallSession.QuestDiseases`; transformation cures use the permanent transformation owner |
| QST-ACT-038 | `LegalRepute.cs` | Change legal reputation | R — `DaggerfallQuestSocial` |
| QST-ACT-039 | `LevelCompleted.cs` | Trigger at a minimum player level | R — `DaggerfallQuestRuntime` reads the actual level |
| QST-ACT-041 | `MakePcDiseased.cs` | Inflict disease on the player | R — `DaggerfallSession.QuestDiseases`; a source disease bypasses the saving throw |
| QST-ACT-057 | `ReputeExceedsDo.cs` | Branch when reputation exceeds a threshold | R — `DaggerfallQuestSocial` |
| QST-ACT-065 | `SetPlayerCrime.cs` | Set a player crime flag | R — `DaggerfallQuestSocial`; no classic usage |
| QST-ACT-073 | `TrainPc.cs` | Train a skill with cost/time/reward semantics | R — `DaggerfallQuestRuntime` (time, fatigue, skill tally); no classic usage |
| QST-ACT-077 | `WhenAttributeLevel.cs` | Trigger on minimum attribute | R — `DaggerfallQuestRuntime`; no classic usage |
| QST-ACT-080 | `WhenReputeWith.cs` | Always-on reputation threshold condition | R — `DaggerfallQuestSocial` |
| QST-ACT-081 | `WhenSkillLevel.cs` | Trigger on minimum skill | R — `DaggerfallQuestRuntime`; no classic usage |

### Magic and effects

Prerequisites: normalized spell/effect definitions, common effect admission,
actor targeting, resistance/cure policy, and elapsed-time state.

| ID | File | Semantic role | Disposition |
| --- | --- | --- | --- |
| QST-ACT-004 | `CastEffectDo.cs` | Cast an effect, then perform a task | R — `DaggerfallQuestMagic` requires a real successful delivery |
| QST-ACT-005 | `CastSpellDo.cs` | Cast a spell, then perform a task | R — `DaggerfallQuestMagic` requires a real successful delivery |
| QST-ACT-006 | `CastSpellOnFoe.cs` | Cast a classic/custom spell on a foe | R — `DaggerfallQuestMagic`; a spell queued before placement is delivered once |

### Climate, sound, song, video, and weather

Prerequisites: calendar/climate state, Engine presentation/audio/media contracts,
normalized sound IDs, and explicit scope decisions. The coverage plan excludes
MIDI playback; it does not silently turn song references into successful audio.

| ID | File | Semantic role | Disposition |
| --- | --- | --- | --- |
| QST-ACT-014 | `Climate.cs` | Always-on climate condition/update | R — `DaggerfallQuestEnvironment`; no classic usage |
| QST-ACT-050 | `PlaySong.cs` | Play a song/MIDI resource | R — `DaggerfallQuestEnvironment` maps to an ordinary music cue and diagnoses a missing track; MIDI excluded; no classic usage |
| QST-ACT-051 | `PlaySound.cs` | Play a quest sound, including count/periodic forms | U — `DaggerfallQuestEnvironment` plays periodic and counted sounds with persisted state and diagnoses a missing admitted clip. Remaining: the diagnosis halts the whole quest instance, and sound 386 (`vengence`, `S0000977`) is in no site media manifest |
| QST-ACT-052 | `PlayVideo.cs` | Play quest video | U — Engine Cinematics waits for completion or skip (SUP-18). Remaining: with videos disabled the action is diagnosed and the story quest stalls, where the donor completes; needs a product decision |
| QST-ACT-064 | `Season.cs` | Always-on season condition | R — `DaggerfallQuestEnvironment`; no classic usage |
| QST-ACT-076 | `Weather.cs` | Always-on weather condition/update | R — `DaggerfallQuestEnvironment`; no classic usage |

### Donor helper/demo

| ID | File | Semantic role | Disposition |
| --- | --- | --- | --- |
| QST-ACT-020 | `Demo/JuggleAction.cs` | Demonstration `juggle` action | H — not registered by the production action list and not part of classic parity scope |

The donor `QuestMachine.RegisterActionTemplates()` implementation at
`Game/Questing/QuestMachine.cs:339-432` registers the 82 production action
files. `JuggleAction` is present only as a commented demo registration at
line 342. This is the source-backed distinction between the action inventory
and the helper/demo row above; no generic custom-action ABI should be recreated
downstream.

## Semantic orientation for import and task drafting

This is a focused semantic audit boundary, not a claim that every quest script
has been read.

* `QuestMachine.cs:562-610` reads quest files from `StreamingAssets/Quests`
  and table files from `StreamingAssets/Tables`. `:640-688` parses a quest,
  while `:705-744` starts or schedules it. `:441-516` updates active quests,
  tombstones completed instances, and removes old tombstones. `:522-527`
  treats `S0000999`, `S0000977`, and `_BRISIEN` as protected special quests;
  this is a donor special case to audit as product policy, not a reason to
  hardcode those names in Kit.
* `QuestListsManager.cs:107-204` recursively discovers `QuestList-*.txt` in
  quest packs, loads `QuestList-Classic` and `QuestList-DFU`, then parses group,
  membership, minimum rank/reputation, adult, and one-time flags. The classic
  table's own schema comments at `QuestList-Classic.txt:10-21` are the stable
  catalog contract for those fields. Group labels include guild, temple,
  social, and `Oblivion` records; selection behavior and unused Daedra entries
  remain explicit audit work.
* `Parser.cs:55-162` recognizes `Quest`, `DisplayName`, `QRC`, and `QBN`, and
  `:303-501` parses message blocks, resource declarations, tasks, conditions,
  clocks, and global-variable references. Unknown source lines throw in the
  donor. Import should normalize supported records and retain diagnostics for
  unsupported fields; it must not embed a new general-purpose gameplay DSL.
* `QuestAction.cs:23-107` defines the donor action/template contract,
  including trigger/always-on flags, update/check methods, rearming, and save
  data. `Task.cs:21-46` describes tasks as condition/action subroutines;
  repeating and always-on behavior must be made explicit in local state and
  ordering. `Quest.cs` owns per-quest UID, start/end/tombstone state, resource
  and task updates, and save data. These are behavior references for a compiled
  Daggerfall ruleset, not a reason to port donor class/reflection topology.
* `Clock.cs:112-256` accepts `dd.hh:mm`, `hh:mm`, minute values, `flag`, and
  `range` forms. Its comments and TODOs leave flag meanings and some timing
  behavior partly unresolved. `QuestMachine.Update()` uses a donor Unity
  update cadence, while the clock advances in game time; local work must use
  the one Engine-admitted update and Daggerfall calendar/rest/travel policy.
* `Message.cs` splits variants on `<--->`, recognizes `<ce>`, and expands
  macros at presentation time. `Utility/QuestMacroHelper.cs:91-160` layers
  quest resources over context macros; `:285-294` recognizes context (`%`),
  binding (`=#`), detail (`=`), faction (`==`), and resource (`_`/`__`) forms.
  Preserve token meaning and deterministic references while making text a thin
  DOM projection. Do not port `MacroHelper` as a global Unity singleton.

The open differences for task records are:

* `WorldUpdate` block, building and new-location forms (QST-ACT-083).
* An unavailable sound, spell, disease or media operation returns out of the
  whole instance's advance, so every later task of that quest stops evaluating;
  world, item and social actions end only their own task. Sound 386
  (`vengence`) in the protected `S0000977` is the concrete case (QST-ACT-051).
* Quest video with videos disabled stalls the story quests, where the donor
  always completes (QST-ACT-052, SUP-18).
* Quest destinations outside the published site profiles (below).

Macro context values follow F091. No unsupported action may be a successful
no-op.

## Quest destinations

Place allocation (`DaggerfallQuestPlaceAllocator` in `DaggerfallQuestPlaces.cs`)
selects from the whole imported location catalog, its RMB building records and
the RDB/RMB quest markers, and binds durable region, location and building
identity without consulting site profiles. Travel arrival, building entry,
portals, transitions, relocation and quest teleport admit only the site profiles
the bundle publishes. A quest that selects an unpublished town, building or
dungeon therefore binds correctly, but its destination cannot be entered and the
operation is diagnosed; most `permanent` places, including Daggerfall, Sentinel
and Wayrest, have no profile. Full-world destination publication is open work.

## Shipped quest corpus and deterministic catalog scope

All paths in this section are relative to
`/home/research/daggerfall-unity/Assets/StreamingAssets`.

### Directory accounting

| Directory/file set | Measured contents at donor revision | Scope |
| --- | ---: | --- |
| `Quests/*.txt` | 265 source files | Includes classic source, main/tutorial, cure, one DFU quest, and 21 demo fixtures |
| `QuestPacks/` | `readme.txt` and `.gitignore`; no shipped pack list/content | External/mod extension point; not shipped baseline |
| `Text/Quests/` | `readme.txt` and `.meta`; no localized quest files | Localization override format exists, but no shipped override corpus |
| `Tables/QuestList-Classic.txt` | 210 named rows: 187 active, 23 disabled/commented | Classic guild/social catalog, including broken and unused rows |
| `Tables/QuestList-DFU.txt` | 1 active row: `CUSTOM01` | DFU addition, separate from classic baseline |

The 265 quest source files partition deterministically as follows:

* 205 files correspond to the 187 active plus 18 source-backed disabled rows
  in `QuestList-Classic.txt`.
* Five list rows have no source file at this revision: `80C00Y00`,
  `A0C00Y04`, `M0B40Y04`, `N0C00Y01`, and `R0C40Y23`.
* The 36 main/tutorial files are the 34 `S########.txt` files plus
  `_BRISIEN.txt` and `_TUTOR__.txt`; they are intentionally not list rows.
* `$CUREVAM.txt` and `$CUREWER.txt` are the two classic cure files, also not
  list rows.
* `CUSTOM01.txt` is the one DFU list addition.
* `__DEMO01.txt` through `__DEMO21.txt` are DFU demo/test fixtures. For
  example, `__DEMO03.txt` declares `Quest: __DEMO02`, and `__DEMO06.txt`
  declares `Quest: __DEMO006`; these collisions are evidence that demos are
  not a clean classic content baseline.

`QuestPacks/readme.txt` says to extract packs into that directory before the
game runs. `QuestListsManager` discovers list files recursively and also
allows contributions from mods, but no such pack is present in this checkout.
An exhaustive shipped-corpus backlog therefore ends at the files and tables
listed here; dynamic mod packs require a separate discovered-input inventory.

### Active classic guild, temple, social, and misc catalog

The names below are the active rows (no leading `-`) in the exact donor table.
All of them are published and compiled (see the published corpus below). The
category labels preserve the donor enum; the
`GeneralPopulace` section is the donor's Thieves Guild section.

| Catalog group | Active count | Deterministic source filenames (without `.txt`) |
| --- | ---: | --- |
| FightersGuild | 20 | `M0C00Y11`, `M0C00Y12`, `M0C00Y13`, `M0C00Y14`, `M0B00Y00`, `M0B00Y06`, `M0B00Y07`, `M0B00Y15`, `M0B00Y16`, `M0B00Y17`, `M0B1XY01`, `M0B11Y18`, `M0B20Y02`, `M0B21Y19`, `M0B30Y03`, `M0B30Y04`, `M0B30Y08`, `M0B40Y05`, `M0B50Y09`, `M0B60Y10` |
| MagesGuild | 18 | `N0C00Y10`, `N0C00Y11`, `N0C00Y12`, `N0C00Y13`, `N0B00Y04`, `N0B00Y06`, `N0B00Y08`, `N0B00Y09`, `N0B00Y16`, `N0B00Y17`, `N0B10Y01`, `N0B10Y03`, `N0B11Y18`, `N0B20Y02`, `N0B20Y05`, `N0B21Y14`, `N0B30Y15`, `N0B40Y07` |
| HolyOrder (Temples general) | 16 | `C0C00Y10`, `C0C00Y11`, `C0C00Y12`, `C0C00Y13`, `C0B00Y00`, `C0B00Y01`, `C0B00Y02`, `C0B00Y03`, `C0B00Y04`, `C0B00Y14`, `C0B10Y05`, `C0B10Y06`, `C0B10Y07`, `C0B10Y15`, `C0B20Y08`, `C0B3XY09` |
| HolyOrder (Temples specific) | 8 | `00B00Y00`, `D0B00Y00`, `E0B00Y00`, `F0B00Y00`, `G0B00Y00`, `H0B00Y00`, `I0B00Y00`, `J0B00Y00` |
| GeneralPopulace (donor Thieves Guild) | 15 | `O0A0AL00`, `O0B00Y00`, `O0B00Y01`, `O0B00Y11`, `O0B00Y12`, `O0B10Y00`, `O0B10Y03`, `O0B10Y05`, `O0B10Y06`, `O0B10Y07`, `O0B20Y02`, `O0B2XY04`, `O0B2XY08`, `O0B2XY09`, `O0B2XY10` |
| DarkBrotherHood | 13 | `L0A01L00`, `L0B00Y00`, `L0B00Y01`, `L0B00Y02`, `L0B00Y03`, `L0B10Y01`, `L0B10Y03`, `L0B20Y02`, `L0B30Y03`, `L0B30Y09`, `L0B40Y04`, `L0B50Y11`, `L0B60Y10` |
| KnightlyOrder | 17 | `B0C00Y05`, `B0C00Y06`, `B0C00Y10`, `B0C00Y13`, `B0B00Y00`, `B0B00Y01`, `B0B10Y04`, `B0B20Y07`, `B0B40Y08`, `B0B40Y09`, `B0B50Y11`, `B0B60Y12`, `B0B70Y14`, `B0B70Y16`, `B0B71Y03`, `B0B80Y17`, `B0B81Y02` |
| Witches | 10 | `Q0C00Y01`, `Q0C00Y03`, `Q0C00Y04`, `Q0C00Y06`, `Q0C00Y07`, `Q0C00Y08`, `Q0C0XY02`, `Q0C10Y00`, `Q0C20Y02`, `Q0C4XY04` |
| Commoners | 20 | `A0C00Y00`, `A0C00Y06`, `A0C00Y07`, `A0C00Y08`, `A0C00Y10`, `A0C00Y11`, `A0C00Y12`, `A0C00Y14`, `A0C00Y15`, `A0C00Y16`, `A0C00Y17`, `A0C01Y01`, `A0C01Y03`, `A0C01Y06`, `A0C01Y09`, `A0C01Y13`, `A0C0XY04`, `A0C10Y02`, `A0C10Y05`, `A0C41Y18` |
| Merchants | 12 | `K0C00Y00`, `K0C00Y02`, `K0C00Y03`, `K0C00Y04`, `K0C00Y05`, `K0C00Y07`, `K0C00Y08`, `K0C00Y09`, `K0C01Y00`, `K0C01Y10`, `K0C0XY01`, `K0C30Y03` |
| Vampires | 10 | `P0A01L00`, `P0B00L01`, `P0B00L03`, `P0B00L04`, `P0B00L06`, `P0B01L02`, `P0B10L07`, `P0B10L08`, `P0B10L10`, `P0B20L09` |
| Nobility | 28 | `R0C10Y00`, `R0C10Y01`, `R0C10Y02`, `R0C10Y04`, `R0C10Y05`, `R0C10Y06`, `R0C10Y08`, `R0C10Y09`, `R0C10Y10`, `R0C10Y11`, `R0C10Y12`, `R0C10Y13`, `R0C10Y14`, `R0C10Y15`, `R0C10Y17`, `R0C10Y18`, `R0C10Y20`, `R0C10Y21`, `R0C11Y03`, `R0C11Y16`, `R0C11Y19`, `R0C11Y26`, `R0C11Y28`, `R0C20Y07`, `R0C20Y22`, `R0C30Y25`, `R0C4XY23`, `R0C60Y24` |
| **Total** | **187** | **All active classic list rows** |

The table schema records membership (`N`, `M`, `P`, and temple/specific
letters), minimum rank/level or reputation, adult flag `X`, and one-time flag
`1`. Those values stay in the normalized catalog rather than being inferred
from the filename. `QuestList-Classic.txt` records `R0C11Y28` as needing the
`%vcn` vampire-clan macro for its donor test; that is a concrete macro audit
anchor, not a reason to exclude the rest of Nobility.

### Disabled, broken, duplicate, and unused classic records

The 23 leading-`-` rows remain in the audit scope:

* Ordinary disabled/broken/duplicate rows: `M0B40Y04` (missing QBN; duplicate
  of `m0b30y04`), `N0C00Y01` (missing QRC; intended non-member Banish Daedra),
  `A0C00Y04` (missing QRC; duplicate of `a0c0xy04`), `K0C00Y06` (disabled),
  `R0C11Y27` (duplicate of `r0c10y17`), and `R0C40Y23` (missing QRC; duplicate
  of `r0c4xy23`).
* Daedra rows marked unused because the donor handles them in code:
  `10C00Y00`, `20C00Y00`, `30C00Y00`, `40C00Y00`, `50C00Y00`, `60C00Y00`,
  `70C00Y00`, `80C00Y00`, `80C0XY00`, `90C00Y00`, `T0C00Y00`, `U0C00Y00`,
  `V0C00Y00`, `W0C00Y00`, `X0C00Y00`, `Y0C00Y00`, and `Z0C00Y00`.

Five of the 23 rows have no source file: `M0B40Y04`, `N0C00Y01`, `A0C00Y04`,
`R0C40Y23`, and `80C00Y00`. The other 18 disabled rows have source files and
must not be silently promoted to active content. The donor table's own notes
also identify script errors, adult flags, and one-time behavior; later work
must preserve those dispositions in the catalog.

### Main, tutorial, cure, and DFU additions

The 36 main/tutorial source filenames are:

| File | Donor display name |
| --- | --- |
| `S0000001.txt` | Missing Prince |
| `S0000002.txt` | Prince Helseth's Blackmail |
| `S0000003.txt` | Freeing Medora |
| `S0000004.txt` | Morgiah's Wedding |
| `S0000005.txt` | Concern for Nulfaga |
| `S0000006.txt` | Elysana's Robe |
| `S0000007.txt` | The Werebeast |
| `S0000008.txt` | Who Gets the Totem |
| `S0000009.txt` | Elysana's Betrayal |
| `S0000010.txt` | Stronghold of the Blades |
| `S0000011.txt` | Barenziah's Book |
| `S0000012.txt` | The Emperor's Courier |
| `S0000013.txt` | Mynisera's Letters |
| `S0000015.txt` | Lysandus' Revenge |
| `S0000016.txt` | Journey to Aetherius |
| `S0000017.txt` | Wayrest Painting |
| `S0000018.txt` | Dust of Restful Death |
| `S0000020.txt` | Orcish Treaty |
| `S0000021.txt` | Lich's Soul |
| `S0000022.txt` | Lysandus' Revelation |
| `S0000100.txt` | no `DisplayName` in source |
| `S0000101.txt` | no `DisplayName` in source |
| `S0000102.txt` | no `DisplayName` in source |
| `S0000103.txt` | no `DisplayName` in source |
| `S0000104.txt` | no `DisplayName` in source |
| `S0000106.txt` | no `DisplayName` in source |
| `S0000107.txt` | no `DisplayName` in source |
| `S0000500.txt` | Lord K'avar Quest Part II |
| `S0000501.txt` | Lord K'avar Quest Part III |
| `S0000502.txt` | Former Student Part II |
| `S0000503.txt` | Former Student Part III |
| `S0000977.txt` | Curse of Daggerfall |
| `S0000988.txt` | Mantella Revealed |
| `S0000999.txt` | Main Quest Backbone |
| `_BRISIEN.txt` | Lady Brisienna |
| `_TUTOR__.txt` | Tutorial |

The two classic cure files are `$CUREVAM.txt` (Cure for Vampirism) and
`$CUREWER.txt` (Cure for Lycanthropy). They are source-backed candidate
content, not active list rows. `CUSTOM01.txt` is listed only by
`QuestList-DFU.txt` as a Fighters Guild member quest with minimum requirement
40. It is a DFU addition and must be classified separately from original-game
coverage. `__DEMO01.txt` through `__DEMO21.txt` are DFU demo fixtures and are
not a substitute for missing classic records.

### Published corpus

The quest packs (`daggerfall.quests.*`) publish 243 quests, each compiled from
its source with no source diagnostics:

| Published set | Quests |
| --- | ---: |
| Active classic list rows | 187 |
| Disabled rows with sources: 16 Daedra (`summonOnly`), `K0C00Y06` and `R0C11Y27` (`notOffered`) | 18 |
| Main/tutorial (`story-early` 20, `story-late` 16) | 36 |
| Cures | 2 |
| **Total** | **243** |

The five list rows with no source are absent from every pack, which gives 243 =
248 − 5. `CUSTOM01` and the `__DEMO` scripts are unpublished under DEC-06. Two
source lines (`S0000007:538` and `B0B71Y03:437`) are excluded as provenance only,
and the three `S0000999` references to quests 997 and 998, which have no donor
source, are diagnosed.

## Source tables and catalog references

These files are loaded by `QuestMachine.Awake()` at
`Game/Questing/QuestMachine.cs:282-290`. Counts are non-comment data rows from
the checked snapshot; duplicate aliases and special rows are called out where
they affect normalization.

| ID | Donor table | Rows | Important semantics / references |
| --- | --- | ---: | --- |
| QST-TBL-001 | `Tables/Quests-GlobalVars.txt` | 66 rows, 64 numbered slots | Classic saves store global state in `SAVEVARS.DAT`; rows 5 and 10 are reused aliases for `TookTheCure` and `OpenedShapeshifters`. Preserve slot IDs and provenance. |
| QST-TBL-002 | `Tables/Quests-StaticMessages.txt` | 17 rows, 13 unique IDs | IDs 0, 1000-1010, and 1045 cover base message, quest offer/refusal/acceptance/fail/complete, rumor and questor post-state, journal, and time-lapse messages; 1006-1009 have case aliases. |
| QST-TBL-003 | `Tables/Quests-Places.txt` | 130 rows | Permanent/remote/local/random place keys. The comments define the joined `p2`/`p3` internal code and teleport-transfer byte. `Mantellan_Crux` is a parse/catalog alias for true `MantellanCrux`; retain both source spelling and canonical identity. |
| QST-TBL-004 | `Tables/Quests-Sounds.txt` | 295 rows | Symbolic IDs resolve to `DAGGER.SND`; comments state a sound index space of 460. Examples include 8 `empty`, 13 `fire_daemon`, 92-94 storm, and voice/effect ranges. Audio capability and missing entries remain explicit. |
| QST-TBL-005 | `Tables/Quests-Items.txt` | 120 rows | `p1,p2` class/subclass identity. Includes 24 artifact entries beginning with Masque of Clavicus Vile and ending with Shifters Shirt, plus generic quest items, random maps/recipes, and coins. |
| QST-TBL-006 | `Tables/Quests-Factions.txt` | 438 rows | Person/faction classes: occupations, groups, guild/social classes, permanent NPCs, gods/Daedra, and aliases. Several social mappings are commented/uncertain in source; do not infer a universal faction enum without audit. |
| QST-TBL-007 | `Tables/Quests-Foes.txt` | 62 rows | Non-human foe IDs 0-42 and human classes 128-145. Preserve source aliases such as `Sorceror`/`Sorcerer`; `Knight` has no `MONSTER.BSA` entry according to the table comments. |
| QST-TBL-008 | `Tables/Quests-Diseases.txt` | 17 rows | IDs 0-16 used by `make pc ill` and `cure`; includes ordinary diseases and transformation-related quest flows. |
| QST-TBL-009 | `Tables/Quests-Spells.txt` | 89 rows | Sparse classic spell IDs, including internal nearby/remote unlock codes, duplicate `Holy_Word`/`Holy_Touch` ID 58, and poison IDs 71-76. Normalize source ID and name separately. |

`Tables/Spells-Entity.txt` is present as a related spell table but is not one
of the nine quest tables loaded by `QuestMachine`. `Assets/StreamingAssets/Factions/FACTION.TXT`
is another source-format faction reference; it is not a replacement for the
quest `Quests-Factions.txt` resource catalog.

## Donor consultation record

The configured donor project was resolved as `daggerfall-unity` with root
`/home/research/daggerfall-unity`; its indexed project and source graph were
used to locate the exact classes, registration path, parser, tables, and
corpus. Direct source reads confirmed the action list, quest lifecycle, list
discovery, parser forms, clock/message/macro behavior, and directory counts.
The consultation outcome is **adapted**: donor semantics and source files are
the evidence for the inventory, while the local implementation must keep
Daggerfall policy in the compiled ruleset, source conversion in Import,
authored records in packs, reusable coordination in Kit, and lifecycle/input/
rendering/audio in the existing Engine/Host boundary. Unity `MonoBehaviour`,
`Resources`/`StreamingAssets` runtime loading, static managers, reflection
registration, Unity UI/video widgets, and the donor update loop are excluded
as implementation topology.

Primary references used for this bounded survey:

* `/home/research/daggerfall-unity/Assets/Scripts/Game/Questing/QuestMachine.cs:339-432,441-516,522-610,640-744`
* `/home/research/daggerfall-unity/Assets/Scripts/Game/Questing/QuestListsManager.cs:107-204,276-365`
* `/home/research/daggerfall-unity/Assets/Scripts/Game/Questing/QuestAction.cs:23-107,120-218`
* `/home/research/daggerfall-unity/Assets/Scripts/Game/Questing/Parser.cs:55-162,174-297,303-501,522-601`
* `/home/research/daggerfall-unity/Assets/Scripts/Game/Questing/Quest.cs`,
  `QuestResource.cs`, `Task.cs`, and `Clock.cs:112-156,276-`
* `/home/research/daggerfall-unity/Assets/Scripts/Game/Questing/Message.cs`
  and `/home/research/daggerfall-unity/Assets/Scripts/Utility/QuestMacroHelper.cs:61-160,285-294`
* `/home/research/daggerfall-unity/Assets/StreamingAssets/Tables/QuestList-Classic.txt`,
  `QuestList-DFU.txt`, all nine `Quests-*.txt` tables, and
  `Assets/StreamingAssets/Text/Quests/readme.txt`.

This record intentionally stops at inventory and bounded semantic orientation.
It does not certify quest behavior, enumerate every opcode use in every script,
or establish that a source-backed candidate is implementable without further
Engine/API and content-owner checks.
