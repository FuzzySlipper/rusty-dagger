using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>Ruleset consequences of accepted Engine movement, kept at the same health application boundary as combat and effects.</summary>
internal sealed class DaggerfallVitalityConsequences
{
    private static readonly TrackId HealthTrack = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private readonly CombatResolution _combat;

    internal DaggerfallVitalityConsequences(CombatResolution combat) => _combat = combat ?? throw new ArgumentNullException(nameof(combat));

    /// <summary>
    /// Applies the damage a worn enchantment does to its wearer, at the same health boundary combat and
    /// movement use, so an enchantment that takes the last point of health defeats its wearer the way any
    /// other accepted damage does rather than leaving a track at zero.
    /// </summary>
    internal DamageResult ResolveHeldEnchantmentDamage(Actor player, int damage)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (damage <= 0) throw new ArgumentOutOfRangeException(nameof(damage));
        Track health = player.Get<StatsComponent>().GetTrack(HealthTrack);
        return _combat.ApplyToHealth(new CombatParticipants(player, player, "held enchantment"), damage, 0, health).Result;
    }

    /// <summary>
    /// Applies the damage a poison does to its victim, at the same health boundary combat and movement use,
    /// so a poison that takes the last point of health defeats its victim through the ordinary consequence
    /// path rather than leaving a track at zero.
    /// </summary>
    internal DamageResult ResolvePoisonDamage(Actor victim, int damage)
    {
        ArgumentNullException.ThrowIfNull(victim);
        if (damage <= 0) throw new ArgumentOutOfRangeException(nameof(damage));
        Track health = victim.Get<StatsComponent>().GetTrack(HealthTrack);
        return _combat.ApplyToHealth(new CombatParticipants(victim, victim, "poison"), damage, 0, health).Result;
    }

    /// <summary>Applies only the Engine-reported landing, never an input or a locally integrated trajectory.</summary>
    internal DamageResult? ResolveLanding(Actor player, DaggerfallLanding? landing, bool preventsFallDamage)
    {
        if (preventsFallDamage || landing is not { } accepted) return null;
        int damage = DaggerfallFormulaPolicy.FallDamage(accepted.Validate().Distance);
        if (damage == 0) return null;
        Track health = player.Get<StatsComponent>().GetTrack(HealthTrack);
        return _combat.ApplyToHealth(new CombatParticipants(player, player, "fall"), damage, 0, health).Result;
    }
}
