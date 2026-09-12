# Daggerfall implementation task index

Created 2026-09-10 in Den project `rusty-dagger`, campaign **#7922**. The user approved the preparation packet before creation. **255 planned child tasks: 254 new and existing #7083 rewritten**, with **981 dependency links**. Existing #7913, #7228 and #7229 remain external dependencies; their work was not duplicated or restarted.

This is a creation snapshot and navigation aid. **Den owns live task status, dependency changes and execution records.** No gameplay implementation was performed during task creation. The campaign is an aggregate; execute its ready children, not a separate campaign implementation or numbered-area gate. Area 1 preparation is already complete.

## Start and navigate

- **#7923 — Extend durable authored and dynamic world identities.**
- **#7924 — Extend the existing source manifest to archive records and dispositions.**
- Filter Den by `daggerfall-coverage` and `coverage-area-02` through `coverage-area-12`; dependency availability determines execution, not the area number.
- Read [task preparation](../daggerfall-task-preparation.md), [coverage plan](../daggerfall-coverage-plan.md), the owning Den task and its relevant inventory leaves before implementation.

## Records

| File | Meaning |
| --- | --- |
| [Source manifest](content-source-manifest.csv) | All 1,680 supplied source files. Importers refine per-record dispositions without renumbering file IDs. |

Task contracts, stable keys, created IDs, concrete dependency IDs, capability routing, aliases and exclusions live in Den on campaign #7922 rather than in a duplicate local snapshot. A coverage ID attached to several tasks means their specified contributions are jointly required; it does not mean each task implements the whole feature. The five named quest-list rows with no source are assigned to source comparison, not silently excluded from accounting or invented as scripts.

## Scheduling and integration decisions

- Early quest identity/transitions and effect lifetime are independent primitives. Special transformations consume quest start/lifecycle; quest disease/casting actions consume the relevant magic operations. There is no all-quests/all-magic cycle.
- Offline quest compilation/publication is separate from actual provider, action, story and corpus integration. Disabled ordinary-list Daedric records remain summon-only where supported; missing `80C00Y00` produces explicit unavailability.
- Content tasks extend existing readers, normalized contracts and publication. Non-quest corpus assembly and quest publication meet in the full product bundle integration task.
- Artifact tasks are individual operations. Oghma grants the source 30-point attribute allocation without ordinary level/health gains; Sanguine Rose uses actual nearby enemy eligibility and actor admission; Mace of Molag Bal preserves its explicitly provisional donor magicka behavior.
- Narrow drift reviews informed decomposition. During execution, use useful lanes for Engine reuse, local reuse, ownership/tuning and behavior/interoperability; root judges concrete findings. They are not mandatory multi-review or interactive gates on each child.
- Late reconciliation tasks own concrete catalog/callback, time/save, Kit/Engine and product-integration corrections. They are not prerequisites for unrelated primitives. The final coverage reconciliation must report actual retained behavior and remaining uncertainty honestly.

## Accounting verified at creation

- All 141 feature rows are mapped or explicitly excluded; five aliases resolve to 136 canonical rows.
- All 99 formula names are mapped, with the override registry and no-op mod hook explicitly excluded.
- All 153 effect-file leaves are mapped, including two explicit donor demo/WIP exclusions; all inventoried parameter variants remain part of their owning task contracts.
- All 83 quest-action leaves and documented core/helper/table IDs are mapped, including the unregistered demonstration action exclusion.
- All 265 donor quest files are accounted for: 243 source-backed classic/summon-only/cure/story candidates and 22 DFU extra/demo exclusions. Five additional missing-source classic-list names remain explicit source-comparison work.
- All 18 supplemental families, 26 dungeon-action flags and 28 content families are accounted for; MIDI is excluded, ordinary music and its bounded two-hour exercise retained.
- Den readback matched all 255 descriptions, parent/status records and dependency counts. The local graph is acyclic; actual dependency details were spot-checked, with bounded Den context reads explicitly not an independent exhaustive edge export.

## Tasks by plan area

| Area | Children |
| --- | --- |
| 2. Durable identity, state and time | 5 |
| 3. Normalized catalogs, text and source import | 25 |
| 4. World sites and persistent interactions | 18 |
| 5. Character, progression and items | 16 |
| 6. Physical gameplay and actor behavior | 19 |
| 7. Effect and casting foundations | 10 |
| 8. Living world, travel and maps | 11 |
| 9. Society, dialogue and services | 26 |
| 10. Magic breadth, crafting and special behavior | 50 |
| 11. Quest runtime, actions and content | 53 |
| 12. Presentation, content assembly and reconciliation | 22 |

### 2. Durable identity, state and time

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #7923 | `state.identity` | Extend durable authored and dynamic world identities | Ready at creation |
| #7925 | `state.save` | Extend the existing save payload for per-owner world state reconstruction | #7923 |
| #7936 | `state.time` | Implement the Daggerfall calendar and admitted elapsed-time advancement | #7925 |
| #7937 | `state.modes` | Extend product play, pause, modal, death and session transitions | #7925 |
| #7974 | `state.variables` | Persist scoped Daggerfall global and regional variables | #7925, #7967 |

