# Magic and effects inventory

**Status:** bounded task-preparation inventory, 2026-09-10. This expands the
magic rows in the coverage ledger (especially F025–F036 and F101) into named
behavior families. It is an input to task drafting, not a set of Den tasks or
an implementation claim.

**Behavior target:** original Daggerfall behavior is the default. Daggerfall
Unity (DFU) is the semantic donor. Unity object topology, reflective discovery,
the donor singleton broker/manager, Unity windows and widgets, and Unity
import/rendering bridges are outside the target unless a row below explicitly
calls out a behavior that still needs an adapted product owner. DFU-only/demo
code remains visible so it does not silently disappear from planning.

**Donor revision:** `/home/research/daggerfall-unity` at
`81e89e90c27bc3c1a7a61871e545fad129174dec`. DFU paths in this document are
relative to `Assets/Scripts` in that checkout. The direct enumeration found 153
`.cs` files below `Game/MagicAndEffects/Effects`: Alteration 7, Destruction 28,
Diseases 22, Enchanting 24, Illusion 10, Mysticism 10, Poisons 1, Restoration
27, Special 13, and Thaumaturgy 11.

## How to read this inventory

The `MAG-*` identifiers are stable inventory family/leaf IDs. Each exhaustive
file entry uses the unique leaf form `MAG-<family>.<ClassName>` (for example,
`MAG-005.DamageHealth`); if a future class name collides, append the normalized
path segment rather than renumbering an existing ID. A listed file is covered by
its leaf ID and the family ID on its subsection; an ID does not mean that the
file should become one class, ticket, or port. `runtime target` means that the named
behavior belongs in the compiled Daggerfall ruleset when its domain operations
are available. `base/helper` means shared donor behavior that should be
re-expressed as a Rusty contract or ruleset helper rather than copied as an
inheritance requirement. `DFU-only/demo` and `DFU-only/WIP` are retained for
assessment and are not baseline original-game commitments.

This inventory records file identity, broad behavior, dependency families, and
representative source anchors. It is **not a full semantic audit**: individual
formula ranges, classic rounding, every target restriction, every caller,
source-record variant, and every save edge still need focused decisions when a
task is drafted. The uncertainties at the end are task-local questions rather
than assertions that an entire area is missing.

## Dependency spine and ownership

