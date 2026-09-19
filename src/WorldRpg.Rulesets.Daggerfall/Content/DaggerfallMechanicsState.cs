using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Product-owned Daggerfall mechanics definitions and actor construction.</summary>
internal sealed class DaggerfallMechanicsState
{
    private const long MinimumStatValue = 0;
    private const long MaximumStatValue = 10_000;

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
        StatsComponent result = new();
        foreach (var (id, stat) in stats) result.AddStat(id, stat);
        (TrackId Id, Track Value)[] tracks =
            [
                (TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value), new Track(
                    staminaMaximum, vitals.StaminaMaximum, MinimumStatValue, quantum: 1,
                    rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero)),
                (TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value), new Track(
                    magickaMaximum, vitals.MagickaMaximum, MinimumStatValue, quantum: 1,
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
