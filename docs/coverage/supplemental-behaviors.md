# Behaviors implicit in the structural feature map

These stable SUP IDs make cross-cutting behavior explicit for task creation.
They supplement [F001–F141](feature-ledger.md), not duplicate their implementation
owners. They are planned coverage, not current implementation claims. A task can
cover a SUP ID and its linked F IDs together; keep that mapping visible.

Donor paths below are relative to `/home/research/daggerfall-unity/Assets/Scripts`
at `81e89e90c27bc3c1a7a61871e545fad129174dec`. References identify the source family
to inspect while specifying exact semantics. Only the action registration and
named source observations below were read as a bounded source check; this is not
an exhaustive audit of every listed behavior.

| ID | Included behavior to specify | Related F IDs / area | Owners and donor starting point |
| --- | --- | --- | --- |
| SUP-01 | Persistent dynamic IDs and ownership for spawned actors, quest people/items, corpses and containers; unloaded changes and removal survive save/load | F005, F049, F054, F112 / 2 | Kit lifetime coordination and Daggerfall save meaning; existing `DaggerfallSavePayload.ValidateForRestore` currently uses authored actor identities. |
| SUP-02 | Game-time advancement during ordinary play, rest, travel and prison; ordering of expiry, periodic damage, deadline and daily state changes | F031, F032, F078, F082, F136 / 2 | Daggerfall calendar/policy inside admitted updates; `Utility/DaggerfallDateTime.cs`, `Game/Questing/Clock.cs`, rest/travel/court consumers. |
| SUP-03 | Persistent regional/global variables shared by quests, politics, NPC responses and world actions | F005, F079, F132 / 2, 9, 11 | Daggerfall state; `Game/Player/PersistentGlobalVars.cs`, `PersistentFactionData.cs`, `Game/Entities/PlayerEntity.cs`; no ambient global singleton. |
| SUP-04 | Dungeon action flags, link chains, activation conditions, timing/reversal/cooldowns and save state | F058, F024 / 4 | Import normalized actions; Daggerfall action meaning; Engine motion/spatial presentation; `Internal/DaggerfallAction.cs` and `API/DFBlock.cs`. See enumeration below. |
| SUP-05 | Door open/close/lock/unlock/bash state, special doors, blocking geometry and action links agree across reload | F024, F049, F058 / 4, 6 | Daggerfall policy over Kit/Engine; `Internal/DaggerfallActionDoor.cs`, `DaggerfallActionDoorSpecial.cs`, `Game/PlayerActivate.cs`. |
| SUP-06 | Health/fatigue/breath recovery and depletion, drowning/falling/exhaustion consequences, and movement eligibility | F004, F010, F022, F023 / 5–8 | Existing Mechanics tracks/recovery/controls; Daggerfall policy; `Game/PlayerMotor.cs`, `PlayerHealth.cs`, `Player/` motors, `Game/Formulas/FormulaHelper.cs`. |
| SUP-07 | Skill-use attribution, counters, advancement timing, level eligibility and level-up choices; classic progression consumes these events | F002, F006, F016 / 5 | Daggerfall over existing Kit progression/stat coordination; `Game/Entities/PlayerEntity.cs` and `Game/Formulas/FormulaHelper.cs`; existing kill-XP leveling is insufficient. |
| SUP-08 | Theft/shoplifting/pickpocket/assault/trespass consequences, witnesses, guards, arrest/resistance, accusation/court, fines/prison and legal reputation | F005, F015, F070, F105 / 6, 9 | Daggerfall crime state; `Game/Entities/PlayerEntity.cs` crime/arrest fields and mutation; `Game/UserInterfaceWindows/DaggerfallCourtWindow.cs`, `Game/PlayerActivate.cs`. |
| SUP-09 | Rest/loiter eligibility, duration, healing/recharge, interruption and encounter effects | F010, F072, F102 / 8 | Daggerfall time and rest policy; `Game/UserInterfaceWindows/DaggerfallRestWindow.cs`; UI is only caller/projection. |
| SUP-10 | Travel route/time/price/options, transport ownership, arrival position and elapsed-time consequences; enforce meaningful nonnegative transaction amounts | F048, F049, F103 / 8 | Daggerfall policy; `Game/Utility/TravelTimeCalculator.cs`, `Game/TransportManager.cs`; donor notes a classic negative-cost issue, do not reproduce exploit arithmetic by accident. |
| SUP-11 | Store stock/generation/restock, shop quality, purchase/sale pricing, repair duration/cost, identification, training and lodging | F039, F040, F074, F104 / 9 | Daggerfall policies, common item/time state; `Game/Formulas/FormulaHelper.cs`, `Game/Guilds/Services.cs`, relevant trade/repair/tavern windows. |
| SUP-12 | Guild invitation/admission/rank/expulsion, faction-specific privileges, quest offer eligibility and reputation consequences | F074–F076, F105 / 9, 11 | Daggerfall guild policies/content; `Game/Guilds/` concrete guilds, `Game/Entities/PlayerEntity.cs` starting crime-guild quest handling. |
| SUP-13 | Property/ship purchase, ownership, access, storage and regional financial state; horse/cart/wagon inventory use | F048, F077, F044 / 8, 9 | Daggerfall over current item/site ownership; `Game/DaggerfallBankManager.cs`, `Game/TransportManager.cs`, banking/inventory windows. |
| SUP-14 | NPC identity/reaction, directions, rumor/news/work, language/etiquette/streetwise outcomes and faction/location-sensitive text | F015, F070, F088, F091 / 9 | Daggerfall talk/social policy; `Game/TalkManager.cs`, `Game/StaticNPC.cs`, `Utility/MacroHelper.cs`. |
| SUP-15 | User notes and quest journal entries, ordering/removal and persistence; book/text references remain readable independently of quest lifetime | F085, F094, F105 / 9, 11 | Daggerfall record meaning + thin DOM; `Game/Player/PlayerNotebook.cs` exposes note add/remove/reorder; no copied token/widget layout. |
| SUP-16 | Summoning days/cost/chance, artifact rewards and transformation cure/infection chains use real faction, time, quest and effect operations | F034, F075, F106 / 9–11 | Daggerfall rules; `Game/Formulas/FormulaHelper.cs` summoning methods, `Game/UserInterfaceWindows/DaggerfallDaedraSummonedWindow.cs`, effect/quest inventories. |
| SUP-17 | New-game, pause/menu/modal/death, save-slot/load/quit workflows and input consequences through existing lifecycle and persistence | F023, F095, F107, F111, F112 / 2, 12 | Host selection/lifecycle; Daggerfall mode meaning and UI projections. Donor windows are workflow evidence, not implementation shapes. |
| SUP-18 | Main-story/ending/cinematic content triggers, completion state and media presentation, including quest-invoked video where retained | F086, F100 / 11, 12 | Daggerfall/Import/content with verified Engine media capability; `Game/Questing/Actions/PlayVideo.cs`, `Game/UserInterface/DaggerfallVideo.cs`. Exclude Unity video-player implementation, not story meaning. |

