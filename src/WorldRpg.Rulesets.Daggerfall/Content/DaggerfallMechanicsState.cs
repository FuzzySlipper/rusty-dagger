using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Product-owned Daggerfall mechanics definitions and actor construction.</summary>
internal sealed class DaggerfallMechanicsState
{
    private const long MinimumStatValue = 0;
    private const long MaximumStatValue = 10_000;

    /// <summary>
    /// The resistance window: the donor never clamps, and negative resistance is vulnerability,
    /// so the window spans both sides rather than cutting weakness off at zero.
    /// </summary>
    private const long MinimumResistanceValue = -10_000;
    private const long MaximumResistanceValue = 10_000;

    /// <summary>Creates one actor's shared stat and track state from authored policy.</summary>
    internal StatsComponent CreateStats(
        DaggerfallActorDefinition definition,
        DaggerfallVitalValues vitals)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateVitals(vitals);
        List<(StatId Id, Stat Value)> stats = [];
        foreach ((DaggerfallStatId id, int value) in definition.Stats.Values)
        {
            stats.Add(Stat(id, value));
        }

        Stat staminaMaximum = AddStat(stats, DaggerfallMechanicsIds.StaminaMaximum, vitals.StaminaMaximum);
        Stat magickaMaximum = AddStat(stats, DaggerfallMechanicsIds.MagickaMaximum, vitals.MagickaMaximum);
        Stat healthMaximum = AddStat(stats, DaggerfallMechanicsIds.HealthMaximum, vitals.HealthMaximum);
        AddBoundedStat(stats, DaggerfallMechanicsIds.ResistanceFire, 0, MinimumResistanceValue, MaximumResistanceValue);
        AddBoundedStat(stats, DaggerfallMechanicsIds.ResistanceFrost, 0, MinimumResistanceValue, MaximumResistanceValue);
        AddBoundedStat(stats, DaggerfallMechanicsIds.ResistanceDiseaseOrPoison, 0, MinimumResistanceValue, MaximumResistanceValue);
        AddBoundedStat(stats, DaggerfallMechanicsIds.ResistanceShock, 0, MinimumResistanceValue, MaximumResistanceValue);
        AddBoundedStat(stats, DaggerfallMechanicsIds.ResistanceMagic, 0, MinimumResistanceValue, MaximumResistanceValue);
        AddBoundedStat(stats, DaggerfallMechanicsIds.ImmunityParalysis, 0, 0, 1);
        AddBoundedStat(stats, DaggerfallMechanicsIds.ImmunityDisease, 0, 0, 1);
        StatsComponent result = new();
        foreach (var (id, stat) in stats) result.AddStat(id, stat);
        // A maximum change on stamina or magicka never moves current by itself: the donor keeps
        // current where it is and rest restores, so those tracks state the preserve-current
        // policy rather than inheriting whatever the default happens to be today. Health keeps
        // preserve-missing-amount because level-up raises maximum and current equally through
        // the track's own policy.
        (TrackId Id, Track Value)[] tracks =
            [
                (TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value), new Track(
                    staminaMaximum, vitals.StaminaMaximum, MinimumStatValue, TrackMaximumChangePolicy.PreserveCurrent,
                    quantum: 1,
                    rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero)),
                (TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value), new Track(
                    magickaMaximum, vitals.MagickaMaximum, MinimumStatValue, TrackMaximumChangePolicy.PreserveCurrent,
                    quantum: 1,
                    rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero)),
                (TrackId.Parse(DaggerfallMechanicsIds.Health.Value), new Track(
                    healthMaximum,
                    vitals.HealthMaximum,
                    MinimumStatValue,
                    TrackMaximumChangePolicy.PreserveMissingAmount,
                    quantum: 1,
                    rounding: MidpointRounding.ToZero,
                    integerRounding: MidpointRounding.ToZero)),
            ];
        foreach (var (id, track) in tracks) result.AddTrack(id, track);
        return result;
    }

    private static (StatId Id, Stat Value) Stat(DaggerfallStatId id, int value) =>
        (StatId.Parse(id.Value), new Stat(
            value,
            MinimumStatValue,
            MaximumStatValue,
            quantum: 1,
            rounding: MidpointRounding.ToZero,
            integerRounding: MidpointRounding.ToZero));

    private static Stat AddStat(List<(StatId Id, Stat Value)> stats, DaggerfallStatId id, int value)
    {
        (StatId stat, Stat result) = Stat(id, value);
        stats.Add((stat, result));
        return result;
    }

    private static void AddBoundedStat(List<(StatId Id, Stat Value)> stats, DaggerfallStatId id, int value, long minimum, long maximum)
    {
        StatId stat = StatId.Parse(id.Value);
        Stat result = new(value, minimum, maximum, quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero);
        stats.Add((stat, result));
    }

    private static void ValidateVitals(DaggerfallVitalValues vitals)
    {
        if (vitals.HealthMaximum < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vitals), "Health maximum cannot be negative.");
        }

        if (vitals.StaminaMaximum < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vitals), "Stamina maximum cannot be negative.");
        }

        if (vitals.MagickaMaximum < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vitals), "Magicka maximum cannot be negative.");
        }

        if (vitals.HealthMaximum > MaximumStatValue
            || vitals.StaminaMaximum > MaximumStatValue
            || vitals.MagickaMaximum > MaximumStatValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(vitals),
                $"Daggerfall vitality values cannot exceed {MaximumStatValue}.");
        }
    }
}
