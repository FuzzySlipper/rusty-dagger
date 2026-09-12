# Feature-map disposition ledger

Prepared 2026-09-10 for task creation. This accounts for all **141 feature rows**
in sections 1–5 of the [feature map](../daggerfall-feature-map.md). Introductory,
Engine-audit and summary tables are supporting guidance, not additional features.

The map remains an historical structural survey; the decisions below supersede
its status where explicitly noted. This is not a fresh full parity audit.

IDs are permanent: do not renumber when inserting or regrouping work. Add new IDs
or sub-behavior IDs such as `F011.wear`; preserve the parent link. Canonical aliases
prevent duplicate tasks; distinct behavior within a shared family can still split.
MAG/QST inventories expand F030–F034/F086 rather than competing with these IDs.

Disposition: **R** reuse existing behavior/mechanism, inspect any missing callers;
**E** extend an existing owner/partial behavior; **M** implement missing behavior
using existing substrates; **A** adapt donor semantics to Rusty ownership without
porting its architecture; **X** exclude the stated donor feature/topology. Neither
R nor any old Covered label promises full-game parity. X excludes only its stated
subject, not related game behavior retained elsewhere.

Owner codes: **K** Kit mechanisms; **D** Daggerfall ruleset semantics; **I** offline
Import; **P** packs/authored records; **H** Host selection/lifecycle; **U** thin DOM
UI; **E** Engine mechanisms. A row listing E does not establish a missing Engine
API: check the published safe surface before creating an upstream request.

Area is the preferred home in the twelve-area plan, not an all-area prerequisite.
See [task preparation](../daggerfall-task-preparation.md) for capability ordering,
current implementation anchors, content scope and decision handling.

