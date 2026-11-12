using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The light the first-person view stands in, as a tint. The viewmodel layer doesn't take the scene's lights, so the arms,
/// weapons, mount and classic hand multiply their art by this: white under the clear noon sky, falling with the sky and
/// rising beside a lamp. It is sampled every few updates and moves only in steps, since each change is an Engine call
/// for every look drawn.
/// </summary>
internal sealed class DaggerfallViewLight(DaggerfallSiteLightingTuning tuning)
{
    // Updates between samples: a walker crosses a lamp's reach over seconds.
    private const int SampleEvery = 6;

    // The share of its brightness the level moves before it is drawn anew: the step shows no more in a dark room than in daylight.
    private const float Step = .03F;

    private int sinceSample = SampleEvery;

    /// <summary>The tint drawn now.</summary>
    internal Color Level { get; private set; } = new(1, 1, 1, 1);

    /// <summary>The tint for <paramref name="light"/>: full at the tuned light, kept in hue above it, and never under the tuned floor, so the hands stay readable in the dark.</summary>
    internal static Vector3 Tint(Vector3 light, DaggerfallSiteLightingTuning tuning)
    {
        Vector3 level = light / tuning.ViewFull;
        float peak = Peak(level);
        if (peak <= 0) return new(tuning.ViewFloor);
        return peak > 1 ? level / peak : peak < tuning.ViewFloor ? level * (tuning.ViewFloor / peak) : level;
    }

    /// <summary>Called each update with how to read the light the eye stands in; reads it when a sample is due.</summary>
    internal void Update(Func<Vector3> light)
    {
        if (++sinceSample < SampleEvery) return;
        sinceSample = 0;
        Vector3 next = Tint(light(), tuning);
        Vector3 now = new(Level.R, Level.G, Level.B);
        if (Peak(Vector3.Abs(next - now)) >= Step * Peak(now))
            Level = new(next.X, next.Y, next.Z, 1);
    }

    private static float Peak(Vector3 value) => MathF.Max(value.X, MathF.Max(value.Y, value.Z));
}
