using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>Ruleset-owned passive player stamina recovery on the admitted fixed-step timeline.</summary>
internal sealed class DaggerfallStaminaRecoveryModule
{
    private static readonly TrackId HealthTrack = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly TrackId StaminaTrack = TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value);
    private readonly PassiveTrackRecovery _recovery;

    internal DaggerfallStaminaRecoveryModule(DaggerfallStaminaRecoveryTuning tuning)
    {
        DaggerfallStaminaRecoveryTuning validated = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _recovery = new PassiveTrackRecovery(validated.PointsPerSecond, validated.DelayAfterAttackSeconds);
    }

    /// <summary>Only admitted player swings delay recovery; rejected attempts never reset the quiet period.</summary>
    internal void React(IProductFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact is not PlayerAttackStartedFact) return;
        _recovery.RestartQuietPeriod();
    }

    /// <summary>Advances from exactly one Engine-admitted fixed simulation step; no host clock is consulted.</summary>
    internal void Update(StatsComponent player, double fixedDeltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
        if (player.GetTrack(HealthTrack).Current <= 0) return;
        _recovery.Advance(player.GetTrack(StaminaTrack), fixedDeltaSeconds);
    }
}