### 3. Normalized catalogs, text and source import

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #7924 | `content.manifest` | Extend the existing source manifest to archive records and dispositions | Ready at creation |
| #7926 | `content.catalogs` | Publish foundational normalized Daggerfall reference catalogs | #7923 |
| #7927 | `content.monster-archive` | Enumerate MONSTER.BSA media and configuration links | #7924 |
| #7928 | `content.items-sourcegap` | Resolve FALL.EXE item-template source gap and establish provenance | #7924 |
| #7929 | `content.qbn-decoder` | Decode QBN source envelopes and preserve quest source identity | #7924 |
| #7930 | `content.qrc-decoder` | Decode QRC source envelopes and preserve quest companion identity | #7924 |
| #7931 | `content.textures` | Inventory and normalize the complete texture-leaf corpus | #7924 |
| #7932 | `content.ui-media-inventory` | Inventory main, guild and service-art source records | #7924 |
| #7933 | `content.character-media-inventory` | Inventory character, face, NPC and story-art families | #7924 |
| #7934 | `content.audio` | Inventory and publish ordinary DAGGER.SND clip catalog | #7924 |
| #7935 | `content.residual-classification` | Classify every remaining classic media and table path | #7924 |
| #7938 | `content.locations` | Normalize all MAPS.BSA regions, locations and map tables | #7926 |
| #7939 | `content.actors` | Publish complete classic actor definitions and media references | #7926, #7927 |
| #7940 | `content.spells` | Decode and publish SPELLS.STD and MAGIC.DEF catalogs | #7926 |
| #7941 | `content.names-biographies` | Normalize name, biography and rumor source tables | #7926 |
| #7942 | `content.text` | Normalize TEXT.RSC and textual source references for the shared text contract | #7926 |
| #7943 | `content.weapon-media` | Normalize all weapon CIF media and item mappings | #7931 |
| #7948 | `content.climate-politics` | Normalize climate and political grid records | #7938 |
| #7949 | `content.blocks-inventory` | Enumerate the complete BLOCKS.BSA corpus and block references | #7924, #7938 |
| #7950 | `content.items` | Publish normalized item templates, materials and presentation references | #7928, #7926, #7943 |
| #7951 | `content.books` | Normalize supplied BOK book metadata and page token records | #7942 |
| #7952 | `content.map-travel-media-inventory` | Inventory automap, world-map, travel and town artwork | #7924, #7938 |
| #7953 | `content.fonts` | Normalize all classic font tables and text/UI references | #7942 |
| #7966 | `content.geometry-inventory` | Enumerate ARCH3D mesh identities and usage dispositions | #7924, #7949 |
| #7967 | `content.factions` | Normalize FACTION.TXT identities, relations and bindings | #7926, #7948 |

### 4. World sites and persistent interactions

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #7946 | `world.sites` | Implement shared location, building and dungeon site context | #7925, #7938 |
| #7965 | `content.terrain` | Inventory and normalize wilderness terrain source records | #7938, #7948 |
| #7978 | `world.interact` | Extend activation modes and stable contextual target dispatch | #7946, #7945, #7961 |
| #7982 | `content.geometry` | Normalize referenced ARCH3D geometry and material links | #7966, #7931 |
| #7992 | `content.dungeons` | Publish normalized RDB dungeon layouts, actions and placements | #7949, #7931, #7982 |
| #7993 | `content.exteriors` | Publish RMB city and exterior block assemblies | #7949, #7938, #7982 |
| #7999 | `world.transitions` | Implement interior, exterior and dungeon enter/exit lifecycle | #7946, #7992, #7993, #7945 |
| #8000 | `world.doors` | Implement persistent door motion, locks and blocking state | #7946, #7992 |
| #8010 | `world.streaming` | Extend exterior admission, unloading and origin coordination | #7999, #7965 |
| #8011 | `world.action_graph` | Implement normalized dungeon triggers and action-link execution | #8000, #7974, #7992 |
| #8012 | `world.relocation` | Implement shared teleport and durable destination relocation | #7999 |
| #8013 | `world.lighting` | Apply site lighting and camera-background policy through Engine | #7999 |
| #8040 | `world.terrain` | Realize exterior terrain, nature and water content | #8010, #7965, #7993 |
| #8041 | `world.action_motion` | Implement dungeon translation and rotation action variants | #8011 |
| #8042 | `world.action_doors` | Connect dungeon door actions to persistent door operations | #8011 |
| #8043 | `world.action_text` | Implement dungeon text, answer and door-text actions | #8011, #8003, #7937 |
| #8044 | `world.action_damage` | Implement dungeon hurt and magicka-drain actions | #8011, #8016 |
| #8106 | `world.action_magic` | Implement dungeon spell and poison action delivery | #8011, #8089 |

