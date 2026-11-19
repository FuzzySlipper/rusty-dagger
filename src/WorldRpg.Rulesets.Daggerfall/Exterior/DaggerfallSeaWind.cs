namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The wind the sea's waves stand to (#10910). It eases toward the weather's wind over game hours, so waves build as a storm
/// comes and fall after it; waiting or travelling passes the hours at once. Its bearing holds while the sea is in view and
/// is taken up from the weather when the sea is next shown: turning the waves under the player would sweep the whole sea.
/// The sea's shader makes the waves from it; nothing else rides them.
/// </summary>
internal sealed class DaggerfallSeaWind(DaggerfallWaterTuning tuning)
{
    /// <summary>The wind over the water: the bearing it blows toward (radians from +x toward +z) and its speed (m/s).</summary>
    internal readonly record struct Blowing(float Bearing, float Speed);

    private double _hours = double.NaN;
    private bool _held;

    internal Blowing Now { get; private set; } = new(0, tuning.CalmWind);

    /// <summary>
    /// Eases toward the weather's wind (<paramref name="wind"/> 0 to 1, blowing toward <paramref name="degrees"/> on the
    /// compass) by the game hours passed since the last call; the first call after <see cref="Release"/> stands at it.
    /// </summary>
    internal void Follow(double gameHours, float wind, float degrees)
    {
        float target = tuning.WindFor(wind);
        double passed = double.IsNaN(_hours) ? double.PositiveInfinity : Math.Max(0, gameHours - _hours);
        float speed = Now.Speed + ((target - Now.Speed) * (float)(1 - Math.Exp(-passed / tuning.BuildHours)));
        _hours = gameHours;
        float bearing = Now.Bearing;
        if (!_held)
        {
            // The compass's degrees run clockwise from north (-z), so east is +x and south +z.
            float angle = degrees * MathF.PI / 180;
            bearing = MathF.Atan2(-MathF.Cos(angle), MathF.Sin(angle));
            _held = true;
        }
        Now = new(bearing, speed);
    }

    /// <summary>The sea is out of view: the waves take up the weather's bearing when it is next shown.</summary>
    internal void Release() => _held = false;
}

/// <summary>The sea's ripple and foam maps, opened as data for its shader.</summary>
internal sealed record DaggerfallSeaMaps(Rusty.Engine.RenderResource Ripples, Rusty.Engine.RenderResource Foam);
