# Daggerfall feature map (DFU structures → rusty-dagger coverage)

**Status:** survey complete + Engine cross-check (point-in-time coverage notes). Scope is code structures and features that support Daggerfall content, not the content itself.
**DFU donor:** `/home/research/daggerfall-unity` at `81e89e90c27bc3c1a7a61871e545fad129174dec` (~849 C# files under `Assets/Scripts`).
**rusty-dagger:** C#-only product cutover shape per `docs/code-migration-map.md` — Engine guarantees, Kit shapes, ruleset decides, bundle assembles, host launches.
**Reading rule:** DFU paths below are relative to `Assets/Scripts` in the DFU checkout. rusty-dagger paths are relative to this repo. Coverage is a point-in-time note, not a roadmap promise.

## Scope and method

- This map covers engine-adjacent structures DFU uses to run Daggerfall: entities/stats, player motors, combat, magic/effects, items/loot, enemies/AI, world/terrain/dungeons, guilds/factions, quests/dialogue/text, UI windows, saves, audio, rendering/assets, Arena2 formats, and utility.
- It deliberately excludes authored content payloads (quest text, book text, map data, textures) except for the code that loads, interprets, or presents them.
- DFU file counts (`.cs`, excluding `.meta`) for scale: `Game/MagicAndEffects` 164, `Game/Questing` 99, `Game/UserInterfaceWindows` 85, `API` 64, `Game/Player` 16, `Game/Entities` 12, `Game/Utility` 12, `Game/Guilds` 11, `Terrain` 11, `Game/Items` 9, `Game/Serialization` 9, `Game/Formulas` 1 (+ `Utility/WeaponBasics.cs`, `EnemyBasics.cs`).
- rusty-dagger was assessed from checked C# only (`src/WorldRpg.Kit`, `src/WorldRpg.Rulesets.Daggerfall`, `src/WorldRpg.Host`, `src/Daggerfall.Import`, `content/worldrpg`, `src/ui`, `src/sprite-ui`); ignored `obj/`/`bin/` output and the retired Rust/TS paths were not counted as coverage.
- Five survey slices contributed one row per distinct code structure; each row keeps one DFU reference, one behavior description, and one coverage note. A second pass cross-checked every coverage claim against the paired Engine checkout (`csharp/Rusty.Engine` SDK surface + `docs/csharp-capabilities.md`); where the product consumes an upstream mechanism, the row names it, and `Upstream Engine cross-check` below records the full verdict including unused hooks and near-duplicate audits.

## Coverage legend

| Label | Meaning |
| --- | --- |
| Covered | A live rusty-dagger path owns the behavior (ruleset policy, Kit mechanism, Host lifecycle, or Import decoder + normalized publication). |
| Partial | The shape exists but is narrowed to the live slice (notably Privateer's Hold combat/loot/appearance) or a subset of modes/formulas. |
| Import-only | Offline decoding/normalization/provenance exists; no live runtime behavior yet. |
| Absent | No live or import path yet; DFU reference is the donor for future work. |
| Engine-owned | Behavior belongs to the Engine boundary (update admission, spatial, rendering, audio, persistence substrate); rusty-dagger consumes it through named Engine services rather than reimplementing it. |
| Engine-unused | An upstream Engine mechanism exists for this shape but the product does not consume it yet. This is an unused hook, not a duplication — the row stays Absent/Partial until product policy wires it up. |

Engine-boundary reminder: product code owns application/gameplay state and policy inside one Engine-admitted update; it must not add a second loop/clock/thread, renderer, scheduler, or downstream-Rust substitute. A missing safe Engine contract is an upstream request and an honest stop.

## DFU layout (where to look)

| Area | DFU location | What lives there |
| --- | --- | --- |
| Bootstrap / game state | `DaggerfallUnity.cs`, `DaggerfallUnityApplication.cs`, `Game/GameManager.cs`, `Game/StateManager.cs` | Singleton bootstrap, mod system hooks, game-state machine, Dansk save plumbing entry points |
| Entities / stats | `Game/Entities/` (`PlayerEntity.cs`, `EnemyEntity.cs`, `DaggerfallEntity.cs`, `DaggerfallStats.cs`, `DaggerfallSkills.cs`, `EntityEnums.cs`, `RaceTemplate.cs`) | Attributes, skills, resistances, careers, enemy records |
| Player | `Game/Player/` + `Game/Player*.cs` (motors, `CharacterDocument.cs`, `PlayerNotebook.cs`, faction/global vars) | First-person motors (ground/climb/swim/fly), vitals, notebook, persistent vars |
| Combat | `Game/FPSWeapon.cs`, `Game/WeaponManager.cs`, `Game/EnemyAttack.cs`, `Game/EnemyHealth.cs`, `Game/Formulas/FormulaHelper.cs`, `Utility/WeaponBasics.cs` | Swing modes, to-hit/damage, weapon material tiers, enemy retaliation |
| Magic | `Game/MagicAndEffects/` (`EntityEffectBroker.cs`, `EntityEffectManager.cs`, `Effects/<School>/`) | Effect broker/bundles, ~100+ classic effects by school |
| Items | `Game/Items/` (`DaggerfallUnityItem.cs`, `ItemCollection.cs`, `ItemBuilder.cs`, `LootTables.cs`) | Item records, collections, equip tables, loot generation |
| Enemies / AI | `Game/EnemyMotor.cs`, `Game/EnemySenses.cs`, `Game/MobilePerson*.cs`, `Game/Utility/RandomEncounters.cs`, `Utility/EnemyBasics.cs` | Steering, senses, spawning, civilian/person motors |
| World | `Terrain/` (`StreamingWorld.cs`, `DaggerfallTerrain.cs`), `Game/WeatherManager.cs`, `Game/TransportManager.cs`, `Game/BuildingDirectory.cs`, `Utility/RMBLayout.cs`, `RDBLayout.cs` | Streaming exterior, climate, weather, travel, buildings, dungeon blocks |
| Guilds / factions | `Game/Guilds/` (`GuildManager.cs`, `Guild.cs`, `Services.cs`, guild implementations) | Membership, rank, services (training, cure, loans, daedra) |
| Quests | `Game/Questing/` (`QuestMachine.cs`, `Quest.cs`, `Parser.cs`, `Actions/`, `Place.cs`, `Person.cs`, `Foe.cs`) | Quest runtime, script parser, quest resources |
| Dialogue / text | `Game/TalkManager.cs`, `Game/TextManager.cs`, `Utility/MacroHelper.cs`, `Localization/` | Talk UI backing, text tables, macro expansion, books |
| UI | `Game/UserInterfaceWindows/` (~85 windows), `Game/UserInterface/` (controls), `Game/DaggerfallUI.cs` | HUD, sheets, inventory, spellbook/maker, guild/bank/court, chargen wizard |
| Saves | `Game/Serialization/` (`SaveLoadManager.cs`, `Serializable*.cs`) | Classic + DFU save/load, serializable entity/world state |
| Audio | `Game/SongManager.cs`, `SongFiles.cs`, `SoundClips.cs`, `SoundReader.cs`, `Game/AmbientEffectsPlayer.cs` | MIDI/song banks, clip banks, ambient loops |
| Rendering / assets | `MaterialReader.cs`, `MeshReader.cs`, `TextureReader.cs`, `ModelCombiner.cs`, `RetroRenderer.cs`, `Utility/FloatingOrigin.cs` | Billboard/atlas/model pipeline, retro post, origin rebasing |
| Source formats | `API/` (`DFBlock.cs`, `DFLocation.cs`, `MapsFile.cs`, `FactionFile.cs`, `BsaFile.cs`, …), `Utility/ContentReader.cs` | Direct Arena2 file readers |
| Shared utility | `Utility/DaggerfallDateTime.cs`, `DFRandom.cs`, `Table.cs`, `Dice100.cs`, `Game/Utility/TravelTimeCalculator.cs` | Calendar, seeded RNG, lookup tables, travel math |

## rusty-dagger layout (coverage owners)

| Owner | Path | Current responsibility (per migration map) |
| --- | --- | --- |
| Kit mechanisms | `src/WorldRpg.Kit/` (Controls, Actors, Inventory, Facts, Progression, Presentation) | Reusable world-RPG mechanisms over Engine services; never names Daggerfall |
| Daggerfall ruleset | `src/WorldRpg.Rulesets.Daggerfall/` (Content, Modules, Policies, Presentation, Facts) | Daggerfall identities, formulas, attack/reward policy, save behavior, Privateer's Hold composition |
| Host | `src/WorldRpg.Host/` (`WorldRpgProduct.cs`, `WorldRpgSaveStore.cs`, `BuiltInRulesets.cs`) | Product entry, lifecycle, built-in selection, Engine persistence composition |
| Import (offline) | `src/Daggerfall.Import/` (Arena2, Normalization, Publication) + `.Tool` | Arena2 decoding, normalized publication, provenance/differential validation; no runtime authority |
| Content packs | `content/worldrpg/` (packs, payloads, tuning, imports, bundles) | Loaded Privateer's Hold publication, tuning profiles, bundle selection |
| UI (thin) | `src/ui`, `src/sprite-ui`, `src/WorldRpg.SpriteWorkbench` | DOM presentation of Engine projections; sprite inspection via Engine services |

> Subsystem feature tables follow. Each table row is: Feature | DFU code ref | Description | rusty-dagger coverage.

## 1. Character, player embodiment, combat, formulas

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| Attribute stats container | `Game/Entities/DaggerfallStats.cs` — `DaggerfallStats`, live vs permanent values, `AssignMods` | Holds 8 attributes (Str..Luc) with live/permanent split plus modifier assignment. | Partial — stat IDs in `Rulesets.Daggerfall/Content/DaggerfallDefinitions.cs`; Engine `ExactStatContribution` stacking exists upstream but only level-up HP sources use it (`DaggerfallRewardReactions.cs`) — general live/permanent mod layer absent (Engine-unused, not duplicated) |
| Skill container | `Game/Entities/DaggerfallSkills.cs` — live/permanent values, defaults, career mods | Holds ~35 skills with live/permanent values and career assignment. | Partial — skills as stat IDs + `SkillAdvancementMultipliers` in `Rulesets.Daggerfall/Policies/DaggerfallFormulaPolicy.cs`; reads evaluate base-only (`Array.Empty<ExactSource>()` in `Kit/Actors/ActorsState.cs`) — upstream modifier hook unused (Engine-unused, not duplicated) |
| Resistance container | `Game/Entities/DaggerfallResistances.cs` | Fire/Frost/Disease-Poison/Shock/Magic resistances, live/permanent split. | Absent — no resistance container |
| Base entity vitals + effect flags | `Game/Entities/DaggerfallEntity.cs` — health/fatigue/magicka, level, armor, paralysis/invisibility/immunity flags | Shared entity base for pools and magic-effect state flags. | Partial — vitals/tracks in `Content/DaggerfallDefinitions.cs` + Engine Mechanics tracks; effect-flag layer absent |
| Player character state | `Game/Entities/PlayerEntity.cs` — `AssignCharacter`, gold/weight, crime, guilds, lycanthropy/vampirism | Full player record: chargen assignment, gold/encumbrance, reputation, crime, guilds, transformations. | Partial — vitals/identity in `Content/DaggerfallBaseContent.cs` + `DaggerfallSavePayload.cs`; crime/guild/lycanthropy absent |
| Character-creation document | `Game/Player/CharacterDocument.cs` | Persistent chargen record consumed by `PlayerEntity.AssignCharacter`. | Import-only — creation data in `Daggerfall.Import`/content packs (`content/worldrpg/content-packs/daggerfall.base.pack.json`); no Kit/ruleset chargen flow |
| Enemy entity setup | `Game/Entities/EnemyEntity.cs` — `SetEnemyCareer`, `SetEnemyEquipment`, `SetEnemySpells` | Instantiates a mobile enemy from career data: stats, equipment, spells, loot/quest queues. | Partial — actor definitions/attacks/loot keys in `Content/DaggerfallDefinitions.cs` + packs; career-to-entity builder absent |
| Enemy catalog lookup | `Utility/EnemyBasics.cs` — `GetEnemy`, corpse-texture mapping | Static mobile-enemy lookup by type/name plus corpse textures. | Partial — actor/item catalogs in `Content/DaggerfallDefinitions.cs` + `DaggerfallBaseContent.cs`; corpse-texture mapping absent |
| Attribute-derived formulas | `Game/Formulas/FormulaHelper.cs` — `DamageModifier`, `ToHitModifier`, `HitPointsModifier`, `MagicResist`, `MaxEncumbrance`, `SpellPoints` | Pure stat→bonus functions from Str/Agi/End/Int/Wil. | Covered — ported in `Rulesets.Daggerfall/Policies/DaggerfallFormulaPolicy.cs` against `DaggerfallFormulaTuning` |
| Vital/recovery formulas | `Game/Formulas/FormulaHelper.cs` — `RollMaxHealth`, health/fatigue/spell-point recovery rates | Max-HP rolls and per-rest recovery rates. | Covered — `HealthRecoveryRate`, `FatigueRecoveryRate`, `SpellPointRecoveryRate`, `HitPointsPerLevelUp` in `Policies/DaggerfallFormulaPolicy.cs` |
| Melee damage pipeline | `Game/Formulas/FormulaHelper.cs` — weapon/hand-to-hand ranges, `CalculateAttackDamage`, `AdjustWeaponAttackDamage`, `DamageEquipment` | Full attack-damage resolution incl. material-vs-armor adjustment and equipment wear. | Partial — ranges + hit resolution in `Rulesets.Daggerfall/Modules/Combat/CombatModule.cs` + `Policies/DaggerfallFormulaPolicy.cs`; equipment-damage-through-hit absent |
| To-hit pipeline | `Game/Formulas/FormulaHelper.cs` — weapon/armor/stats/skills/adrenaline adjustments, body-part roll | Full hit-chance chain with struck-body-part roll and success test. | Partial — consolidated `CalculateHitChance` + `StruckBodyPart` in `Policies/DaggerfallFormulaPolicy.cs` via `Modules/Combat/CombatModule.cs`; per-factor breakdown not separately ported |
| Swing/proficiency/racial/backstab modifiers | `Game/Formulas/FormulaHelper.cs` — `CalculateSwingModifiers`, `CalculateProficiencyModifiers`, `CalculateRacialModifiers`, backstab, weapon timing | Swing, proficiency, racial, enemy-type, backstab modifiers plus weapon timing. | Partial — `BackstabChance` + cooldowns via `DaggerfallActionDefinition`/`CombatDefinitions.cs`; swing/proficiency/racial/enemy-type modifiers absent |
| Saving throws vs magic | `Game/Formulas/FormulaHelper.cs` — `SavingThrow`, `ModifyEffectAmount`, `InflictPoison`, `OnMonsterHit` | Elemental saving throws, effect modification, poison, monster special hits. | Absent — no saving-throw/poison/monster-effect logic (magic not ported) |
| Non-combat skill-chance formulas | `Game/Formulas/FormulaHelper.cs` — lockpicking, pickpocketing, shoplifting, stealth, climbing, pacification | Theft/stealth/climbing/pacification success chances. | Absent — no such chance formulas in ruleset |
| Level/skill-advancement formulas | `Game/Formulas/FormulaHelper.cs` — `CalculateSkillUsesForAdvancement`, `CalculatePlayerLevel`, `CalculateHitPointsPerLevelUp` | Skill-use counts, level from skill-sum deltas, HP-per-level rolls. | Covered — `SkillUsesForAdvancement`, `ClassicPlayerLevel`/`ExperimentalXpLevel`, `HitPointsPerLevelUp` in `Policies/DaggerfallFormulaPolicy.cs` |
| Player melee input + hit resolution | `Game/WeaponManager.cs` — gesture attack dirs, sphere-cast hit test, sheath/equip state | Mouse-gesture melee dispatch, hit test, damage routing, sheath state. | Partial — `Modules/Combat/CombatModule.cs` (`TryPlayerMelee`, `ResolveExplicit`, cooldown latch) + `Modules/Combat/DaggerfallMeleeTargeting.cs`; gesture/sheath presentation absent |
| On-screen weapon presentation | `Game/FPSWeapon.cs` — swing states, timing, hit-frame queries, sounds | First-person weapon sprite animation states and sounds. | Absent — no FPS weapon presentation (hit cues only via `Modules/Combat/CombatDefinitions.cs` media scopes) |
| Weapon animation data | `Utility/WeaponBasics.cs` — `MeleeWeaponAnims`, alignments, offsets, frame rates | Melee/bow animation records table. | Import-only — weapon visuals referenced by content packs; anim table not ported |
| Enemy melee + ranged attacks | `Game/EnemyAttack.cs` — `MeleeDistance`, melee timer, `BowDamage` | Enemy attack cadence: melee range/timer gating and archer dispatch. | Partial — cadence via `Modules/Combat/CombatModule.cs` cooldowns + `Modules/Behavior/DaggerfallEnemyBehaviorModule.cs`; bow-specific dispatch absent |
| Enemy damage intake | `Game/EnemyHealth.cs` — `RemoveHealth` | Hit-point sink routing player damage into death handling. | Covered — damage intake over Engine Mechanics tracks in `Modules/Combat/CombatModule.cs` (`ResolveExplicit`) |
| Player locomotion state | `Game/PlayerMotor.cs` + `Game/Player/` motors (ground/climb/acrobatic/friction/hanging/rappel, speed/height/head-bob) | Movement state machine: run/walk/crouch/jump/swim/climb/levitate/ride. | Partial — generic locomotion in `Kit/Controls/SpatialMovementSystem.cs` + `Kit/Controls/PlayerControlState.cs` + `Kit/Actors/ActorNavigationCoordinator.cs`; DFU climb/swim/levitate/mount modes absent |
| Player health/death | `Game/PlayerHealth.cs`; `Game/PlayerDeath.cs` — `DeathInProgress`, `ResetCamera` | Player-side damage intake and death sequence (fade, camera, reset). | Partial — vitals damage via `Modules/Combat/CombatModule.cs`; death-sequence presentation absent |
| Player activation/interaction | `Game/PlayerActivate.cs` — `IPlayerActivable`, modes, `Pickpocket`, `AttemptExteriorDoorBash`, lock values | Crosshair activation: talk/loot/steal/pickpocket, door bash, locks, building/quest checks. | Partial — loot interaction via `Modules/Loot/DaggerfallCorpseLootModule.cs` + `Modules/Combat/DaggerfallMeleeTargeting.cs`; talk/steal/bash/lock modes absent |

## 2. Magic and effects, items, inventory, loot

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| Effect template registry (broker) | `Game/MagicAndEffects/EntityEffectBroker.cs` — template discovery by key/classic-key | Singleton registry building live bundles from classic spell records. | Absent — no effect broker/registry |
| Per-entity effect manager | `Game/MagicAndEffects/EntityEffectManager.cs` — ready spell, live/incumbent bundles, casting cost/anim, cures | Component on every entity owning spells, bundles, casting, stat/skill merges, HUD icons. | Absent — no casting/ready-spell/incumbent runtime |
| Spell bundle data model | `Game/MagicAndEffects/EntityEffectBundle.cs`, `LiveEffectBundle.cs` | Serializable spell/recipe payload vs in-flight cast instance. | Absent — no bundle/element model |
| Base effect class hierarchy | `Game/MagicAndEffects/EntityEffect.cs`, `IncumbentEffect.cs`, `MagicAndEffectsEnums.cs`, `MagicAndEffectsStructs.cs` | Base effect lifecycle hooks, persistent-magic rounds, target/bundle/disease enums. | Absent — no effect base classes or magic enums |
| Classic spell-record import | `EntityEffectBroker.cs` — classic-key lookup from SPELL.RSC records | Maps classic spell records to live templates incl. saved spells. | Absent — no classic spell-record mapping |
| School effect families | `Game/MagicAndEffects/Effects/Alteration\|Destruction\|Illusion\|Mysticism\|Restoration\|Thaumaturgy/*.cs` (~100+ per-effect classes) | One class per classic effect (damage, fortify, dispel, movement, …) with magnitude/duration/chance. | Absent — no spell-effect implementations |
| Disease system | `Game/MagicAndEffects/Effects/Diseases/DiseaseEffect.cs` + ~20 diseases; `EntityEffectManager.CureDisease` | Incubation, periodic stat damage, infection vectors; cures; vampirism/lycanthropy gates. | Absent — no disease/infection/cure runtime |
| Poison system | `Game/MagicAndEffects/Effects/Poisons/PoisonEffect.cs`; `CureAllPoisons` | Timed poison archetype with periodic damage, curable via effects/potions. | Absent — no poison runtime |
| Enchantment-as-effect family | `Game/MagicAndEffects/Effects/Enchanting/*.cs` (strike/held/used/passive triggers) | ~25 item-enchantment effects evaluated by combat/equip code. | Absent — no enchantment triggers |
| Artifact/special effects | `Game/MagicAndEffects/Effects/Special/*.cs` (Azura's Star, Wabbajack, vampirism, lycanthropy, …) | Hardcoded artifact/transformation effects with bespoke logic. | Absent — no artifact/transformation effects |
| Potion recipe matching | `Game/MagicAndEffects/PotionRecipe.cs` — ingredient-set matching | Matches ingredient sets to classic recipes; backs alchemy and `ItemBuilder.CreatePotion`. | Absent — no alchemy/recipe matching |
| Spellmaker / spellbook / potion-maker / item-maker windows | `Game/UserInterfaceWindows/DaggerfallSpellMakerWindow.cs`, `DaggerfallSpellBookWindow.cs`, `DaggerfallPotionMakerWindow.cs`, `DaggerfallItemMakerWindow.cs` | Custom-spell crafting, known-spell list/ready spell, alchemy station, enchanting station. | Absent — no spell-crafting/book, alchemy, or enchanting UI/flow |
| Item data model | `Game/Items/DaggerfallUnityItem.cs` — UID, materials, condition, legacy/custom magic, save round-trip | Single runtime item record with enchantments and quest flags. | Partial — definitions in content-pack payload + Engine catalog via Kit; condition/enchant/quest-flag runtime absent |
| Item container with stacking | `Game/Items/ItemCollection.cs` — UID-keyed ordered container, split/transfer, weight | Auto-stacking container with equip-table-aware removal. | Covered — Kit coordination over Engine-owned Mechanics inventory/equipment catalog and lifecycle (`Kit/Inventory/MechanicsInventoryCoordinator.cs` + `MechanicsInventoryContainerCoordinator.cs`); DFU `ItemCollection`/`ItemEquipTable` not duplicated |
| Item factory | `Game/Items/ItemBuilder.cs` — `CreateItem/CreateWeapon/CreateArmor/CreatePotion` | Mints items from group/template indices with material/variant rolls. | Partial — creation via Engine catalog from content-pack definitions; classic template/material-roll coverage incomplete |
| Item stat/text helpers | `Game/Items/ItemHelper.cs`, `ItemEnums.cs`, `ItemRepairData.cs` | Names, values, armor/weapon stats, repair state, identify text, artifacts. | Partial — display meaning in `Rulesets.Daggerfall/Presentation/DaggerfallInventoryPresentation.cs`; repair/identify/artifact rules absent |
| Equip table + slot rules | `Game/Items/ItemEquipTable.cs` — fixed `EquipSlots`, 2H-vs-shield exclusivity | One-item-per-slot enforcement with two-hand/shield conflicts and equip sounds. | Partial — DF→Kit slot mapping in `Rulesets.Daggerfall/Presentation/DaggerfallInventoryPresentation.cs`; 2H/shield conflict policy unverified |
| Randomized loot tables | `Game/Items/LootTables.cs` — letter tables A–Q + dungeon-type odds | Level-scaled gold + categorized drops for corpses/dungeons. | Partial — table/level-scaling/category ordering in `Rulesets.Daggerfall/Policies/DaggerfallLootPolicy.cs` + `Modules/Loot/DaggerfallCorpseLootModule.cs`; only weapons/armor pools resolve, other pools `Unsupported` |
| Loot presentation/transfer | `LootTables.cs` + `DaggerfallInventoryWindow.cs` (loot mode) | Corpse/dungeon loot as transfer inventory into own collection. | Partial — thin projection in `Rulesets.Daggerfall/Presentation/DaggerfallLootPresentation.cs`; full transfer UX absent |
| Inventory window | `Game/UserInterfaceWindows/DaggerfallInventoryWindow.cs` — grid, equip slots, gold/weight | Grid list, equip/unequip, potion use, wagon access. | Partial — `Rulesets.Daggerfall/Presentation/DaggerfallInventoryPresentation.cs` + `Kit/Inventory/InventoryGridLayout.cs`; paper-doll interactions absent |
| Weapon-item link (equipped → view) | `Game/FPSWeapon.cs` — equipped type/material keyed renderer; bow ammo check reads `PlayerEntity.Items` | First-person weapon renderer keyed off equipped item. | Absent — no equipped-weapon→view binding |

## 3. World simulation, enemies/AI, movement

### World simulation and movement

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| Climate-driven weather simulation | `Game/WeatherManager.cs`, `Game/Weather/Weather.cs` | Advances regional weather from climate + Chronicles tables; drives sky, sunlight, fog, precipitation. | Absent — no weather/climate module |
| Player weather effects | `Game/PlayerWeather.cs`, `Game/AmbientEffectsPlayer.cs` | Rain/snow effects, sounds, exposure around player based on weather and shelter. | Absent — no ambient weather effects |
| Transport modes (foot/horse/cart/ship) | `Game/TransportManager.cs`, `UserInterfaceWindows/DaggerfallTransportWindow.cs` | Foot/Horse/Cart/Ship modes, travel speed/price effects, ownership, ship gating. | Absent — no transport/mount/ship concept |
| Interior/exterior/dungeon transitions | `Game/PlayerEnterExit.cs` | Door transitions into buildings/dungeons and back; tracks inside/dungeon/swimming/holy-place state. | Partial — one fixed dungeon scene via `Content/PrivateersHoldContent.cs` + `content/worldrpg` spatial payload; no general enter/exit system |
| Building directory and lookup | `Game/BuildingDirectory.cs` | Per-location building dictionary linking automap, quest sites, building text. | Absent — no building directory |
| Dungeon automap + discovery | `Game/Automap.cs`, `Game/AutomapModel.cs` | Incremental dungeon geometry reveal as player explores; persists discovery. | Absent — no automap/discovery tracking |
| Exterior/city automap | `Game/ExteriorAutomap.cs` | Town/city footprints, names, quest markers for outdoor locations. | Absent — no exterior map |
| Dungeon light range culling | `Game/DungeonLightHandler.cs` | Enables/disables per-block dungeon lights by player range for performance. | Absent — lighting authority sits with Engine; no dungeon light policy |
| Streaming open world | `Terrain/StreamingWorld.cs`, `Terrain/TerrainHelper.cs` | Streams terrain/location tiles across the Iliac Bay map-pixel grid with teleport support. | Partial — single-scene stepping via `Kit/Controls/SpatialMovementSystem.cs` + `content/worldrpg/imports/privateers-hold/spatial/*`; no tile streaming |
| Terrain sampling and heightfields | `Terrain/DaggerfallTerrain.cs`, `Terrain/TerrainSampler.cs`, `DefaultTerrainSampler.cs`, `NoiseTerrainSampler.cs`, `SimpleTerrainSampler.cs` | Heightmap terrain from MAPS.BSA height data plus per-climate noise samplers. | Import-only — `Daggerfall.Import/Normalization/DungeonNormalizer.cs` normalizes dungeon geometry offline; no runtime terrain sampler |
| Terrain texturing and nature | `Terrain/TerrainTexturing.cs`, `TerrainNature.cs`, `TerrainMaterialProvider.cs`, `Utility/TerrainAtlasBuilder.cs` | Climate ground tiling plus tree/rock/nature billboard scatter. | Absent — appearance comes from admitted content pack, not climate texturing |
| RMB city/town block assembly | `Utility/RMBLayout.cs` | Instantiates outdoor RMB blocks: buildings, ground, flats, lights, city gates. | Import-only — baked into `content/worldrpg/imports/privateers-hold/spatial/` static-mesh payload; no runtime RMB assembler |
| RDB dungeon block assembly | `Utility/RDBLayout.cs` | Instantiates dungeon RDB blocks: doors, lights, flats, water, enemies, treasure, action links. | Import-only — baked into Privateer's Hold spatial payload via `Daggerfall.Import`; random/fixed enemy placement logic has no runtime counterpart |
| Climate/season texture swaps | `Utility/ClimateSwaps.cs` | Remaps texture archive indices by climate/season, with classic quirks (door record exempt). | Absent — no climate swap policy |
| Dungeon texture tables | `DungeonTextureTables.cs` | Selects the 6-entry dungeon texture table by seed (classic vs climate-based variants). | Absent — dungeon textures arrive pre-resolved in the content pack |
| First-person player motor | `Game/PlayerMotor.cs` (+ `Player/PlayerGroundMotor.cs`, `AcrobatMotor.cs`, `ClimbingMotor.cs`) | CharacterController walk/run/jump/crouch/climb/swim with classic timer speed scaling. | Covered — Engine-owned stepping via `engine.Spatial` (`ProposeCharacterStep`, continuation capture/restore) + managed `Look.Integrate` in `Kit/Controls/PlayerInputSystem.cs`; policy in `SpatialMovementSystem.cs`, `PlayerControlState.cs`, `FirstPersonCameraSystem.cs` (over Engine `CameraView`) |
| Levitation/swimming motor | `Game/LevitateMotor.cs` | Replacement motor for levitating flight and swimming vertical movement. | Partial — `CharacterMotion`/`CharacterSupport` plumbed through `Kit/Controls/SpatialMovementSystem.cs`; no levitate vertical-flight policy |

### Enemies, AI, civilians

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| Enemy locomotion + melee AI | `Game/EnemyMotor.cs`, `Game/EnemyAttack.cs` | Pursuit/retreat/strafe state machine with obstacle avoidance, door opening, fly/swim flags. | Partial — Idle/Chase/Attack/Dead in `Rulesets.Daggerfall/Modules/Behavior/DaggerfallEnemyBehaviorModule.cs` via Engine perception + `Kit/Actors/ActorNavigationCoordinator.cs`; no strafing, doors, fly/swim |
| Enemy senses and detection | `Game/EnemySenses.cs` | Sight radius/FOV, hearing radius, stealth-aware detection feeding aggro. | Partial — Engine `IPerceptionService` visibility via `DaggerfallEnemyBehaviorModule.cs`; no DFU stealth/hearing formulas |
| Enemy vocal and combat sounds | `Game/EnemySounds.cs` | Attract/bark/attack/hit/parry/miss sounds per mobile type. | Partial — classic clips admitted via `Content/PrivateersHoldContent.cs`; no per-mobile sound policy |
| Blood and magic hit feedback | `Game/EnemyBlood.cs` | Blood-splash particles by blood index; sparkle feedback for magic hits. | Absent — no hit-particle/blood system (corpse sprite only) |
| Enemy death and corpse | `Game/EnemyDeath.cs`, `Game/EnemyHealth.cs` | Death broadcast, corpse billboard swap, loot/removal handoff. | Partial — corpse sprites via `Content/PrivateersHoldContent.cs` `ReadCorpse`; defeat via `Kit/Actors/ActorsState.cs`; no loot handoff |
| Town civilian wandering | `Game/MobilePersonMotor.cs` (+ `Game/Utility/CityNavigation.cs` navgrid) | Townsfolk driven along city navgrid with idle/seek states. | Absent — patrol/wander explicitly not ported (noted in `DaggerfallEnemyBehaviorModule.cs` header) |
| Mobile civilian identity | `Game/MobilePersonNPC.cs` | Race/gender/face randomization and guard flag for townsfolk billboards. | Absent — no civilian identities |
| Static NPC records | `Game/StaticNPC.cs` | Static RMB/RDB NPC flats with questor injection, relocation hiding, quest removal. | Absent — no static-NPC/questor concept |
| Civilian entity stats | `Game/Entities/CivilianEntity.cs` | Lightweight non-combat entity defaults for townsfolk. | Absent — `Kit/Actors/ActorsState.cs` covers player + hostile actors only |
| Random encounter tables | `Utility/RandomEncounters.cs` | 20-entry per-dungeon-type spawn tables from FALL.EXE plus wilderness logic. | Absent — actors authored per scene in `Content/PrivateersHoldContent.cs`; no encounter tables or level-scaled spawning |
| Enemy test/demo setup | `Game/SetupDemoEnemy.cs` | Inspector/test harness for enemy type, reaction, gender, spawn distance. | Partial — validation sprite pages under `content/validation/sprites/`; no in-engine spawn harness |

## 4. Guilds, factions, quests, dialogue, UI, character creation

### Guilds / factions / banking

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| Guild framework + manager | `Game/Guilds/Guild.cs`, `Game/Guilds/IGuild.cs`, `Game/Guilds/GuildManager.cs` | Base guild class + contract and manager for membership, rank, reputation, service dispatch. | Absent — no guild/faction membership or rank |
| Major guild implementations | `Game/Guilds/FightersGuild.cs`, `MagesGuild.cs`, `ThievesGuild.cs`, `DarkBrotherhood.cs`, `Temple.cs`, `KnightlyOrder.cs` | Per-faction rank requirements, skill gates, advancement, quest-service hooks. | Absent — no faction policy |
| Non-member / service stubs | `Game/Guilds/NonMemberGuild.cs`, `Game/Guilds/Services.cs` | Fallback guild object plus shared service enumerations (training, cure, donation). | Absent — no service-gate logic |
| Bank accounts + transactions | `Game/DaggerfallBankManager.cs` | Regional accounts, deposits/withdrawals, letters of credit, ship/house settlement. | Absent — no currency/banking state |
| Loan issuance + default | `Game/LoanChecker.cs` | Loan tracking, repayment deadlines, default/reputation consequences. | Absent — no loan state |

### Quests

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| Quest runtime (machine) | `Game/Questing/QuestMachine.cs`, `QuestListsManager.cs`, `QuestMCP.cs` | Instantiates, schedules, ticks, serializes live quests; loads compiled packs. | Absent — no quest runtime (`Facts/ProductFacts.cs` covers combat/loot facts only) |
| Compiled quest instance | `Game/Questing/Quest.cs`, `QuestResource.cs`, `QuestAction.cs` | Runtime quest object with tasks, symbols, resources, action state machine. | Absent — no quest instance model |
| Quest script parser | `Game/Questing/Parser.cs` | Parses classic QRC/QBN quest source into tasks/conditions/actions. | Absent — no quest-source parsing |
| Quest tasks + clock | `Game/Questing/Task.cs`, `Clock.cs` | Task transitions with quest-clock timing. | Absent — `Kit/Progression/ProgressionState.cs` tracks XP/level only |
| Quest places + persons | `Game/Questing/Place.cs`, `Person.cs` | Symbolic dungeon/building and NPC resources resolved to world sites. | Absent — no quest anchors |
| Quest foes + items | `Game/Questing/Foe.cs`, `Item.cs` | Spawnable enemy parties and symbolic quest items with placement/tracking. | Absent — foes/items exist only as static combat/loot content |
| Quest messages + symbols | `Game/Questing/Message.cs`, `Symbol.cs` | Text resources with macro expansion and shared symbol table. | Absent — no message/symbol tables |
| Quest action library | `Game/Questing/Actions/` (~80 opcodes: `Say.cs`, `EndQuest.cs`, `GivePc.cs`, `JournalNote.cs`, `CreateFoe.cs`, …) | One class per quest opcode (dialogue, rewards, spawns, reputation, journals). | Absent — no quest opcodes |
| Quest macro expansion | `Utility/QuestMacroHelper.cs` | Quest-scoped `%` macro resolution layered over global macro helper. | Absent — no macro expansion |

### Dialogue / text / localization

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| NPC talk engine | `Game/TalkManager.cs` | Rumor/news/work/quest dialogue tree, reactions, talk-session state. | Absent — no dialogue engine |
| Talk mod-protocol compat | `Game/TalkManagerMCP.cs` | Mod-message-path exposure of talk topics/responses. | Absent — no talk surface |
| Text table manager | `Game/TextManager.cs` | Loads TEXT.RSC string tables; serves UI/quest/book strings. | Absent — no string-table loading |
| Global macro expansion | `Utility/MacroHelper.cs`, `MacroDataSource.cs`, `IMacroContextProvider.cs` | `%pcn`/`%g`/date/faction macros expanded against player/world context. | Absent — outcome strings hardcoded in `Rulesets.Daggerfall/Presentation/DaggerfallOutcomePresentation.cs` |
| Text provider chain | `Utility/TextProvider.cs`, `DefaultTextProvider.cs`, `FallbackTextProvider.cs` | Pluggable text-source chain with mod override and classic fallback. | Absent — no provider chain |
| Grammar + string import | `Localization/Grammar.cs`, `DefaultGrammarRules.cs`, `DaggerfallStringTableImporter.cs` | Pluralization/grammar rules and editor string-table import. | Absent — no localization pipeline |
| Localized books | `Game/LocalizedBook.cs` | Book-record wrapper resolving localized page text for reader window. | Absent — no book text |

### UI framework, windows, character creation

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| UI root + messages + factory | `Game/DaggerfallUI.cs`, `DaggerfallUIMessages.cs`, `UserInterfaceWindows/UIWindowFactory.cs`, `UserInterface/UserInterfaceManager.cs` | Window stack/mode manager, `UIMessage` routing, factory, base input/render loop. | Partial — structured values + action wire only (`Kit/Presentation/UiValueBuilder.cs`, `Kit/Presentation/PresentationState.cs`, `Rulesets.Daggerfall/Presentation/DaggerfallUiAction.cs`) |
| Base window + widget kit | `Game/UserInterfaceWindows/DaggerfallBaseWindow.cs`, `Game/UserInterface/Panel.cs`, `Button.cs`, `TextLabel.cs`, `ListBox.cs`, `PaperDoll.cs` | Retained-mode classic widgets (panels, buttons, labels, scrollers, paper doll, HUD components). | Partial — no widget kit; thin DOM projection via `src/ui`, `Kit/Presentation/SpriteAtlasAdapter.cs` |
| HUD | `Game/UserInterfaceWindows/DaggerfallHUD.cs` | In-world HUD: vitals, compass, crosshair, active spells, interaction mode, escorts. | Partial — resource/XP/outcome projection in `Rulesets.Daggerfall/Presentation/DaggerfallHudProjection.cs` |
| Inventory window | `Game/UserInterfaceWindows/DaggerfallInventoryWindow.cs` | Grid inventory, equipment slots, drag/drop, wagon access, info popups. | Partial — grid + equipment projection + TS view (`Rulesets.Daggerfall/Presentation/DaggerfallInventoryPresentation.cs`, `src/ui/inventory.ts`) |
| Character sheet window | `Game/UserInterfaceWindows/DaggerfallCharacterSheetWindow.cs` | Attributes, skills, level/XP, reputation, history display. | Partial — read-only projection + TS view (`Rulesets.Daggerfall/Presentation/DaggerfallCharacterPresentation.cs`, `src/ui/character.ts`) |
| Outcome / message popups | `Game/UserInterfaceWindows/DaggerfallMessageBox.cs`, `DaggerfallQuestPopupWindow.cs`, `DaggerfallPopupWindow.cs` | Modal message, quest-offer, generic popup text display. | Partial — message text only, no windows (`Rulesets.Daggerfall/Presentation/DaggerfallOutcomePresentation.cs`, `src/ui/loot.ts`) |
| Spellbook + magic crafting | `Game/UserInterfaceWindows/DaggerfallSpellBookWindow.cs`, `DaggerfallSpellMakerWindow.cs`, `DaggerfallUseMagicItemWindow.cs` | Known spells/casting, custom spell maker, magic-item activation. | Absent — no spellbook/maker state |
| Rest window | `Game/UserInterfaceWindows/DaggerfallRestWindow.cs` | Timed rest/loiter with encounter, healing, spell-recharge outcomes. | Absent — no rest mechanic |
| Travel system windows | `Game/UserInterfaceWindows/DaggerfallTravelMapWindow.cs`, `DaggerfallTransportWindow.cs`, `DaggerfallTravelPopUp.cs`, `DaggerfallTeleportPopUp.cs` | Region travel map, ship/horse/cart choice, teleport confirmation. | Absent — no travel UI |
| Guild/bank/merchant service windows | `Game/UserInterfaceWindows/DaggerfallGuildService*.cs`, `DaggerfallBankingWindow.cs`, `DaggerfallMerchantServicePopupWindow.cs`, `DaggerfallMerchantRepairPopupWindow.cs` | Training, donations, disease cure, banking, merchant repair menus. | Absent — no service windows |
| Talk / quest / tavern / court windows | `Game/UserInterfaceWindows/DaggerfallTalkWindow.cs`, `DaggerfallQuestJournalWindow.cs`, `DaggerfallQuestOfferWindow.cs`, `DaggerfallTavernWindow.cs`, `DaggerfallCourtWindow.cs` | Talk topics, journal log, quest offers, tavern rooms, court fines. | Absent — no dialogue/quest UI |
| Trade / automap / book / craft windows | `Game/UserInterfaceWindows/DaggerfallTradeWindow.cs`, `DaggerfallAutomapWindow.cs`, `DaggerfallExteriorAutomapWindow.cs`, `DaggerfallBookReaderWindow.cs`, `DaggerfallPotionMakerWindow.cs`, `DaggerfallItemMakerWindow.cs`, `DaggerfallDaedraSummonedWindow.cs` | Shop trade, interior/exterior automaps, book reader, alchemy, Daedric item maker, summoning days. | Absent — shop/automap/crafting UI missing (`src/sprite-ui` is a sprite tool, not game UI) |
| Character creation chain + wizards | `Game/UserInterfaceWindows/CreateChar*.cs` (race, gender, class Q&A, custom class, bonuses, face, bio, summary), `DaggerfallStartNewGameWizard.cs`, `DaggerfallUnitySetupGameWizard.cs` | Full classic chargen flow plus new-game/setup wizards. | Absent — no chargen; fixed `PrivateersHoldContent.cs` player definition only |

## 5. App bootstrap, saves, audio, rendering, formats, utility

### App bootstrap and saves

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| Content-reader singleton bootstrap | `DaggerfallUnity.cs` | Central singleton wiring all Arena2 readers and global game state. | Partial — session composition instead via `Host/WorldRpgProduct.cs` + `Rulesets.Daggerfall/DaggerfallSession.cs` |
| Application paths and Arena2 setup | `DaggerfallUnityApplication.cs` | App paths, persistent-data setup, Arena2 source resolution. | Partial — source hashes/paths recorded offline in `content/worldrpg/imports/privateers-hold/import-manifest.json`; no runtime path resolver |
| Top-level game orchestration | `Game/GameManager.cs` | Singleton owning player, streaming world, dungeon/interior setup, per-frame dispatch. | Partial — admitted-update stepping in `Rulesets.Daggerfall/DaggerfallSession.cs` + `Host/WorldRpgProduct.cs` |
| UI/game state machine | `Game/StateManager.cs` | Event-driven `StateTypes` stack (UI windows, game modes) with change events. | Partial — quiescent-boundary state capture in `DaggerfallSession.cs`; no UI state stack |
| Serializable save/load pipeline | `Game/Serialization/SaveLoadManager.cs`, `SerializablePlayer.cs`, `SerializableEnemy.cs`, `SerializableLootContainer.cs`, `SerializableActionDoor.cs`, `SerializableGameObject.cs`, `SerializableStateManager.cs` | Save/load logic plus per-object snapshots for player, enemies, loot, doors, state. | Partial — Engine-owned envelope via `ProductStateStore` in `Host/WorldRpgSaveStore.cs` + `Rulesets.Daggerfall/DaggerfallSavePayload.cs` (player/actors/inventory/corpses/cooldowns); no per-door/enemy DFU parity |
| Classic save records | `API/Save/SaveTree.cs`, `SaveGames.cs`, `CharacterRecord.cs`, `ItemRecord.cs`, `SaveVars.cs`, `GuildMembershipRecord.cs` | Low-level classic SAVE format records: tree, character, items, guilds, global vars. | Partial — equivalent meaning in `Rulesets.Daggerfall/DaggerfallSavePayload.cs` via Engine persistence, not classic binary shape |

### Audio

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| Contextual music director | `Game/SongManager.cs` | MIDI songs selected by climate, location, season, time of day. | Absent — Engine `Audio` owns clips/voices/playback, not direction; no product music director (nearest `Rulesets.Daggerfall/DaggerfallTuning.cs` volume/pitch only) |
| Song catalog enum | `SongFiles.cs` | Enum of all songs in MIDI.BSA. | Absent — no MIDI.BSA catalog (`Import/Arena2/SoundArchiveDecoder.cs` covers DAGGER.SND only) |
| Sound-clip catalog enum | `SoundClips.cs` | Enum identifying classic sound clips. | Import-only — `Import/Arena2/SoundArchiveDecoder.cs` numeric-BSA clips; no runtime clip catalog |
| Sound importer | `SoundReader.cs` | Imports classic sounds into Unity AudioClips. | Import-only — `Import/Arena2/SoundArchiveDecoder.cs` decodes offline; playback via Engine Audio (`Presentation/PrivateersHoldAppearance.cs`) |
| Ambient audio/visual effects | `Game/AmbientEffectsPlayer.cs` | Random-interval ambient sounds and timed visuals (e.g. lightning). | Absent — nearest `Presentation/PrivateersHoldAppearance.cs` one-shot presentation audio |

### Rendering and assets

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| Material/image importer | `MaterialReader.cs` | Imports classic images into Unity materials. | Import-only — `Import/Arena2/TextureArchiveDecoder.cs` + `PaletteDecoder.cs` + `Normalization/SpriteAtlasNormalizer.cs` |
| Mesh importer | `MeshReader.cs` | Imports classic 3D models into Unity Meshes. | Import-only — `Import/Arena2/Arch3dDecoder.cs` + `Arena2/RdbDecoder.cs` (source units retained) |
| Texture archive reader | `Utility/TextureReader.cs` | Loads TEXTURE.xxx archives as Texture2D. | Import-only — `Import/Arena2/TextureArchiveDecoder.cs` + `Normalization/MediaManifestNormalizer.cs` |
| General image reader | `Utility/ImageReader.cs` | Uniform classic-image reader for direct-to-texture use (UI etc.). | Import-only — `Import/Arena2/ImgDecoder.cs` + `Arena2/PaletteDecoder.cs` |
| Model combiner | `Utility/ModelCombiner.cs` | Merges classic model data into combined meshes. | Import-only — `Import/Normalization/DungeonNormalizer.cs` + `DungeonSpatialPublication.cs` merge blocks offline |
| Retro rendering mode | `Utility/RetroRenderer.cs`, `RetroPresentation.cs` | Retro-mode render settings plus final viewport presentation. | Absent — rendering is Engine-owned; nearest `Presentation/PrivateersHoldAppearance.cs` |
| Texture atlases | `Utility/TextureAtlasBuilder.cs`, `TerrainAtlasBuilder.cs` | Archive texture atlases; terrain atlas export/load. | Import-only — `Import/Normalization/SpriteAtlasNormalizer.cs` + `Arena2DungeonMediaPublication.cs` + `content/worldrpg/imports/privateers-hold/media/classic/manifest.json` |
| Floating origin | `Utility/FloatingOrigin.cs` | Re-centers world at map-pixel boundaries for float precision. | Absent (Engine-unused) — upstream `IWorldOriginService` rebase mechanism exists but product never calls it (single scene); nearest `Kit/Controls/SpatialMovementSystem.cs` |
| Camera clear management | `Utility/CameraClearManager.cs` | Switches camera clear mode between interior and exterior. | Absent — nearest `Kit/Controls/FirstPersonCameraSystem.cs`; no interior/exterior switch |

### Source formats (Arena2 readers → Import decoders)

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| 3D mesh archive format | `API/Arch3dFile.cs` | Reads ARCH3D.BSA mesh data. | Import-only — `Import/Arena2/Arch3dDecoder.cs` |
| Dungeon/city block format | `API/DFBlock.cs` (+ `API/BlocksFile.cs`) | Type-safe struct over native RDB/RMB block records. | Import-only — `Import/Arena2/RdbDecoder.cs` + `Normalization/DungeonNormalizer.cs` |
| Location and region records | `API/DFLocation.cs`, `API/DFRegion.cs` | Cities/dungeons/buildings and region location tables. | Import-only — `Import/Arena2/MapsDecoder.cs` (location facts + linked RDB refs) |
| World-map archive | `API/MapsFile.cs` | Reads MAPS.BSA: region enumeration, layouts, climates. | Import-only — `Import/Arena2/MapsDecoder.cs` |
| Faction data | `API/FactionFile.cs` | Reads FACTION.TXT faction records. | Absent — only incidental faction-byte handling in `Content/DaggerfallBaseContent.cs`; no faction table |
| Item templates | `API/ItemsFile.cs` | Native FALL.EXE item templates. | Partial — `Content/DaggerfallBaseContent.cs` (`ReadItems`) + `content/worldrpg/content-packs/daggerfall.base.pack.json` |
| Monster data | `API/MonsterFile.cs` | Reads MONSTER.BSA monster records (WIP upstream). | Partial — `Import/Arena2/MobileSourceMetadata.cs` (DFU `EnemyBasics`-derived mobile facts) + base-pack actors |
| Reader-to-Unity bridge | `Utility/ContentReader.cs` | Interface between API readers and Unity (`MapSummary` etc.). | Import-only — `Import/Normalization/DungeonNormalizer.cs` + `Normalized/NormalizedContracts.cs` bridge to normalized packs |

### Shared utility and input

| Feature | DFU code ref | Description | rusty-dagger coverage |
|---|---|---|---|
| Daggerfall calendar clock | `Utility/DaggerfallDateTime.cs` | Game calendar with fixed 30-day months, seasons, timescale. | Absent — no calendar/clock; nearest `Rulesets.Daggerfall/DaggerfallState.cs` (no time/date fields) |
| Classic RNG reimplementation | `API/DFRandom.cs` | Reimplements classic random sequences (e.g. building names) for parity. | Import-only — `Import/Arena2/ClassicDaggerfallRandom.cs` offline only; runtime uses Engine `IRandomService` in `DaggerfallSession.cs` |
| Action input bindings | `Game/InputManager.cs` | Singleton mapping game actions to keys. | Partial — `Kit/Controls/PlayerInputSystem.cs` + `PlayerControlState.cs` (Engine-admitted input, no DFU key-bind schema) |
| Controls settings + hotkeys | `Game/ControlsConfigManager.cs`, `Game/HotkeySequence.cs` | Controls-settings bindings and multi-key hotkey detection. | Partial — adjustable values in `content/worldrpg/tuning/daggerfall.defaults.tuning.json`; no hotkey-sequence detector |
| Mouse look | `Game/PlayerMouseLook.cs` | Mouse-look camera with smoothing setting. | Covered — `Kit/Controls/FirstPersonCameraSystem.cs` over Engine `CameraView`; look integration via managed `Look.Integrate` in `Kit/Controls/PlayerInputSystem.cs` |
| Player activation/interaction | `Game/PlayerActivate.cs` | Raycast activation of objects, loot-spawn events. | Partial — corpse `IsInteractable` policy in `Modules/Loot/DaggerfallCorpseLootModule.cs`; no raycast activation |

## Upstream Engine cross-check (second pass)

Cross-checked against the paired Engine checkout (`csharp/Rusty.Engine` SDK surface, `docs/csharp-capabilities.md`).
Engine paths below are relative to that checkout; product paths relative to this repo.
Verdict vocabulary: **Proper use** (product consumes the upstream mechanism as designed),
**Engine-unused** (hook exists, product does not wire it yet — not a duplication),
**Deliberate-local** (product owns a narrower slice by design; upstream offers the substrate, not the policy).

### The stats question, answered directly

Yes — upstream covers stat *mechanisms*: `csharp/Rusty.Engine/Mechanics/ExactStats.cs`
(`ExactStatContribution` Add/Scale/Minimum/Maximum, stacking groups, evaluated decisions) and
`Effects.cs` (active-effect stacking: IndependentByProvenance/Refresh/Replace).
But rusty-dagger barely touches them: `Kit/Actors/ActorsState.cs` (`ReadStat`) evaluates with
`Array.Empty<ExactSource>()` — base values only — and the sole contribution writer is level-up HP
accumulation (`Rulesets.Daggerfall/DaggerfallRewardReactions.cs` via `DaggerfallLevelUpHealthSource`).
So DFU's live/permanent `AssignMods` semantics (`DaggerfallStats`/`DaggerfallSkills`) remain genuinely
**Partial (Engine-unused)**, not duplicated: the modifier machinery is waiting upstream for a future
buff/equipment/career-mod policy, and no less-capable local copy of it exists.

### Consumed upstream mechanisms (proper use — no duplication found)

| Upstream mechanism | Engine location | rusty-dagger consumer | Verdict |
|---|---|---|---|
| Exact stats/tracks, guarded mutations | `csharp/Rusty.Engine/Mechanics/` (`ExactStats.cs`, `ExactTracks.cs`, `ExactStatTrackState.cs`, `MechanicsValues.cs`) | `Rulesets.Daggerfall/Content/DaggerfallMechanicsState.cs` (identities, bases, vitality policy), `Kit/Actors/ActorsState.cs` (reads, spend/restore/set receipts) | Proper use — product owns identities/policy, Engine owns checked invariants |
| Item/inventory/equipment catalog + lifecycle | `csharp/Rusty.Engine/Mechanics/` (`Item*.cs`, `Inventory.cs`, `Equipment.cs`) | `Kit/Inventory/MechanicsInventoryCoordinator.cs`, `MechanicsInventoryContainerCoordinator.cs` | Proper use — DFU `ItemCollection`/`ItemEquipTable` semantics ride the Engine world, not a port |
| Deterministic random streams | `IRandomService` (`Random` family) | `Rulesets.Daggerfall/DaggerfallSession.cs` (`engine.Random`); loot/combat draws | Proper use — `Import/Arena2/ClassicDaggerfallRandom.cs` stays offline-only; no runtime RNG fork |
| Visibility queries | `IPerceptionService` (`Perception` family) | `Modules/Behavior/DaggerfallEnemyBehaviorModule.cs`, `Modules/Combat/DaggerfallMeleeTargeting.cs`, `DaggerfallSession.cs` (`CastRay`) | Proper use — product keeps AI/targeting policy, Engine answers visibility |
| Character stepping + continuation | `ISpatialService` (`Spatial` family) | `Kit/Controls/SpatialMovementSystem.cs` (`ProposeCharacterStep`, continuation capture/restore), `Kit/Actors/ActorNavigationCoordinator.cs` | Proper use — no custom physics or second mover |
| Look integration (managed helper) | `csharp/Rusty.Engine/Look.cs` (`Look.Integrate`, `Look.Diagnose`) | `Kit/Controls/PlayerInputSystem.cs` | Proper use — radian state via the helper, not a local look model |
| Camera retain/update/clear | `ICameraViewService` (`CameraView` family) | `Kit/Controls/FirstPersonCameraSystem.cs` | Proper use |
| UI projection transport | `IUiService` (`Ui` family, `runtime-ui` crate) | `Rulesets.Daggerfall/Presentation/DaggerfallHudProjection.cs`, `SpriteWorkbenchProduct.cs` | Proper use — thin DOM projections only, no gameplay state in TS |
| Audio clips/voices/playback | `Audio` family | `Rulesets.Daggerfall/Presentation/PrivateersHoldAppearance.cs` | Proper use — one-shot presentation audio; direction (music director) correctly stays product-side and Absent |
| Sprite atlas + playback resources | `Graphics` family | `PrivateersHoldAppearance.cs`, `Kit/Presentation/SpriteAtlasAdapter.cs`, `SpriteWorkbenchProduct.cs` | Proper use — billboard sprites via atlas playback, not a UI renderer |
| Admitted content reads | `Content` family (`AuthoredContent`) | `DaggerfallSession.cs` (`engine.Content`), `PrivateersHoldContent.cs` over `ProductContent` | Proper use — runtime reads admitted packs, never source files |
| Bounded persistence blobs/stores | `Persistence` family (`ProductStateStore`) | `Host/WorldRpgSaveStore.cs` | Proper use — opaque envelope + ruleset payload codec |
| Product-owned state machines | `Rusty.Engine.StateMachine` namespace | `Modules/Behavior/DaggerfallEnemyBehaviorModule.cs` (Idle/Chase/Attack/Dead) | Proper use — Engine definitions, product transitions |
| Normalized input + lifecycle fences | `runtime-input`, `runtime-lifecycle` crates | `Kit/Controls/PlayerInputSystem.cs` → `PlayerControlState.cs` | Proper use — Engine normalizes delivery; product maps to control state (layering, not duplication) |

### Unused upstream hooks (gaps that are NOT missing-engine requests)

| Upstream hook | Why unused | What would wire it |
|---|---|---|
| `ExactStatContribution` stacking (Add/Scale/Min/Max) beyond level-up HP | No buff/equipment/career-mod policy exists yet | Magic effects, enchantments, or career mods writing `ExactSource`s |
| `Effects.cs` active-effect instances/stacking | No magic runtime at all | A future effect policy (broker/manager are product-side concepts per capabilities map) |
| `ContinuousStats`/`ContinuousTracks` | Daggerfall's integer stats map to Exact; nothing needs continuous values | Unforeseen analog subsystem; likely never |
| `IWorldOriginService` rebase | Single Privateer's Hold scene; no streaming | Streaming world / floating-origin need |
| `Animation` rig/clip graphs | Product presents billboard sprites via Graphics playback | Skeletal/rigged presentation need (none on the Daggerfall roadmap) |
| `IMotionService` native dynamics | Character stepping via Spatial suffices | Physics-body gameplay need |
| `ContentStore` durable generations | Bundle assembly lives in Kit (`GameComposition.cs` over `ProductContent` files) | Durable multi-generation content workflow |

### Near-duplicate audits (checked, cleared)

- **Kit `ActorsState` vs Engine `Entities/EntityWorld`.** `EntityWorld` is product-owned component storage (revisions, batches, snapshots); `ActorsState` is a deliberately small lifecycle map over Mechanics identities. Non-use of `EntityWorld` is allowed by the capabilities map ("a `using` declaration is enough to ignore a helper that is irrelevant"), not a fork. Revisit only if actor storage needs revisions/batches/snapshots.
- **Kit `GameComposition` vs Engine `ContentStore`.** Different seams: Kit assembles bundles/packs/tuning ("Bundle assembles"); ContentStore plans/publishes durable generations. No overlap.
- **Graphics sprite playback vs `Animation` rigs.** Different presentation kinds (billboard atlas frames vs rigged clips). Choosing Graphics is correct for Daggerfall sprites.
- **Compiled `DaggerfallFormulaPolicy` vs retired native Rules families.** The capabilities map retires native Rules/Mechanics/Resolution/StateMachine *service* families; managed `Mechanics` values + compiled product policy is the sanctioned shape. No violation.
- **No second RNG, clock, scheduler, or renderer found.** `ClassicDaggerfallRandom` is import-only; there is no product calendar/clock to rival anything upstream; `rusty dev`/CoreCLR staging is the only loop. The Engine-boundary reminder in the legend holds.

## Coverage summary (where rusty-dagger stands)

- Strongest live coverage: attribute/vital/level formulas (`Policies/DaggerfallFormulaPolicy.cs`), melee hit/damage intake (`Modules/Combat/CombatModule.cs` + `DaggerfallMeleeTargeting.cs`), container/stack/transfer inventory (`Kit/Inventory/*`), corpse-loot policy subset (`Policies/DaggerfallLootPolicy.cs` + `Modules/Loot/DaggerfallCorpseLootModule.cs`), first-person input/camera/locomotion stepping (`Kit/Controls/*`), save envelope + payload (`Host/WorldRpgSaveStore.cs` + `DaggerfallSavePayload.cs`), and thin HUD/character/inventory/loot/outcome projections (`Rulesets.Daggerfall/Presentation/*` + `src/ui`).
- Strongest offline coverage: Arena2 decoding and normalized Privateer's Hold publication (`Daggerfall.Import/Arena2/*`, `Normalization/*`, `Publication/*`, `content/worldrpg/imports/privateers-hold/**`) with hashes/provenance; weapon visuals, dungeon geometry, and audio clips arrive pre-resolved in packs rather than assembled at runtime.
- Largest gaps (all Absent above): the entire magic/effect runtime (~100+ school effects, broker/manager/bundles, diseases/poisons/enchanting/artifacts, spellbook/maker, alchemy), quest runtime/parser/actions/resources, guilds/factions/banking/loans, dialogue/talk/text/macros/localization/books, travel/transport/rest, civilians/encounters, weather/climate, automaps, and character creation. These DFU references are the donor list for future ruleset/content work, not a commitment order.
- Boundary reminder: rendering, spatial mechanisms, audio/midi playback substrate, and persistence substrate stay Engine-owned (see `Upstream Engine cross-check` for the consumed/unused inventory). rusty-dagger gaps that need a missing safe Engine contract should become one narrow purpose-neutral `rusty-engine` request with an honest stop at that boundary — never downstream Rust, a C# reimplementation of Engine machinery, or a fake proof path.

## How to use and maintain this map

- When starting a feature, read the DFU reference first for classic shape, then check `Upstream Engine cross-check` for an existing mechanism before building: place reusable mechanisms in `WorldRpg.Kit`, Daggerfall policy in `WorldRpg.Rulesets.Daggerfall`, offline quirks in `Daggerfall.Import`, authored values in `content/worldrpg`, and selection/lifecycle in `WorldRpg.Host`.
- Keep Kit free of Daggerfall/Arena2/Privateer's Hold/DFUnity vocabulary; keep content meaning out of the Host except at its explicit catalog/default seam.
- Update the row's coverage cell when a feature lands (link the new owner file); keep the DFU reference column stable so the donor stays findable.

<!-- SURVEY-TABLES -->