| ID | Family and planning link | Dependencies to settle | Rusty ownership and disposition | Donor anchors |
| --- | --- | --- | --- | --- |
| MAG-001 | Effect contract and lifecycle (F028) | Stable effect key/source identity; `Start`, restore/resume, constant state, magic-round, end; incumbent matching, stacking, cancellation and cleanup | Kit may coordinate reusable active-effect identity and lifecycle over verified Engine mechanisms. Daggerfall owns effect meaning and policy. Adapt; no required donor inheritance tree. | `Game/MagicAndEffects/EntityEffect.cs` (`IEntityEffect`, `BaseEntityEffect`); `IncumbentEffect.cs`; `MagicAndEffectsEnums.cs`; `MagicAndEffectsStructs.cs` |
| MAG-002 | Bundle, registry and cast admission (F025–F029, F014) | Compiled effect definitions and stable keys; normalized spell records; bundle versus live instance; caster/target, ready/cast, costs, target shape, chance, saves, immunity, absorption and reflection | Kit can provide typed coordination; Daggerfall interprets records and decides rules. Adapt; no reflective broker, `Activator`, singleton, or runtime assembly loading. | `EntityEffectBroker.cs` (`RegisterEffectTemplate`, `InstantiateEffect`, `MapClassicKey`); `EntityEffectBundle.cs`; `LiveEffectBundle.cs`; `EntityEffectManager.cs` (`AssignBundle`, `SetReadySpell`, `Update`) |
| MAG-003 | Time, active state and save integration (F031–F034, F112, F136) | One admitted game-time model; minute/day ticks; rest/travel elapsed catch-up; periodic application exactly once; cure/expiry; effect-specific state and reconstruction | Daggerfall session/time policy owns advancement and save meaning; Kit supplies only reusable state coordination. Extend `DaggerfallSession.Update`, `DaggerfallSavePayload`, and existing save envelope; no second clock or object-graph serialization. | `EntityEffectManager.Update`; `Effects/Diseases/DiseaseEffect.cs`; `Effects/Poisons/PoisonEffect.cs`; `Game/Questing/Clock.cs`; `Game/Questing/Place.cs` |
| MAG-004 | Alteration: resistance, conditions and magical movement (F030, F022, F062) | Stat/resistance sources; condition flags; shield damage; climb/jump/slowfall/water breathing and target rules over spatial stepping | Daggerfall owns keys, magnitude/chance, and eligibility. Kit/Engine provide stat/track and spatial coordination; extend existing movement and actor owners. Adapt. | `Effects/Alteration/*.cs`; `Player/` motors for donor behavior; `Kit/Controls/SpatialMovementSystem.cs`; `Kit/Actors/ActorNavigationCoordinator.cs` |
| MAG-005 | Destruction: direct and continuous damage (F030, F011, F014) | Health/fatigue/magicka sinks, continuous rounds, disintegrate, aggro and target/caster ordering | Daggerfall policy invokes the existing guarded combat/vital-track operations. Kit may coordinate effect instances; no duplicate damage path. Implement/adapt. | `Effects/Destruction/ContinuousDamage*.cs`; `Damage*.cs`; `Disintegrate.cs`; `Modules/Combat/CombatModule.cs` |
| MAG-006 | Destruction: drain and transfer (F030, F014) | Permanent/live attribute distinction; drain floor, healing/removal, same-kind accumulation; transfer target drain plus caster restoration | Daggerfall owns classic formulas and semantics; Engine ExactStat contributions/tracks are the candidate substrate through Kit. Implement/adapt; inspect health/fatigue/magicka and caster absence paths separately. | `Effects/Destruction/DrainEffect.cs`; `Drain*.cs`; `TransferEffect.cs`; `Transfer*.cs`; `Effects/Restoration/HealEffect.cs` |
| MAG-007 | DFU-only vampiric-fortify helper | Whether any original behavior actually needs a permanent range-bound link | Keep visible as a donor experiment only. Exclude from baseline unless a separate source decision adopts it; do not create a provisional runtime wrapper. | `Effects/Destruction/VampiricFortifyEffect.cs` explicitly says work in progress and unused by any effect |
| MAG-008 | Restoration: cure, fortify, heal and absorption (F030, F031, F032) | Cure scopes; live/permanent stat restoration; health/fatigue/magicka bounds; duration/magnitude/stacking; spell absorption | Daggerfall owns policy and effect keys. Kit coordinates Engine stat/track sources and active instances; existing formula/recovery policy remains the ruleset owner. Implement/adapt. | `Effects/Restoration/Cure*.cs`; `FortifyEffect.cs`; `HealEffect.cs`; `Regenerate.cs`; `SpellAbsorption.cs` |
| MAG-009 | Illusion: concealment, light and morph (F030, F034) | Concealment flag composition/cleanup, normal versus true variants, light presentation, morph caller and transformation state | Daggerfall owns concealment meaning and spell policy. Engine Graphics/appearance presents admitted effects; no Unity `Light`, scene parenting, or motor bridge. `MorphSelf` and transformation need explicit caller decisions. Adapt. | `Effects/Illusion/ConcealmentEffect.cs`; concealment variants; `LightNormal.cs`; `MorphSelf.cs`; `PrivateersHoldAppearance.cs` |
| MAG-010 | Mysticism: language, item, dispel, lock/open, silence, soul trap and teleport (F030, F034) | Each world/item operation and failure result; dispel ordering; door identity; soul/item identity; teleport destination and time/world consequences | Daggerfall owns records and rules. Route doors/items/world/time through the existing Kit/ruleset owners when those operations land; no magical parallel world or inventory path. Implement/adapt. | `Effects/Mysticism/*.cs`; `PlayerActivate.cs`; `Kit/Inventory/MechanicsInventoryCoordinator.cs`; `GameComposition.cs` |
| MAG-011 | Thaumaturgy: charm, pacify, detection, identify, levitate, reflection, resistance and water walking (F030, F034) | Enemy disposition and attack break; detection projection; identify transaction; levitation/water support; spell reflection/resistance ordering | Daggerfall owns target classes and outcomes. Use existing perception, spatial, inventory and thin presentation owners; no HUD compass/window or motor singleton port. Implement/adapt. | `Effects/Thaumaturgy/*.cs`; `EnemySenses.cs`; `LevitateMotor.cs`; `DaggerfallInventoryPresentation.cs` |
| MAG-012 | Classic diseases (F031) | Infection eligibility, incubation, daily matrix effects, permanent versus finite disease, cure, overlapping instances and save/restore | Daggerfall owns disease data and policy; Kit coordinates active state/time. Source table is a donor anchor, not a claim that every value has been independently re-audited. Implement/adapt. | `Effects/Diseases/DiseaseEffect.cs`; 17 classic disease classes; `EntityEffectManager.CureDisease`; `Game/Formulas/FormulaHelper.cs` |
| MAG-013 | Infection and transformation stages (F031, F034, F086 selective) | Vampire/lycanthrope stage timers, race/state replacement, quest start/end, faction, spells, world relocation, full cure and save data | Daggerfall ruleset owns transformation meaning. Quest/world dependencies are selective and must use their future stable operations. Exclude the DFU video-player and Unity scene topology; preserve required original story/cinematic behavior under SUP-18 through an adapted Engine/media path. | `Effects/Diseases/LycanthropyInfection.cs`; `VampirismInfection.cs`; `WereboarInfection.cs`; `WerewolfInfection.cs`; `Effects/Special/VampirismEffect.cs`; `LycanthropyEffect.cs` |
| MAG-014 | Poison archetype (F032) | Weapon poison/drug identity; minute onset and duration; periodic health/vital/stat effects; completion, positive-stat cleanup, cure, item consumption and save | Daggerfall owns poison tables and policy; Kit coordinates active effect/time state. Reuse item use/strike and guarded track/stat operations. Implement/adapt. | `Effects/Poisons/PoisonEffect.cs`; `Effects/Restoration/CurePoison.cs`; `FormulaHelper.InflictPoison` |
| MAG-015 | Enchanting: held/passive effects (F033, F037, F041) | Equip/unequip source identity; stat/skill/reaction/weight/armor changes; item condition and magic-round cadence; stacking/exclusivity | Kit coordinates Engine equipment/inventory and effect sources. Daggerfall owns enchantment definitions, restrictions, values and policy. Adapt; no separate item store. | `Effects/Enchanting/EnhancesSkill.cs`, `GoodRepWith.cs`, `StrengthensArmor.cs`, etc.; `Kit/Inventory/MechanicsInventoryCoordinator.cs`; `MechanicsEquipmentCoordinator` |
| MAG-016 | Enchanting: use/strike/held triggers and item callbacks (F033, F011, F017) | Callback context (`Equipped`, `Used`, `Strikes`, `Enchanted`, `Breaks`, `MagicRound`); spell delivery; durability; damage modulation and cleanup | Daggerfall ruleset consumes accepted equipment/strike/use results; Kit coordinates lifecycle. Extend `CombatModule`, item use/equipment callers and existing inventory; no second hit or dispatch path. | `Effects/Enchanting/CastWhenHeld.cs`; `CastWhenStrikes.cs`; `CastWhenUsed.cs`; `HealthLeech.cs`; `VampiricEffect.cs`; `CombatModule.cs` |
| MAG-017 | Artifacts, passive specials and racial overrides (F034, F005, F086 selective) | Artifact use/strike/hold effects; soul/summon/transform side effects; racial display, crime, inventory, rest/travel, quest and cure interactions | Daggerfall owns artifact identities and policy. Use Kit item/actor/time/fact coordination, ruleset quest/world operations, and current presentation. `RacialOverrideEffect` is a helper boundary, not a donor topology to copy. Implement/adapt; inspect each artifact caller. | `Effects/Special/*.cs`; `Game/Questing/QuestMachine.cs`; `DaggerfallSession.cs`; `PrivateersHoldAppearance.cs` |
| MAG-018 | Spellbook, spellmaker, potion and item-maker scope (F035–F036, F101) | Known/ready spells; effect eligibility and costs; ingredient recipe identity; potion payloads; item enchantment choices; consume/create/condition behavior | Daggerfall records and crafting policy; Kit inventory/equipment coordination; thin DOM actions/projections. Exclude donor windows/widgets and runtime Unity form state. Adapt. | `Game/MagicAndEffects/PotionRecipe.cs`; `MagicAndEffectsStructs.cs` (`EffectProperties`, `EnchantmentSettings`, `PotionProperties`); `DaggerfallSpellBookWindow.cs`; `DaggerfallSpellMakerWindow.cs`; `DaggerfallPotionMakerWindow.cs`; `DaggerfallItemMakerWindow.cs` |
| MAG-019 | Runtime callers, facts and presentation (F014, F045, F066, F097, F100) | Real damage/heal/condition/world/item callers; status/HUD projections; cast/impact feedback; save and UI interoperability | Daggerfall session remains the one Engine-admitted update. Facts/projections stay thin; Engine owns rendering/audio/spatial. Extend current owners and record separate consumers honestly. Adapt. | `DaggerfallSession.cs`; `Kit/Facts/FactBuffer.cs`; `Kit/Presentation/PresentationState.cs`; `DaggerfallHudProjection.cs`; `PrivateersHoldAppearance.cs` |

## Effect-file inventory

The entries below are exhaustive for the donor directory named above. The
family descriptions intentionally group files that share lifecycle and domain
dependencies; they do not assert identical formulas or target behavior.

### MAG-004 — Alteration (7 files)

These are incumbent effects with duration/cleanup state. Climbing, jumping,
slowfall and water breathing set movement/support capabilities; `Paralyze`
sets a target condition and interacts with aggro/hostile state; `Shield` keeps
a damage pool and ends when it is consumed; `ElementalResistance` is a
five-variant fire/frost/poison/shock/magic resistance effect with chance and
potion-maker eligibility.

- `MAG-004.Climbing` — `Effects/Alteration/Climbing.cs` — `Climbing` (runtime target)
- `MAG-004.ElementalResistance` — `Effects/Alteration/ElementalResistance.cs` — `ElementalResistance` (runtime target; five variants)
- `MAG-004.Jumping` — `Effects/Alteration/Jumping.cs` — `Jumping` (runtime target)
- `MAG-004.Paralyze` — `Effects/Alteration/Paralyze.cs` — `Paralyze` (runtime target)
- `MAG-004.Shield` — `Effects/Alteration/Shield.cs` — `Shield` (runtime target; custom shield pool/save state)
- `MAG-004.Slowfall` — `Effects/Alteration/Slowfall.cs` — `Slowfall` (runtime target)
- `MAG-004.WaterBreathing` — `Effects/Alteration/WaterBreathing.cs` — `WaterBreathing` (runtime target)