### 5. Character, progression and items

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #7945 | `actor.lifecycle` | Extend actor registration and retirement beyond authored PH actors | #7925, #7939 |
| #7960 | `actor.stats` | Complete permanent and live attributes, skills and resistance reads | #7945 |
| #7961 | `item.state` | Extend durable item instance and ownership metadata | #7925, #7950 |
| #7975 | `character.identity` | Implement character identity, race and career choices | #7960, #7926, #7969, #7941 |
| #7976 | `progression.uses` | Record classic skill-use events from gameplay operations | #7960, #7936 |
| #7977 | `item.factory` | Materialize all classic item categories through shared inventory | #7961 |
| #7986 | `character.custom_class` | Implement custom class skills, advantages and difficulty rules | #7975 |
| #7987 | `progression.advancement` | Implement rest-time skill advancement and level eligibility | #7976 |
| #7988 | `item.equipment` | Complete equip restrictions, slot conflicts and change outcomes | #7977, #7960 |
| #7989 | `item.weight_money` | Implement encumbrance and currency movement semantics | #7977, #7960 |
| #7990 | `item.loot` | Complete classic loot pools and seeded container contents | #7977 |
| #7996 | `character.background` | Implement background questions and initial attribute allocation | #7986, #7942, #7941 |
| #7997 | `progression.levelup` | Implement level-up attribute choices and health gains | #7987, #7937 |
| #7998 | `item.condition` | Implement condition loss, breakage and item identification state | #7988 |
| #8009 | `item.actions` | Complete inventory use, consume, drop, transfer and inspection actions | #7989, #7998, #7978 |
| #8141 | `character.new_game` | Construct a new game from the complete character and initial grants | #7996, #7977, #7937, #8089, #8131 |

### 6. Physical gameplay and actor behavior

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #8001 | `movement.modes` | Complete walk, run, crouch and jump policy over Engine movement | #7960, #7989, #7946, #7976 |
| #8014 | `movement.climb` | Implement climbing eligibility, progress and loss of support | #8001 |
| #8015 | `movement.levitate` | Implement vertical levitation and movement effect transitions | #8001 |
| #8016 | `combat.damage` | Complete shared accepted damage, death and hit-result operations | #7960, #7998 |
| #8017 | `ai.senses` | Complete enemy perception, hearing, stealth and pacification policy | #7960, #8001, #7976 |
| #8045 | `movement.consequences` | Implement falling, exhaustion and vitality consequences | #8001, #8016 |
| #8046 | `combat.hit` | Complete body-part selection and classic physical hit chance | #8016, #7976 |
| #8047 | `combat.corpses` | Extend defeat, corpse loot and retirement for dynamic actors | #8016, #7990, #7946 |
| #8048 | `combat.player_death` | Complete player defeat controls and load/new-game outcomes | #8016, #7937 |
| #8049 | `interaction.locks` | Implement lockpicking, door bashing and failed-attempt outcomes | #7978, #8000, #7976, #8016 |
| #8082 | `movement.swim` | Implement swimming, diving, breath and drowning consequences | #8001, #8040, #8016 |
| #8083 | `combat.melee_damage` | Complete weapon, unarmed and enemy attack-set damage policy | #8046 |
| #8084 | `combat.bows` | Implement player and enemy bow attacks with ammunition delivery | #8046, #8009, #8001 |
| #8107 | `combat.wear` | Apply physical hit equipment wear and breakage once | #8083 |
| #8108 | `combat.player_input` | Complete player swing gestures, readiness and attack admission | #8083, #8001 |
| #8109 | `combat.enemy_cadence` | Complete enemy attack selection, multi-attacks and cancellation | #8083, #7913 |
| #8110 | `ai.movement` | Complete pursuit, retreat, strafe, door, flying and swimming AI | #8017, #8082, #8015, #8000 |
| #8142 | `combat.on_hit` | Connect poison, disease and enchantment consequences to accepted hits | #8107, #8089, #8068, #8006 |
| #8143 | `combat.presentation` | Complete equipped weapon, actor attack and hit feedback mappings | #8108, #8084, #7934, #7943 |

### 7. Effect and casting foundations

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #7983 | `magic.lifecycle` | Define active-effect identity and lifecycle coordination | #7960, #7936, #7925 |
| #7994 | `magic.effect-time-save` | Integrate effect rounds, elapsed time, and save ordering | #7983, #7936, #7925 |
| #8006 | `magic.diseases-a-f` | Implement classic diseases BloodRot through Consumption | #7994, #7926 |
| #8027 | `magic.diseases-d-l` | Implement classic diseases Dementia through Leprosy | #8006 |
| #8028 | `magic.diseases-p-y` | Implement classic diseases Plague through YellowFever | #8006 |
| #8039 | `magic.construction_costs` | Implement spell and enchantment construction costs and eligibility | #7983, #7940, #7998 |
| #8067 | `magic.cast-formulas` | Connect magic admission formulas to Daggerfall policy | #7983, #8039 |
| #8068 | `magic.poison` | Implement poison and drug archetype variants | #7994, #8009, #8016 |
| #8089 | `magic.casting` | Implement compiled casting admission and live bundle delivery | #7983, #7926, #8016, #8067, #7940, #7994 |
| #7083 | `enemy.spells` | Implement complete classic enemy spell lists and cast decisions | #8089, #7945, #8017, #7940 |

