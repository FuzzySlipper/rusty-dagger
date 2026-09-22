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
