# Feature disposition ledger

This is the stable F001–F141 ID index. Each ID corresponds to one of the **141
feature rows** in sections 1–5 of the original DFU feature survey, archived with its
donor source references as `[doc: rusty-dagger/daggerfall-feature-map-2026-09]`;
consult that archive when a row's donor source matters. The dispositions below
supersede the survey's status notes. This is not a full parity audit.

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

A disposition records the planned approach and stays when the work lands. The last
column states current behavior; a **Remaining** clause names an open gap and its
owners. A row without one has no recorded gap against its stated behavior, which
is still not a parity claim.

Owner codes: **K** Kit mechanisms; **D** Daggerfall ruleset semantics; **I** offline
Import; **P** packs/authored records; **H** Host selection/lifecycle; **U** thin DOM
UI; **E** Engine mechanisms. A row listing E does not establish a missing Engine
API: check the published safe surface before creating an upstream request.

Area is the preferred home in the twelve-area plan, not an all-area prerequisite.
See [coverage scope and decisions](../daggerfall-task-preparation.md) for the scope
contract and decision register; Den owns tasks, their ID mapping and dependencies.

| ID | Survey feature | Disposition | Area / owners | Canonical | Required behavior or explicit exclusion |
| --- | --- | --- | --- | --- | --- |
| F001 | Attribute stats container | E | 5 / K,D | F001 | Permanent/live attributes; source-based modifiers, clamping and removal; preserve existing Mechanics bases. |
| F002 | Skill container | E | 5 / K,D | F002 | Skill initialization, career modifiers, usage counters and advancement; use the existing stat owner. |
| F003 | Resistance container | M | 5 / D,K | F003 | Elemental and disease/poison resistance identities, permanent bases and immunity stats, and live effect-chance resistance plus career/biography tolerance; consumed by casting, disease, artifacts and the character sheet. |
| F004 | Base entity vitals + effect flags | E | 7 / K,D | F004 | Vitality tracks plus typed condition/effect flags with common reads for movement, perception and casting. |
| F005 | Player character state | E | 5 / D | F005 | Player record carries career, reputation/crime, guild and transformation state in the respective owners. Remaining (D): refuse a current save without the character section instead of loading a default identity. |
| F006 | Character-creation document | A | 5 / D,I,P | F006 | Creation choices produce a validated character record and initial loadout; no Unity serialized document. |
| F007 | Enemy entity setup | E | 5 / D,K | F007 | Career-driven enemy stats, equipment, spells, dynamic identity and spawn initialization. Remaining (D,P): monster skills at min(100, level×5+30) for every skill, and equipment for Orc Shaman and Orc Warlord. |
| F008 | Enemy catalog lookup | E | 3 / I,P,D | F008 | Enemy definitions with mobility/attack/corpse metadata through the mobile import and catalogs. Remaining (I,D): soul points (soul-gem value), glow/shadow presentation and ranged preference. |
| F009 | Attribute-derived formulas | R | 5 / D | F009 | Derived formulas drive melee/bash damage, saves, encumbrance, breath and fatigue/magicka maxima. Remaining (D,U): to-hit, hit-point and healing-rate consumers, and filling the attribute text macros (%dam %thd %hea %hmd %mad %enc, attributes) for creation and the character sheet. |
| F010 | Vital/recovery formulas | R | 8 / D | F010 | Recovery formulas drive rest, loiter, travel and exhaustion-collapse recovery with elapsed-time semantics. Fatigue never returns over real time: it drains at the donor rates (11 units per swing or bow shot, 11 per jump, and per game minute 11 idle, 22 climbing, 88 running while moving, 44 for a swimming minute whose Swimming roll fails, never for an Argonian), each scaled by career Athleticism (0.9, or 0.8 with the improved talent) and clamped at zero; travel, prison and the vampire awakening raise the clock without that per-minute loss, as the donor's raised time does; low fatigue refuses no swing, run, jump or climb. An emptied pool collapses the living player once: out of water with no enemy near, one hour passes with one rest hour of recovery and a Medical tally (text 1071); near an enemy (text 1072) or in water (`exhaustedInWater`) the player dies. |
| F011 | Melee damage pipeline | E | 6 / D,K | F011 | Weapon/unarmed damage, material eligibility, enemy attack sets, armor interaction, condition loss and hit consequences. |
| F012 | To-hit pipeline | E | 6 / D | F012 | Body-part selection; skill, armor, material, luck/agility/dodging factors; hit/miss ordering and boundaries. |
| F013 | Swing/proficiency/racial/backstab modifiers | E | 6 / D | F013 | Swing direction, proficiency, race, backstab, enemy-type bonuses and attack timing; no donor global manager access. |
| F014 | Saving throws vs magic | M | 7 / D | F014 | Saving throws, effect amount modification, immunity and monster-hit poison/disease effects through common effect admission. Spells, diseases and poisons resolve through one saving-throw rule with classic tolerance precedence (DEC-01): an immune race or career resists without a roll, otherwise critical weakness takes the full effect; the donor's additive mixing of those two is not used. |
| F015 | Non-combat skill-chance formulas | M | 6 / D | F015 | Lockpick, pickpocket, shoplift, stealth, climb and language/pacification chance rules, each recording its skill use. |
| F016 | Level/skill-advancement formulas | E | 5 / D,K | F016 | Classic skill-use advancement, skill-sum level eligibility, level-up allocation and hit points, with save and UI, through rest and travel; kill XP remains only as disabled tuning. Remaining (D): advancement also runs on elapsed and quest calendar advances, where the donor advances only on rest and travel. |
| F017 | Player melee input + hit resolution | E | 6 / D,K | F017 | Gesture/action input, readiness, equipped weapon, targeting and accepted attack ordering admit player melee through the shared hit resolution. |
| F018 | On-screen weapon presentation | E | 6 / D,E | F018 | Viewmodel playback, hit-frame impact and sheath toggle. The strike played is the swing direction the rules publish on the attack (a bow always strikes down; a melee swing, hands included, follows the tracked gesture, and a swing without one takes the donor's click-attack random direction); a strike that cannot reach its hit frame fails instead of dropping the impact. Drawn/sheathed is session state saved with the player. Remaining (D,I): draw sound, hand switch and metal-tinted weapon art. |
| F019 | Weapon animation data | E | 3 / I,P,D | F019 | Normalized weapon actions, frame markers, alignment and sequences; hit frames are ruleset invariants beside the attack policy. Admission refuses a strike shorter than the melee hit frame, and a bow's art whose loosing strike is shorter than the bow hit frame. Remaining: metal-variant/dye art records (I,P). No Unity FPSWeapon tables. |
| F020 | Enemy melee + ranged attacks | E | 6 / D | F020 | Enemy melee sets/cadence and authored archer shots with quiver, flight and cover consequences through common hit policy. Remaining (D,P): bow attacks for bow-capable class encounters, and the donor min/max ranged envelope with melee fallback. |
| F021 | Enemy damage intake | R | 6 / D,K | F021 | Retain guarded damage sink; use it for new attack/effect callers and avoid a second health path. |
| F022 | Player locomotion state | E | 6 / K,D,E | F022 | Walk/run/crouch/jump/climb/swim/levitate/ride policies, costs and transitions over Engine character movement. |
| F023 | Player health/death | E | 6 / D,E | F023 | Death consequences, control suppression, camera/fade and new/load choices; preserve existing damage state. |
| F024 | Player activation/interaction | E | 6 / K,D | F024 | Crosshair activation, context/mode dispatch, reach, talk/loot/steal/bash/lock outcomes and stable target identity. |
| F025 | Effect template registry (broker) | A | 7 / D,P | F025 | Compiled effect definitions keyed by stable identity plus normalized spell records; exclude reflective/singleton discovery. |
| F026 | Per-entity effect manager | A | 7 / K,D | F026 | Ready/cast state, bundle admission, active lifetime, resistance/cure and persistence; no MonoBehaviour manager. |
| F027 | Spell bundle data model | M | 7 / D,P | F027 | Spell definition versus cast-instance records, effect settings, target/element, provenance and durable references. |
| F028 | Base effect class hierarchy | A | 7 / K,D | F028 | Lifetime/stacking/chance/round semantics over verified Engine mechanisms; no mandated donor inheritance hierarchy. |
| F029 | Classic spell-record import | M | 3 / I,P,D | F029 | SPELLS.STD and MAGIC.DEF decoded and normalized into the imported magic catalog; classic (type, subtype) keys resolve through the compiled effect catalog. Supplied source names verified in CNT-012, not SPELL.RSC. |
| F030 | School effect families | M | 10 / D | F030 | Every school effect in magic-inventory.md has a compiled runtime leaf with session callers; per-leaf formula and edge parity follows that inventory. |
| F031 | Disease system | M | 7 / D | F031 | Enumerated disease types, infection vectors, incubation, elapsed-day damage, cure and transformation handoff. Remaining (D): the daily "you feel somewhat bad" HUD notice. |
| F032 | Poison system | M | 7 / D | F032 | Enumerated poison types, onset, elapsed-minute damage, duration, cure and poisoned-item consumption. Remaining (D): the per-tick "you feel somewhat bad" HUD notice. |
| F033 | Enchantment-as-effect family | M | 10 / D,K | F033 | Held/strike/used/passive enchantments share the item/effect lifecycle with trigger-specific cleanup; effects enumerated as MAG-015/MAG-016. |
| F034 | Artifact/special effects | M | 10 / D | F034 | Artifact and transformation behaviors individually enumerated and implemented; quest invocation depends selectively on them, not on a stage cycle. |
| F035 | Potion recipe matching | M | 10 / D,P | F035 | Ingredient identity/count matching, recipes, creation/consumption and potion payloads through existing inventory. |
| F036 | Spellmaker / spellbook / potion-maker / item-maker windows | A | 10 / D,U | F036 | Spellbook/maker, potion and item-maker commands, eligibility, costs and projections; no donor window classes. |
| F037 | Item data model | E | 5 / K,D | F037 | Condition, material/variant, enchantment, quest binding, identification and durable instance state; no parallel item store. |
| F038 | Item container with stacking | R | 5 / K,D | F038 | Retain stack/split/transfer/container mechanisms; inspect ordering, weight and equip-aware operations when extending. |
| F039 | Item factory | E | 5 / D,K,P | F039 | Template/material/variant selection for every item category and lifecycle creation through current inventory coordinator. |
| F040 | Item stat/text helpers | E | 9 / D | F040 | Item names/value/armor/weapon meaning, repair and identification records, artifacts and service consumers. Remaining (D): one shared long-name path (material prefix, identified magic and book names) for inventory, loot and merchant rows. |
| F041 | Equip table + slot rules | E | 5 / D,K | F041 | Two-hand/shield conflicts, slot/class restrictions, equip eligibility, cues and item-effect hooks. Remaining (D): apply the computed per-hand equip delay (attacks refused until it elapses, "Equipping" message). |
| F042 | Randomized loot tables | E | 5 / D,P | F042 | Letter/category pools, dungeon/level selection, currency and enemy/dungeon extras. Remaining (I,P,D): publish RDB treasure markers and populate dungeon treasure containers by dungeon type (with F058); retire the stale deferred `lootCategoryPools` records in the authored base payload. |
| F043 | Loot presentation/transfer | E | 5 / D,K,U | F043 | Corpse, ground and property container registration, seeding and transfer, with contextual open, quantity and result presentation. |
| F044 | Inventory window | A | 5 / D,U | F044 | Inventory/equipment/drop/use/info/quantity/wagon workflows using existing projections; do not duplicate inventory truth. |
| F045 | Weapon-item link (equipped → view) | E | 6 / D,E | F045 | Equipped-to-viewmodel mapping for every weapon, unarmed/werecreature and bow/arrow use. Remaining (I,D): enchanted and metal-tinted viewmodel variants. |
| F046 | Climate-driven weather simulation | M | 8 / D,P,E | F046 | Daily climate/season weather transitions from tuned odds and sky/sun/fog/daylight policy through Engine CameraView, saved as current state. |
| F047 | Player weather effects | M | 8 / D,E | F047 | Player shelter probe, rain/snow precipitation and ambient/thunder audio through Engine presentation; no local particle/audio renderer. |
| F048 | Transport modes (foot/horse/cart/ship) | M | 8 / D,K | F048 | Foot/horse/cart/ship ownership, mode eligibility, wagon capacity, speed, cost, transitions and save. Remaining (D,E): riding overlay and hoof/neigh audio. |
| F049 | Interior/exterior/dungeon transitions | E | 4 / D | F049 | General enter/exit with location/building/dungeon context, return positions and save/unload behavior, owned by the site lifecycle. |
| F050 | Building directory and lookup | M | 4 / D,P | F050 | Stable building IDs, type/name/faction/site lookup and links shared by map, dialogue, services and quests. |
| F051 | Dungeon automap + discovery | M | 8 / D,K,E,U | F051 | Persistent discovered dungeon geometry/markers and map controls through Engine/UI; no second world renderer. Remaining (D,U): current-visit shading projection and the building-interior automap (F106). |
| F052 | Exterior/city automap | M | 8 / D,P,U,E | F052 | City footprints/names/markers, player position and map selection from common location data. Remaining (D,U): persistent per-building discovery and name reveal (enter, activate, NPC marking). |
| F053 | Dungeon light range culling | A | 4 / D,E | F053 | Publish authored lights and product visibility policy where needed; reuse Engine light/resource culling, not Unity handlers. |
| F054 | Streaming open world | E | 4 / K,D,E | F054 | Location/terrain admission, unloading, durable deltas, teleport and origin coordination over exterior cell residency; location coverage follows F057/F058 publication. |
| F055 | Terrain sampling and heightfields | A | 4 / I,P,D,E | F055 | Exterior height and climate data imported; terrain sampling, location flattening and world-height reads select terrain semantics, separate from dungeon mesh import. |
| F056 | Terrain texturing and nature | M | 4 / I,P,D,E | F056 | Ground tiling, nature placement, climate appearance and seasonal/winter variants over admitted Engine resources. |
| F057 | RMB city/town block assembly | A | 4 / I,P,D | F057 | RMB exterior/building records and placements normalized per block for every location a catalog places and assembled when first needed, with building doors, transition doors and city gates (open by day, closed at night). Remaining (I,P): the authored Charing exterior closure still draws its gates as static open geometry. |
| F058 | RDB dungeon block assembly | E | 4 / I,P,D | F058 | RDB publication carries doors, actions, markers and fixed enemies. Remaining (I,D,P): random-monster markers, block water level and dungeon water volumes, treasure publication (F042) and corpus-wide dungeon publication. |
| F059 | Climate/season texture swaps | M | 8 / I,P,D | F059 | Terrain climate/season swaps land with F056. Remaining (I,P): climate/season remapping with exceptions for RMB models and ground. |
| F060 | Dungeon texture tables | E | 3 / I,P | F060 | DungeonTextureTableTransform implements classic location tables. Remaining (I,P): apply the classic table to every published dungeon and the corpus. |
| F061 | First-person player motor | E | 6 / K,D,E | F022 | Same movement work as F022; walk, run, crouch, jump, climb, swim, levitate and ride live under that owner. |
| F062 | Levitation/swimming motor | E | 6 / K,D,E | F022 | Vertical swim/levitate and support transitions are sub-behaviors of the same movement owner. Remaining (I,D): dungeon water volumes, carried by F058. |
| F063 | Enemy locomotion + melee AI | E | 6 / D,K,E | F063 | Pursue/retreat/strafe/door/fly/swim behavior over the Kit pursuit coordinator and navigation. Remaining (K,D): donor retreat gating (the current band retreats just short of melee reach), last-known-position search, and session tests for maneuvers and doors. |
| F064 | Enemy senses and detection | E | 6 / D,E | F064 | FOV/range/hearing/stealth/pacification decisions over Engine queries, sharing condition reads with effects. Remaining (D): persist language pacification and apply the classic spawn-range stealth gate. |
| F065 | Enemy vocal and combat sounds | E | 6 / D,P,E | F065 | Mobile-specific idle/attract/attack/hit/parry/miss cues and content through presentation audio. |
| F066 | Blood and magic hit feedback | E | 6 / D,P,E | F066 | Blood-index policy and magic hit feedback through the appearance path. Remaining (D): blood for hits by non-player attackers. |
| F067 | Enemy death and corpse | E | 6 / D,K,E | F067 | Death, durable corpse identity, loot registration, removal and cleanup for dynamic actors, saved in site deltas. Remaining (D,P): body-fall cue. |
| F068 | Town civilian wandering | M | 8 / D,K,E | F068 | Civilian population admission and wander/idle/seek over Engine navigation; no local navigation engine. Remaining (K,D): city navigation-grid routes and the player-proximity idle stop. |
| F069 | Mobile civilian identity | M | 8 / D,P | F069 | Race/gender/billboard/guard identity and persistent reconstruction. Remaining (D,U): seeded name composition and faces for talk. |
| F070 | Static NPC records | M | 8 / D,K,P | F070 | Interior static NPC and questor identity, talk/service binding, relocation/hiding/removal and save. Remaining (I,D): exterior faction and RDB dungeon NPC flats, and seeded display names (F069). |
| F071 | Civilian entity stats | E | 8 / K,D | F071 | Civilian and static-NPC non-combat actors over the actor lifecycle, with crime-reporting damage, death and removal. |
| F072 | Random encounter tables | M | 8 / D,P | F072 | Dungeon/wilderness/rest/travel encounter tables, selection, level scaling and persistent spawns for rest and travel. Remaining (D): ordinary-play intermittent spawns (exterior night, wilderness) and placement at the donor minimum spawn distance. |
| F073 | Enemy test/demo setup | X | 12 / D | F073 | DFU demo/inspector spawn harness is excluded from game coverage; existing sprite workbench remains useful tooling. |
| F074 | Guild framework + manager | A | 9 / D,K | F074 | Membership/rank/reputation review, expulsion and persistence; no singleton guild manager topology. Remaining (D,U): a player-reachable join and rejoin action through the admission and crime-invitation gates. |
| F075 | Major guild implementations | M | 9 / D,P | F075 | Fighters, Mages, Thieves, Dark Brotherhood, Temples and Knightly Orders have individual rank, skill and service rules. Remaining (D): Mages teleport, spymaster, buying and selling magic items, buying soul gems, any-hour hall access, free healing, free magicka recharge and free ship travel. |
| F076 | Non-member / service stubs | A | 9 / D | F076 | Non-member eligibility and typed denials for every wired guild service; services without a handler are carried by F075. |
| F077 | Bank accounts + transactions | M | 9 / D,K | F077 | Regional accounts, gold and letter-of-credit deposits/withdrawals with fees, transfers, wagon gold, and house and ship payments, with UI and save. |
| F078 | Loan issuance + default | M | 9 / D | F078 | Loan issuance and cap, one-year due date on shared game time, reminders, repayment, default with regional and faction reputation loss, and persistence. Remaining (D): show the due date as a calendar date in the issue message. |
| F079 | Quest runtime (machine) | A | 11 / D | F079 | Quest start/update/end and durable instance lifecycle inside admitted updates; not donor singleton/scheduler topology. |
| F080 | Compiled quest instance | M | 11 / D | F080 | Task/condition/resource/symbol state, completion/failure and save reconstruction, validated against the compiled source and live bindings. |
| F081 | Quest script parser | A | 11 / I,D,P | F081 | Normalize existing quest language/source semantics offline; compile handlers in ruleset, no new general gameplay DSL. |
| F082 | Quest tasks + clock | M | 11 / D | F082 | Task transitions and elapsed-time deadlines run in order within admitted intervals, with cancellation, rearm and save/restore. |
| F083 | Quest places + persons | M | 11 / D,K | F083 | Place/person resources bind to location/building/NPC identities and normalized markers, with persistent relocation, hiding and cleanup. |
| F084 | Quest foes + items | M | 11 / D,K | F084 | Foe/item resources, pre-spawn queued operations, ownership, death/loot/reward and removal through shared roster, vitality and corpse operations. |
| F085 | Quest messages + symbols | M | 11 / D,P | F085 | Quest message records and scoped symbols, persisted prompts and choices, and macro inputs, all from quest sources; no hardcoded quest dialogue. |
| F086 | Quest action library | M | 11 / D | F086 | Every candidate action is enumerated in quest-content-inventory.md with its compiled handler or explicit disposition. |
| F087 | Quest macro expansion | M | 11 / D | F087 | Quest-scoped macro context layered over shared text values; a missing binding renders as unresolved and reports a diagnostic. |
| F088 | NPC talk engine | M | 9 / D,U | F088 | Talk session, NPC reaction and greeting, tone and skill bands, directions with disclosure, news/rumors, work, quest and guild topics, with generated news persisted. |
| F089 | Talk mod-protocol compat | X | 9 / D | F089 | DFU talk mod-message protocol excluded; ordinary talk behavior remains F088. |
| F090 | Text table manager | M | 3 / I,P,D | F090 | TEXT.RSC records plus books, rumors, biographies, names and internal strings normalized offline into one keyed text set; runtime lookup by stable text identity. |
| F091 | Global macro expansion | M | 9 / D | F091 | Player/date/location/faction/item macro values cover the donor macro table with explicit context. Remaining (D): donor-format date with day and month names, region and race display names in caller contexts, and item/shop context for item macros. |
| F092 | Text provider chain | A | 3 / D,P | F092 | Normalized text lookup and defined missing-text behavior; mod/provider chain topology is not required. |
| F093 | Grammar + string import | A | 3 / I,P,D,U | F093 | Base-language grammar/text presentation and source string import; localization expansion remains a recorded option. |
| F094 | Localized books | A | 9 / I,P,D,U | F094 | Book records/pages and reader actions; preserve selected base-language content without Unity localized wrappers. |
| F095 | UI root + messages + factory | A | 12 / D,K,U,H | F095 | Existing projection/action transport; game modes, modal choices and input consequences via explicit composition. |
| F096 | Base window + widget kit | A | 12 / U,D | F096 | Adapt required controls/workflows to DOM; exclude Unity widget/window hierarchy and factory replication. |
| F097 | HUD | E | 12 / D,U | F097 | Resource rows, compass, crosshair, status effects and relevant followers/escorts. Remaining (D,U): breath bar and an on-HUD activation-mode indicator. |
| F098 | Inventory window | A | 5 / D,U | F044 | Same inventory workflows as F044; do not create a second window implementation. |
| F099 | Character sheet window | E | 5 / D,U | F099 | Character sheet with career, live/permanent values, advancement and level-up, affiliations and reputation, legal standing and history. |
| F100 | Outcome / message popups | A | 12 / D,U | F100 | Messages, modal choice/confirmation, quest offers and journal notifications from typed product state/actions. Remaining (K,D,U): stacked HUD messages; the single outcome line keeps only the last write in an update. |
| F101 | Spellbook + magic crafting | A | 10 / D,U | F036 | Spellbook/maker/use-magic-item workflows belong to the F036 family. |
| F102 | Rest window | A | 8 / D,U | F102 | Rest/loiter choices, duration and interruption/outcome projections over shared rest operations. |
| F103 | Travel system windows | A | 8 / D,U | F103 | Travel destination/options/cost/duration, transport and teleport confirmation over shared world operations. Remaining (D,U): Mages Guild paid teleport to a map destination (F075). |
| F104 | Guild/bank/merchant service windows | A | 9 / D,U | F104 | Guild/bank/trade/repair eligibility and transactions; UI cannot substitute fake service results. Remaining (D,U): guild join (F074), magic item trade, soul gems and spymaster (F075), and a training confirmation in the DOM. |
| F105 | Talk / quest / tavern / court windows | A | 9 / D,U | F105 | Talk/offer/journal/tavern room/court actions and outcomes; quest views depend selectively on area 11. Remaining (D,U): tavern food and drink (priced menu, hunger gate, holiday prices, health gain). |
| F106 | Trade / automap / book / craft windows | A | 12 / D,U,E | F106 | Trade/maps/books/crafting/summoning workflows attached to their area 8/9/10 owners, not a giant UI task. Remaining (D,U): building-interior automap. |
| F107 | Character creation chain + wizards | A | 5 / D,U,H | F107 | Full creation choices/custom class/bio/summary/new game; exclude DFU installation/setup wizard. |
| F108 | Content-reader singleton bootstrap | X | 3 / I,H | F108 | Exclude content-reader singleton bootstrap; reuse offline Import and admitted pack composition. |
| F109 | Application paths and Arena2 setup | X | 3 / I,H | F109 | Exclude Arena2 installation/app-path distribution setup; local source selection belongs to offline tooling. |
| F110 | Top-level game orchestration | X | 2 / H,D | F110 | Exclude donor GameManager/global orchestration topology; extend existing Host/session when behavior requires. |
| F111 | UI/game state machine | A | 2 / H,D,K | F111 | Required play/menu/pause/death transitions and input consequences; do not port DFU StateManager architecture. |
| F112 | Serializable save/load pipeline | E | 2 / H,D,K | F112 | Current-state payload carries dynamic actors, locations, doors, effects, quests and service state; each new family extends it. No Unity serialization. |
| F113 | Classic save records | A | 2 / D,I | F113 | Preserve meaningful character/item/guild/world state in Rusty saves; classic binary save interoperability excluded by baseline. |
| F114 | Contextual music director | M | 12 / D,P,E | F114 | Contextual track choice and loop lifecycle from admitted ordinary audio; separate bounded long-duration audio exercise. Remaining (D,P): weather and building-environment contexts and cue publication beyond the current seven. |
| F115 | Song catalog enum | X | 12 / D,I | F115 | MIDI.BSA song enum/playback compatibility excluded; ordinary music uses content IDs under F114. |
| F116 | Sound-clip catalog enum | E | 3 / I,P,D | F116 | Named/typed classic sound identity over current DAGGER.SND import. Remaining (D,P): consumers for the gameplay cue families (doors, footsteps, player pain, spell impacts, falls, level-up, swimming, guard halt, equip, books, crafting, gold). |
| F117 | Sound importer | A | 3 / I,E | F117 | Reuse offline sound decoder/publication; exclude Unity AudioClip importer and use Engine playback. |
| F118 | Ambient audio/visual effects | M | 8 / D,P,E | F118 | Ambient selection/timing/lightning cues via game state and Engine audio/appearance, not a second timer. |
| F119 | Material/image importer | A | 3 / I,E | F119 | Reuse texture/palette/image normalization; exclude Unity materials and material reader topology. |
| F120 | Mesh importer | A | 3 / I,E | F120 | Reuse ARCH3D/RDB decoding/publication; exclude Unity Mesh objects. The product-wide world media publication carries every mesh the archive serves, once, and site closures reference it. |
| F121 | Texture archive reader | E | 3 / I,P | F121 | Texture archive decoding and publication for admitted sites; no Texture2D runtime reader. Remaining (I,P): archives used by block-referenced models, now marked unused, for the supported corpus. |
| F122 | General image reader | E | 3 / I,P,U | F122 | Normalize required image/UI records with current IMG/palette/media paths; adapt DOM presentation. Remaining (I,P,U): spell icons (ICON00I0.IMG), compass images and CMPA BSS banks. |
| F123 | Model combiner | A | 3 / I,E | F123 | Reuse offline geometry publication and Engine resource batching where applicable; no donor ModelCombiner class port. |
| F124 | Retro rendering mode | X | 12 / E | F124 | DFU retro postprocess/viewport mode is outside baseline; classic game art remains included and no downstream renderer. |
| F125 | Texture atlases | E | 3 / I,P,E | F125 | Atlas publication for admitted sites and selected media; Engine owns playback/realization. Remaining (I,P): the full supported sprite/terrain/media corpus. |
| F126 | Floating origin | A | 4 / K,D,E | F126 | Use verified Engine origin-rebase capability for exterior traversal; do not reproduce FloatingOrigin machinery. |
| F127 | Camera clear management | A | 4 / D,E | F127 | Interior/exterior camera/background policy through Engine camera/presentation, not Unity camera clear handler. |
| F128 | 3D mesh archive format | E | 3 / I,P | F128 | ARCH3D decoding covers every archive record with facts and use-site dispositions; publication of referenced models is F120. |
| F129 | Dungeon/city block format | E | 3 / I,P | F129 | RDB and RMB records inventoried and summarized corpus-wide (RDI donor-unsupported); per-building interior publication follows site coverage. |
| F130 | Location and region records | E | 3 / I,P | F130 | Region/location/building/dungeon records, names and references published corpus-wide, with empty regions named. |
| F131 | World-map archive | E | 3 / I,P | F131 | Complete MAPS.BSA metadata/layout/height/climate sources through current decoder where applicable. |
| F132 | Faction data | M | 3 / I,P,D | F132 | Faction hierarchy/relationships/regions with stable IDs normalized; runtime policy and guild saves consume pack records. |
| F133 | Item templates | E | 3 / I,P,D | F133 | Item definitions cover the selected classic template groups and magic templates; templates come from the donor export substitute (CNT-011 source gap). |
| F134 | Monster data | E | 3 / I,P,D | F134 | Mobile metadata and actors for the donor enemy identities; raw MONSTER.BSA records stay separate and are not claimed as runtime support. |
| F135 | Reader-to-Unity bridge | X | 3 / I | F135 | Exclude reader-to-Unity bridge; existing normalized contracts are the product-facing representation. |
| F136 | Daggerfall calendar clock | M | 2 / D | F136 | Calendar dates/seasons/moons/time scale and persisted elapsed-time advancement on admitted time, shared by all consumers. |
| F137 | Classic RNG reimplementation | R | 3 / I,E,D | F137 | Keep classic deterministic import transform RNG; runtime randomness uses Engine. Exact runtime classic sequence needs explicit task decision. |
| F138 | Action input bindings | E | 6 / K,D | F138 | Semantic bindings/modes, normalized input and control settings through existing player input owner. |
| F139 | Controls settings + hotkeys | E | 12 / D,K,U | F139 | Rebinding/settings/hotkey semantics for included actions; exclude Unity controls configuration topology. Remaining (D,K,U): bindings for activation modes, quick save/load, logbook/notebook, travel map and use magic item/recast, and look sensitivity/invert as a player setting. |
| F140 | Mouse look | R | 6 / K,E | F140 | Reuse Engine Look/CameraView and existing integration; no second mouselook implementation. |
| F141 | Player activation/interaction | E | 6 / K,D | F024 | Same activation behavior as F024, including perception query and contextual target dispatch. |
