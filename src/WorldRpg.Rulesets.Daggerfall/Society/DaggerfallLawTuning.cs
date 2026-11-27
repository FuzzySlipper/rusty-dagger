using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How quickly and how strongly the city watch answers a case, and how far an arrest reaches.</summary>
internal sealed record DaggerfallLawTuning(int MinimumResponseSeconds, int MaximumResponseSeconds,
    int MinimumGuards, int MaximumGuards, float MinimumArrivalDistance, float MaximumArrivalDistance, double ArrestReach)
{
    /// <summary>
    /// The extra guards each point of a crime crackdown's severity calls out in its towns, from their reserve watch
    /// (<see cref="DaggerfallExteriorPopulation.CrackdownGuardSlots"/> at most).
    /// </summary>
    internal int CrackdownGuardsPerSeverity { get; init; } = 1;

    internal DaggerfallLawTuning Validate() => MinimumResponseSeconds >= 0 && MaximumResponseSeconds >= MinimumResponseSeconds
        && MinimumGuards > 0 && MaximumGuards >= MinimumGuards && MaximumGuards <= 64
        && float.IsFinite(MinimumArrivalDistance) && MinimumArrivalDistance > 0 && float.IsFinite(MaximumArrivalDistance)
        && MaximumArrivalDistance >= MinimumArrivalDistance && double.IsFinite(ArrestReach) && ArrestReach > 0
        && CrackdownGuardsPerSeverity >= 0
        && CrackdownGuardsPerSeverity * DaggerfallWorldConditionToken.MaxSeverity <= DaggerfallExteriorPopulation.CrackdownGuardSlots
        ? this : throw new ArgumentException("Law tuning requires ordered finite response delays, guard counts and arrival distances, and crackdown guards a town's reserve watch can field.");

    internal static DaggerfallLawTuning Read(JsonElement section) => new DaggerfallLawTuning(
        section.GetProperty("minimumResponseSeconds").GetInt32(),
        section.GetProperty("maximumResponseSeconds").GetInt32(),
        section.GetProperty("minimumGuards").GetInt32(),
        section.GetProperty("maximumGuards").GetInt32(),
        section.GetProperty("minimumArrivalDistance").GetSingle(),
        section.GetProperty("maximumArrivalDistance").GetSingle(),
        section.GetProperty("arrestReach").GetDouble())
    {
        CrackdownGuardsPerSeverity = section.GetProperty("crackdownGuardsPerSeverity").GetInt32(),
    }.Validate();
}