Drafting needs the exact Engine-admitted movement and condition operations,
resistance source/removal behavior, and whether shield damage enters the same
accepted damage result as ordinary combat. The existing Kit movement and
Mechanics stat/track owners are reuse points; this family must not add a
magical mover or parallel resistance evaluator.

### MAG-005 — Destruction direct and continuous damage (7 files)

The three `ContinuousDamage*` effects tick health, fatigue or spell points as
incumbent effects. The three `Damage*` effects apply one immediate payload. The
`Disintegrate` effect is a chance-bearing immediate health-ending effect. All
must share target/caster ordering, save/resistance handling, and the existing
vital-track/damage outcome path.

- `MAG-005.ContinuousDamageFatigue` — `Effects/Destruction/ContinuousDamageFatigue.cs` — `ContinuousDamageFatigue` (runtime target)
- `MAG-005.ContinuousDamageHealth` — `Effects/Destruction/ContinuousDamageHealth.cs` — `ContinuousDamageHealth` (runtime target)
- `MAG-005.ContinuousDamageSpellPoints` — `Effects/Destruction/ContinuousDamageSpellPoints.cs` — `ContinuousDamageSpellPoints` (runtime target)
- `MAG-005.DamageFatigue` — `Effects/Destruction/DamageFatigue.cs` — `DamageFatigue` (runtime target)
- `MAG-005.DamageHealth` — `Effects/Destruction/DamageHealth.cs` — `DamageHealth` (runtime target)
- `MAG-005.DamageSpellPoints` — `Effects/Destruction/DamageSpellPoints.cs` — `DamageSpellPoints` (runtime target)
- `MAG-005.Disintegrate` — `Effects/Destruction/Disintegrate.cs` — `Disintegrate` (runtime target)

### MAG-006 — Destruction drain and transfer (20 files)

`DrainEffect` is the shared incumbent base for permanent attribute damage and
its eight attribute leaves. `TransferEffect` is the shared drain-plus-caster
restoration base; its eight attribute leaves combine with direct health and
fatigue transfer leaves. These effects need source-aware stat contributions,
minimum-value rules, same-kind accumulation, healing/removal, and a truthful
caster/target relationship.

- `MAG-006.DrainEffect` — `Effects/Destruction/DrainEffect.cs` — `DrainEffect` (base/helper)
- `MAG-006.DrainAgility` — `Effects/Destruction/DrainAgility.cs` — `DrainAgility` (runtime target)
- `MAG-006.DrainEndurance` — `Effects/Destruction/DrainEndurance.cs` — `DrainEndurance` (runtime target)
- `MAG-006.DrainIntelligence` — `Effects/Destruction/DrainIntelligence.cs` — `DrainIntelligence` (runtime target)
- `MAG-006.DrainLuck` — `Effects/Destruction/DrainLuck.cs` — `DrainLuck` (runtime target)
- `MAG-006.DrainPersonality` — `Effects/Destruction/DrainPersonality.cs` — `DrainPersonality` (runtime target)
- `MAG-006.DrainSpeed` — `Effects/Destruction/DrainSpeed.cs` — `DrainSpeed` (runtime target)
- `MAG-006.DrainStrength` — `Effects/Destruction/DrainStrength.cs` — `DrainStrength` (runtime target)
- `MAG-006.DrainWillpower` — `Effects/Destruction/DrainWillpower.cs` — `DrainWillpower` (runtime target)
- `MAG-006.TransferEffect` — `Effects/Destruction/TransferEffect.cs` — `TransferEffect` (base/helper)
- `MAG-006.TransferAgility` — `Effects/Destruction/TransferAgility.cs` — `TransferAgility` (runtime target)
- `MAG-006.TransferEndurance` — `Effects/Destruction/TransferEndurance.cs` — `TransferEndurance` (runtime target)
- `MAG-006.TransferFatigue` — `Effects/Destruction/TransferFatigue.cs` — `TransferFatigue` (runtime target)
- `MAG-006.TransferHealth` — `Effects/Destruction/TransferHealth.cs` — `TransferHealth` (runtime target)
- `MAG-006.TransferIntelligence` — `Effects/Destruction/TransferIntelligence.cs` — `TransferIntelligence` (runtime target)
- `MAG-006.TransferLuck` — `Effects/Destruction/TransferLuck.cs` — `TransferLuck` (runtime target)
- `MAG-006.TransferPersonality` — `Effects/Destruction/TransferPersonality.cs` — `TransferPersonality` (runtime target)
- `MAG-006.TransferSpeed` — `Effects/Destruction/TransferSpeed.cs` — `TransferSpeed` (runtime target)
- `MAG-006.TransferStrength` — `Effects/Destruction/TransferStrength.cs` — `TransferStrength` (runtime target)
- `MAG-006.TransferWillpower` — `Effects/Destruction/TransferWillpower.cs` — `TransferWillpower` (runtime target)

`TransferHealth` and `TransferFatigue` are direct one-tick target-sink plus
caster-restoration leaves; the attribute leaves use `TransferEffect`.

### MAG-007 — Donor-only destruction helper (1 file)

- `MAG-007.VampiricFortifyEffect` — `Effects/Destruction/VampiricFortifyEffect.cs` — `VampiricFortifyEffect` (base/helper; **DFU-only/WIP**)

The donor marks this as work in progress and says no effect currently uses it.
It models a permanent range-bound target drain/caster fortify link. Keep the
source anchor for future comparison, but exclude it from the original baseline
until a separate behavioral decision and real caller exist.

### MAG-008 — Restoration (27 files)

The cure leaves terminate disease, paralysis or poison scopes. `FortifyEffect`
and its eight attribute leaves are incumbent temporary increases. `HealEffect`
and its eight attribute leaves remove matching drain state; health, fatigue and
spell-point leaves restore tracks directly. `FreeAction`, `Regenerate` and
`SpellAbsorption` are incumbent condition/recovery/defense effects.