### 8. Living world, travel and maps

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #7979 | `npc.identity` | Implement static NPC, questor and civilian identity and site binding | #7945, #7946, #7967 |
| #8002 | `world.encounters` | Implement dungeon, wilderness, rest and travel encounter selection | #7979, #7990, #7936 |
| #8018 | `travel.transport` | Implement horse, cart/wagon and ship mode ownership | #7989, #7999, #8001 |
| #8019 | `map.discovery` | Persist dungeon discovery and authored map markers | #7999 |
| #8050 | `time.rest` | Implement rest and loiter with ordered recovery and interruption | #7936, #8002, #7987, #8016 |
| #8051 | `travel.routes` | Implement travel destination, route duration and cost calculation | #7946, #8018, #7936 |
| #8052 | `map.views` | Implement dungeon and city map navigation and selection | #8019, #7938, #7970 |
| #8085 | `travel.execute` | Execute travel with payment, elapsed consequences and arrival | #8051, #8012, #8050 |
| #8086 | `world.weather` | Implement climate, season and time-driven weather state | #8040, #7936 |
| #8111 | `world.ambient` | Implement shelter, weather exposure and ambient sound/light cues | #8086, #7934 |
| #8144 | `npc.wandering` | Implement civilian presence and town wandering policy | #7979, #8110, #7936 |

### 9. Society, dialogue and services

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #7991 | `social.state` | Implement faction reputation, reaction and membership records | #7974, #7979 |
| #8003 | `text.resolve` | Implement normalized text lookup and contextual macro expansion | #7942, #7936, #7991 |
| #8004 | `service.transactions` | Implement shared service admission and transaction outcomes | #7991, #7989, #7936 |
| #8020 | `social.dialogue` | Implement NPC talk sessions, reaction and topic selection | #8003, #7979, #7976 |
| #8021 | `economy.prices` | Implement regional prices, shop quality and trade valuation | #8004 |
| #8022 | `service.training` | Implement skill training costs, limits and elapsed time | #8004, #7987 |
| #8023 | `guild.membership` | Implement guild admission, rank, expulsion and privilege changes | #8004, #7987 |
| #8024 | `bank.accounts` | Implement regional bank balances and credit transactions | #8004 |
| #8053 | `text.books` | Implement book reading and persistent user notes | #8003, #8009, #7951 |
| #8054 | `crime.incidents` | Implement theft, assault, trespass and witness incident state | #7991, #8017, #7978, #8009 |
| #8055 | `service.shops` | Implement merchant stock, purchase, sale and restocking | #8021, #7990 |
| #8056 | `service.repair` | Implement repair quotes, custody, completion and collection | #8021, #7998 |
| #8057 | `service.identify` | Implement item identification and provider charges | #8021, #7998 |
| #8058 | `guild.fighters` | Implement Fighters Guild policies and privileges | #8023 |
| #8059 | `guild.mages` | Implement Mages Guild policies and privileges | #8023 |
| #8060 | `guild.thieves` | Implement Thieves Guild policies and privileges | #8023 |
| #8061 | `guild.brotherhood` | Implement Dark Brotherhood policies and privileges | #8023 |
| #8062 | `guild.temples` | Implement Temples policies and privileges | #8023 |
| #8063 | `guild.knights` | Implement Knightly Orders policies and privileges | #8023 |
| #8064 | `bank.loans` | Implement loan issue, repayment, due dates and default | #8024 |
| #8065 | `bank.property` | Implement house and ship purchase, access and storage ownership | #8024, #7999, #8018 |
| #8087 | `social.directions` | Implement location directions, rumors, news and work topics | #8020, #8052, #7974 |
| #8088 | `service.lodging` | Implement tavern lodging, duration and room access | #8004, #8050 |
| #8157 | `crime.arrest` | Implement guard pursuit, arrest and resistance decisions | #8054, #8144 |
| #8158 | `service.cures` | Implement temple cures, donations and blessings | #8062, #7983, #8146 |
| #8165 | `crime.court` | Implement accusation, court outcomes, fines and prison time | #8157, #7936, #7989, #8012 |