| ID | Survey feature | Disposition | Area / owners | Canonical | Required behavior or explicit exclusion |
| --- | --- | --- | --- | --- | --- |
| F001 | Attribute stats container | E | 5 / K,D | F001 | Permanent/live attributes; source-based modifiers, clamping and removal; preserve existing Mechanics bases. |
| F002 | Skill container | E | 5 / K,D | F002 | Skill initialization, career modifiers, usage counters and advancement; use the existing stat owner. |
| F003 | Resistance container | M | 5 / D,K | F003 | Elemental and disease/poison resistance identities, bases, live modifiers and consumers. |
| F004 | Base entity vitals + effect flags | E | 7 / K,D | F004 | Reuse vitality tracks; add condition/effect flags and common reads for movement, perception and casting. |
| F005 | Player character state | E | 5 / D | F005 | Extend player record with career, reputation/crime, guild and transformation state in the respective owners. |
| F006 | Character-creation document | A | 5 / D,I,P | F006 | Creation choices produce a validated character record and initial loadout; no Unity serialized document. |
| F007 | Enemy entity setup | E | 5 / D,K | F007 | Career-driven enemy stats, equipment, spells, dynamic identity and spawn initialization. |
| F008 | Enemy catalog lookup | E | 3 / I,P,D | F008 | Complete enemy definitions, mobility/attack/corpse metadata; extend existing mobile import and catalogs. |
| F009 | Attribute-derived formulas | R | 5 / D | F009 | Retain derived formulas; map each to live-stat consumers, units and bounds before scheduling missing wiring. |
| F010 | Vital/recovery formulas | R | 8 / D | F010 | Retain recovery formulas; implement rest/loiter and ordinary recovery callers separately with elapsed-time semantics. |
| F011 | Melee damage pipeline | E | 6 / D,K | F011 | Weapon/unarmed damage, material eligibility, enemy attack sets, armor interaction, condition loss and hit consequences. |
| F012 | To-hit pipeline | E | 6 / D | F012 | Body-part selection; skill, armor, material, luck/agility/dodging factors; hit/miss ordering and boundaries. |
| F013 | Swing/proficiency/racial/backstab modifiers | E | 6 / D | F013 | Swing direction, proficiency, race, backstab, enemy-type bonuses and attack timing; no donor global manager access. |
| F014 | Saving throws vs magic | M | 7 / D | F014 | Saving throws, effect amount modification, immunity and monster-hit poison/disease effects through common effect admission. |
| F015 | Non-combat skill-chance formulas | M | 6 / D | F015 | Lockpick, pickpocket, shoplift, stealth, climb and language/pacification chance rules with skill-use consequences. |
| F016 | Level/skill-advancement formulas | E | 5 / D,K | F016 | Classic skill-use advancement and level eligibility; current reward path is experimental XP despite classic formula helpers. |
| F017 | Player melee input + hit resolution | E | 6 / D,K | F017 | Gesture/action input, readiness, equipped weapon, targeting and accepted attack ordering; extend existing melee path. |
| F018 | On-screen weapon presentation | E | 6 / D,E | F018 | Existing viewmodel playback and sheath state exist; complete assigned weapon classes, direction/timing and cues. |
| F019 | Weapon animation data | E | 3 / I,P,D | F019 | Extend normalized weapon actions/frame markers/alignment records; do not port Unity FPSWeapon tables as runtime infrastructure. |
| F020 | Enemy melee + ranged attacks | E | 6 / D | F020 | Enemy melee sets/cadence and bow attacks with ammunition/projectile consequences; common hit policy. |
| F021 | Enemy damage intake | R | 6 / D,K | F021 | Retain guarded damage sink; use it for new attack/effect callers and avoid a second health path. |
| F022 | Player locomotion state | E | 6 / K,D,E | F022 | Walk/run/crouch/jump/climb/swim/levitate/ride policies, costs and transitions over Engine character movement. |
| F023 | Player health/death | E | 6 / D,E | F023 | Death consequences, control suppression, camera/fade and new/load choices; preserve existing damage state. |
| F024 | Player activation/interaction | E | 6 / K,D | F024 | Crosshair activation, context/mode dispatch, reach, talk/loot/steal/bash/lock outcomes and stable target identity. |
| F025 | Effect template registry (broker) | A | 7 / D,P | F025 | Compiled effect definitions keyed by stable identity plus normalized spell records; exclude reflective/singleton discovery. |
| F026 | Per-entity effect manager | A | 7 / K,D | F026 | Ready/cast state, bundle admission, active lifetime, resistance/cure and persistence; no MonoBehaviour manager. |
| F027 | Spell bundle data model | M | 7 / D,P | F027 | Definition versus cast-instance records, effect settings, target/element, provenance and durable references. |
| F028 | Base effect class hierarchy | A | 7 / K,D | F028 | Lifetime/stacking/chance/round semantics over verified Engine mechanisms; no mandated donor inheritance hierarchy. |
| F029 | Classic spell-record import | M | 3 / I,P,D | F029 | SPELLS.STD and MAGIC.DEF decoding/normalization and classic-key mapping; supplied source names verified in CNT-012, not SPELL.RSC. |
| F030 | School effect families | M | 10 / D | F030 | Enumerate each school effect in magic-inventory.md; prerequisites by affected operation, not a single magic task. |
| F031 | Disease system | M | 7 / D | F031 | Enumerated disease types, infection vectors, incubation, elapsed-day damage, cure and transformation handoff. |
| F032 | Poison system | M | 7 / D | F032 | Enumerated poison types, onset, elapsed-minute damage, duration, cure and poisoned-item consumption. |
| F033 | Enchantment-as-effect family | M | 10 / D,K | F033 | Held/strike/used/passive enchantments share item/effect lifecycle; enumerate effects and trigger-specific cleanup. |
| F034 | Artifact/special effects | M | 10 / D | F034 | Artifact and transformation behaviors individually enumerated; quest invocation dependency is selective, not a stage cycle. |
| F035 | Potion recipe matching | M | 10 / D,P | F035 | Ingredient identity/count matching, recipes, creation/consumption and potion payloads through existing inventory. |
| F036 | Spellmaker / spellbook / potion-maker / item-maker windows | A | 10 / D,U | F036 | Spellbook/maker, potion and item-maker commands, eligibility, costs and projections; no donor window classes. |
| F037 | Item data model | E | 5 / K,D | F037 | Condition, material/variant, enchantment, quest binding, identification and durable instance state; no parallel item store. |
| F038 | Item container with stacking | R | 5 / K,D | F038 | Retain stack/split/transfer/container mechanisms; inspect ordering, weight and equip-aware operations when extending. |
| F039 | Item factory | E | 5 / D,K,P | F039 | Template/material/variant selection for every item category and lifecycle creation through current inventory coordinator. |
| F040 | Item stat/text helpers | E | 9 / D | F040 | Item names/value/armor/weapon meaning, repair and identification records, artifacts and service consumers. |
| F041 | Equip table + slot rules | E | 5 / D,K | F041 | Two-hand/shield conflicts, slot/class restrictions, equip eligibility, time/cues and item-effect hooks. |
| F042 | Randomized loot tables | E | 5 / D,P | F042 | Complete letter/category pools, dungeon/level selection, currency and non-weapon/armor items; no Unsupported pools counted complete. |
| F043 | Loot presentation/transfer | E | 5 / D,K,U | F043 | Extend existing corpse/container registration, seeding and transfer path; contextual selection, quantity and result presentation. |
| F044 | Inventory window | A | 5 / D,U | F044 | Inventory/equipment/drop/use/info/quantity/wagon workflows using existing projections; do not duplicate inventory truth. |
| F045 | Weapon-item link (equipped → view) | E | 6 / D,E | F045 | Existing equipment-to-viewmodel selection exists; expand content mappings, bow/ammo and unarmed behavior. |
| F046 | Climate-driven weather simulation | M | 8 / D,P,E | F046 | Climate/season weather transitions and sky/sun/fog/precipitation policy using Engine presentation capabilities. |
| F047 | Player weather effects | M | 8 / D,E | F047 | Player shelter/exposure and rain/snow/ambient effects; no local particle/audio renderer. |
| F048 | Transport modes (foot/horse/cart/ship) | M | 8 / D,K | F048 | Foot/horse/cart/ship ownership, mode eligibility, capacity, speed, cost and transitions. |
| F049 | Interior/exterior/dungeon transitions | E | 4 / K,D | F049 | General enter/exit with location/building/dungeon context, return positions and save/unload behavior. |
| F050 | Building directory and lookup | M | 4 / K,D,P | F050 | Stable building IDs, type/name/faction/site lookup and links shared by map, dialogue, services and quests. |
| F051 | Dungeon automap + discovery | M | 8 / D,K,E,U | F051 | Persistent discovered dungeon geometry/markers and map controls through Engine/UI; no second world renderer. |
| F052 | Exterior/city automap | M | 8 / D,P,U,E | F052 | City footprints/names/markers, player position and map selection from common location data. |
| F053 | Dungeon light range culling | A | 4 / D,E | F053 | Publish authored lights and product visibility policy where needed; reuse Engine light/resource culling, not Unity handlers. |
| F054 | Streaming open world | E | 4 / K,D,E | F054 | Location/terrain admission, unloading, durable deltas, teleport and origin coordination; fixed-scene coverage is insufficient. |
| F055 | Terrain sampling and heightfields | A | 4 / I,P,D,E | F055 | Import exterior height/climate data and select terrain semantics; existing dungeon mesh import is not terrain coverage. |
| F056 | Terrain texturing and nature | M | 4 / I,P,D,E | F056 | Ground tiling, nature placement, climate appearance and seasonal variants over admitted Engine resources. |
| F057 | RMB city/town block assembly | A | 4 / I,P,D | F057 | RMB exterior/building records, placements and gates; Privateer's Hold RDB mesh is not RMB implementation evidence. |
| F058 | RDB dungeon block assembly | E | 4 / I,P,D | F058 | Extend RDB publication to doors, water, actions, markers, fixed/random enemies and treasure across dungeons. |
| F059 | Climate/season texture swaps | M | 8 / I,P,D | F059 | Climate/season remapping rules and exceptions; reuse existing archive remapping helpers where applicable. |
| F060 | Dungeon texture tables | E | 3 / I,P | F060 | DungeonTextureTableTransform already implements classic location tables; extend corpus use rather than reimplement. |
| F061 | First-person player motor | E | 6 / K,D,E | F022 | Same movement work as F022; survey Covered label does not establish climb/swim/mount coverage. |
| F062 | Levitation/swimming motor | E | 6 / K,D,E | F022 | Vertical swim/levitate and support transitions are sub-behaviors of the same movement owner. |
| F063 | Enemy locomotion + melee AI | E | 6 / D,K,E | F063 | Pursue/retreat/strafe/door/fly/swim behavior; extend existing state machine and navigation coordination. |
| F064 | Enemy senses and detection | E | 6 / D,E | F064 | FOV/range/hearing/stealth/pacification decisions over Engine queries; share condition reads with effects. |
| F065 | Enemy vocal and combat sounds | E | 6 / D,P,E | F065 | Mobile-specific idle/attract/attack/hit/parry/miss cues and content, extending current presentation audio. |
| F066 | Blood and magic hit feedback | E | 6 / D,P,E | F066 | Existing blood presentation exists; extend blood-index policy and magic feedback through the same appearance path. |
| F067 | Enemy death and corpse | E | 6 / D,K,E | F067 | Existing death/corpse/loot registration exists; extend dynamic actors, corpse identity, removal and cleanup. |
| F068 | Town civilian wandering | M | 8 / D,K,E | F068 | Civilian wander/idle/seek and city routes using published spatial/navigation mechanisms; no local navigation engine. |
| F069 | Mobile civilian identity | M | 8 / D,P | F069 | Race/gender/face/name/guard identity and persistent/reconstructed presentation policy. |
| F070 | Static NPC records | M | 8 / D,K,P | F070 | Static NPC and questor identity, talk/service binding, relocation/hiding/removal and site ownership. |
| F071 | Civilian entity stats | E | 8 / K,D | F071 | Extend actor lifecycle for civilian/non-hostile definitions, interactions and applicable damage/death rules. |
| F072 | Random encounter tables | M | 8 / D,P | F072 | Dungeon/wilderness/rest/travel encounter tables, selection, level scaling and persistent spawn consequences. |
| F073 | Enemy test/demo setup | X | 12 / D | F073 | DFU demo/inspector spawn harness is excluded from game coverage; existing sprite workbench remains useful tooling. |
| F074 | Guild framework + manager | A | 9 / D,K | F074 | Membership/rank/reputation/service operations and persistence; no singleton guild manager topology. |
| F075 | Major guild implementations | M | 9 / D,P | F075 | Fighters, Mages, Thieves, Dark Brotherhood, Temples and Knightly Orders: individual rank, skill and service rules. |
| F076 | Non-member / service stubs | A | 9 / D | F076 | Non-member service eligibility and real denial/outcome semantics; no no-op service stubs counted as coverage. |
| F077 | Bank accounts + transactions | M | 9 / D,K | F077 | Regional accounts, cash/letters of credit, deposits/withdrawals, transfers and ship/house payments. |
| F078 | Loan issuance + default | M | 9 / D | F078 | Loan issuance, due dates, repayments/default and reputation consequences using shared game time. |
| F079 | Quest runtime (machine) | A | 11 / D,K | F079 | Quest start/update/end and durable instance lifecycle inside admitted updates; not donor singleton/scheduler topology. |
| F080 | Compiled quest instance | M | 11 / D | F080 | Task/condition/resource/symbol state, completion/failure and save reconstruction; expand quest inventory. |
| F081 | Quest script parser | A | 11 / I,D,P | F081 | Normalize existing quest language/source semantics offline; compile handlers in ruleset, no new general gameplay DSL. |
| F082 | Quest tasks + clock | M | 11 / D | F082 | Task transitions and elapsed-time deadlines, ordering, cancellation and save/restore. |
| F083 | Quest places + persons | M | 11 / D,K | F083 | Bind place/person resources to location/building/NPC identities and normalized markers; persistent relocation/cleanup. |
| F084 | Quest foes + items | M | 11 / D,K | F084 | Foe/item resources, pre-spawn queued operations, ownership, death/loot/reward and removal through shared operations. |
| F085 | Quest messages + symbols | M | 11 / D,P | F085 | Quest message records and scoped symbols, choices and macro inputs; no hardcoded quest dialogue. |
| F086 | Quest action library | M | 11 / D | F086 | Every candidate action is enumerated in quest-content-inventory.md with domain prerequisites and disposition. |
| F087 | Quest macro expansion | M | 11 / D | F087 | Quest-scoped macro context layered over shared text values; deterministic missing-symbol diagnostics. |
| F088 | NPC talk engine | M | 9 / D,U | F088 | Talk session, NPC reaction, topics, directions, rumors/work and quest bindings with text/macros. |
| F089 | Talk mod-protocol compat | X | 9 / D | F089 | DFU talk mod-message protocol excluded; ordinary talk behavior remains F088. |
| F090 | Text table manager | M | 3 / I,P,D | F090 | TEXT.RSC records normalized offline; lookup by stable text identity at runtime. |
| F091 | Global macro expansion | M | 9 / D | F091 | Player/date/location/faction/item macro values with explicit context and supported substitutions. |
| F092 | Text provider chain | A | 3 / D,P | F092 | Normalized text lookup and defined missing-text behavior; mod/provider chain topology is not required. |
| F093 | Grammar + string import | A | 3 / I,P,D,U | F093 | Base-language grammar/text presentation and source string import; localization expansion remains a recorded option. |
| F094 | Localized books | A | 9 / I,P,D,U | F094 | Book records/pages and reader actions; preserve selected base-language content without Unity localized wrappers. |
| F095 | UI root + messages + factory | A | 12 / D,K,U,H | F095 | Existing projection/action transport; game modes, modal choices and input consequences via explicit composition. |
| F096 | Base window + widget kit | A | 12 / U,D | F096 | Adapt required controls/workflows to DOM; exclude Unity widget/window hierarchy and factory replication. |
| F097 | HUD | E | 12 / D,U | F097 | Resource rows, compass, crosshair, status effects, interaction mode and relevant followers/escorts. |
| F098 | Inventory window | A | 5 / D,U | F044 | Same inventory workflows as F044; do not create a second window implementation. |
| F099 | Character sheet window | E | 5 / D,U | F099 | Extend current sheet with career, live/permanent values, advancement, reputation and history as owners land. |
| F100 | Outcome / message popups | A | 12 / D,U | F100 | Messages, modal choice/confirmation, quest offers and journal notifications from typed product state/actions. |
| F101 | Spellbook + magic crafting | A | 10 / D,U | F036 | Spellbook/maker/use-magic-item workflows belong to the F036 family. |
| F102 | Rest window | A | 8 / D,U | F102 | Rest/loiter choices, duration and interruption/outcome projections over shared rest operations. |
| F103 | Travel system windows | A | 8 / D,U | F103 | Travel destination/options/cost/duration, transport and teleport confirmation over shared world operations. |
| F104 | Guild/bank/merchant service windows | A | 9 / D,U | F104 | Guild/bank/trade/repair eligibility and transactions; UI cannot substitute fake service results. |
| F105 | Talk / quest / tavern / court windows | A | 9 / D,U | F105 | Talk/offer/journal/tavern/court actions and outcomes; quest views depend selectively on area 11. |
| F106 | Trade / automap / book / craft windows | A | 12 / D,U,E | F106 | Trade/maps/books/crafting/summoning workflows attached to their area 8/9/10 owners, not a giant UI task. |
| F107 | Character creation chain + wizards | A | 5 / D,U,H | F107 | Full creation choices/custom class/bio/summary/new game; exclude DFU installation/setup wizard. |
| F108 | Content-reader singleton bootstrap | X | 3 / I,H | F108 | Exclude content-reader singleton bootstrap; reuse offline Import and admitted pack composition. |
| F109 | Application paths and Arena2 setup | X | 3 / I,H | F109 | Exclude Arena2 installation/app-path distribution setup; local source selection belongs to offline tooling. |
| F110 | Top-level game orchestration | X | 2 / H,D | F110 | Exclude donor GameManager/global orchestration topology; extend existing Host/session when behavior requires. |
| F111 | UI/game state machine | A | 2 / H,D,K | F111 | Required play/menu/pause/death transitions and input consequences; do not port DFU StateManager architecture. |
| F112 | Serializable save/load pipeline | E | 2 / H,D,K | F112 | Extend current envelope/payload for dynamic actors, locations, doors, effects, quests and service state; no Unity serialization. |
| F113 | Classic save records | A | 2 / D,I | F113 | Preserve meaningful character/item/guild/world state in Rusty saves; classic binary save interoperability excluded by baseline. |
| F114 | Contextual music director | M | 12 / D,P,E | F114 | Contextual track choice and loop lifecycle from admitted ordinary audio; separate bounded long-duration audio exercise. |
| F115 | Song catalog enum | X | 12 / D,I | F115 | MIDI.BSA song enum/playback compatibility excluded; ordinary music uses content IDs under F114. |
| F116 | Sound-clip catalog enum | E | 3 / I,P,D | F116 | Named/typed classic sound identity and complete cue mappings over current DAGGER.SND import. |
| F117 | Sound importer | A | 3 / I,E | F117 | Reuse offline sound decoder/publication; exclude Unity AudioClip importer and use Engine playback. |
| F118 | Ambient audio/visual effects | M | 8 / D,P,E | F118 | Ambient selection/timing/lightning cues via game state and Engine audio/appearance, not a second timer. |
| F119 | Material/image importer | A | 3 / I,E | F119 | Reuse texture/palette/image normalization; exclude Unity materials and material reader topology. |
| F120 | Mesh importer | A | 3 / I,E | F120 | Reuse ARCH3D/RDB decoding/publication; complete needed model attributes and resources, exclude Unity Mesh objects. |
| F121 | Texture archive reader | E | 3 / I,P | F121 | Complete texture archive coverage/variants using current decoder and publication; no Texture2D runtime reader. |
| F122 | General image reader | E | 3 / I,P,U | F122 | Normalize required image/UI records with current IMG/palette/media paths; adapt DOM presentation. |
| F123 | Model combiner | A | 3 / I,E | F123 | Reuse offline geometry publication and Engine resource batching where applicable; no donor ModelCombiner class port. |
| F124 | Retro rendering mode | X | 12 / E | F124 | DFU retro postprocess/viewport mode is outside baseline; classic game art remains included and no downstream renderer. |
| F125 | Texture atlases | E | 3 / I,P,E | F125 | Extend atlas publication to full supported sprite/terrain/media corpus; Engine owns playback/realization. |
| F126 | Floating origin | A | 4 / K,D,E | F126 | Use verified Engine origin-rebase capability for exterior traversal; do not reproduce FloatingOrigin machinery. |
| F127 | Camera clear management | A | 4 / D,E | F127 | Interior/exterior camera/background policy through Engine camera/presentation, not Unity camera clear handler. |
| F128 | 3D mesh archive format | E | 3 / I,P | F128 | Extend existing ARCH3D archive decoding to the supported model corpus and normalized records. |
| F129 | Dungeon/city block format | E | 3 / I,P | F129 | RDB decoding exists; account separately for RMB outdoor/interior structures and linked records. |
| F130 | Location and region records | E | 3 / I,P | F130 | Complete region/location/building/dungeon records, names and references beyond Privateer's Hold. |
| F131 | World-map archive | E | 3 / I,P | F131 | Complete MAPS.BSA metadata/layout/height/climate sources through current decoder where applicable. |
| F132 | Faction data | M | 3 / I,P,D | F132 | Normalize faction hierarchy/relationships/regions and stable IDs; runtime policy consumes pack records. |
| F133 | Item templates | E | 3 / I,P,D | F133 | Expand current item definitions to all selected classic template groups and derived item meanings. |
| F134 | Monster data | E | 3 / I,P,D | F134 | Expand current mobile metadata/actors; distinguish DFU-derived tables from raw MONSTER.BSA support claims. |
| F135 | Reader-to-Unity bridge | X | 3 / I | F135 | Exclude reader-to-Unity bridge; existing normalized contracts are the product-facing representation. |
| F136 | Daggerfall calendar clock | M | 2 / D | F136 | Calendar dates/seasons/time scale and persisted elapsed-time advancement shared by all consumers. |
| F137 | Classic RNG reimplementation | R | 3 / I,E,D | F137 | Keep classic deterministic import transform RNG; runtime randomness uses Engine. Exact runtime classic sequence needs explicit task decision. |
| F138 | Action input bindings | E | 6 / K,D | F138 | Semantic bindings/modes, normalized input and control settings through existing player input owner. |
| F139 | Controls settings + hotkeys | E | 12 / D,K,U | F139 | Rebinding/settings/hotkey semantics needed for included actions; exclude Unity controls configuration topology. |
| F140 | Mouse look | R | 6 / K,E | F140 | Reuse Engine Look/CameraView and existing integration; no second mouselook implementation. |
| F141 | Player activation/interaction | E | 6 / K,D | F024 | Same activation behavior as F024, including perception query and contextual target dispatch. |
