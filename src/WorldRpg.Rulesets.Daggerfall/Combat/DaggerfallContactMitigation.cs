using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using EquipmentAssignment = WorldRpg.Kit.Inventory.EquipmentAssignment;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// Physical mitigation by contact. A blow that connects is never missed; the target instead deflects or
/// absorbs it, by channel:
/// <list type="bullet">
/// <item>The weapon-material requirement is a target property: an immune target is never harmed, and a
/// player whose racial override requires silver is cut only by silver or better.</item>
/// <item>Deflection is the target's chance (Dodging and Agility) to turn the blow aside. On a channel the
/// tuning marks full it negates the whole blow before damage is rolled; on a partial channel it turns aside
/// a share of the rolled damage.</item>
/// <item>Absorption is the armour or shield covering the struck body part (or a monster's natural armour),
/// scaled per channel, taken off the damage that reaches it.</item>
/// </list>
/// </summary>
internal sealed class DaggerfallContactMitigation(DaggerfallContactInputs session, DaggerfallContactTuning tuning, DaggerfallContactGuard guard)
{
    /// <summary>The deflection draw's salt.</summary>
    internal const int DeflectionSalt = 71;

    /// <summary>Whether the target turns the whole harm away before it is calculated.</summary>
    public void Negate(NegationEvent negation, DaggerfallPhysicalHarm harm)
    {
        string? minimum = harm.TargetId == DaggerfallActorIdentity.PlayerEntityId && session.Character()?.RacialOverrides?.Current?.RequiresSilver == true
            ? "silver" : harm.Target.MinimumMaterial;
        if (harm.WeaponMaterial is not null
            && !DaggerfallFormulaPolicy.CanHitMaterial(harm.WeaponMaterial, minimum, DaggerfallFormulaPolicy.ClassicWeaponMaterialRanks))
            negation.Negation = HarmNegation.Immune;
        if (negation.Negated || !DaggerfallHarmChannels.IsPhysical(negation.Channel)) return;
        if (tuning.DeflectionFor(negation.Channel) is { Mode: DaggerfallDeflectionMode.Full } && Deflects(harm))
            negation.Negation = HarmNegation.Deflected;
    }

    /// <summary>What the target deflects, absorbs or resists of the calculated harm.</summary>
    public void Mitigate(MitigationEvent mitigation, DaggerfallPhysicalHarm harm)
    {
        if (!DaggerfallHarmChannels.IsPhysical(mitigation.Channel)) return;
        if (tuning.DeflectionFor(mitigation.Channel) is { Mode: DaggerfallDeflectionMode.Partial, Fraction: double fraction } && Deflects(harm))
            mitigation.Deflected += (int)Math.Round(mitigation.Incoming * fraction, MidpointRounding.AwayFromZero);
        mitigation.Absorbed += Absorption(harm, mitigation.Channel);
    }

    /// <summary>
    /// The target's deflection chance in percent: a base, its Dodging skill, Agility and Luck above average, and the
    /// player's biography. A
    /// player holding a guard up adds the guard's bonus, more behind a shield, up to the guard's own cap.
    /// </summary>
    internal int DeflectionChance(long target)
    {
        if (session.StatsOf(target) is not StatsComponent stats) return 0;
        double chance = tuning.DeflectionBaseChance
            + Read(stats, DaggerfallMechanicsIds.Dodging) * tuning.DeflectionPerDodgingPoint
            + (Read(stats, DaggerfallMechanicsIds.Agility) - 50) * tuning.DeflectionPerAgilityAbove50
            + (Read(stats, DaggerfallMechanicsIds.Luck) - 50) * tuning.DeflectionPerLuckAbove50
            + (target == DaggerfallActorIdentity.PlayerEntityId ? session.PlayerAvoidHit() : 0);
        double maximum = tuning.DeflectionMaximumChance;
        if (target == DaggerfallActorIdentity.PlayerEntityId && guard.Raised)
        {
            chance += tuning.GuardDeflectionBonus + (HoldsShield(target) ? tuning.GuardShieldDeflectionBonus : 0d);
            maximum = Math.Max(maximum, tuning.GuardMaximumChance);
        }
        return (int)Math.Clamp(Math.Round(chance, MidpointRounding.AwayFromZero), 0d, maximum);
    }

    private bool HoldsShield(long owner) => session.EquipmentOf(owner).Assignments.Any(assignment => assignment.Slot.Value == "left-hand"
        && session.Definitions.RequireItem(new DaggerfallItemId(assignment.Item.Definition.Value)).Shield is not null);

    /// <summary>What the target's protection on the struck body part takes off one blow on a channel.</summary>
    internal int Absorption(DaggerfallPhysicalHarm harm, HarmChannelId channel)
    {
        double points = 0d;
        string slot = DaggerCombatRules.BodySlot(harm.BodyPart);
        foreach (EquipmentAssignment assignment in session.EquipmentOf(harm.TargetId).Assignments)
        {
            DaggerfallItemDefinition item = session.Definitions.RequireItem(new DaggerfallItemId(assignment.Item.Definition.Value));
            if (item.Armor is { } worn && assignment.Slot.Value == slot)
                points += session.Definitions.ArmorValuesByMaterial[worn.Material] * tuning.AbsorptionPerArmorPoint;
            else if (item.Shield is { } shield && assignment.Slot.Value == "left-hand" && DaggerCombatRules.ShieldCovers(item, harm.BodyPart))
                points += shield.Armor * tuning.AbsorptionPerShieldPoint;
        }
        // The player's worn armor enchantments shift its whole armor value once, not any one piece; the
        // donor's value falls as armor strengthens, so a negative shift absorbs more.
        if (harm.TargetId == DaggerfallActorIdentity.PlayerEntityId)
            points = Math.Max(0d, points - session.PlayerArmorValueModifier() * tuning.AbsorptionPerArmorValueShift);
        // A monster's natural armour class is lower the tougher its hide; the baseline is an unarmoured beast.
        if (harm.Target.Kind == DaggerfallActorKinds.Monster)
            points += Math.Max(0d, tuning.MonsterArmorBaseline - harm.Target.Armor) * tuning.MonsterAbsorptionPerPoint;
        return (int)Math.Round(points * tuning.AbsorptionMultiplier(channel), MidpointRounding.AwayFromZero);
    }

    private bool Deflects(DaggerfallPhysicalHarm harm)
    {
        // A civilian's lightweight definition carries no combat skills, and it has none to turn a blow aside with.
        if (harm.Target.Kind == DaggerfallActorKinds.Civilian) return false;
        int chance = DeflectionChance(harm.TargetId);
        if (chance <= 0) return false;
        bool enemy = harm.AttackerId != DaggerfallActorIdentity.PlayerEntityId;
        long roll = session.Random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, enemy ? CombatRandomKey.EnemyScope : CombatRandomKey.PlayerScope,
            CombatRandomKey.For(harm.Generation, harm.SimulationStep, harm.AttackerId, harm.TargetId, DeflectionSalt), 1, 100)).Value;
        return roll <= chance;
    }

    private static int Read(StatsComponent stats, DaggerfallStatId stat) => stats.GetStat(StatId.Parse(stat.Value)).ValueInt;
}