### 10. Magic breadth, crafting and special behavior

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #8069 | `magic.enchant-held-stats` | Implement held stat, skill, talent, spell-point, and carry enchantments | #7983, #8009, #7936, #8017 |
| #8070 | `magic.enchant-held-social` | Implement held social reaction enchantments | #7983, #8009, #7991 |
| #8071 | `magic.enchant-held-condition` | Implement held condition, armor, and periodic vitality enchantments | #7994, #8009, #8016 |
| #8072 | `magic.enchant-item-mutation` | Implement enchant-time weight and soul-bound item state | #8009, #7960 |
| #8078 | `magic.potion_recipes` | Publish complete classic potion recipes and payload identities | #7950, #7940, #8039 |
| #8079 | `magic.artifact-sanguine-rose` | Implement Sanguine Rose summoned-actor use | #8009, #7988, #7960, #7946, #7983, #8017, #7999, #7998 |
| #8080 | `magic.artifact-masque-clavicus` | Implement Masque of Clavicus equipped behavior | #8009, #7988, #7960, #7991, #7983 |
| #8081 | `magic.artifact-oghma-infinium` | Implement Oghma Infinium use and progression outcome | #8009, #7960, #7983, #7997 |
| #8090 | `magic.transformation-infection` | Implement vampire and lycanthrope infection stages | #7994, #8007, #7991, #7946, #8073, #8012 |
| #8103 | `magic.summoning` | Implement Daedric summoning dates, eligibility, payment and quest result | #8059, #8062, #8004, #8073, #7936, #8038, #8008, #7945, #7946 |
| #8105 | `magic.artifact-skull-corruption` | Implement Skull of Corruption use and target-copy outcome | #8009, #7988, #7960, #8007, #8073, #7983, #7999, #8017 |
| #8112 | `magic.alteration-resistance-shield` | Add elemental resistance and shield effects | #8089, #8016 |
| #8113 | `magic.alteration-mobility` | Add climbing, jumping, slowfall, and water-breathing effects | #8089, #8001, #8014, #8045, #8082 |
| #8114 | `magic.alteration-paralysis` | Add paralysis condition effect | #8089, #7960, #8016 |
| #8115 | `magic.destruction-immediate` | Implement immediate destruction payloads | #8089, #8016 |
| #8116 | `magic.destruction-continuous` | Implement continuous destruction payloads | #7994, #8016, #8089 |
| #8117 | `magic.drain-attributes` | Implement permanent attribute drain variants | #8089, #7960 |
| #8118 | `magic.transfer-vitals` | Implement health and fatigue transfer | #8089, #8016 |
| #8119 | `magic.restoration-fortify` | Implement temporary attribute fortification | #7983, #7960, #8089 |
| #8120 | `magic.restoration-recovery-defense` | Implement free action, regenerate, and spell absorption | #7994, #8089 |
| #8121 | `magic.illusion-concealment` | Implement chameleon, invisibility, and shadow variants | #7983, #7960, #7947, #8017, #8089 |
| #8122 | `magic.illusion-light-morph` | Implement normal light and self morph | #8089, #7947, #7960 |
| #8123 | `magic.mysticism-language-dispel` | Implement comprehend languages and dispel effects | #8089, #7960 |
| #8124 | `magic.mysticism-item-soul` | Implement create-item and soul-trap effects | #8089, #8009, #7960 |
| #8125 | `magic.mysticism-lock-open` | Implement magical lock and open world operations | #8089, #7978, #8000 |
| #8126 | `magic.mysticism-silence-teleport` | Implement silence and teleport effects | #8089, #7946, #7936, #8012 |
| #8127 | `magic.thaumaturgy-detection` | Implement enemy, magic, and treasure detection | #7983, #7960, #8009, #7947, #8089 |
| #8128 | `magic.thaumaturgy-social-identify` | Implement charm, pacify, and identify | #8089, #7979, #7991, #8009, #8017, #7998 |
| #8129 | `magic.thaumaturgy-spatial-defense` | Implement levitate, water walking, reflection, and resistance | #8089, #8001, #8015, #8082 |
| #8130 | `magic.enchant-held-absorb` | Implement held spell absorption enchantment | #8089, #8009 |
| #8131 | `magic.known-ready-spells` | Add known and ready spell state and semantic casting actions | #8089, #7925, #7947 |
| #8132 | `magic.runtime-presentation` | Connect magic outcomes to facts, projections, appearance, and audio | #8089, #7947, #7934 |
| #8137 | `magic.potion_making` | Implement potion making, buying and accepted consumption | #8078, #8009, #8004, #8089 |
| #8138 | `magic.artifact-mehrunes-razor` | Implement Mehrunes' Razor terminal strike policy | #8016, #8009, #7988, #7960, #7983, #8083, #8047, #7998 |
| #8139 | `magic.artifact-ring-namira` | Implement Ring of Namira reflection and durability | #8016, #8009, #7988, #7960, #7983, #8083, #8047, #7998 |
| #8140 | `magic.artifact-wabbajack` | Implement Wabbajack quest-safe strike transformation | #8016, #8009, #7988, #7960, #8007, #8073, #7983, #8083, #8047, #7998 |
| #8145 | `magic.transfer-attributes` | Implement attribute transfer variants | #8117, #7960 |
| #8146 | `magic.restoration-cures` | Implement disease, paralysis, and poison cures | #7983, #7960, #8068, #8006, #8114 |
| #8147 | `magic.restoration-healing` | Implement drain healing and vital restoration | #8117, #8016 |
| #8148 | `magic.enchant-cast-triggers` | Implement held, used, and strike spell triggers | #8089, #8009, #8016, #8107, #7988 |
| #8152 | `magic.spellmaker` | Implement spellmaker creation, purchase and custom spell persistence | #8039, #8131, #8004, #8059 |
| #8153 | `magic.spell_sales` | Implement spell purchase and spellbook management | #8131, #8004, #8059 |
| #8154 | `magic.special-racial-passives` | Coordinate racial overrides and passive special rules | #7960, #7991, #8054, #8018, #8050, #8131 |
| #8155 | `magic.artifact-azuras-star` | Implement Azura's Star soul capture and reuse | #8009, #7988, #7960, #7983, #8124 |
| #8156 | `magic.artifact-mace-molag-bal` | Implement Mace of Molag Bal strike transfer | #8016, #8009, #7988, #7960, #7983, #8067, #8117, #7994, #8083 |
| #8159 | `magic.enchant-strike-effects` | Implement leech, damage modulation, and vampiric enchantments | #8148, #8016, #8083 |
| #8163 | `magic.special-vampirism` | Implement permanent vampirism state and lifecycle | #8090, #8154, #7960, #7991, #8054, #8018, #8050, #8131 |
| #8164 | `magic.special-lycanthropy` | Implement permanent lycanthropy state and lifecycle | #8090, #8154, #7960, #7991, #8018, #8050, #8131 |
| #8167 | `magic.item_making` | Implement item-maker enchantment selection, payment and result state | #8039, #8148, #8072, #8004, #8059, #8069, #8070, #8071, #8130, #8159 |
| #8172 | `magic.consumer_completion` | Reconcile spell catalogs, constructed payloads and item callbacks | #7083, #8142, #8152, #8153, #8137, #8167, #8103, #8132, #7983, #7994, #8089, #8067, #8112, #8113, #8114, #8115, #8116, #8117, #8145, #8118, #8146, #8119, #8147, #8120, #8121, #8122, #8123, #8124, #8125, #8126, #8127, #8128, #8129, #8006, #8027, #8028, #8090, #8068, #8069, #8070, #8071, #8130, #8148, #8159, #8072, #8154, #8163, #8164, #8155, #8079, #8156, #8138, #8139, #8140, #8080, #8081, #8105, #8131, #8039, #8078 |

