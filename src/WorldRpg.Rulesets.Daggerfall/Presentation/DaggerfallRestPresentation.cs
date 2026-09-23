using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>The thin DOM-facing result of the most recent rest operation.</summary>
internal sealed class DaggerfallRestPresentation
{
    private ulong _revision;
    private DaggerfallRestResult? _result;

    internal void Publish(DaggerfallRestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _result = result;
        _revision = checked(_revision + 1);
    }

    internal DaggerfallRestView Read() => _result is null
        ? new(false, _revision, null, 0, 0, 0, 0, 0, 0, DaggerfallRestInterruption.None, null)
        : new(true, _revision, _result.Mode.ToString(), _result.RequestedSeconds, _result.ElapsedSeconds,
            _result.RecoveryHours, _result.HealthRecovered, _result.FatigueRecovered,
            _result.SpellPointsRecovered, _result.Interruption, _result.Message);
}

internal sealed record DaggerfallRestView(
    bool HasResult,
    ulong Revision,
    string? Mode,
    long RequestedSeconds,
    long ElapsedSeconds,
    int RecoveryHours,
    int HealthRecovered,
    int FatigueRecovered,
    int SpellPointsRecovered,
    DaggerfallRestInterruption Interruption,
    string? Message);
