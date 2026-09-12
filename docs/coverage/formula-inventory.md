# Formula and policy-entry inventory

Snapshot: DFU `81e89e90c27bc3c1a7a61871e545fad129174dec`,
`Assets/Scripts/Game/Formulas/FormulaHelper.cs`.
This records **99 distinct public static method names (101 declarations,
including overloads)** found in this file. It is a bounded source enumeration,
not a claim that all game policy resides here or a full audit of every formula.

Each named method is a candidate semantic sub-behavior under its FORM family.
Stable leaf IDs use `FORM-04.CalculateAttackDamage`, for example. Overloads share
one leaf and must all be considered when relevant. Lines below are source lookup
hints for this revision, not task acceptance or permanent line identities.

FORM-12 is excluded mod infrastructure, not a gameplay task.

Default disposition for the other families: adapt the underlying classic behavior in the Daggerfall
ruleset, reuse existing local formula/mechanics owners, and move static source
conversion/naming into Import where appropriate. Do not port override registries,
mod hooks, Unity objects or global GameManager reads. In particular,
`AdjustWeaponHitChanceMod` is described by the donor as a no-op mod hook: exclude
that hook architecture unless an actual retained behavior needs an adjustment.
`CalculateAttackDamage` also contains a documented enemy weapon-selection change
from classic; see DEC-03 in the [task packet](../daggerfall-task-preparation.md).

Current reuse: `src/WorldRpg.Rulesets.Daggerfall/Policies/DaggerfallFormulaPolicy.cs`,
`Modules/Combat/CombatModule.cs`, `DaggerfallRewardReactions.cs`,
`Policies/DaggerfallLootPolicy.cs`, and Kit actors/inventory over Engine Mechanics.
Similar formulas use different method names locally: do not classify absence by
name mismatch. Costs, probabilities, units, bounds, modifiers, state changes and
all required callers are resolved while drafting each task.

## FORM-01: Character values and recovery

Related coverage: F001–F010, SUP-06.

| Leaf method | Donor line(s) |
| --- | --- |
| `DamageModifier` | 66 |
| `MaxEncumbrance` | 75 |
| `SpellPoints` | 84 |
| `MagicResist` | 93 |
| `ToHitModifier` | 102 |
| `HitPointsModifier` | 111 |
| `HealingRateModifier` | 120 |
| `MaxStatValue` | 131 |
| `BonusPool` | 140 |
| `RollMaxHealth` | 160 |
| `CalculateHealthRecoveryRate` | 178 |
| `CalculateFatigueRecoveryRate` | 209 |
| `CalculateSpellPointRecoveryRate` | 219 |

## FORM-02: Skills, advancement and interaction

Related coverage: F015/F016, SUP-07.

| Leaf method | Donor line(s) |
| --- | --- |
| `CalculateInteriorLockpickingChance` | 232 |
| `CalculateExteriorLockpickingChance` | 243 |
| `CalculatePickpocketingChance` | 254 |
| `CalculateShopliftingChance` | 270 |
| `CalculateStealthChance` | 282 |
| `CalculateClimbingChance` | 293 |
| `CalculateSkillUsesForAdvancement` | 319 |
| `CalculatePlayerLevel` | 330 |
| `CalculateHitPointsPerLevelUp` | 340 |
| `CalculateEnemyPacification` | 357 |

## FORM-03: Temple and transformation identity

Related coverage: F034/F075, SUP-16.

| Leaf method | Donor line(s) |
| --- | --- |
| `CalculateTempleBlessing` | 394 |
| `GetVampireClan` | 400 |

## FORM-04: Physical damage and timing

Related coverage: F011/F013/F017/F020.

| Leaf method | Donor line(s) |
| --- | --- |
| `CalculateHandToHandMinDamage` | 434 |
| `CalculateHandToHandMaxDamage` | 443 |
| `CalculateWeaponMinDamage` | 455 |
| `CalculateWeaponMaxDamage` | 490 |
| `CalculateAttackDamage` | 538 |
| `CalculateWeaponAttackDamage` | 730 |
| `CalculateHandToHandAttackDamage` | 771 |
| `GetMeleeWeaponAnimTime` | 830 |
| `GetBowCooldownTime` | 840 |
| `CalculateSwingModifiers` | 873 |
| `CalculateProficiencyModifiers` | 908 |
| `CalculateRacialModifiers` | 933 |
| `CalculateBackstabChance` | 964 |
| `CalculateBackstabDamage` | 978 |
| `GetBonusOrPenaltyByEnemyType` | 993 |
| `AdjustWeaponAttackDamage` | 1068 |

## FORM-05: Hit selection and equipment wear

Related coverage: F011/F012/F041.