### 11. Quest runtime, actions and content

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #7944 | `quest.import.contract` | Define the normalized Daggerfall quest-pack contract | #7923, #7926 |
| #7956 | `quest.import.compiler` | Compile DFU textual Daggerfall quest sections into normalized records | #7944, #7926 |
| #7957 | `quest.tables.global-messages` | Publish quest globals and static-message tables | #7944, #7926 |
| #7958 | `quest.tables.world-audio` | Publish quest place and sound tables | #7944, #7926, #7938, #7934 |
| #7959 | `quest.tables.disease-spell` | Publish quest disease and spell tables | #7944, #7926, #7940 |
| #7984 | `quest.import.catalog` | Compile classic quest-list membership and selection metadata | #7944, #7967 |
| #7985 | `quest.tables.actor-item` | Publish quest item, faction, and foe tables | #7944, #7926, #7939, #7950, #7967 |
| #7995 | `quest.import.resources` | Normalize quest resources, symbols, and place/person references | #7956, #7958, #7985, #7926, #7938, #7939, #7950, #7967 |
| #8007 | `quest.runtime.state` | Add persistent Daggerfall quest instances, resources, and symbols | #7995, #7923, #7925, #7926 |
| #8008 | `quest.import.original-comparison` | Reconcile normalized quest sources with original QBN and QRC records | #7956, #7929, #7930, #7995 |
| #8029 | `quest.runtime.transitions` | Run ordered quest tasks and explicit basic transitions | #8007 |
| #8030 | `quest.runtime.time` | Advance Daggerfall quest clocks through admitted game time | #8007, #7936, #7925 |
| #8031 | `quest.runtime.messages` | Render quest messages, macros, journal, and semantic prompts | #8007, #7957, #8003, #7947, #7925 |
| #8032 | `quest.corpus.fighters` | Publish retained Fighters Guild quest corpus | #7984, #7956, #8008 |
| #8033 | `quest.corpus.mages` | Publish retained Mages Guild quest corpus | #7984, #7956, #8008 |
| #8034 | `quest.corpus.temples` | Publish retained general and specific temple quest corpus | #7984, #7956, #8008 |
| #8035 | `quest.corpus.social-guilds` | Publish retained Thieves, Dark Brotherhood, and Knightly quest corpus | #7984, #7956, #8008 |
| #8036 | `quest.corpus.witches-commoners` | Publish retained Witches and Commoners quest corpus | #7984, #7956, #8008 |
| #8037 | `quest.corpus.merchant-vampire` | Publish retained Merchant and Vampire quest corpus | #7984, #7956, #8008 |
| #8038 | `quest.corpus.disabled-classic` | Publish source-backed disabled classic quest records with dispositions | #7984, #7956, #8008 |
| #8073 | `quest.runtime.lifecycle` | Admit quest starts, child quests, tombstones, and cleanup in the session update | #8029, #7984, #8007, #7923, #7925 |
| #8074 | `quest.runtime.bindings` | Bind quest persons, places, foes, and items to durable world state | #8007, #7946, #7979, #7960, #8009 |
| #8075 | `quest.action.prompts` | Record quest prompt and multi-choice results | #8031, #7947, #7925 |
| #8076 | `quest.action.progression` | Implement quest progression and attribute/skill conditions | #8029, #7991, #7936, #7925, #7997 |
| #8077 | `quest.corpus.nobility` | Publish retained Nobility quest corpus | #7984, #7956, #8031, #8008 |
| #8091 | `quest.action.world-triggers` | Implement player location and world-transition quest triggers | #8029, #8074, #7946, #8001 |
| #8092 | `quest.action.world-placement` | Place quest foes, items, and NPCs including queued pre-spawn work | #8074, #7946, #7979, #7960, #8009, #7999 |
| #8093 | `quest.action.world-state` | Implement quest world item, discovery, teleport, and update actions | #8074, #7946, #8009, #7978, #8001, #8012, #8019 |
| #8094 | `quest.action.questors-faces` | Manage questor registration, faces, and NPC dialogue visibility | #8074, #7979, #7947, #7925 |
| #8095 | `quest.action.foe-spawning` | Create quest foes and city guards with durable spawn schedules | #8074, #7960, #7946, #7991, #7936, #8054 |
| #8096 | `quest.action.foe-lifecycle` | React to and command quest foe injury, death, and removal | #8074, #7960, #8016, #7925, #8047 |
| #8097 | `quest.action.npc-lifecycle` | Create, hide, restore, destroy, and schedule quest NPCs | #8074, #7979, #7960, #7946, #7936, #7925 |
| #8098 | `quest.action.item-lifecycle` | Grant, test, take, and make permanent quest items | #8074, #8009, #7925 |
| #8099 | `quest.action.dialogue-links` | Bind quest dialogue, rumor, and link records | #8031, #8074, #7979, #7991, #8003, #8020 |
| #8100 | `quest.action.clicked-actors` | Dispatch foe and NPC click quest actions from stable interactions | #8074, #8031, #7978, #7979, #7960 |
| #8101 | `quest.action.journal-message` | Write and remove quest journal/log/message entries | #8031, #8003, #7925, #7947, #8053 |
| #8102 | `quest.action.social` | Apply quest reputation and legal-state actions and conditions | #8029, #7991, #7925, #7926, #8054, #8023 |
| #8104 | `quest.offers` | Implement quest offers, acceptance, refusal and guild invitation callers | #8073, #8031, #8020, #8023, #8075 |
| #8133 | `quest.action.item-interactions` | Implement quest item transfers, payments, and interaction triggers | #8098, #8009, #7978, #7979, #7991 |
| #8134 | `quest.action.rewards` | Complete quests with ordered player rewards and notification | #8029, #8098, #8009, #7991, #7947 |
| #8135 | `quest.action.magic` | Execute quest spell and effect actions through admitted casting | #7959, #8089, #7983, #7960, #8016, #8074 |
| #8136 | `quest.action.environment-media` | Implement quest climate, weather, sound, song, and video semantics | #8030, #7958, #7947, #7936, #8086, #7964, #7981 |
| #8149 | `quest.action.foe-relations` | Apply quest foe team, hostility, and restraint policy | #8074, #7960, #8016, #8001, #7925, #8017, #8110 |
| #8150 | `quest.corpus.main-story-early` | Publish early named main-story quest corpus | #7956, #8073, #8136, #8008 |
| #8151 | `quest.corpus.main-story-late` | Publish late main-story, tutorial, and Brisienna corpus | #7956, #8073, #8136, #8008 |
| #8160 | `quest.action.disease` | Apply and cure quest disease and transformation effects | #7959, #7983, #7936, #7925, #8074, #8146, #8090 |
| #8161 | `quest.story_start` | Wire new-game tutorial and main-story initialization | #8141, #8073, #8151 |
| #8162 | `quest.story_endings` | Implement main-story branch outcomes and ending completion state | #8150, #8151, #8136, #8102, #8134 |
| #8166 | `quest.corpus.cures` | Publish classic vampirism and lycanthropy cure quests | #7956, #8160, #8008 |
| #8168 | `quest.integrate.guilds` | Connect guilds quest corpus to complete runtime operations | #8104, #8030, #8074, #8008, #8032, #8033, #8034, #8035, #8058, #8059, #8062, #8060, #8061, #8063, #8091, #8092, #8093, #8094, #8095, #8149, #8096, #8097, #8098, #8133, #8134, #8099, #8100, #8101, #8075, #8102, #8076, #8160, #8135, #8136 |
| #8169 | `quest.integrate.regional` | Connect regional quest corpus to complete runtime operations | #8104, #8030, #8074, #8008, #8036, #8037, #8077, #8087, #8154, #8163, #8164, #8091, #8092, #8093, #8094, #8095, #8149, #8096, #8097, #8098, #8133, #8134, #8099, #8100, #8101, #8075, #8102, #8076, #8160, #8135, #8136 |
| #8170 | `quest.integrate.story` | Connect story quest corpus to complete runtime operations | #8104, #8030, #8074, #8008, #8150, #8151, #8161, #8162, #8091, #8092, #8093, #8094, #8095, #8149, #8096, #8097, #8098, #8133, #8134, #8099, #8100, #8101, #8075, #8102, #8076, #8160, #8135, #8136 |
| #8171 | `quest.integrate.special` | Connect special quest corpus to complete runtime operations | #8104, #8030, #8074, #8008, #8038, #8166, #8103, #8154, #8163, #8164, #8091, #8092, #8093, #8094, #8095, #8149, #8096, #8097, #8098, #8133, #8134, #8099, #8100, #8101, #8075, #8102, #8076, #8160, #8135, #8136 |