## Dungeon action enumeration

The `actionFunctions` registration in `Internal/DaggerfallAction.cs` lists these
26 flags. Each is a candidate semantic leaf under SUP-04, with stable IDs based on
flag name. Aliases sharing a handler remain listed because data values differ.
The task author reads the handler and `API/DFBlock.cs` record fields to specify
parameter interpretation. Unknown raw flags must get a diagnostic/disposition,
not an accidental successful no-op.

| Leaf IDs (prefix `SUP-04.`) | Behavior family / prerequisites |
| --- | --- |
| `Translation`, `Rotation`, `PositiveX`, `NegativeX`, `PositiveZ`, `NegativeZ`, `PositiveY`, `NegativeY` | Motion parameters, duration, activation/reversal and persistent state; Engine motion/spatial property must be verified. |
| `CastSpell` | Normalized spell reference, caster/target and common effect admission; defer this leaf until casting exists. |
| `ShowText`, `ShowTextWithInput`, `DoorText` | Text lookup, input/answer conditions and action-chain outcome; thin UI. |
| `Teleport` | Target/source identity and actual relocation through the shared world/movement owner. |
| `LockDoor`, `UnlockDoor`, `OpenDoor`, `CloseDoor` | Same persistent door operations as player activation and spells. |
| `Hurt21`, `Hurt22`, `Hurt23`, `Hurt24`, `Hurt25` | Distinct source damage modes/parameters through common actor tracks; preserve random versus level-scaled semantics. |
| `Poison`, `DrainMagicka` | Shared effect/track state, immunity and timing rules where applicable. |
| `Activate`, `SetGlobalVar` | Linked activation and persistent variable changes; explicit bounded ordering, not a generic downstream scheduler. |

Trigger enumeration in the same donor file: `None`, `ActionObject`, `Direct`,
`WalkOn`, `WalkInto`, `Attack`, `Door`. The donor notes that its walk-on handling
uses walk-into; resolve this behavioral distinction when drafting, not by copying
the Unity collision callback. Normalized import must retain enough source meaning
for the selected trigger semantics.

This enumeration is separate from the quest action inventory. Dungeon actions
and quest actions reuse world/item/effect operations, but their source formats
and ruleset interpretation are distinct.
