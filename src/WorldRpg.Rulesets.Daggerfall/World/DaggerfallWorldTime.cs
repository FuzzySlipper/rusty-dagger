namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The world's clock: a calendar and the fraction of a game second not yet applied to it.
/// </summary>
/// <remarks>
/// The clock is advanced by whoever owns the admitted update, with the duration that update was
/// admitted for and the tuning that says how many game seconds a real second buys. It keeps no timer,
/// schedules nothing and never advances itself: the fraction is here so that a scale which does not
/// divide evenly does not lose time, which is why it is persisted beside the date.
/// </remarks>
internal sealed class DaggerfallWorldTime(
    DaggerfallCalendar calendar,
    double remainderSeconds,
    double gameSecondsPerRealSecond)
{
    private double _remainder = remainderSeconds;

    /// <summary>How many game seconds one admitted real second buys, from the corpus's tuning.</summary>
    internal double GameSecondsPerRealSecond { get; } = gameSecondsPerRealSecond > 0d && double.IsFinite(gameSecondsPerRealSecond)
        ? gameSecondsPerRealSecond
        : throw new ArgumentOutOfRangeException(nameof(gameSecondsPerRealSecond));

    internal DaggerfallCalendar Calendar { get; private set; } = calendar;

    /// <summary>The part of a game second not yet applied to the calendar.</summary>
    internal double RemainderSeconds => _remainder;

    /// <summary>Advances the clock by an admitted real duration at the given scale.</summary>
    internal void Advance(double realSeconds)
    {
        if (!double.IsFinite(realSeconds) || realSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(realSeconds));

        _remainder += realSeconds * GameSecondsPerRealSecond;
        // Three tenths of a second, three times, then a tenth is a whole second by arithmetic but not
        // in binary floating point: the floor is taken with a tolerance, or the clock would lose the
        // second the player watched it earn.
        long whole = (long)Math.Floor(_remainder + 1e-9);
        if (whole <= 0)
        {
            return;
        }

        Calendar = Calendar.Advance(whole, out _);
        _remainder -= whole;
    }
}