- `MAG-008.CureDisease` — `Effects/Restoration/CureDisease.cs` — `CureDisease` (runtime target)
- `MAG-008.CureParalyzation` — `Effects/Restoration/CureParalyzation.cs` — `CureParalyzation` (runtime target)
- `MAG-008.CurePoison` — `Effects/Restoration/CurePoison.cs` — `CurePoison` (runtime target)
- `MAG-008.FortifyEffect` — `Effects/Restoration/FortifyEffect.cs` — `FortifyEffect` (base/helper)
- `MAG-008.FortifyAgility` — `Effects/Restoration/FortifyAgility.cs` — `FortifyAgility` (runtime target)
- `MAG-008.FortifyEndurance` — `Effects/Restoration/FortifyEndurance.cs` — `FortifyEndurance` (runtime target)
- `MAG-008.FortifyIntelligence` — `Effects/Restoration/FortifyIntelligence.cs` — `FortifyIntelligence` (runtime target)
- `MAG-008.FortifyLuck` — `Effects/Restoration/FortifyLuck.cs` — `FortifyLuck` (runtime target)
- `MAG-008.FortifyPersonality` — `Effects/Restoration/FortifyPersonality.cs` — `FortifyPersonality` (runtime target)
- `MAG-008.FortifySpeed` — `Effects/Restoration/FortifySpeed.cs` — `FortifySpeed` (runtime target)
- `MAG-008.FortifyStrength` — `Effects/Restoration/FortifyStrength.cs` — `FortifyStrength` (runtime target)
- `MAG-008.FortifyWillpower` — `Effects/Restoration/FortifyWillpower.cs` — `FortifyWillpower` (runtime target)
- `MAG-008.FreeAction` — `Effects/Restoration/FreeAction.cs` — `FreeAction` (runtime target)
- `MAG-008.HealEffect` — `Effects/Restoration/HealEffect.cs` — `HealEffect` (base/helper)
- `MAG-008.HealAgility` — `Effects/Restoration/HealAgility.cs` — `HealAgility` (runtime target)
- `MAG-008.HealEndurance` — `Effects/Restoration/HealEndurance.cs` — `HealEndurance` (runtime target)
- `MAG-008.HealFatigue` — `Effects/Restoration/HealFatigue.cs` — `HealFatigue` (runtime target)
- `MAG-008.HealHealth` — `Effects/Restoration/HealHealth.cs` — `HealHealth` (runtime target)
- `MAG-008.HealIntelligence` — `Effects/Restoration/HealIntelligence.cs` — `HealIntelligence` (runtime target)
- `MAG-008.HealLuck` — `Effects/Restoration/HealLuck.cs` — `HealLuck` (runtime target)
- `MAG-008.HealPersonality` — `Effects/Restoration/HealPersonality.cs` — `HealPersonality` (runtime target)
- `MAG-008.HealSpeed` — `Effects/Restoration/HealSpeed.cs` — `HealSpeed` (runtime target)
- `MAG-008.HealSpellPoints` — `Effects/Restoration/HealSpellPoints.cs` — `HealSpellPoints` (runtime target)
- `MAG-008.HealStrength` — `Effects/Restoration/HealStrength.cs` — `HealStrength` (runtime target)
- `MAG-008.HealWillpower` — `Effects/Restoration/HealWillpower.cs` — `HealWillpower` (runtime target)
- `MAG-008.Regenerate` — `Effects/Restoration/Regenerate.cs` — `Regenerate` (runtime target)
- `MAG-008.SpellAbsorption` — `Effects/Restoration/SpellAbsorption.cs` — `SpellAbsorption` (runtime target)

The donor `HealEffect` comments that disease attribute loss is not currently
healed and asks whether classic allows it. Preserve that as a focused question;
do not silently conflate drain healing, disease cure, and ordinary restoration.

### MAG-009 — Illusion (10 files)

`ConcealmentEffect` centralizes flag set/clear for chameleon, invisibility and
shadow. Normal and true variants have different allowed records and target
semantics. `LightNormal` creates the ordinary light effect, while `MorphSelf`
is a self-only morph caller. `MageLight` is a custom demo effect and is
excluded from the original baseline.

- `MAG-009.ConcealmentEffect` — `Effects/Illusion/ConcealmentEffect.cs` — `ConcealmentEffect` (base/helper)
- `MAG-009.ChameleonNormal` — `Effects/Illusion/ChameleonNormal.cs` — `ChameleonNormal` (runtime target)
- `MAG-009.ChameleonTrue` — `Effects/Illusion/ChameleonTrue.cs` — `ChameleonTrue` (runtime target)
- `MAG-009.InvisibilityNormal` — `Effects/Illusion/InvisibilityNormal.cs` — `InvisibilityNormal` (runtime target)
- `MAG-009.InvisibilityTrue` — `Effects/Illusion/InvisibilityTrue.cs` — `InvisibilityTrue` (runtime target)
- `MAG-009.ShadowNormal` — `Effects/Illusion/ShadowNormal.cs` — `ShadowNormal` (runtime target)
- `MAG-009.ShadowTrue` — `Effects/Illusion/ShadowTrue.cs` — `ShadowTrue` (runtime target)
- `MAG-009.LightNormal` — `Effects/Illusion/LightNormal.cs` — `LightNormal` (runtime target)
- `MAG-009.MorphSelf` — `Effects/Illusion/MorphSelf.cs` — `MorphSelf` (runtime target; caller/transform dependency)
- `MAG-009.MageLight` — `Effects/Illusion/MageLight.cs` — `MageLight` (**DFU-only/demo**; reflective-enumeration-disabled custom glow)

Adapt the observable concealment and light results to Engine-backed
presentation/perception. Do not port Unity `Light` creation or scene parenting.

### MAG-010 — Mysticism (10 files)

These are discrete spell operations rather than one common incumbent state:
language comprehension, item creation, dispel targets, lock/open, silence, soul
trap, and teleport. Their real dependencies are item identity/creation,
door/world state, effect removal, enemy/actor state, and elapsed-time/world
relocation. Each operation needs its own outcome and cleanup contract.

- `MAG-010.ComprehendLanguages` — `Effects/Mysticism/ComprehendLanguages.cs` — `ComprehendLanguages` (runtime target)
- `MAG-010.CreateItem` — `Effects/Mysticism/CreateItem.cs` — `CreateItem` (runtime target)
- `MAG-010.DispelDaedra` — `Effects/Mysticism/DispelDaedra.cs` — `DispelDaedra` (runtime target)
- `MAG-010.DispelMagic` — `Effects/Mysticism/DispelMagic.cs` — `DispelMagic` (runtime target)
- `MAG-010.DispelUndead` — `Effects/Mysticism/DispelUndead.cs` — `DispelUndead` (runtime target)
- `MAG-010.Lock` — `Effects/Mysticism/Lock.cs` — `Lock` (runtime target; door/world operation)
- `MAG-010.Open` — `Effects/Mysticism/Open.cs` — `Open` (runtime target; door/world operation)
- `MAG-010.Silence` — `Effects/Mysticism/Silence.cs` — `Silence` (runtime target)
- `MAG-010.SoulTrap` — `Effects/Mysticism/SoulTrap.cs` — `SoulTrap` (runtime target; item/soul identity)
- `MAG-010.Teleport` — `Effects/Mysticism/Teleport.cs` — `Teleport` (runtime target; world/time operation)

### MAG-011 — Thaumaturgy (11 files)

`DetectEffect` is the shared detector state used by enemy, magic and treasure
variants. `CharmEffect` and `PacifyEffect` alter disposition with different
target classes; the donor notes charm is effectively persistent until the player
attacks. `Identify` is a transaction/UI caller, not merely an effect flag.
`Levitate` and `WaterWalking` affect spatial support; reflection and resistance
participate in cast admission.

- `MAG-011.DetectEffect` — `Effects/Thaumaturgy/DetectEffect.cs` — `DetectEffect` (base/helper)
- `MAG-011.DetectEnemy` — `Effects/Thaumaturgy/DetectEnemy.cs` — `DetectEnemy` (runtime target)
- `MAG-011.DetectMagic` — `Effects/Thaumaturgy/DetectMagic.cs` — `DetectMagic` (runtime target)
- `MAG-011.DetectTreasure` — `Effects/Thaumaturgy/DetectTreasure.cs` — `DetectTreasure` (runtime target)
- `MAG-011.CharmEffect` — `Effects/Thaumaturgy/CharmEffect.cs` — `CharmEffect` (runtime target; target-class/persistence question)
- `MAG-011.PacifyEffect` — `Effects/Thaumaturgy/PacifyEffect.cs` — `PacifyEffect` (runtime target; four target variants)
- `MAG-011.Identify` — `Effects/Thaumaturgy/Identify.cs` — `Identify` (runtime target; item/trade caller)
- `MAG-011.Levitate` — `Effects/Thaumaturgy/Levitate.cs` — `Levitate` (runtime target; spatial support caller)
- `MAG-011.SpellReflection` — `Effects/Thaumaturgy/SpellReflection.cs` — `SpellReflection` (runtime target)
- `MAG-011.SpellResistance` — `Effects/Thaumaturgy/SpellResistance.cs` — `SpellResistance` (runtime target)
- `MAG-011.WaterWalking` — `Effects/Thaumaturgy/WaterWalking.cs` — `WaterWalking` (runtime target)

