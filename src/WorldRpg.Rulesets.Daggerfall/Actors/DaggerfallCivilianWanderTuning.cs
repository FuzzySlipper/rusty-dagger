namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Outdoor civilians' wander movement (MobilePersonMotor's walk speed and idle distance) and the
/// distance at which a wandering civilian is recycled away from the player.
/// </summary>
internal sealed record DaggerfallCivilianWanderTuning(float MovementSpeedUnitsPerSecond, float WaypointDistance,
    float IdleDurationSeconds, uint NavigationMaximumVisited, float RecycleDistanceMeters)
{
    internal static DaggerfallCivilianWanderTuning Classic { get; } = new(1.3f, 2.5f, 2.5f, 64, 150f);

    internal DaggerfallCivilianWanderTuning Validate()
    {
        if (!float.IsFinite(MovementSpeedUnitsPerSecond) || MovementSpeedUnitsPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(MovementSpeedUnitsPerSecond));
        if (!float.IsFinite(WaypointDistance) || WaypointDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(WaypointDistance));
        if (!float.IsFinite(IdleDurationSeconds) || IdleDurationSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(IdleDurationSeconds));
        if (NavigationMaximumVisited == 0) throw new ArgumentOutOfRangeException(nameof(NavigationMaximumVisited));
        if (!float.IsFinite(RecycleDistanceMeters) || RecycleDistanceMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(RecycleDistanceMeters));
        return this;
    }
}
