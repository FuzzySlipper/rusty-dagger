namespace WorldRpg.Rulesets.Daggerfall.Policies;

using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// Named, compiled Daggerfall formulas.  The donor catalogs are evidence for
/// these policies; they are not an evaluator or a runtime rules language.
/// Values which are part of a selected profile live in the tuning record so a
/// caller can identify and replace them without hunting through call sites.
/// </summary>
internal static class DaggerfallFormulaPolicy
{
    internal static DaggerfallFormulaTuning Classic { get; } = new();
    // The current live profile is intentionally named separately from the
    // classic skill-sum profile even while it shares the 500-XP tuning value.
    internal static DaggerfallFormulaTuning Experimental { get; } = Classic with { ExperiencePerLevel = 500 };

    internal static int DamageModifier(int strength, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(strength - selected.AttributeBaseline, selected.DamageModifierDivisor);
    }

    internal static int ToHitModifier(int agility, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(agility, selected.ToHitAttributeDivisor) - selected.ToHitBaseline;
    }

    internal static int HitPointsModifier(int endurance, DaggerfallFormulaTuning? tuning = null) =>
        FloorDivide(endurance - tuning.GetValueOrDefault(Classic).AttributeBaseline, tuning.GetValueOrDefault(Classic).AttributeDivisor);

    internal static int HealingRateModifier(int endurance, DaggerfallFormulaTuning? tuning = null) =>
        HitPointsModifier(endurance, tuning);

    internal static int MagicResist(int willpower, DaggerfallFormulaTuning? tuning = null) =>
        FloorDivide(willpower, tuning.GetValueOrDefault(Classic).AttributeDivisor);

    internal static int MaxEncumbrance(int strength, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(checked(strength * selected.EncumbranceNumerator), selected.EncumbranceDenominator);
    }

    internal static int MaxBreath(int endurance, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(endurance, selected.BreathDivisor);
    }

    internal static int MaxFatigue(int strength, int endurance, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked((strength + endurance) * selected.FatigueUnitsPerAttributePoint);
    }

    /// <summary>Donor PlayerHealth fall policy: five health points for every metre after the five-metre grace distance.</summary>
    internal static int FallDamage(float distance)
    {
        if (!float.IsFinite(distance) || distance < 0f) throw new ArgumentOutOfRangeException(nameof(distance));
        const float GraceDistanceMetres = 5f;
        const float HealthPerMetre = 5f;
        return distance > GraceDistanceMetres
            ? checked((int)((distance - GraceDistanceMetres) * HealthPerMetre))
            : 0;
    }

    /// <summary>Donor CalculateClimbingChance: racial and effect bonuses precede the skill clamp; luck is added after interpolation.</summary>
    internal static int CalculateClimbingChance(int climbingSkill, int luck, bool khajiit, bool enhancedClimbing, int basePercentSuccess)
    {
        int effectiveSkill = checked(climbingSkill + (khajiit ? 30 : 0));
        if (enhancedClimbing) effectiveSkill = checked(effectiveSkill * 2);
        effectiveSkill = Math.Clamp(effectiveSkill, 5, 95);
        float luckBonus = Math.Clamp(luck * .01f, 0f, 1f) * 10f;
        return (int)(basePercentSuccess + ((100 - basePercentSuccess) * (effectiveSkill * .01f)) + luckBonus);
    }

    /// <summary>
    /// Donor <c>FormulaHelper.CalculateStealthChance</c>, expressed over the
    /// normalized Engine distance and the already-resolved player skill. The
    /// classic distance conversion intentionally truncates before the fixed
    /// point shift; callers own the random roll that compares this chance.
    /// </summary>
    internal static int CalculateStealthChance(float distanceToTarget, int targetStealthSkill) =>
        CalculateStealthChance((double)distanceToTarget, targetStealthSkill);

    internal static int CalculateStealthChance(double distanceToTarget, int targetStealthSkill)
    {
        if (!double.IsFinite(distanceToTarget) || distanceToTarget < 0d)
            throw new ArgumentOutOfRangeException(nameof(distanceToTarget));
        if (targetStealthSkill is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(targetStealthSkill));

        int classicDistance = checked((int)(distanceToTarget / DaggerfallPerceptionQueryDefaults.ClassicGlobalScale));
        return checked(2 * ((classicDistance * targetStealthSkill) >> 10));
    }

    /// <summary>
    /// Donor <c>FormulaHelper.CalculateEnemyPacification</c>'s chance term.
    /// Etiquette and Streetwise use their social skill scaling; language skills
    /// use their direct skill value. The caller supplies the independent
    /// 0..199 roll to the boolean overload below.
    /// </summary>
    internal static int CalculateEnemyPacificationChance(
        string languageSkill,
        int languageSkillValue,
        int personality,
        bool weaponSheathed,
        int comprehendLanguagesBonus = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageSkill);
        if (languageSkillValue is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(languageSkillValue));
        if (personality is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(personality));
        if (comprehendLanguagesBonus < 0)
            throw new ArgumentOutOfRangeException(nameof(comprehendLanguagesBonus));

