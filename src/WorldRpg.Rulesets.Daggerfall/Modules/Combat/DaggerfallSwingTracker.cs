namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// Reads the player's weapon swing gesture out of the committed look turns, the product's stand-in
/// for the donor's timestamped mouse trail. Each admitted update contributes the turn it committed;
/// an attack that arrives once the accumulated turn reaches the gesture threshold classifies the
/// gesture's direction and clears the trail, while an attack that arrives early swings straight and
/// leaves the trail accumulating toward the next one.
/// </summary>
/// <remarks>
/// Only the motion within the trailing window counts, matching the donor's one-second gesture trail:
/// a turn held long enough ago is part of aiming, not of the swing that is being classified. The
/// gesture is classified only for weapon attacks, as the donor classifies the screen weapon's swing;
/// a stationary hand-to-hand attack has no gesture to read.
/// </remarks>
internal sealed class DaggerfallSwingTracker(double minimumGestureRadians, double gestureWindowSeconds = 1d)
{
    private readonly record struct Turn(double Seconds, double Yaw, double Pitch, double Distance);

    private readonly Queue<Turn> _turns = new();
    private double _windowTravel;
    private double _windowSeconds;
    private double _windowYaw;
    private double _windowPitch;
    private float _lastYaw;
    private float _lastPitch;
    private bool _hasLast;

    /// <summary>
    /// Records the look turn committed by one admitted update. Yaw wrap is folded so a turn through
    /// the ±π seam counts as the short turn it is.
    /// </summary>
    internal void Observe(double deltaSeconds, float yawRadians, float pitchRadians)
    {
        if (_hasLast)
        {
            double yaw = WrapYaw(yawRadians - _lastYaw);
            double pitch = pitchRadians - _lastPitch;
            double distance = Math.Sqrt((yaw * yaw) + (pitch * pitch));
            _turns.Enqueue(new Turn(deltaSeconds, yaw, pitch, distance));
            _windowSeconds += deltaSeconds;
            _windowTravel += distance;
            _windowYaw += yaw;
            _windowPitch += pitch;
            // Keep every turn whose motion is still inside the trailing window, plus at most the one
            // turn that straddles the window edge, so the boundary swing keeps its full travel as the
            // donor's trail does.
            while (_turns.Count > 1 && _windowSeconds - _turns.Peek().Seconds > gestureWindowSeconds)
            {
                Turn stale = _turns.Dequeue();
                _windowSeconds -= stale.Seconds;
                _windowTravel -= stale.Distance;
                _windowYaw -= stale.Yaw;
                _windowPitch -= stale.Pitch;
            }
        }

        _lastYaw = yawRadians;
        _lastPitch = pitchRadians;
        _hasLast = true;
    }

    /// <summary>
    /// The swing direction for an attack issued now: the classified gesture when the trail carries a
    /// full swing, consumed so the same swing is not read twice, or straight when the player attacked
    /// without one.
    /// </summary>
    internal DaggerfallSwingDirection TryGesture()
    {
        if (_windowTravel < minimumGestureRadians) return DaggerfallSwingDirection.None;
        DaggerfallSwingDirection gesture = Classify(_windowYaw, _windowPitch);
        Clear();
        return gesture;
    }

    internal void Clear()
    {
        _turns.Clear();
        _windowSeconds = 0;
        _windowTravel = 0;
        _windowYaw = 0;
        _windowPitch = 0;
    }

    /// <summary>
    /// The donor's sector map: the gesture's net direction, measured in fifteens of a degree from
    /// straight right and turning upward, names the swing the player drew.
    /// </summary>
    private static DaggerfallSwingDirection Classify(double yaw, double pitch)
    {
        double angle = Math.Atan2(pitch, yaw) * (180d / Math.PI);
        if (angle < 0) angle += 360d;
        return (int)Math.Ceiling(angle / 15d) switch
        {
            <= 1 or 24 => DaggerfallSwingDirection.StrikeRight,
            <= 11 => DaggerfallSwingDirection.StrikeUp,
            <= 13 => DaggerfallSwingDirection.StrikeLeft,
            <= 17 => DaggerfallSwingDirection.StrikeDownLeft,
            <= 19 => DaggerfallSwingDirection.StrikeDown,
            _ => DaggerfallSwingDirection.StrikeDownRight,
        };
    }

    private static double WrapYaw(double yawRadians)
    {
        double wrapped = yawRadians % (2 * Math.PI);
        if (wrapped <= -Math.PI) wrapped += 2 * Math.PI;
        else if (wrapped > Math.PI) wrapped -= 2 * Math.PI;
        return wrapped;
    }
}
