using Rusty.Engine.Mechanics;

namespace WorldRpg.Kit.Actors;

/// <summary>
/// Recovers a track from explicitly admitted simulation time after an optional quiet period.
/// Product policy decides when to restart the quiet period and whether recovery is eligible.
/// </summary>
public sealed class PassiveTrackRecovery
{
    private readonly double _pointsPerSecond;
    private readonly double _quietDelaySeconds;
    private double _quietSeconds;
    private double _recoveryCarry;

    public PassiveTrackRecovery(double pointsPerSecond, double quietDelaySeconds)
    {
        if (!double.IsFinite(pointsPerSecond) || pointsPerSecond <= 0d)
            throw new ArgumentOutOfRangeException(nameof(pointsPerSecond));
        if (!double.IsFinite(quietDelaySeconds) || quietDelaySeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(quietDelaySeconds));

        _pointsPerSecond = pointsPerSecond;
        _quietDelaySeconds = quietDelaySeconds;
    }

    /// <summary>Restarts the configured quiet period and discards any partial recovery.</summary>
    public void RestartQuietPeriod()
    {
        _quietSeconds = _quietDelaySeconds;
        _recoveryCarry = 0d;
    }

    /// <summary>
    /// Advances recovery by one already-admitted simulation delta. The caller supplies the track
    /// and decides whether recovery is currently eligible.
    /// </summary>
    public void Advance(Track track, double admittedDeltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!double.IsFinite(admittedDeltaSeconds) || admittedDeltaSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(admittedDeltaSeconds));

        double recoverableSeconds = admittedDeltaSeconds;
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

        if (track.Current >= track.MaximumValue)
        {
            _recoveryCarry = 0d;
            return;
        }

        _recoveryCarry += recoverableSeconds * _pointsPerSecond;
        long requested = checked((long)Math.Floor(_recoveryCarry));
        if (requested <= 0) return;

        track.Restore(requested);
        if (track.Current >= track.MaximumValue)
            _recoveryCarry = 0d;
        else
            _recoveryCarry -= requested;
    }
}
