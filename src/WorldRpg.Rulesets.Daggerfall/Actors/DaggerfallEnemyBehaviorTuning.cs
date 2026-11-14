namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Ruleset policy for visibility-led enemy chase and attack decisions.
/// </summary>
/// <remarks>
/// There is no reach here: how far an attack carries is a property of the attack, so it is authored on the
/// action the actor swings with and read from there. A single tuned reach would make every enemy in the
/// world reach the same distance, which is wrong the moment one of them carries a bow.
/// </remarks>
internal sealed record DaggerfallEnemyBehaviorTuning(
    double DetectionDistance,
    float ChaseSpeedUnitsPerSecond,
    uint NavigationMaximumVisited,
    float SpawnGroundProbeLift = .2f,
    double SpawnGroundProbeDistance = 3d)
{
    /// <summary>EnemyMotor's close-range retreat and strafe decisions, which the Kit pursuit policy applies.</summary>
    internal DaggerfallPursuitManeuverTuning Maneuvers { get; init; } = DaggerfallPursuitManeuverTuning.Default;

    internal DaggerfallEnemyBehaviorTuning Validate()
    {
        Maneuvers.Validate();
        if (!double.IsFinite(DetectionDistance) || DetectionDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(DetectionDistance));
        if (!float.IsFinite(ChaseSpeedUnitsPerSecond) || ChaseSpeedUnitsPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(ChaseSpeedUnitsPerSecond));
        if (!float.IsFinite(SpawnGroundProbeLift) || SpawnGroundProbeLift < 0f) throw new ArgumentOutOfRangeException(nameof(SpawnGroundProbeLift));
        if (!double.IsFinite(SpawnGroundProbeDistance) || SpawnGroundProbeDistance <= SpawnGroundProbeLift) throw new ArgumentOutOfRangeException(nameof(SpawnGroundProbeDistance));
        if (NavigationMaximumVisited == 0) throw new ArgumentOutOfRangeException(nameof(NavigationMaximumVisited));
        return this;
    }
}

/// <summary>EnemyMotor's default enhanced combat decisions and bounded last-position search.</summary>
internal sealed record DaggerfallPursuitManeuverTuning(bool EnhancedCombatAi, float RetreatDistance, float StrafeDistance,
    float RetreatMultiplier, float TooCloseMultiplier, float AdvanceDistanceMultiplier, float SearchDistance, float SearchStep)
{
    internal static DaggerfallPursuitManeuverTuning Default { get; } = new(true, 2.5f, 1.5f, 2.25f, .8f, 2.75f, 10f, 1f);

    internal DaggerfallPursuitManeuverTuning Validate()
    {
        foreach (float value in new[] { RetreatDistance, StrafeDistance, RetreatMultiplier, TooCloseMultiplier, AdvanceDistanceMultiplier, SearchDistance, SearchStep })
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return this;
    }
}