        int chance = languageSkill is DaggerfallSkills.Etiquette or DaggerfallSkills.Streetwise
            ? (languageSkillValue / 10) + (personality / 5)
            : languageSkillValue + (personality / 10);
        return checked(chance + (weaponSheathed ? 10 : -25) + comprehendLanguagesBonus);
    }

    /// <summary>Compares a caller-owned classic pacification roll in [0, 200).</summary>
    internal static bool CalculateEnemyPacification(
        string languageSkill,
        int languageSkillValue,
        int personality,
        bool weaponSheathed,
        int roll) =>
        CalculateEnemyPacification(languageSkill, languageSkillValue, personality, weaponSheathed, 0, roll);

    /// <summary>Compares a caller-owned classic pacification roll after effect bonus.</summary>
    internal static bool CalculateEnemyPacification(
        string languageSkill,
        int languageSkillValue,
        int personality,
        bool weaponSheathed,
        int comprehendLanguagesBonus,
        int roll)
    {
        if (roll is < 0 or >= 200)
            throw new ArgumentOutOfRangeException(nameof(roll));
        return roll < CalculateEnemyPacificationChance(languageSkill, languageSkillValue, personality,
            weaponSheathed, comprehendLanguagesBonus);
    }

    /// <summary>Donor FormulaHelper fatigue consequence: two fatigue points per accepted health point, in Daggerfall units.</summary>
    internal static int FatigueDamage(int healthDamage, DaggerfallFormulaTuning? tuning = null)
    {
        if (healthDamage < 0) throw new ArgumentOutOfRangeException(nameof(healthDamage));
        return checked(healthDamage * 2 * tuning.GetValueOrDefault(Classic).FatigueUnitsPerAttributePoint);
    }

    internal static int SpellPoints(int intelligence, int multiplierMilli, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(checked(intelligence * multiplierMilli), selected.MilliScale);
    }

    /// <summary>The classic character-sheet ceiling used by creation and level allocation.</summary>
    internal static int MaxStatValue() => 100;

    /// <summary>Validates the Engine-random creation attribute pool (six through fourteen).</summary>
    internal static int CreationBonusPool(int roll)
    {
        if (roll is < 6 or > 14) throw new ArgumentOutOfRangeException(nameof(roll));
        return roll;
    }

    /// <summary>
    /// Donor <c>FormulaHelper.RollMaxHealth</c>: level one is the base 25 plus career health,
    /// then every subsequent level consumes one caller-owned Engine random health roll.
    /// </summary>
    internal static int RollMaxHealth(int level, int hitPointsPerLevel, int endurance, Func<int, int, int> rollInclusive, DaggerfallFormulaTuning? tuning = null)
    {
        if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
        ArgumentNullException.ThrowIfNull(rollInclusive);
        int health = checked(25 + hitPointsPerLevel);
        for (int current = 1; current < level; current++)
        {
            (int minimum, int maximum) = HitPointsPerLevelRollBounds(hitPointsPerLevel, tuning);
            int roll = rollInclusive(minimum, maximum);
            if (roll < minimum || roll > maximum) throw new ArgumentOutOfRangeException(nameof(rollInclusive));
            health = checked(health + HitPointsPerLevelUp(roll, endurance, tuning));
        }
        return health;
    }

    internal static int HandToHandMinimumDamage(int skill, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked(FloorDivide(skill, selected.HandToHandMinimumDivisor) + 1);
    }

    internal static int HandToHandMaximumDamage(int skill, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked(FloorDivide(skill, selected.HandToHandMaximumDivisor) + 1);
    }

    internal static int HealthRecoveryRate(int endurance, int medical, int maximumHealth, bool rapidHealing, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        int careerBonus = rapidHealing ? selected.RapidHealingBonus : 0;
        int recovery = checked(
            HealingRateModifier(endurance, selected)
            + FloorDivide(checked((medical + selected.HealthRecoveryBase + careerBonus) * maximumHealth), selected.HealthRecoveryScale));
        return Math.Max(selected.MinimumRecoveryRate, recovery);
    }

    /// <summary>FORM-01.CalculateHealthRecoveryRate, with the donor's integer floor and minimum of one.</summary>
    internal static int CalculateHealthRecoveryRate(int endurance, int medical, int maximumHealth, bool rapidHealing, DaggerfallFormulaTuning? tuning = null) =>
        HealthRecoveryRate(endurance, medical, maximumHealth, rapidHealing, tuning);

    internal static int FatigueRecoveryRate(int maximumFatigue, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return Math.Max(selected.MinimumRecoveryRate, FloorDivide(maximumFatigue, selected.RecoveryDivisor));
    }

    /// <summary>FORM-01.CalculateFatigueRecoveryRate, retaining the donor's maximum-fatigue divisor.</summary>
    internal static int CalculateFatigueRecoveryRate(int maximumFatigue, DaggerfallFormulaTuning? tuning = null) =>
        FatigueRecoveryRate(maximumFatigue, tuning);

    internal static int SpellPointRecoveryRate(int maximumMagicka, bool noRegeneration, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        if (noRegeneration) return 0;
        return Math.Max(selected.MinimumRecoveryRate, FloorDivide(maximumMagicka, selected.RecoveryDivisor));
    }

    /// <summary>FORM-01.CalculateSpellPointRecoveryRate, including the no-regeneration career branch.</summary>
    internal static int CalculateSpellPointRecoveryRate(int maximumMagicka, bool noRegeneration, DaggerfallFormulaTuning? tuning = null) =>
        SpellPointRecoveryRate(maximumMagicka, noRegeneration, tuning);

    /// <summary>
    /// Donor <c>FormulaHelper.CalculateInteriorLockpickingChance</c> for an
    /// animating interior door. The level contribution is deliberately kept in
    /// this overload; exterior doors use a separate donor formula without it.
    /// </summary>
    internal static int CalculateInteriorLockpickingChance(int level, int lockValue, int lockpickingSkill)
    {
        int chance = checked((5 * (level - lockValue)) + lockpickingSkill);
        return Math.Clamp(chance, 5, 95);
    }

    /// <summary>
    /// Donor <c>FormulaHelper.CalculateExteriorLockpickingChance</c> for a
    /// building door leading to an interior. Its lock penalty is independent of
    /// player level and retains the donor's [5, 95] clamp.
    /// </summary>
    internal static int CalculateExteriorLockpickingChance(int lockValue, int lockpickingSkill)
    {
        int chance = checked(lockpickingSkill - (5 * lockValue));
        return Math.Clamp(chance, 5, 95);
    }

    /// <summary>Donor <c>FormulaHelper.CalculateBackstabChance</c>: an accepted facing-away opportunity contributes the live skill to hit chance.</summary>
    internal static int BackstabChance(int skill, bool targetFacingAway) => targetFacingAway ? Math.Max(0, skill) : 0;

    internal static int CalculateBackstabChance(int skill, bool targetFacingAway) => BackstabChance(skill, targetFacingAway);

    /// <summary>
    /// Donor <c>FormulaHelper.CalculateBackstabDamage</c>: with a backstabbing level above one and a
    /// successful d100 roll at or under that level the struck damage triples. The caller owns the
    /// keyed roll and only draws it when the level admits one, matching the donor's short-circuit.
    /// </summary>
    internal static int CalculateBackstabDamage(int damage, int backstabbingLevel, bool rollSucceeded) =>
        backstabbingLevel > 1 && rollSucceeded ? checked(damage * 3) : damage;

    /// <summary>
    /// Donor <c>FormulaHelper.CalculateSwingModifiers</c> over the weapon states of the player's
    /// on-screen weapon. The donor reaches this only for the player, and only the drawn weapon can
    /// carry a state, so the caller passes <see cref="DaggerfallSwingDirection.None"/> for
    /// hand-to-hand attacks and for every non-player attacker.
    /// </summary>
    internal static (int ToHit, int Damage) CalculateSwingModifiers(DaggerfallSwingDirection swing) => swing switch
    {
        DaggerfallSwingDirection.StrikeUp => (10, -4),
        DaggerfallSwingDirection.StrikeDownRight => (5, -2),
        DaggerfallSwingDirection.StrikeDownLeft => (-5, 2),
        DaggerfallSwingDirection.StrikeDown => (-10, 4),
        _ => (0, 0),
    };

    /// <summary>
    /// Donor <c>FormulaHelper.CalculateProficiencyModifiers</c>: an expert proficiency in the skill
    /// the attack uses adds the attacker's level to chance and <c>level / 3 + 1</c> to damage. The
    /// donor's second arm grants the hand-to-hand expert the same modifiers while unarmed; its own
    /// comment notes classic never applied unarmed proficiency, and the provisional donor baseline
    /// (DEC-11) keeps the live code, so callers pass whether the attack's skill is expert.
    /// </summary>
    internal static (int ToHit, int Damage) CalculateProficiencyModifiers(bool expertProficiency, int attackerLevel) =>
        expertProficiency ? (attackerLevel, checked(attackerLevel / 3 + 1)) : (0, 0);

    /// <summary>
    /// Donor <c>FormulaHelper.CalculateRacialModifiers</c>, entirely guarded by the donor's weapon
    /// check: a Dark Elf adds <c>level / 4</c> to chance and damage with any weapon, a Wood Elf adds
    /// <c>level / 3</c> with a bow, and a Redguard adds <c>level / 3</c> with any other non-bow
    /// weapon. The caller passes zero modifiers when the attack carries no weapon.
    /// </summary>
    internal static (int ToHit, int Damage) CalculateRacialModifiers(int donorRaceId, bool isArcheryWeapon, int attackerLevel)
    {
        if (donorRaceId == DonorRaceDarkElf) return (attackerLevel / 4, attackerLevel / 4);
        if (isArcheryWeapon) return donorRaceId == DonorRaceWoodElf ? (attackerLevel / 3, attackerLevel / 3) : (0, 0);
        return donorRaceId == DonorRaceRedguard ? (attackerLevel / 3, attackerLevel / 3) : (0, 0);
    }

    private const int DonorRaceDarkElf = 4;
    private const int DonorRaceWoodElf = 6;
    private const int DonorRaceRedguard = 2;

    /// <summary>
    /// Donor <c>FormulaHelper.GetBonusOrPenaltyByEnemyType</c> over the attacker's career
    /// attack-modifier flags: a Bonus flag for the target's classic enemy group adds the attacker's
    /// level, a Phobia flag subtracts it. Flag layout from <c>CLASS??.CFG</c> and the
    /// <c>ENEMY???.CFG</c> records inside MONSTER.BSA at byte 10: Undead 0x01 bonus / 0x10 phobia,
    /// Daedra 0x02 / 0x20, Humanoid 0x04 / 0x40, Animals 0x08 / 0x80. The donor reads both bits per
    /// group independently, so a career carrying both nets zero for that group.
    /// </summary>
    internal static int BonusOrPenaltyByEnemyType(int attackModifierFlags, DaggerfallEnemyGroup targetGroup, int attackerLevel) =>
        targetGroup switch
        {
            DaggerfallEnemyGroup.Undead => CareerAttackModifier(attackModifierFlags, 0x01, 0x10, attackerLevel),
            DaggerfallEnemyGroup.Daedra => CareerAttackModifier(attackModifierFlags, 0x02, 0x20, attackerLevel),
            DaggerfallEnemyGroup.Humanoid => CareerAttackModifier(attackModifierFlags, 0x04, 0x40, attackerLevel),
            DaggerfallEnemyGroup.Animals => CareerAttackModifier(attackModifierFlags, 0x08, 0x80, attackerLevel),
            _ => 0,
        };

    private static int CareerAttackModifier(int attackModifierFlags, int bonusBit, int phobiaBit, int attackerLevel)
    {
        int modifier = 0;
        if ((attackModifierFlags & bonusBit) != 0) modifier += attackerLevel;
        if ((attackModifierFlags & phobiaBit) != 0) modifier -= attackerLevel;
        return modifier;
    }

    /// <summary>
    /// Donor <c>FormulaHelper.CalculateWeaponAttackDamage</c> in source order: the weapon's base
    /// damage roll plus the caller-accumulated damage modifiers, then the Skeletal Warrior weapon
    /// rules, the strength modifier, and the weapon material modifier; a total below one becomes
    /// zero before the career bonus or penalty for the target's enemy group applies. The caller
    /// supplies the keyed base roll from the attack's authored minimum and maximum.
    /// </summary>
    internal static int CalculateWeaponAttackDamage(int baseDamage, int damageModifier, bool targetIsSkeletalWarrior,
        bool weaponIsEdged, bool weaponIsSilver, int strengthModifier, int materialDamageModifier, int enemyTypeModifier)
    {
        int damage = checked(baseDamage + damageModifier);
        if (targetIsSkeletalWarrior)
        {
            // Classic halved non-edged damage against Skeletal Warriors and doubled silver. The
            // donor reads the classic item flag 0x10 off DaggerfallUnityItem.flags; DFU leaves that
            // bit zero for every created item and its item template carries only isBluntWeapon, so
            // the classic edged property is represented by the weapon's skill instead — blades and
            // axes are edged, blunt weapons, bows and unarmed strikes are not.
            if (!weaponIsEdged) damage /= 2;
            if (weaponIsSilver) damage = checked(damage * 2);
        }
        damage = checked(damage + strengthModifier + materialDamageModifier);
        if (damage < 1) damage = 0;
        damage = checked(damage + enemyTypeModifier);
        return AdjustWeaponAttackDamage(damage);
    }

    /// <summary>
    /// Donor <c>FormulaHelper.CalculateHandToHandAttackDamage</c>: the live-skill base roll plus the
    /// caller-accumulated damage modifiers, the strength modifier for the player only (the donor's
    /// <c>player</c> gate), and the career bonus or penalty for the target's enemy group. Unlike the
    /// weapon path this donor path keeps negative totals; the shared clamp at the end of
    /// <c>CalculateAttackDamage</c> is what bounds them to zero.
    /// </summary>
    internal static int CalculateHandToHandAttackDamage(int baseDamage, int damageModifier, int strengthModifier, int enemyTypeModifier) =>
        checked(baseDamage + damageModifier + strengthModifier + enemyTypeModifier);

    /// <summary>Donor <c>FormulaHelper.AdjustWeaponAttackDamage</c>: a mod-hook seam, identity in DFU.</summary>
    internal static int AdjustWeaponAttackDamage(int damage) => damage;

    /// <summary>Donor <c>FormulaHelper.AdjustWeaponHitChanceMod</c>: a mod-hook seam, identity in DFU.</summary>
    internal static int AdjustWeaponHitChanceMod(int chanceToHitModifier) => chanceToHitModifier;

    /// <summary>
    /// Donor monster multi-attack dodge gate: each of the up-to-three attack slots is attempted only
    /// when a d100 lands under <c>50 − 10 × (player reflexes − 2)</c>. The donor applies no clamp here;
    /// a reflexes value of seven or more refuses every slot and one or two accepts every roll.
    /// </summary>
    internal static int MonsterAttackReflexChance(int playerReflexes) => 50 - 10 * (playerReflexes - 2);

    /// <summary>
    /// The classic edged property behind the Skeletal Warrior rule: the donor's item flag 0x10 has
    /// no per-record carrier in the product pack (its item template knows only isBluntWeapon), so
    /// the weapon's skill states it — blades and axes cut, blunt weapons, bows and unarmed strikes
    /// do not, which matches the classic weapon corpus the flag described.
    /// </summary>
    internal static bool WeaponIsEdged(string weaponSkill) => weaponSkill
        is DaggerfallSkills.ShortBlade or DaggerfallSkills.LongBlade or DaggerfallSkills.Axe;

    /// <summary>Classic skill-sum progression, kept separate from the live XP experiment.</summary>
    internal static int ClassicPlayerLevel(int currentLevelUpSkills, int startingLevelUpSkills, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(checked(currentLevelUpSkills - startingLevelUpSkills + selected.LevelFormulaOffset), selected.LevelFormulaDivisor);
    }

    /// <summary>The donor's level check, ordered as its starting and current skill-set sums.</summary>
    internal static int CalculatePlayerLevel(int startingLevelUpSkillsSum, int currentLevelUpSkillsSum, DaggerfallFormulaTuning? tuning = null) =>
        ClassicPlayerLevel(currentLevelUpSkillsSum, startingLevelUpSkillsSum, tuning);

    /// <summary>The selected live profile's 500-XP threshold count.</summary>
    internal static int ExperimentalXpLevel(int experience, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return Math.Max(0, FloorDivide(experience, selected.ExperiencePerLevel));
    }

    internal static int HitPointsPerLevelUp(int roll, int endurance, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return Math.Max(selected.MinimumRecoveryRate, checked(roll + HealingRateModifier(endurance, selected)));
    }

    internal static (int Minimum, int Maximum) HitPointsPerLevelRollBounds(int hitPointsPerLevel, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        if (hitPointsPerLevel < selected.MinimumHitPointsPerLevel) throw new ArgumentOutOfRangeException(nameof(hitPointsPerLevel));
        return (Math.Max(selected.MinimumHitPointsPerLevel, FloorDivide(hitPointsPerLevel, selected.HitPointsRollDivisor)), hitPointsPerLevel);
    }

    internal static int ReflexesSkillUseScaleMilli(int reflexes, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked(selected.MilliScale - ((reflexes - selected.ReflexesBaseline) * selected.ReflexesPenaltyMilli));
    }

    internal static int SkillUsesForAdvancement(int skillValue, int skillMultiplier, int careerMultiplierCenti, int level, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        selected.Validate();
        if (skillValue < 0 || skillMultiplier < 0 || careerMultiplierCenti < 0 || level < 0 || level > selected.MaximumSkillLevel) throw new ArgumentOutOfRangeException();
        long powerMilli = selected.MilliScale;
        for (int current = 0; current < level; current++) powerMilli = checked(powerMilli * selected.SkillLevelPowerMilli / selected.MilliScale);
        long numerator = checked((long)skillValue * skillMultiplier * careerMultiplierCenti * powerMilli * selected.SkillUsesNumerator);
        return checked((int)(FloorDivide(numerator, selected.SkillUsesDenominator) + 1));
    }

    /// <summary>The donor's career-float overload, retained for authored career multipliers.</summary>
    internal static int CalculateSkillUsesForAdvancement(int skillValue, int skillAdvancementMultiplier, float careerAdvancementMultiplier, int level)
    {
        if (skillValue < 0 || skillAdvancementMultiplier < 0 || !float.IsFinite(careerAdvancementMultiplier)
            || careerAdvancementMultiplier < 0 || level < 0)
            throw new ArgumentOutOfRangeException();

        double levelModifier = Math.Pow(1.04d, level);
        double uses = Math.Floor((skillValue * skillAdvancementMultiplier * careerAdvancementMultiplier * levelModifier * 2d / 5d) + 1d);
        return checked((int)uses);
    }

    /// <summary>Compatibility overload for exact-centi callers using the existing tuned profile.</summary>
    internal static int CalculateSkillUsesForAdvancement(int skillValue, int skillAdvancementMultiplier, int careerAdvancementMultiplierCenti, int level, DaggerfallFormulaTuning? tuning = null) =>
        SkillUsesForAdvancement(skillValue, skillAdvancementMultiplier, careerAdvancementMultiplierCenti, level, tuning);

    internal static int SkillAdvancementMultiplier(string skill)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skill);
        return SkillAdvancementMultipliers.TryGetValue(skill, out int value) ? value : throw new ArgumentException($"Unknown Daggerfall skill '{skill}'.", nameof(skill));
    }

    /// <summary>
    /// Donor <c>CalculateWeaponToHit</c>: the weapon's material modifier is a
    /// separate table from the minimum-metal rank used by material eligibility.
    /// </summary>
    internal static int CalculateWeaponToHit(string? weaponMaterial) => CalculateWeaponToHit(weaponMaterial, ClassicWeaponMaterialModifiers);

    internal static int CalculateWeaponToHit(string? weaponMaterial, IReadOnlyDictionary<string, int> weaponMaterialModifiers)
    {
        ArgumentNullException.ThrowIfNull(weaponMaterialModifiers);
        if (weaponMaterial is null) return 0;
        // DFU's GetWeaponMaterialModifier returns zero for an unknown material;
        // retain that donor fallback while authored content validation catches
        // an invalid material at its source boundary.
        return weaponMaterialModifiers.TryGetValue(weaponMaterial, out int modifier)
            ? checked(modifier * 10)
            : 0;
    }

    /// <summary>
    /// The same donor <c>GetWeaponMaterialModifier</c> table enters
    /// <c>CalculateWeaponAttackDamage</c> unscaled, unlike the ten-fold to-hit use:
    /// the donor's comment notes the in-game display that suggests otherwise is wrong.
    /// </summary>
    internal static int WeaponMaterialDamageModifier(string? weaponMaterial, IReadOnlyDictionary<string, int> weaponMaterialModifiers)
    {
        ArgumentNullException.ThrowIfNull(weaponMaterialModifiers);
        return weaponMaterial is null || !weaponMaterialModifiers.TryGetValue(weaponMaterial, out int modifier) ? 0 : modifier;
    }

    /// <summary>Donor <c>CalculateArmorToHit</c>: the selected body part's already-composed live armor value.</summary>
    internal static int CalculateArmorToHit(int struckArmor) => struckArmor;

    /// <summary>Donor <c>CalculateAdrenalineRushToHit</c>, including the strict one-eighth health boundary.</summary>
    internal static int CalculateAdrenalineRushToHit(bool attackerHasRush, bool attackerImprovedRush, double attackerHealth, double attackerMaximum,
        bool targetHasRush, bool targetImprovedRush, double targetHealth, double targetMaximum)
    {
        // FormulaHelper compares integer health values against integer division
        // (MaxHealth / 8). Engine tracks are double-backed, so truncate the
        // authored/current values at this classic integer boundary.
        static bool Active(bool hasRush, double health, double maximum) => hasRush
            && Math.Truncate(health) < Math.Truncate(Math.Truncate(maximum) / 8d);
        if (!double.IsFinite(attackerHealth) || !double.IsFinite(attackerMaximum) || attackerMaximum < 0d
            || !double.IsFinite(targetHealth) || !double.IsFinite(targetMaximum) || targetMaximum < 0d)
            throw new ArgumentOutOfRangeException(nameof(attackerHealth));
        int result = Active(attackerHasRush, attackerHealth, attackerMaximum) ? attackerImprovedRush ? 8 : 5 : 0;
        return checked(result - (Active(targetHasRush, targetHealth, targetMaximum) ? targetImprovedRush ? 8 : 5 : 0));
    }

    /// <summary>Donor <c>CalculateStatsToHit</c>, preserving C# integer truncation toward zero for negative differentials.</summary>
    internal static int CalculateStatsToHit(int attackerLuck, int targetLuck, int attackerAgility, int targetAgility, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked(TruncateDivide(attackerLuck - targetLuck, selected.HitChanceAttributeDivisor)
            + TruncateDivide(attackerAgility - targetAgility, selected.HitChanceAttributeDivisor));
    }

    /// <summary>Donor <c>CalculateSkillsToHit</c>: target dodging always applies; a separately keyed critical-strike success supplies its bonus.</summary>
    internal static int CalculateSkillsToHit(int targetDodging, int attackerCriticalStrike, bool criticalStrikeSucceeded, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked(-FloorDivide(targetDodging, selected.HitChanceDodgingDivisor)
            + (criticalStrikeSucceeded ? FloorDivide(attackerCriticalStrike, selected.HitChanceAttributeDivisor) : 0));
    }

    /// <summary>Donor <c>CalculateAdjustmentsToHit</c>: biography avoidance, the monster bonus, then the classic -50 baseline.</summary>
    internal static int CalculateAdjustmentsToHit(bool targetIsMonster, int targetBiographyAvoidHit, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked((targetIsMonster ? 40 : 0) - targetBiographyAvoidHit + selected.HitChanceBase);
    }

    /// <summary>One complete donor hit pipeline before its caller compares the independently drawn 1..100 roll.</summary>
    internal static int CalculateSuccessfulHitChance(int chanceToHitModifier, int struckArmor, int adrenalineRush, int stats, int skills, int adjustments,
        DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return Math.Clamp(checked(chanceToHitModifier + CalculateArmorToHit(struckArmor) + adrenalineRush + stats + skills + adjustments),
            selected.MinimumHitChance, selected.MaximumHitChance);
    }

    /// <summary>Boolean donor-shaped overload retained for callers that already own their actual Engine random roll.</summary>
    internal static bool CalculateSuccessfulHit(int chanceToHitModifier, int struckArmor, int adrenalineRush, int stats, int skills, int adjustments, int roll,
        DaggerfallFormulaTuning? tuning = null)
    {
        if (roll is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(roll));
        return roll <= CalculateSuccessfulHitChance(chanceToHitModifier, struckArmor, adrenalineRush, stats, skills, adjustments, tuning);
    }

    /// <summary>Compatibility read for existing callers that do not own weapon, adrenaline, or critical-strike inputs.</summary>
    internal static int CalculateHitChance(int skill, int struckArmor, int attackerLuck, int targetLuck, int attackerAgility, int targetAgility, int targetDodging, int targetBiographyAvoidHit = 0, DaggerfallFormulaTuning? tuning = null) =>
        CalculateSuccessfulHitChance(skill, struckArmor, 0,
            CalculateStatsToHit(attackerLuck, targetLuck, attackerAgility, targetAgility, tuning),
            CalculateSkillsToHit(targetDodging, 0, false, tuning),
            CalculateAdjustmentsToHit(false, targetBiographyAvoidHit, tuning), tuning);

    /// <summary>Classic material gate: a weapon must meet the target's minimum material.</summary>
    internal static bool CanHitMaterial(string? weaponMaterial, string? targetMinimumMaterial, IReadOnlyDictionary<string, int> weaponMaterialRanks)
    {
        if (targetMinimumMaterial is null) return true;
        if (weaponMaterial is null) return false;
        return weaponMaterialRanks.TryGetValue(weaponMaterial, out int weaponRank)
            && weaponMaterialRanks.TryGetValue(targetMinimumMaterial, out int targetRank)
            && weaponRank >= targetRank;
    }

    /// <summary>
    /// The classic weapon-material ladder is distinct from armor values:
    /// leather and chain are armor-only materials and cannot satisfy a weapon
    /// minimum-metal gate.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> ClassicWeaponMaterialRanks { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["iron"] = 0, ["steel"] = 1, ["silver"] = 2, ["elven"] = 3, ["dwarven"] = 4,
        ["mithril"] = 5, ["adamantium"] = 6, ["ebony"] = 7, ["orcish"] = 8, ["daedric"] = 9,
    };

    /// <summary>
    /// Donor <c>DaggerfallUnityItem.GetWeaponMaterialModifier</c>, the single weapon-material table
    /// feeding both <c>CalculateWeaponToHit</c> and the material term of
    /// <c>CalculateWeaponAttackDamage</c>. Steel and silver share the zero modifier; mithril and
    /// adamantium share the three modifier.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> ClassicWeaponMaterialModifiers { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["iron"] = -1, ["steel"] = 0, ["silver"] = 0, ["elven"] = 1, ["dwarven"] = 2,
        ["mithril"] = 3, ["adamantium"] = 3, ["ebony"] = 4, ["orcish"] = 5, ["daedric"] = 6,
    };

    /// <summary>Maps a donor 0..19 body roll to the selected struck body part.</summary>
    internal static int CalculateStruckBodyPart(int roll) => StruckBodyPart(roll);

    internal static int StruckBodyPart(int roll)
    {
        if ((uint)roll >= StruckBodyTable.Length) throw new ArgumentOutOfRangeException(nameof(roll));
        return StruckBodyTable[roll];
    }

    /// <summary>
    /// Classic enemy-class health: a 10-point base plus one inclusive [1, hitPointsPerLevel]
    /// roll per level. Donor: <c>FormulaHelper.RollEnemyClassMaxHealth</c>, which draws each
    /// roll from <c>UnityEngine.Random.Range(1, hitPointsPerLevel + 1)</c>; the caller supplies
    /// the roll so keyed product RNG stays at the call site.
    /// </summary>
    internal static int RollEnemyClassMaxHealth(int level, int hitPointsPerLevel, Func<int, int, int> rollInclusive)
    {
        ArgumentNullException.ThrowIfNull(rollInclusive);
        if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));
        if (hitPointsPerLevel < 1) throw new ArgumentOutOfRangeException(nameof(hitPointsPerLevel));
        int maxHealth = EnemyClassBaseHealth;
        for (int i = 0; i < level; i++)
        {
            int roll = rollInclusive(1, hitPointsPerLevel);
            if (roll < 1 || roll > hitPointsPerLevel)
                throw new InvalidOperationException($"Enemy class health roll {roll} is outside [1, {hitPointsPerLevel}].");
            maxHealth = checked(maxHealth + roll);
        }

        return maxHealth;
    }

    /// <summary>
    /// Classic enemy grouping for one actor definition. Donor:
    /// <c>FormulaHelper.GetEnemyEntityEnemyGroup</c>, which switches on the career index for both
    /// monsters and class enemies alike — a class enemy therefore lands wherever its class index
    /// falls among the monster arms (the pack thief reads as Humanoid through the Nymph arm, the
    /// pack archer through the Harpy arm). That coincidence is replicated rather than repaired:
    /// both published class enemies group as Humanoid, which is also the semantically honest
    /// answer their future pacify/charm consumers need.
    /// </summary>
    internal static DaggerfallEnemyGroup EnemyGroupFor(DaggerfallActorDefinition actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.MobileId is int mobileId ? EnemyGroupFor(actor.Kind, mobileId) : DaggerfallEnemyGroup.None;
    }

    internal static DaggerfallEnemyGroup EnemyGroupFor(string kind, int mobileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (kind != DaggerfallActorKinds.EnemyClass && kind != DaggerfallActorKinds.Monster) return DaggerfallEnemyGroup.None;
        int careerIndex = kind == DaggerfallActorKinds.EnemyClass ? mobileId - EnemyClassMobileBase : mobileId;
        return careerIndex switch
        {
            0 or 3 or 4 or 5 or 6 or 11 or 20 or 34 or 39 or 40 => DaggerfallEnemyGroup.Animals,
            1 or 2 or 7 or 8 or 9 or 10 or 12 or 13 or 14 or 16 or 21 or 22 or 24 or 41 or 42 => DaggerfallEnemyGroup.Humanoid,
            15 or 17 or 18 or 19 or 23 or 28 or 30 or 32 or 33 => DaggerfallEnemyGroup.Undead,
            25 or 26 or 27 or 29 or 31 => DaggerfallEnemyGroup.Daedra,
            _ => DaggerfallEnemyGroup.None,
        };
    }

    /// <summary>
    /// Classic language skill for one actor definition, as the donor's dialogue eligibility reads
    /// it. Donor: <c>FormulaHelper.GetEnemyEntityLanguageSkill</c>. A null answer is the donor's
    /// <c>Skills.None</c>: the actor has no language skill to check.
    /// </summary>
    internal static string? LanguageSkillFor(DaggerfallActorDefinition actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.MobileId is int mobileId ? LanguageSkillFor(actor.Kind, mobileId) : null;
    }

    internal static string? LanguageSkillFor(string kind, int mobileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (kind == DaggerfallActorKinds.EnemyClass)
        {
            // Donor note, kept: classic uses Etiquette for every class. The DFU baseline this
            // task adopts instead gives the six roguish classes Streetwise (DEC-11 provisional).
            return (mobileId - EnemyClassMobileBase) switch
            {
                5 or 7 or 8 or 9 or 10 or 11 => DaggerfallSkills.Streetwise,
                _ => DaggerfallSkills.Etiquette,
            };
        }

        if (kind != DaggerfallActorKinds.Monster) return null;
        return mobileId switch
        {
            7 or 12 or 21 or 24 => DaggerfallSkills.Orcish,
            13 => DaggerfallSkills.Harpy,
            16 or 22 => DaggerfallSkills.Giantish,
            34 or 40 => DaggerfallSkills.Dragonish,
            10 or 42 => DaggerfallSkills.Nymph,
            25 or 26 or 27 or 29 or 31 => DaggerfallSkills.Daedric,
            2 => DaggerfallSkills.Spriggan,
            8 => DaggerfallSkills.Centaurian,
            1 or 41 => DaggerfallSkills.Impish,
            28 or 30 or 32 or 33 => DaggerfallSkills.Etiquette,
            _ => null,
        };
    }

    /// <summary>
    /// Classic encumbrance weight in quarter-unit steps: the donor's base body weight plus four
    /// times the carried item weight, truncating any fractional carried weight the way the
    /// donor's <c>(int)</c> cast does. Donor:
    /// <c>FormulaHelper.GetEnemyEntityWeightInClassicUnits</c>.
    /// </summary>
    internal static int ActorWeightInClassicUnits(int baseWeight, double carriedItemWeight)
    {
        if (baseWeight < 0) throw new ArgumentOutOfRangeException(nameof(baseWeight));
        if (!double.IsFinite(carriedItemWeight) || carriedItemWeight < 0) throw new ArgumentOutOfRangeException(nameof(carriedItemWeight));
        return checked(baseWeight + (int)(carriedItemWeight * WeightCarriedToClassicUnits));
    }

    /// <summary>
    /// The donor's base body weight: a monster weighs what its mobile record states, while a
    /// class enemy weighs 240 or 350 classic units by gender. The pack carries no gender for
    /// class enemies, so the caller supplies it; monsters without a mobile weight are rejected
    /// rather than defaulted.
    /// </summary>
    internal static int EnemyBaseWeight(string kind, int? mobileWeight, bool female)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (kind == DaggerfallActorKinds.Monster)
            return mobileWeight ?? throw new ArgumentException("A monster actor needs its mobile weight.", nameof(mobileWeight));
        if (kind == DaggerfallActorKinds.EnemyClass) return female ? FemaleClassBaseWeight : MaleClassBaseWeight;
        throw new ArgumentException($"Actor kind '{kind}' has no classic body weight.", nameof(kind));
    }

    /// <summary>
    /// The donor's item-condition display unit. Items without condition use are complete rather than
    /// dividing by zero; otherwise the classic integer percentage truncates toward zero.
    /// </summary>
    internal static int ConditionPercentage(int currentCondition, int maximumCondition)
    {
        if (currentCondition < 0 || maximumCondition < 0 || currentCondition > maximumCondition)
            throw new ArgumentOutOfRangeException(nameof(currentCondition), "Item condition must remain within its maximum.");
        return maximumCondition == 0 ? 100 : (int)(100L * currentCondition / maximumCondition);
    }

    private static int FloorDivide(int value, int divisor) => value >= 0 ? value / divisor : -checked(((-value) + divisor - 1) / divisor);
    private static long FloorDivide(long value, long divisor) => value >= 0 ? value / divisor : -checked(((-value) + divisor - 1) / divisor);
    /// <summary>Classic combat text reports whole health points even though Engine tracks preserve fractional current state.</summary>
    internal static int DisplayDamage(double healthLost)
    {
        if (!double.IsFinite(healthLost)) throw new ArgumentOutOfRangeException(nameof(healthLost));
        return checked((int)Math.Truncate(Math.Max(0d, healthLost)));
    }

    private static int TruncateDivide(int value, int divisor) => value / divisor;

    private static readonly int[] StruckBodyTable = [0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 6];

    /// <summary>The donor's class-enemy health base every level roll adds to.</summary>
    private const int EnemyClassBaseHealth = 10;

    /// <summary>Donor humanoid mobile ids start here; a class enemy's career index is its mobile id minus this base.</summary>
    private const int EnemyClassMobileBase = 128;

    /// <summary>Classic weight counts carried items at four times their listed weight.</summary>
    private const int WeightCarriedToClassicUnits = 4;

    /// <summary>Donor base body weights for class enemies by gender, in classic units.</summary>
    private const int FemaleClassBaseWeight = 240;
    private const int MaleClassBaseWeight = 350;

    internal static IReadOnlyDictionary<string, int> SkillAdvancementMultipliers { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["medical"] = 12, ["etiquette"] = 1, ["streetwise"] = 1, ["mercantile"] = 1, ["swimming"] = 1,
        ["backstabbing"] = 1, ["destruction"] = 1, ["illusion"] = 1, ["alteration"] = 1, ["mysticism"] = 1,
        ["archery"] = 1, ["lockpicking"] = 2, ["pickpocket"] = 2, ["stealth"] = 2, ["climbing"] = 2,
        ["restoration"] = 2, ["thaumaturgy"] = 2, ["short-blade"] = 2, ["long-blade"] = 2, ["hand-to-hand"] = 2,
        ["axe"] = 2, ["blunt-weapon"] = 2, ["dodging"] = 4, ["jumping"] = 5, ["critical-strike"] = 8,
        ["orcish"] = 15, ["harpy"] = 15, ["giantish"] = 15, ["dragonish"] = 15, ["nymph"] = 15,
        ["daedric"] = 15, ["spriggan"] = 15, ["centaurian"] = 15, ["impish"] = 15, ["running"] = 50,
    };
}