| Leaf method | Donor line(s) |
| --- | --- |
| `CalculateSuccessfulHit` | 796 |
| `CalculateStruckBodyPart` | 863 |
| `AdjustWeaponHitChanceMod` | 1059 |
| `DamageEquipment` | 1080 |
| `ApplyConditionDamageThroughPhysicalHit` | 1123 |
| `CalculateWeaponToHit` | 1140 |
| `CalculateArmorToHit` | 1149 |
| `CalculateAdrenalineRushToHit` | 1163 |
| `CalculateStatsToHit` | 1185 |
| `CalculateSkillsToHit` | 1202 |
| `CalculateAdjustmentsToHit` | 1224 |

## FORM-06: Effect admission and consequences

Related coverage: F014/F026/F031/F032.

| Leaf method | Donor line(s) |
| --- | --- |
| `CalculateCasterLevel` | 850 |
| `OnMonsterHit` | 1263 |
| `InflictPoison` | 1371 |
| `SavingThrow` | 1443, 1559 |
| `ModifyEffectAmount` | 1575 |
| `GetResistanceModifier` | 1654 |
| `InflictDisease` | 1680 |
| `FatigueDamage` | 1717 |
| `GetToleranceFlag` | 1421 |
| `GetEffectFlags` | 1592 |
| `GetElementType` | 1630 |

## FORM-07: Enemies and encounters

Related coverage: F007/F072.

| Leaf method | Donor line(s) |
| --- | --- |
| `RollEnemyClassMaxHealth` | 1745 |
| `RollRandomSpawn_LocationNight` | 1765 |
| `RollRandomSpawn_WildernessDay` | 1778 |
| `RollRandomSpawn_WildernessNight` | 1791 |
| `RollRandomSpawn_Dungeon` | 1804 |
| `GetEnemyEntityWeightInClassicUnits` | 2881 |
| `GetEnemyEntityEnemyGroup` | 2746 |
| `GetEnemyEntityLanguageSkill` | 2808 |

## FORM-08: Calendar and world identity

Related coverage: F050/F136, SUP-02.

| Leaf method | Donor line(s) |
| --- | --- |
| `GetHolidayId` | 1819 |
| `GenerateBuildingName` | 2923 |

## FORM-09: Economy, services and regional prices

Related coverage: F040/F074/F077/F078, SUP-11/SUP-13/SUP-16.

| Leaf method | Donor line(s) |
| --- | --- |
| `CalculateRoomCost` | 1858 |
| `CalculateCost` | 1884 |
| `CalculateItemRepairCost` | 1901 |
| `CalculateItemRepairTime` | 1924 |
| `CalculateItemIdentifyCost` | 1935 |
| `CalculateDaedraSummoningCost` | 1958 |
| `CalculateDaedraSummoningChance` | 1967 |
| `CalculateTradePrice` | 1976 |
| `CalculateMaxBankLoan` | 2006 |
| `CalculateBankLoanRepayment` | 2017 |
| `ApplyRegionalPriceAdjustment` | 2026 |
| `RandomizeInitialRegionalPrices` | 2047 |
| `UpdateRegionalPrices` | 2053 |

## FORM-10: Items and material selection

Related coverage: F037–F042.

| Leaf method | Donor line(s) |
| --- | --- |
| `IsItemStackable` | 2096 |
| `RandomMaterial` | 2118 |
| `RandomArmorMaterial` | 2152 |

## FORM-11: Spell construction and enchantment costs

Related coverage: F026/F033/F036.

| Leaf method | Donor line(s) |
| --- | --- |
| `CalculateTotalEffectCosts` | 2203 |
| `CalculateEffectCosts` | 2248, 2261 |
| `ApplyTargetCostMultiplier` | 2345 |
| `GetSpellEnchantPtCost` | 2387 |
| `CalculateCastingCost` | 2411 |
| `getCostFromSettings` | 2590 |
| `GetItemEnchantmentPower` | 2660 |
| `GetWeaponEnchantmentMultiplier` | 2680 |
| `GetArmorEnchantmentMultiplier` | 2711 |

## FORM-12: Excluded mod registration infrastructure

Related coverage: F110 / excluded donor topology.

| Leaf method | Donor line(s) |
| --- | --- |
| `RegisterOverride` | 3141 |

## Additional behavior exposed by this inventory

Do not omit temple blessings, clan selection, regional-price initialization and
updates, holiday lookup, enemy classic weight, building names, or cost modifiers
merely because they were not separate feature-map rows. Assign them to the linked
feature/SUP family when creating tasks. Preserve the exact distinction between
random selection, pure calculation and an operation that mutates actor/item state.

Direct source inspection of attack damage confirms material gating, swing/racial/
proficiency factors, body-part selection, monster attack sets, poisoned weapons,
equipment wear and artifact interaction in one donor flow. Split implementation
responsibilities along the existing Rusty owners while keeping the required
ordering and shared accepted results. The donor's combined method is not a reason
to create a new god combat class or copy its entire shape.