Detector results must become a product projection or fact over stable actor/item
identities. Do not recreate the donor HUD compass registration or motor
components.

### MAG-012 — Classic disease base and leaves (18 files)

`DiseaseEffect` owns an incumbent, elapsed-day lifecycle with incubation,
finite/permanent symptoms, daily stat/health/fatigue/magicka effects, cure and
save data. The 17 named leaves primarily select a classic `Diseases` value and
`DiseaseData` matrix; they remain individually named because their source keys,
infection vectors and content references are distinct.

- `MAG-012.DiseaseEffect` — `Effects/Diseases/DiseaseEffect.cs` — `DiseaseEffect` (base/helper; daily lifecycle/data matrix)
- `MAG-012.BloodRot` — `Effects/Diseases/BloodRot.cs` — `BloodRot` (runtime target)
- `MAG-012.BrainFever` — `Effects/Diseases/BrainFever.cs` — `BrainFever` (runtime target)
- `MAG-012.CalironsCurse` — `Effects/Diseases/CalironsCurse.cs` — `CalironsCurse` (runtime target)
- `MAG-012.Cholera` — `Effects/Diseases/Cholera.cs` — `Cholera` (runtime target)
- `MAG-012.Chrondiasis` — `Effects/Diseases/Chrondiasis.cs` — `Chrondiasis` (runtime target)
- `MAG-012.Consumption` — `Effects/Diseases/Consumption.cs` — `Consumption` (runtime target)
- `MAG-012.Dementia` — `Effects/Diseases/Dementia.cs` — `Dementia` (runtime target)
- `MAG-012.Leprosy` — `Effects/Diseases/Leprosy.cs` — `Leprosy` (runtime target)
- `MAG-012.Plague` — `Effects/Diseases/Plague.cs` — `Plague` (runtime target)
- `MAG-012.RedDeath` — `Effects/Diseases/RedDeath.cs` — `RedDeath` (runtime target)
- `MAG-012.StomachRot` — `Effects/Diseases/StomachRot.cs` — `StomachRot` (runtime target)
- `MAG-012.SwampRot` — `Effects/Diseases/SwampRot.cs` — `SwampRot` (runtime target)
- `MAG-012.TyphoidFever` — `Effects/Diseases/TyphoidFever.cs` — `TyphoidFever` (runtime target)
- `MAG-012.WitchesPox` — `Effects/Diseases/WitchesPox.cs` — `WitchesPox` (runtime target)
- `MAG-012.WizardFever` — `Effects/Diseases/WizardFever.cs` — `WizardFever` (runtime target)
- `MAG-012.WoundRot` — `Effects/Diseases/WoundRot.cs` — `WoundRot` (runtime target)
- `MAG-012.YellowFever` — `Effects/Diseases/YellowFever.cs` — `YellowFever` (runtime target)

The donor says its classic matrix comes from `FALL.EXE` 1.07.213. Task drafting
must preserve provenance and decide the original target's exact infection,
overlap, daily tick, cure, and save behavior instead of treating a DFU matrix
copy as a semantic audit.

### MAG-013 — Infection and transformation stages (4 files)

`LycanthropyInfection` is the shared custom infection lifecycle. The two
lycanthrope leaves choose wereboar/werewolf deployment. `VampirismInfection`
has a parallel staged infection and records the infection region for clan
selection. DFU progresses these through dream/death videos, spellbook mutation,
quest/world operations and a permanent racial override. Exclude the DFU
video-player and Unity scene wiring, while preserving required original
story/cinematic behavior under SUP-18 through an adapted Engine/media path.

- `MAG-013.LycanthropyInfection` — `Effects/Diseases/LycanthropyInfection.cs` — `LycanthropyInfection` (base/helper; custom staged infection)
- `MAG-013.VampirismInfection` — `Effects/Diseases/VampirismInfection.cs` — `VampirismInfection` (runtime target; staged infection)
- `MAG-013.WereboarInfection` — `Effects/Diseases/WereboarInfection.cs` — `WereboarInfection` (runtime target; lycanthrope variant)
- `MAG-013.WerewolfInfection` — `Effects/Diseases/WerewolfInfection.cs` — `WerewolfInfection` (runtime target; lycanthrope variant)

The infection rows depend on CAP-TIME/CAP-SAVE plus selective CAP-QSTATE,
CAP-QACTION, CAP-SOCIAL and world relocation. They must not establish a quest
cycle that blocks all quest work or use a fake transformation proof.

### MAG-014 — Poison (1 file)

- `MAG-014.PoisonEffect` — `Effects/Poisons/PoisonEffect.cs` — `PoisonEffect` (runtime archetype/incumbent)

The donor poison has 12 variants, including weapon poisons and drugs. It keeps
its own minute-based onset, active duration, periodic health/vital/stat effects,
completion and positive-stat cleanup, and serializes its timer/state. Reuse the
future item use/strike callers and common admitted time; do not add an independent
poison clock.

### MAG-015 — Enchanting held/passive effects (14 files)

These effects are item-bound and mostly evaluate while equipped or on a magic
round. They cover spell absorption, social reactions, skill/talent/carry/weight
changes, armor changes, health regeneration/leech, item deterioration/repair,
and damage taken while held. Their common dependency is a stable equipped item
identity and reversible source contribution.

- `MAG-015.AbsorbsSpells` — `Effects/Enchanting/AbsorbsSpells.cs` — `AbsorbsSpells` (runtime target; held)
- `MAG-015.BadReactionsFrom` — `Effects/Enchanting/BadReactionsFrom.cs` — `BadReactionsFrom` (runtime target; held/social)
- `MAG-015.BadRepWith` — `Effects/Enchanting/BadRepWith.cs` — `BadRepWith` (runtime target; held/social)
- `MAG-015.EnhancesSkill` — `Effects/Enchanting/EnhancesSkill.cs` — `EnhancesSkill` (runtime target; held/stat/skill)
- `MAG-015.ExtraSpellPts` — `Effects/Enchanting/ExtraSpellPts.cs` — `ExtraSpellPts` (runtime target; held/resource)
- `MAG-015.GoodRepWith` — `Effects/Enchanting/GoodRepWith.cs` — `GoodRepWith` (runtime target; held/social)
- `MAG-015.ImprovesTalents` — `Effects/Enchanting/ImprovesTalents.cs` — `ImprovesTalents` (runtime target; held/talent)
- `MAG-015.IncreasedWeightAllowance` — `Effects/Enchanting/IncreasedWeightAllowance.cs` — `IncreasedWeightAllowance` (runtime target; held/carry)
- `MAG-015.ItemDeteriorates` — `Effects/Enchanting/ItemDeteriorates.cs` — `ItemDeteriorates` (runtime target; held/condition)
- `MAG-015.RegensHealth` — `Effects/Enchanting/RegensHealth.cs` — `RegensHealth` (runtime target; held/periodic recovery)
- `MAG-015.RepairsObjects` — `Effects/Enchanting/RepairsObjects.cs` — `RepairsObjects` (runtime target; held/condition)
- `MAG-015.StrengthensArmor` — `Effects/Enchanting/StrengthensArmor.cs` — `StrengthensArmor` (runtime target; held/armor)
- `MAG-015.UserTakesDamage` — `Effects/Enchanting/UserTakesDamage.cs` — `UserTakesDamage` (runtime target; held/periodic damage)
- `MAG-015.WeakensArmor` — `Effects/Enchanting/WeakensArmor.cs` — `WeakensArmor` (runtime target; held/armor)