/// <summary>Named profile values used by <see cref="DaggerfallFormulaPolicy"/>.</summary>
internal sealed record DaggerfallFormulaTuning(
    int AttributeBaseline = 50,
    int AttributeDivisor = 10,
    int DamageModifierDivisor = 5,
    int ToHitAttributeDivisor = 10,
    int ToHitBaseline = 5,
    int EncumbranceNumerator = 3,
    int EncumbranceDenominator = 2,
    int BreathDivisor = 2,
    int FatigueUnitsPerAttributePoint = 64,
    int MilliScale = 1000,
    int HandToHandMinimumDivisor = 10,
    int HandToHandMaximumDivisor = 5,
    int RapidHealingBonus = 40,
    int HealthRecoveryBase = 60,
    int HealthRecoveryScale = 1000,
    int MinimumRecoveryRate = 1,
    int RecoveryDivisor = 8,
    int LevelFormulaOffset = 28,
    int LevelFormulaDivisor = 15,
    int ExperiencePerLevel = 500,
    int MinimumHitPointsPerLevel = 1,
    int HitPointsRollDivisor = 2,
    int ReflexesBaseline = 2,
    int ReflexesPenaltyMilli = 125,
    int SkillLevelPowerMilli = 1040,
    int SkillUsesNumerator = 2,
    int SkillUsesDenominator = 500000,
    int MaximumSkillLevel = 64,
    int HitChanceBase = -50,
    int HitChanceAttributeDivisor = 10,
    int HitChanceDodgingDivisor = 4,
    int MinimumHitChance = 3,
    int MaximumHitChance = 97)
{
    internal DaggerfallFormulaTuning Validate()
    {
        if (AttributeDivisor <= 0 || DamageModifierDivisor <= 0 || ToHitAttributeDivisor <= 0
            || EncumbranceDenominator <= 0 || BreathDivisor <= 0 || MilliScale <= 0
            || HandToHandMinimumDivisor <= 0 || HandToHandMaximumDivisor <= 0
            || HealthRecoveryScale <= 0 || RecoveryDivisor <= 0 || LevelFormulaDivisor <= 0
            || ExperiencePerLevel <= 0 || HitPointsRollDivisor <= 0 || SkillUsesDenominator <= 0
            || MaximumSkillLevel < 0 || HitChanceAttributeDivisor <= 0 || HitChanceDodgingDivisor <= 0
            || MinimumHitChance > MaximumHitChance)
            throw new ArgumentException("Daggerfall formula tuning contains an invalid divisor, bound, or level range.", nameof(DaggerfallFormulaTuning));
        return this;
    }
}

file static class NullableTuningExtensions
{
    internal static DaggerfallFormulaTuning GetValueOrDefault(this DaggerfallFormulaTuning? tuning, DaggerfallFormulaTuning fallback) => (tuning ?? fallback).Validate();
}
