namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How quickly and how strongly the city watch answers a case, and how far an arrest reaches.</summary>
internal sealed record DaggerfallLawTuning(int MinimumResponseSeconds, int MaximumResponseSeconds,
    int MinimumGuards, int MaximumGuards, float MinimumArrivalDistance, float MaximumArrivalDistance, double ArrestReach)
{
    internal DaggerfallLawTuning Validate() => MinimumResponseSeconds >= 0 && MaximumResponseSeconds >= MinimumResponseSeconds
        && MinimumGuards > 0 && MaximumGuards >= MinimumGuards && MaximumGuards <= 64
        && float.IsFinite(MinimumArrivalDistance) && MinimumArrivalDistance > 0 && float.IsFinite(MaximumArrivalDistance)
        && MaximumArrivalDistance >= MinimumArrivalDistance && double.IsFinite(ArrestReach) && ArrestReach > 0
        ? this : throw new ArgumentException("Law tuning requires ordered finite response delays, guard counts and arrival distances.");
}
