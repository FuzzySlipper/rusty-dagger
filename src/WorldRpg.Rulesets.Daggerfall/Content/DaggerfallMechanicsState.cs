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
            InitialStats(definition.Stats, vitals),
            InitialTracks(vitals));
    }

    private static IEnumerable<(ExactStatDefinition Definition, ExactValue Base)> InitialStats(
        DaggerfallStatBases stats,
        DaggerfallVitalValues vitals)
    {
        yield return Stat(DaggerfallMechanicsIds.Strength, stats.Strength);
        yield return Stat(DaggerfallMechanicsIds.Intelligence, stats.Intelligence);
        yield return Stat(DaggerfallMechanicsIds.Willpower, stats.Willpower);
        yield return Stat(DaggerfallMechanicsIds.Agility, stats.Agility);
        yield return Stat(DaggerfallMechanicsIds.Endurance, stats.Endurance);
        yield return Stat(DaggerfallMechanicsIds.Personality, stats.Personality);
        yield return Stat(DaggerfallMechanicsIds.Speed, stats.Speed);
        yield return Stat(DaggerfallMechanicsIds.Luck, stats.Luck);
        yield return Stat(DaggerfallMechanicsIds.Reflexes, stats.Reflexes);
        yield return Stat(DaggerfallMechanicsIds.LongBlade, stats.LongBlade);
        yield return Stat(DaggerfallMechanicsIds.HandToHand, stats.HandToHand);
        yield return Stat(DaggerfallMechanicsIds.Dodging, stats.Dodging);
        yield return Stat(DaggerfallMechanicsIds.HealthMaximum, vitals.HealthMaximum);
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
        yield return Track(DaggerfallMechanicsIds.Health, DaggerfallMechanicsIds.HealthMaximum, vitals.HealthMaximum);
        yield return Track(DaggerfallMechanicsIds.Stamina, DaggerfallMechanicsIds.StaminaMaximum, vitals.StaminaMaximum);
        yield return Track(DaggerfallMechanicsIds.Magicka, DaggerfallMechanicsIds.MagickaMaximum, vitals.MagickaMaximum);
    }

    private static ExactTrack Track(DaggerfallTrackId id, DaggerfallStatId maximum, int current)
    {
        ExactValue value = new(current);
        ExactValue minimum = new(MinimumStatValue);
        ExactTrackDefinition definition = new(
            TrackId.Parse(id.Value),
            minimum,
            new ExactTrackMaximum.FromStat(StatId.Parse(maximum.Value)));
        return new ExactTrack(definition, value, new ExactTrackBounds(minimum, value));
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