The donor documents several source-observation uncertainties: repair priority,
condition cadence, social stacking/exclusivity, and exact classic behavior for
some item restrictions. `ItemDeteriorates`, `RepairsObjects`, `UserTakesDamage`
and `RegensHealth` all need elapsed-time ordering with rest/travel. The current
Engine-backed inventory/equipment coordinators are the reuse boundary; item
definitions and enchantment policy remain in the Daggerfall ruleset.

### MAG-016 — Enchanting trigger, combat and item-state effects (10 files)

The first three leaves invoke a spell when an equipped item is held, used or a
weapon strikes. `HealthLeech`, `LowDamageVs`, `PotentVs` and `VampiricEffect`
consume accepted use/strike results and can return durability or damage
modulation. `ExtraWeight` and `FeatherWeight` change item data at enchantment
time. `SoulBound` consumes a filled soul source and releases a hostile soul on
item break. The callback flags and item lifecycle are part of the behavior.

- `MAG-016.CastWhenHeld` — `Effects/Enchanting/CastWhenHeld.cs` — `CastWhenHeld` (runtime target; equipped/magic-round/reroll)
- `MAG-016.CastWhenStrikes` — `Effects/Enchanting/CastWhenStrikes.cs` — `CastWhenStrikes` (runtime target; weapon strike)
- `MAG-016.CastWhenUsed` — `Effects/Enchanting/CastWhenUsed.cs` — `CastWhenUsed` (runtime target; item use)
- `MAG-016.HealthLeech` — `Effects/Enchanting/HealthLeech.cs` — `HealthLeech` (runtime target; held/used/strike/enchant)
- `MAG-016.LowDamageVs` — `Effects/Enchanting/LowDamageVs.cs` — `LowDamageVs` (runtime target; strike damage modulation)
- `MAG-016.PotentVs` — `Effects/Enchanting/PotentVs.cs` — `PotentVs` (runtime target; strike damage modulation)
- `MAG-016.VampiricEffect` — `Effects/Enchanting/VampiricEffect.cs` — `VampiricEffect` (runtime target; held/range/strike)
- `MAG-016.ExtraWeight` — `Effects/Enchanting/ExtraWeight.cs` — `ExtraWeight` (runtime target; enchant-time item mutation)
- `MAG-016.FeatherWeight` — `Effects/Enchanting/FeatherWeight.cs` — `FeatherWeight` (runtime target; enchant-time item mutation)
- `MAG-016.SoulBound` — `Effects/Enchanting/SoulBound.cs` — `SoulBound` (runtime target; enchant/break/soul identity)

The donor explicitly leaves the exact reduction/increase amounts for
`LowDamageVs` and `PotentVs` as questions. Preserve that uncertainty for
source comparison. Strike effects must modify or consume the accepted combat
result rather than recompute hit/damage in a second path.

### MAG-017 — Special artifacts, transformations and passive effects (13 files)

These are named special behaviors with bespoke item/actor/world interactions.
`RacialOverrideEffect` is a helper boundary for a single active racial override;
the other special classes are concrete runtime targets. Artifact leaves cover
soul storage, strikes, use, reflection, summons, item destruction, corruption,
attribute/resource transfer and passive special rules. Vampire and lycanthrope
stage-two effects interact with time, quests, race presentation, combat,
rest/travel and cure state.

- `MAG-017.RacialOverrideEffect` — `Effects/Special/RacialOverrideEffect.cs` — `RacialOverrideEffect` (base/helper; one-override policy)
- `MAG-017.AzurasStarEffect` — `Effects/Special/AzurasStarEffect.cs` — `AzurasStarEffect` (runtime target; soul/item use)
- `MAG-017.LycanthropyEffect` — `Effects/Special/LycanthropyEffect.cs` — `LycanthropyEffect` (runtime target; permanent transformation)
- `MAG-017.MaceOfMolagBalEffect` — `Effects/Special/MaceOfMolagBalEffect.cs` — `MaceOfMolagBalEffect` (runtime target; strike transfer/resource overflow)
- `MAG-017.MasqueOfClavicusEffect` — `Effects/Special/MasqueOfClavicusEffect.cs` — `MasqueOfClavicusEffect` (runtime target; artifact hold behavior)
- `MAG-017.MehrunesRazorEffect` — `Effects/Special/MehrunesRazorEffect.cs` — `MehrunesRazorEffect` (runtime target; strike behavior)
- `MAG-017.OghmaInfiniumEffect` — `Effects/Special/OghmaInfiniumEffect.cs` — `OghmaInfiniumEffect` (runtime target; use/progression)
- `MAG-017.PassiveSpecialsEffect` — `Effects/Special/PassiveSpecialsEffect.cs` — `PassiveSpecialsEffect` (runtime target; passive race/career consequences)
- `MAG-017.RingOfNamiraEffect` — `Effects/Special/RingOfNamiraEffect.cs` — `RingOfNamiraEffect` (runtime target; damage reflection/durability)
- `MAG-017.SanguineRoseEffect` — `Effects/Special/SanguineRoseEffect.cs` — `SanguineRoseEffect` (runtime target; summon/use)
- `MAG-017.SkullOfCorruptionEffect` — `Effects/Special/SkullOfCorruptionEffect.cs` — `SkullOfCorruptionEffect` (runtime target; artifact use; donor class is internal)
- `MAG-017.VampirismEffect` — `Effects/Special/VampirismEffect.cs` — `VampirismEffect` (runtime target; permanent transformation)
- `MAG-017.WabbajackEffect` — `Effects/Special/WabbajackEffect.cs` — `WabbajackEffect` (runtime target; strike transformation/quest-safe target handling)

The concrete artifact rows are candidates for separate task leaves only where
their caller and content identity are ready. Do not port `QuestMachine`, the
Unity video-player or window/scene topology, enemy GameObjects, or donor global
access. Preserve required original story/cinematic behavior under SUP-18 and
route its meaning through future ruleset quest/world/item operations and
admitted presentation/media.

## Enum-driven variants and parameter leaves

These are the exact named options observed in the inspected donor enums and
variant setup. They are subleaves of the file IDs above, not extra files. When
an option needs its own task, append `.<EnumValue>` to the file leaf (for
example, `MAG-014.PoisonEffect.Nux_Vomica`); do not renumber the file leaf.

- `MAG-004.ElementalResistance` uses `DFCareer.Elements`: `Fire`, `Frost`,
  `DiseaseOrPoison`, `Shock`, and `Magic`. Its DFU subgroup labels are `Fire`,
  `Frost`, `Poison`, `Shock`, and `Magicka` respectively.
- `MAG-011.PacifyEffect` uses `DFCareer.EnemyGroups`: `Animals`, `Undead`,
  `Humanoid`, and `Daedra`.
