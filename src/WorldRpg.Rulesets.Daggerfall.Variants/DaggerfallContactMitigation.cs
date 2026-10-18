using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using EquipmentAssignment = WorldRpg.Kit.Inventory.EquipmentAssignment;

namespace WorldRpg.Rulesets.Daggerfall.Variants;

/// <summary>
/// Physical mitigation under the contact + mitigation variant. A blow that connects is never missed; the
/// target instead deflects or absorbs it, by channel:
/// <list type="bullet">
/// <item>The weapon-material requirement stays a target property: an immune target is never harmed.</item>
/// <item>Deflection is the target's chance (Dodging and Agility) to turn the blow aside. On a channel the
/// tuning marks full it negates the whole blow before damage is rolled; on a partial channel it turns aside
/// a share of the rolled damage.</item>
/// <item>Absorption is the armour or shield covering the struck body part (or a monster's natural armour),
/// scaled per channel, taken off the damage that reaches it.</item>
/// </list>
/// </summary>
internal sealed class DaggerfallContactMitigation(DaggerfallSeamContext context, DaggerfallContactTuning tuning) : IDaggerfallPhysicalMitigation
{
    /// <summary>The variant's own deflection draw, keyed beside classic's combat salts without sharing one.</summary>
    internal const int DeflectionSalt = 71;
    private readonly DaggerfallClassicPhysicalMitigation _material = new(context.Session.Character);

    public void Negate(NegationEvent negation, DaggerfallPhysicalHarm harm)
    {
        _material.Negate(negation, harm);
        if (negation.Negated || !DaggerfallHarmChannels.IsPhysical(negation.Channel)) return;
        if (tuning.DeflectionFor(negation.Channel) is { Mode: DaggerfallDeflectionMode.Full } && Deflects(harm))
            negation.Negation = HarmNegation.Deflected;
    }

    public void Mitigate(MitigationEvent mitigation, DaggerfallPhysicalHarm harm)
    {
        if (!DaggerfallHarmChannels.IsPhysical(mitigation.Channel)) return;
        if (tuning.DeflectionFor(mitigation.Channel) is { Mode: DaggerfallDeflectionMode.Partial, Fraction: double fraction } && Deflects(harm))
            mitigation.Deflected += (int)Math.Round(mitigation.Incoming * fraction, MidpointRounding.AwayFromZero);
        mitigation.Absorbed += Absorption(harm, mitigation.Channel);
    }

    /// <summary>The target's deflection chance in percent: a base, its Dodging skill, and Agility above average.</summary>
    internal int DeflectionChance(long target)
    {
        if (context.Session.StatsOf(target) is not StatsComponent stats) return 0;
        double chance = tuning.DeflectionBaseChance
            + Read(stats, DaggerfallMechanicsIds.Dodging) * tuning.DeflectionPerDodgingPoint
            + (Read(stats, DaggerfallMechanicsIds.Agility) - 50) * tuning.DeflectionPerAgilityAbove50;
        return (int)Math.Clamp(Math.Round(chance, MidpointRounding.AwayFromZero), 0d, tuning.DeflectionMaximumChance);
    }

    /// <summary>What the target's protection on the struck body part takes off one blow on a channel.</summary>
    internal int Absorption(DaggerfallPhysicalHarm harm, HarmChannelId channel)
    {
        double points = 0d;
        string slot = DaggerCombatRules.BodySlot(harm.BodyPart);
        foreach (EquipmentAssignment assignment in context.Session.EquipmentOf(harm.TargetId).Assignments)
        {
            DaggerfallItemDefinition item = context.Definitions.RequireItem(new DaggerfallItemId(assignment.Item.Definition.Value));
            if (item.Armor is { } worn && assignment.Slot.Value == slot)
                points += context.Definitions.ArmorValuesByMaterial[worn.Material] * tuning.AbsorptionPerArmorPoint;
            else if (item.Shield is { } shield && assignment.Slot.Value == "left-hand" && DaggerCombatRules.ShieldCovers(item, harm.BodyPart))
                points += shield.Armor * tuning.AbsorptionPerShieldPoint;
        }
        // A monster's natural armour class is lower the tougher its hide; the baseline is an unarmoured beast.
        if (harm.Target.Kind == DaggerfallActorKinds.Monster)
            points += Math.Max(0d, tuning.MonsterArmorBaseline - harm.Target.Armor) * tuning.MonsterAbsorptionPerPoint;
        return (int)Math.Round(points * tuning.AbsorptionMultiplier(channel), MidpointRounding.AwayFromZero);
    }

    private bool Deflects(DaggerfallPhysicalHarm harm)
    {
        int chance = DeflectionChance(harm.TargetId);
        if (chance <= 0) return false;
        bool enemy = harm.AttackerId != DaggerfallActorIdentity.PlayerEntityId;
        long roll = context.Session.Random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, enemy ? CombatRandomKey.EnemyScope : CombatRandomKey.PlayerScope,
            CombatRandomKey.For(harm.Generation, harm.SimulationStep, harm.AttackerId, harm.TargetId, DeflectionSalt), 1, 100)).Value;
        return roll <= chance;
    }

    private static int Read(StatsComponent stats, DaggerfallStatId stat) => stats.GetStat(StatId.Parse(stat.Value)).ValueInt;
}
