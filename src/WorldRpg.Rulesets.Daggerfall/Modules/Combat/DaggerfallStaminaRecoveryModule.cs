using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>Ruleset-owned passive player stamina recovery on the admitted fixed-step timeline.</summary>
internal sealed class DaggerfallStaminaRecoveryModule(DaggerfallStaminaRecoveryTuning tuning)
{
    private static readonly TrackId HealthTrack = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly TrackId StaminaTrack = TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value);
    private readonly DaggerfallStaminaRecoveryTuning _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
    private double _quietSeconds;
    private double _recoveryCarry;

    internal readonly record struct Checkpoint(double QuietSeconds, double RecoveryCarry);

    internal Checkpoint Capture() => new(_quietSeconds, _recoveryCarry);

    internal void Restore(Checkpoint checkpoint)
    {
        _quietSeconds = checkpoint.QuietSeconds;
        _recoveryCarry = checkpoint.RecoveryCarry;
    }

    /// <summary>Only admitted player swings delay recovery; rejected attempts never reset the quiet period.</summary>
    internal void React(IProductFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact is not PlayerAttackStartedFact) return;
        _quietSeconds = _tuning.DelayAfterAttackSeconds;
        _recoveryCarry = 0d;
    }

    /// <summary>Advances from exactly one Engine-admitted fixed simulation step; no host clock is consulted.</summary>
    internal void Update(ActorMechanicsState player, double fixedDeltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
        if (player.ReadTrack(HealthTrack).Current.Raw <= 0) return;

        double recoverableSeconds = fixedDeltaSeconds;
        if (_quietSeconds > 0d)
        {
            if (recoverableSeconds <= _quietSeconds)
            {
                _quietSeconds -= recoverableSeconds;
                return;
            }
            recoverableSeconds -= _quietSeconds;
            _quietSeconds = 0d;
        }

        ActorTrackRead stamina = player.ReadTrack(StaminaTrack);
        if (stamina.Current >= stamina.Bounds.Maximum)
        {
            _recoveryCarry = 0d;
            return;
        }

        _recoveryCarry += recoverableSeconds * _tuning.PointsPerSecond;
        long requested = checked((long)Math.Floor(_recoveryCarry));
        if (requested <= 0) return;

        ExactTrackMutationReceipt receipt = player.RestoreTrack(StaminaTrack, new ExactValue(requested));
        if (receipt.After >= receipt.Bounds.Maximum)
            _recoveryCarry = 0d;
        else
            _recoveryCarry -= requested;
    }
}
