using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The one player-only maximum-vitals policy. Health keeps its permanent career and level-roll
/// sources; fatigue and spell points follow the current live attributes as the donor does.
/// </summary>
internal static class DaggerfallPlayerVitals
{
    internal static DaggerfallVitalValues Initial(DaggerfallStatBases stats, DaggerfallCareerDefinition career)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(career);
        return new(
            HealthBase(career),
            DaggerfallFormulaPolicy.MaxFatigue(stats.Strength, stats.Endurance),
            DaggerfallFormulaPolicy.SpellPoints(stats.Intelligence, career.SpellPointMultiplierMilli));
    }

    /// <summary>
    /// Recomputes only derived bases. Existing health sources hold permanent level gains, and the
    /// Engine tracks preserve their configured current-value relationship to a changed maximum.
    /// </summary>
    internal static void Refresh(StatsComponent stats, DaggerfallCareerDefinition career)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(career);
        int strength = Value(stats, DaggerfallMechanicsIds.Strength);
        int intelligence = Value(stats, DaggerfallMechanicsIds.Intelligence);
        int endurance = Value(stats, DaggerfallMechanicsIds.Endurance);
        stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value)).BaseValue = HealthBase(career);
        stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).BaseValue =
            DaggerfallFormulaPolicy.MaxFatigue(strength, endurance);
        stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value)).BaseValue =
            DaggerfallFormulaPolicy.SpellPoints(intelligence, career.SpellPointMultiplierMilli);
    }

    private static int HealthBase(DaggerfallCareerDefinition career) =>
        DaggerfallFormulaPolicy.RollMaxHealth(1, career.HitPointsPerLevel, 0, static (_, _) => throw new InvalidOperationException("Level-one health never rolls."));

    private static int Value(StatsComponent stats, DaggerfallStatId id) =>
        stats.GetStat(StatId.Parse(id.Value)).ValueInt;
}
