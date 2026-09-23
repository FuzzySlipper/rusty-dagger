using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>The three choices offered by the classic rest interaction.</summary>
internal enum DaggerfallRestMode
{
    Timed,
    UntilHealed,
    Loiter,
}

/// <summary>The external condition which stopped an admitted elapsed interval.</summary>
internal enum DaggerfallRestInterruption
{
    None,
    Encounter,
    Prevented,
    Stopped,
    Defeated,
}

/// <summary>One semantic rest choice, before the location and player gates are applied.</summary>
internal sealed record DaggerfallRestRequest(DaggerfallRestMode Mode, int Hours = 0)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        if (Hours < 0) throw new ArgumentOutOfRangeException(nameof(Hours));
        if (Mode == DaggerfallRestMode.Timed && Hours > DaggerfallRestPolicy.MaximumTimedHours)
            throw new ArgumentOutOfRangeException(nameof(Hours), Hours,
                $"Timed rest cannot exceed {DaggerfallRestPolicy.MaximumTimedHours} hours.");
        if (Mode == DaggerfallRestMode.Loiter && Hours > DaggerfallRestPolicy.MaximumLoiterHours)
            throw new ArgumentOutOfRangeException(nameof(Hours), Hours,
                $"Loiter cannot exceed {DaggerfallRestPolicy.MaximumLoiterHours} hours.");
        if (Mode == DaggerfallRestMode.UntilHealed && Hours != 0)
            throw new ArgumentException("Until-healed rest has no caller-supplied duration.", nameof(Hours));
    }
}

/// <summary>Facts the current world owner supplies when deciding whether a player may rest.</summary>
/// <remarks>
/// The donor checks town camping, owned or rented rooms, and guild privileges. Rusty Dagger has no
/// authored rental or ownership service yet, so those facts stay with the caller rather than being
/// guessed by this formula owner.
/// </remarks>
internal sealed record DaggerfallRestEligibility(bool Allowed, bool IsAlive = true, string? Message = null)
{
    internal void Validate()
    {
        if (!IsAlive && Allowed)
            throw new ArgumentException("A defeated player cannot be granted rest eligibility.", nameof(Allowed));
        if (Allowed && !string.IsNullOrWhiteSpace(Message))
            throw new ArgumentException("An allowed rest eligibility cannot carry a rejection message.", nameof(Message));
    }
}

/// <summary>One interval accepted by the shared calendar owner.</summary>
internal readonly record struct DaggerfallRestTimeAdvance(
    long RequestedSeconds,
    long AppliedSeconds,
    DaggerfallRestInterruption Interruption = DaggerfallRestInterruption.None)
{
    internal void Validate()
    {
        if (RequestedSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(RequestedSeconds));
        if (AppliedSeconds < 0 || AppliedSeconds > RequestedSeconds)
            throw new ArgumentOutOfRangeException(nameof(AppliedSeconds));
        if (!Enum.IsDefined(Interruption)) throw new ArgumentOutOfRangeException(nameof(Interruption));
        if (Interruption == DaggerfallRestInterruption.None && AppliedSeconds != RequestedSeconds)
            throw new ArgumentException("An uninterrupted rest interval must apply its full request.", nameof(AppliedSeconds));
    }
}

/// <summary>The stat deltas and elapsed result of a rest operation.</summary>
internal sealed record DaggerfallRestResult(
    bool Accepted,
    DaggerfallRestMode Mode,
    long RequestedSeconds,
    long ElapsedSeconds,
    int RecoveryHours,
    int HealthRecovered,
    int FatigueRecovered,
    int SpellPointsRecovered,
    DaggerfallRestInterruption Interruption,
    string? Message = null)
{
    internal static DaggerfallRestResult Rejected(DaggerfallRestMode mode, string message) =>
        new(false, mode, 0, 0, 0, 0, 0, 0, DaggerfallRestInterruption.Prevented, message);
}

/// <summary>Classic rest limits, units, eligibility, and the full-vitals predicate.</summary>
internal static class DaggerfallRestPolicy
{
    /// <summary>The donor input dialog refuses timed rest above ninety-nine hours.</summary>
    internal const int MaximumTimedHours = 99;

    /// <summary>The donor default in defaults.ini.txt; the UI may supply another admitted profile later.</summary>
    internal const int MaximumLoiterHours = 3;

    internal const long SecondsPerRestTick = 10 * DaggerfallCalendar.SecondsPerMinute;
    internal const long SecondsPerRestHour = 60 * DaggerfallCalendar.SecondsPerMinute;

    internal static bool IsFullyRecovered(StatsComponent player, bool noRegeneration)
    {
        ArgumentNullException.ThrowIfNull(player);
        return Current(player, DaggerfallMechanicsIds.Health) >= Maximum(player, DaggerfallMechanicsIds.HealthMaximum)
            && Current(player, DaggerfallMechanicsIds.Stamina) >= Maximum(player, DaggerfallMechanicsIds.StaminaMaximum)
            && (noRegeneration || Current(player, DaggerfallMechanicsIds.Magicka) >= Maximum(player, DaggerfallMechanicsIds.MagickaMaximum));
    }

    internal static (int Health, int Fatigue, int SpellPoints) RecoveryRates(
        int endurance,
        int medical,
        int maximumHealth,
        int maximumFatigue,
        int maximumMagicka,
        bool rapidHealing,
        bool noRegeneration,
        DaggerfallFormulaTuning? tuning = null)
    {
        if (endurance < 0 || medical < 0 || maximumHealth < 0 || maximumFatigue < 0 || maximumMagicka < 0)
            throw new ArgumentOutOfRangeException(nameof(endurance));
        return (
            DaggerfallFormulaPolicy.CalculateHealthRecoveryRate(endurance, medical, maximumHealth, rapidHealing, tuning),
            DaggerfallFormulaPolicy.CalculateFatigueRecoveryRate(maximumFatigue, tuning),
            DaggerfallFormulaPolicy.CalculateSpellPointRecoveryRate(maximumMagicka, noRegeneration, tuning));
    }

    private static int Current(StatsComponent stats, DaggerfallTrackId id) =>
        checked((int)stats.GetTrack(TrackId.Parse(id.Value)).Current);

    private static int Maximum(StatsComponent stats, DaggerfallStatId id) =>
        checked((int)stats.GetStat(StatId.Parse(id.Value)).ValueInt);
}