### 12. Presentation, content assembly and reconciliation

| Den task | Stable key | Task | Dependencies |
| --- | --- | --- | --- |
| #7947 | `ui.projections` | Extend semantic UI modes, HUD and status projections | #7937 |
| #7954 | `content.videos-provenance` | Normalize original VID cinematic provenance and story hooks | #7924, #7942 |
| #7955 | `content.flc-provenance` | Normalize Daedric and artifact FLC cinematic provenance | #7924, #7942 |
| #7962 | `ui.settings` | Implement semantic controls, hotkeys and persisted settings | #7947 |
| #7963 | `ui.saves` | Implement save-slot, load, new-game and quit workflows | #7925, #7947 |
| #7964 | `audio.music` | Implement contextual ordinary music selection and loop lifetime | #7934, #7937, #7946, #7936 |
| #7968 | `content.ui-media` | Publish semantic UI and service-media artifacts | #7932, #7947 |
| #7969 | `content.character-media` | Publish normalized character and NPC presentation references | #7933, #7926, #7947 |
| #7970 | `content.map-travel-media` | Publish map and travel media for real world-data consumers | #7952, #7938, #7947 |
| #7971 | `content.videos-admission` | Define supported cinematic conversion and pack publication contract | #7954, #7947 |
| #7972 | `content.flc-admission` | Publish supported artifact-cinematic media through the normal content path | #7955, #7947 |
| #7973 | `content.residual-publication` | Publish bounded residual media/table families with named consumers | #7935, #7947 |
| #7980 | `audio.duration` | Exercise long-duration music looping through the ordinary product | #7964 |
| #7981 | `media.cinematics` | Implement story cinematic playback and completion transitions | #7947, #7971, #7972 |
| #8005 | `content.corpus-publication` | Assemble normalized world, catalog and media packs | #7926, #7938, #7948, #7965, #7992, #7993, #7982, #7939, #7950, #7940, #7967, #7942, #7931, #7943, #7934, #7953, #7951, #7941, #7968, #7969, #7970, #7971, #7972, #7973 |
| #8025 | `ui.character_sheet` | Complete character sheet values, advancement and history views | #7997, #7991 |
| #8026 | `content.world-corpus-closure` | Verify publication closure across all regions, dungeon and exterior corpora | #8005, #7946 |
| #8066 | `content.publication-reconciliation` | Reconcile source dispositions, pack consumers and coverage records | #8005, #8026, #7929, #7930, #8008 |
| #8173 | `integration.time_state` | Reconcile shared time, world deltas and save reconstruction across domains | #8085, #8165, #8064, #8056, #8050, #8168, #8169, #8170, #8171, #7994, #8068, #8071, #8090, #8163, #8164 |
| #8174 | `integration.kit_reuse` | Reconcile Kit reuse and Engine ownership after domain implementation | #8173, #8110, #8010, #8026 |
| #8175 | `integration.product` | Complete bundle selection, UI workflows and content-consumer reconciliation | #8173, #8066, #8025, #7962, #7963, #8052, #7964, #7981, #7228, #7229, #8172 |
| #8176 | `integration.coverage` | Reconcile retained feature and content coverage with completed implementation | #8174, #8175, #7980 |