- `MAG-010.CreateItem` uses its local `CreateItemSelection` enum: `LeatherCuirass`,
  `LeatherGauntlets`, `LeatherGreaves`, `LeatherLeftPauldron`,
  `LeatherRightPauldron`, `LeatherHelm`, `LeatherBoots`, `ChainCuirass`,
  `ChainGauntlets`, `ChainGreaves`, `ChainLeftPauldron`, `ChainRightPauldron`,
  `ChainHelm`, `ChainBoots`, `SteelCuirass`, `SteelGauntlets`, `SteelGreaves`,
  `SteelLeftPauldron`, `SteelRightPauldron`, `SteelHelm`, `SteelBoots`,
  `SteelBuckler`, `SteelDagger`, `SteelLongsword`, `SteelStaff`, `ShortBow`,
  `Arrows`, `SteelBattleAxe`, and `Robes`.
- `MAG-009.MageLight` uses its local `VariantTypes` enum: `Inferno`, `Rime`,
  `Venom`, `Storm`, and `Arcane`. This is a DFU-only/demo expansion and stays
  excluded from the original baseline.
- `MAG-014.PoisonEffect` represents the 12 exact `Poisons` values
  `Nux_Vomica`, `Arsenic`, `Moonseed`, `Drothweed`, `Somnalius`,
  `Pyrrhic_Acid`, `Magebane`, `Thyrwort`, `Indulcet`, `Sursum`, `Quaesto_Vil`,
  and `Aegrotat`. The donor labels the first eight as weapon poisons and the
  last four (`Indulcet`, `Sursum`, `Quaesto_Vil`, `Aegrotat`) as drugs.

The following local `Params` enums are selectable item-effect parameters that
one file represents. They stay under the listed file leaf and must retain their
individual eligibility, condition, social, time, and stacking semantics:

- `MAG-015.BadReactionsFrom`: `Humanoids`, `Animals`, `Daedra`.
- `MAG-015.BadRepWith` and `MAG-015.GoodRepWith`: `Commoners`, `Merchants`,
  `Scholars`, `Nobility`, `Underworld`, `All`.
- `MAG-015.ExtraSpellPts`: `DuringWinter`, `DuringSpring`, `DuringSummer`,
  `DuringFall`, `DuringFullMoon`, `DuringHalfMoon`, `DuringNewMoon`,
  `NearUndead`, `NearDaedra`, `NearHumanoids`, `NearAnimals`.
- `MAG-015.ImprovesTalents`: `Hearing`, `Athleticism`, `AdrenalineRush`.
- `MAG-015.IncreasedWeightAllowance`: `OneQuarterExtra`, `OneHalfExtra`.
- `MAG-015.ItemDeteriorates` and `MAG-015.RegensHealth`: `AllTheTime`,
  `InSunlight`, `InHolyPlaces` for deterioration, and `AllTheTime`,
  `InSunlight`, `InDarkness` for regeneration.
- `MAG-016.HealthLeech`: `WheneverUsed`, `UnlessUsedDaily`, `UnlessUsedWeekly`.
- `MAG-016.LowDamageVs` and `MAG-016.PotentVs`: `Undead`, `Daedra`, `Humanoid`,
  `Animals`.
- `MAG-015.UserTakesDamage`: `InSunlight`, `InHolyPlaces`.
- `MAG-016.VampiricEffect`: `AtRange`, `WhenStrikes`.

## Casting, crafting and lifecycle scope

The shared lifecycle is broader than a single effect call. A task using these
IDs must state how an effect is admitted, attached to a caster/target, resumed
from save, advanced on ordinary and elapsed time, stacked/refreshed/replaced,
cured or expired, and cleaned up. The donor base contract distinguishes
`Start`, `Resume`, `ConstantEffect`, `MagicRound`, `End`, chance rolls,
stat/skill/resistance changes, enchantment callbacks, and effect-specific save
data. `IncumbentEffect` adds like-kind admission and incumbent-only persistence.
Those are behavioral questions; the donor class hierarchy is not a required
Rusty shape.

Casting also includes the surrounding records and result policy:

- Effect keys and classic spell keys must resolve to compiled Daggerfall
  definitions plus normalized loaded records. The donor broker's reflection,
  `Activator.CreateInstance`, singleton access and runtime custom-bundle
  discovery are explicitly excluded.
- A bundle must retain source identity (caster, item, equipped/used/struck
  context), target shape, element, effect settings, cost and runtime state.
  Ready-spell state, self versus other delivery, chance, saving throw, immunity,
  absorption/reflection and failure/refund outcomes need explicit ordering.
- Periodic work uses the one Daggerfall game-time model inside the admitted
  session update. Rest, travel and other elapsed-time operations must catch up
  minute/day effects once in a documented order; no effect family gets its own
  timer, thread, update loop or wall-clock subscription.
- Active effects and effect-specific state belong in the existing Daggerfall
  save payload/envelope with composition identity checks. Unity component/object
  graph serialization and binary classic save compatibility are outside the
  default scope.

Spellbook and crafting are separate from casting the resulting spell. The
planning scope includes known spells, ready/cast actions, spellmaker eligibility
and cost, potion ingredient-set matching, potion payloads, item-maker effect
selection, enchantment parameters, item condition/consumption, and the thin DOM
actions/projections needed to operate them. It excludes the DFU window classes,
widget hierarchy and Unity event topology. `EffectProperties`,
`EnchantmentSettings`, `PotionProperties`, `EffectCosts`, and related structs
are donor data contracts to translate into normalized content and typed
Daggerfall records, not a reason to create a universal gameplay DSL.

## Kit coordination versus Daggerfall ruleset semantics

| Concern | Kit coordination | Daggerfall ruleset/content |
| --- | --- | --- |
| Active effect identity | Source/target handles, lifecycle bookkeeping, cancellation and persistence coordination over verified Engine mechanisms | Which keys exist, like-kind meaning, stack/refresh/replace policy, duration/chance interpretation and effect cleanup meaning |
| Stats, tracks and modifiers | Thin adapter to Engine Mechanics `ExactSource`/active-effect substrate after the safe C# contract is reverified | Daggerfall attribute/skill/resistance identities, permanent versus live semantics, magnitude formulas, floors, caps and cure policy |
| Casting | Typed coordination of admission/result plumbing where it is reusable | Spell records, keys, bundle meaning, costs, target/element restrictions, chance/saves/immunity/reflection/absorption and caller policy |
| Time | Reusable ordering shape only if it does not make a second clock | Daggerfall calendar, minute/day interpretation, rest/travel catch-up, periodic effects, infection/poison deadlines and restoration rules |
| Items and equipment | Engine-backed inventory/equipment lifecycle, unique identity, containment and accepted equip/use/strike operation boundaries | Item definitions, enchantment payload parameters, condition values, soul/artifact identity, restrictions, durability and all item-effect policy |
| World and actors | Actor lifetime/navigation/spatial coordination already owned by Kit/Engine | Doors, teleport destinations, faction/reputation, enemy class, pacification, summon/transformation and quest/world side effects |
| Persistence | Existing save envelope/composition compatibility shape | Effect-specific state, active bundle meaning, disease/poison timers, transformation stages, item enchantment state and restore ordering |
| Presentation and UI | Facts/projection transport and Engine Graphics/Audio/Spatial services | Status labels, spell/crafting choices, messages, spell feedback meaning and semantic UI actions; TypeScript remains a thin projection |

Current stat source evidence matters here: `ActorsState.ReadStat` currently
evaluates ordinary stats with `Array.Empty<ExactSource>()`, while
`DaggerfallMechanicsState` creates the ruleset's stat/track identities and
bases. The Engine's `ExactStatContribution` and active-effect mechanisms are
reuse candidates, not proof that magic is already wired. A task should verify
the safe generated C# surface and extend the existing owner rather than add
local modifier arrays, a second inventory, or a downstream Engine substitute.

