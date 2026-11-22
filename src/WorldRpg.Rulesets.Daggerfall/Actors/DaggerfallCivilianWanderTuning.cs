using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Outdoor civilians' wander movement (MobilePersonMotor's walk speed and idle distance), the
/// distance at which a wandering civilian is recycled away from the player, and how they ease their
/// walk: turning to face their way at most <paramref name="TurnDegreesPerSecond"/>, and setting off
/// and stopping over <paramref name="EaseSeconds"/>.
/// </summary>
internal sealed record DaggerfallCivilianWanderTuning(float MovementSpeedUnitsPerSecond, float WaypointDistance,
    float IdleDurationSeconds, uint NavigationMaximumVisited, float RecycleDistanceMeters, float TurnDegreesPerSecond, float EaseSeconds)
{
    internal static DaggerfallCivilianWanderTuning Classic { get; } = new(1.3f, 2.5f, 2.5f, 64, 150f, 240f, .4f);

    internal DaggerfallCivilianWanderTuning Validate()
    {
        if (!float.IsFinite(MovementSpeedUnitsPerSecond) || MovementSpeedUnitsPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(MovementSpeedUnitsPerSecond));
        if (!float.IsFinite(WaypointDistance) || WaypointDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(WaypointDistance));
        if (!float.IsFinite(IdleDurationSeconds) || IdleDurationSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(IdleDurationSeconds));
        if (NavigationMaximumVisited == 0) throw new ArgumentOutOfRangeException(nameof(NavigationMaximumVisited));
        if (!float.IsFinite(RecycleDistanceMeters) || RecycleDistanceMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(RecycleDistanceMeters));
        if (!float.IsFinite(TurnDegreesPerSecond) || TurnDegreesPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(TurnDegreesPerSecond));
        if (!float.IsFinite(EaseSeconds) || EaseSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(EaseSeconds));
        return this;
    }

    internal static DaggerfallCivilianWanderTuning Read(JsonElement section) => new DaggerfallCivilianWanderTuning(
        section.GetProperty("movementSpeedUnitsPerSecond").GetSingle(),
        section.GetProperty("waypointDistance").GetSingle(),
        section.GetProperty("idleDurationSeconds").GetSingle(),
        section.GetProperty("navigationMaximumVisited").GetUInt32(),
        section.GetProperty("recycleDistanceMeters").GetSingle(),
        section.GetProperty("turnDegreesPerSecond").GetSingle(),
        section.GetProperty("easeSeconds").GetSingle()).Validate();
}
