using Rusty.Engine;
using WorldRpg.Kit.World;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// The eyes of a <see cref="SplatFigure"/>: the splat group (the GLB's <c>_GROUP</c>) its eye Gaussians are in, and the
/// colour a blink turns them, its lids' (sRGB-encoded, as the Engine's group tint takes it).
/// </summary>
public sealed record SplatEyes(byte Group, Color Lid);

/// <summary>
/// How walkers drawn as splat figures with eyes blink: each waits <paramref name="IntervalMinSeconds"/> to
/// <paramref name="IntervalMaxSeconds"/> between blinks, drawn from its seed, and its lids close over
/// <paramref name="ClosingSeconds"/>, stay shut <paramref name="ClosedSeconds"/> and open over <paramref name="OpeningSeconds"/>.
/// </summary>
public sealed record SplatBlinks(float IntervalMinSeconds, float IntervalMaxSeconds, float ClosingSeconds, float ClosedSeconds, float OpeningSeconds)
{
    /// <summary>How long one blink takes, closing to open.</summary>
    public float Seconds => ClosingSeconds + ClosedSeconds + OpeningSeconds;

    public SplatBlinks Validate()
    {
        if (!(IntervalMinSeconds > 0) || !(IntervalMaxSeconds >= IntervalMinSeconds) || !float.IsFinite(IntervalMaxSeconds)
            || !(ClosingSeconds >= 0) || !(ClosedSeconds >= 0) || !(OpeningSeconds >= 0) || !float.IsFinite(Seconds) || !(Seconds > 0))
            throw new ArgumentException($"Splat figures blink after more than 0 seconds, up to a longer finite interval, and a blink takes a finite time above 0 " +
                $"of closing, shut and opening times of 0 or more, not {this}.");
        return this;
    }

    /// <summary>How shut the lids are <paramref name="seconds"/> into a blink: 0 open, 1 shut.</summary>
    public float Closure(float seconds) =>
        seconds < 0 || seconds >= Seconds ? 0F
        : seconds < ClosingSeconds ? seconds / ClosingSeconds
        : seconds < ClosingSeconds + ClosedSeconds ? 1F
        : 1F - (seconds - ClosingSeconds - ClosedSeconds) / OpeningSeconds;

    /// <summary>The blinks of a walker of stable <paramref name="seed"/>, from now; the same seed blinks alike in every run.</summary>
    public SplatBlinkClock Clock(int seed) => new(this, seed);
}

/// <summary>
/// One walker's blinks, keyed by its seed. Its first comes within the longest interval, so walkers shown together don't
/// blink together; each later one the seed's next interval after the last ends.
/// </summary>
public sealed class SplatBlinkClock
{
    private readonly SplatBlinks blinks;
    private readonly int seed;
    private int count;
    private float time;
    private float start;

    internal SplatBlinkClock(SplatBlinks blinks, int seed)
    {
        this.blinks = blinks;
        this.seed = seed;
        start = Draw() * blinks.IntervalMaxSeconds;
    }

    /// <summary>Moves on by <paramref name="elapsed"/> seconds of drawn time and says how shut the lids are then: 0 open, 1 shut.</summary>
    public float Step(float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed), elapsed, "Drawn time advances by 0 or more seconds.");
        time += elapsed;
        while (time >= start + blinks.Seconds)
            start += blinks.Seconds + blinks.IntervalMinSeconds + Draw() * (blinks.IntervalMaxSeconds - blinks.IntervalMinSeconds);
        return blinks.Closure(time - start);
    }

    /// <summary>The seed's next draw, 0 to under 1.</summary>
    private float Draw() => StableHash.Mixed($"splat-blink/{count++}/{seed}") % 1_000_000 / 1_000_000F;
}