## Current local reuse pointers

These are current source anchors for task drafting. They identify extension
points and existing behavior; they do not claim that the magic runtime exists.

- **Actor stats/tracks:** `src/WorldRpg.Kit/Actors/ActorsState.cs` and
  `src/WorldRpg.Rulesets.Daggerfall/Content/DaggerfallMechanicsState.cs` own
  actor identity, stat/track bases, reads and guarded mutations. Future effect
  sources should use the Engine Mechanics contribution path through this owner.
- **Definitions and policy:** `src/WorldRpg.Rulesets.Daggerfall/Content/DaggerfallDefinitions.cs`,
  `DaggerfallMechanicsState.cs`, `DaggerfallBaseContent.cs` and
  `src/WorldRpg.Rulesets.Daggerfall/Policies/DaggerfallFormulaPolicy.cs` are
  the current homes for Daggerfall identities, normalized records and formulas.
- **Inventory/equipment:**
  `src/WorldRpg.Kit/Inventory/MechanicsInventoryCoordinator.cs`,
  `MechanicsInventoryContainerCoordinator.cs` and
  `MechanicsEquipmentCoordinator` provide Engine-backed item, containment and
  equipment lifecycle. Enchantments, soul sources, potions and item-triggered
  spells must extend these operations.
- **Combat and accepted damage:**
  `src/WorldRpg.Rulesets.Daggerfall/Modules/Combat/CombatModule.cs`,
  `DaggerfallMeleeTargeting.cs` and `CombatDefinitions.cs` own current
  targeting, hit/damage outcomes, cooldown and combat media. Strike enchantments
  and health transfer should consume this path.
- **Movement/navigation:**
  `src/WorldRpg.Kit/Controls/SpatialMovementSystem.cs` and
  `src/WorldRpg.Kit/Actors/ActorNavigationCoordinator.cs` are the current
  Engine Spatial stepping/navigation owners. Climbing, jumping, slowfall,
  levitation, water breathing and water walking need policy extensions here or
  an explicitly named Engine capability request.
- **Composition and one update:**
  `src/WorldRpg.Kit/GameComposition.cs` resolves loaded packs/tuning;
  `src/WorldRpg.Rulesets.Daggerfall/DaggerfallSession.cs` owns session state,
  the admitted update, facts, presentation and save capture/restore. Magic time
  and active-state work belongs in this existing product path.
- **Save:**
  `src/WorldRpg.Rulesets.Daggerfall/DaggerfallState.cs`,
  `DaggerfallSavePayload.cs` and `src/WorldRpg.Host/WorldRpgSaveStore.cs`
  provide the current product payload/envelope. Active effect, poison/disease,
  enchantment and transformation state should extend this ownership.
- **Thin UI/projections:**
  `src/WorldRpg.Kit/Presentation/PresentationState.cs`, `UiValueBuilder.cs`,
  `src/WorldRpg.Kit/Facts/FactBuffer.cs`,
  `src/WorldRpg.Rulesets.Daggerfall/Presentation/DaggerfallHudProjection.cs`,
  `DaggerfallInventoryPresentation.cs`, `DaggerfallOutcomePresentation.cs`
  and `DaggerfallUiAction.cs` provide the current projection/action pattern for
  status effects, spellbook/maker choices and outcomes.
- **Current appearance:**
  `src/WorldRpg.Rulesets.Daggerfall/Presentation/PrivateersHoldAppearance.cs`
  already selects equipped right-hand/left-hand viewmodel art, advances
  admitted sprite playback, and presents blood effects. Its `SpawnEffect`
  path and the `magicSparkle` normalized resource are a presentation reuse
  pointer for future spell facts; they do not implement spell runtime or magic
  feedback by themselves. `PrivateersHoldContent.cs` admits the current
  normalized effect resources.

## Focused uncertainties for task drafting

These questions are intentionally bounded to the affected family. They should
be resolved with the original game/DFU source and current local caller before a
task claims the behavior complete.

- Exact classic damage, drain, transfer, heal, fortify, resistance and
  disintegrate ranges, rounding, caps, saving-throw amount modification and
  same-kind stacking remain to be mapped to the selected Engine sources.
- The donor marks `LowDamageVs` and `PotentVs` modulation amounts as unknown.
  Where original evidence is unavailable, use the concrete donor behavior as a
  documented provisional baseline under DEC-11, not an invented value or a claim
  of exact classic fidelity. `HealEffect` also flags uncertainty about healing
  disease attribute loss versus curing the disease.
- Charm's donor comments intentionally disable duration after classic
  observation; confirm the original target behavior and target-class split
  against the source before choosing a Rusty rule.
- Resistance, absorption, reflection, immunity and saving-throw ordering needs
  one explicit cast-admission contract, including whether an effect receives a
  first magic round when rejected or absorbed.
- Poison and disease overlapping instances, elapsed catch-up, cure ordering,
  positive drug-stat cleanup, infection eligibility and save migration need
  concrete source-backed rules.
- Enchantment condition cadence, equip/unequip cleanup, social exclusivity,
  strike damage modulation, soul catalog completeness, item-break spawning,
  and the priority of `RepairsObjects` remain task-local decisions.
- `Teleport`, `CreateItem`, `Lock`, `Open`, `SoulTrap`, `Identify`, detection,
  summon and artifact effects need real world/item/actor callers and stable
  identities before scheduling their side effects.
- Vampirism and lycanthropy require explicit decisions for faction/reputation,
  quest start/end, spellbook additions, time advancement, world relocation,
  racial presentation, rest/travel/crime restrictions and cure cleanup. The DFU
  video-player, Unity windows and GameManager/QuestMachine topology are excluded
  implementation shapes; required original story/cinematic behavior remains in
  scope under SUP-18.
- `MageLight` is a DFU custom demo and `VampiricFortifyEffect` is an unused DFU
  work-in-progress helper. Neither should become baseline work through a
  filename-completeness rule.

## Donor consultation and verification limits

The configured Codebase Memory project `daggerfall-unity` was consulted for
exact anchors and call flows, including `EntityEffectBroker.Start` and
`RegisterEffectTemplate`, `EntityEffectBroker.InstantiateEffect`,
`EntityEffectManager.AssignBundle`/`Update`, `BaseEntityEffect` lifecycle and
save hooks, `IncumbentEffect` incumbent admission, `DiseaseEffect`,
`PoisonEffect`, and representative Alteration, Destruction, Restoration,
Thaumaturgy, Enchanting and Special leaves. The index was ready, but its nested
directory coverage check did not report the `Effects` scope; the direct donor
checkout enumeration above is therefore the completeness authority for the 153
files. This is an index limitation, not a donor-absence claim.

The direct source confirms the main boundaries used here: the donor broker uses
reflective template discovery and `Activator`; the manager performs bundle
admission, saving/chance/absorption/reflection checks and effect rounds; disease
and poison manage elapsed day/minute state and serialize their own data; and
enchantment leaves use explicit held/used/strike/enchant/break callback flags.
The coverage plan and feature map remain point-in-time planning documents. The
current local appearance source supersedes the old broad absence note for
equipped viewmodel selection, blood effects, and the reserved `magicSparkle`
resource. No exhaustive per-effect semantic audit, runtime implementation,
deployment, interactive test, proof gate, or Den task creation was performed.
