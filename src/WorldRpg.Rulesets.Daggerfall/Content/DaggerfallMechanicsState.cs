using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Product-owned Daggerfall mechanics definitions and actor construction.
/// The managed Engine value types enforce exact stat/track invariants; this
/// state chooses the identities, bases, and vitality policy for this ruleset.
/// </summary>
internal sealed class DaggerfallMechanicsState
{
    private const long MinimumStatValue = 0;
    private const long MaximumStatValue = 10_000;

    /// <summary>
    /// Creates one actor's managed stat and track state from authored policy.
    /// The returned state owns its mutable current track values and must be
    /// disposed with the actor that owns it.
    /// </summary>
    internal ActorMechanicsState CreateActor(
        DaggerfallActorDefinition definition,
        DaggerfallVitalValues vitals,
        ulong entityId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (entityId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entityId), "Actor entities must be non-zero.");
        }

        ValidateVitals(vitals);
        return new ActorMechanicsState(
            new EntityId(entityId),
            InitialStats(definition, vitals),
            InitialTracks(vitals),
            [InitialHealth(vitals)]);
    }

    private static IEnumerable<(ExactStatDefinition Definition, ExactValue Base)> InitialStats(
        DaggerfallActorDefinition actor,
        DaggerfallVitalValues vitals)
    {
        foreach ((DaggerfallStatId id, int value) in actor.Stats.Values)
        {
            yield return Stat(id, value);
        }
        yield return Stat(DaggerfallMechanicsIds.StaminaMaximum, vitals.StaminaMaximum);
        yield return Stat(DaggerfallMechanicsIds.MagickaMaximum, vitals.MagickaMaximum);
    }

    private static (ExactStatDefinition Definition, ExactValue Base) Stat(
        DaggerfallStatId id,
        int value)
    {
        return (
            new ExactStatDefinition(
                StatId.Parse(id.Value),
                new ExactValue(MinimumStatValue),
                new ExactValue(MaximumStatValue)),
            new ExactValue(value));
    }

    private static IEnumerable<ExactTrack> InitialTracks(DaggerfallVitalValues vitals)
    {
        yield return Track(DaggerfallMechanicsIds.Stamina, DaggerfallMechanicsIds.StaminaMaximum, vitals.StaminaMaximum);
        yield return Track(DaggerfallMechanicsIds.Magicka, DaggerfallMechanicsIds.MagickaMaximum, vitals.MagickaMaximum);
    }

    private static ExactStatTrackState InitialHealth(DaggerfallVitalValues vitals)
    {
        (ExactStatDefinition stat, ExactValue baseValue) = Stat(DaggerfallMechanicsIds.HealthMaximum, vitals.HealthMaximum);
        ExactTrackDefinition track = TrackDefinition(DaggerfallMechanicsIds.Health, DaggerfallMechanicsIds.HealthMaximum);
        return new ExactStatTrackState(stat, baseValue, Array.Empty<ExactSource>(), track, baseValue);
    }

    private static ExactTrack Track(DaggerfallTrackId id, DaggerfallStatId maximum, int current)
    {
        ExactValue value = new(current);
        return new ExactTrack(TrackDefinition(id, maximum), value, new ExactTrackBounds(new ExactValue(MinimumStatValue), value));
    }

    private static ExactTrackDefinition TrackDefinition(DaggerfallTrackId id, DaggerfallStatId maximum) => new(
        TrackId.Parse(id.Value),
        new ExactValue(MinimumStatValue),
        new ExactTrackMaximum.FromStat(StatId.Parse(maximum.Value)));

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
