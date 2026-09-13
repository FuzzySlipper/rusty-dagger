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

    /// <summary>
    /// Advances by an interval its owner supplies in game seconds, stopping at the first consequence.
    /// </summary>
    /// <remarks>
    /// This is the one operation every kind of elapsed time goes through. Ordinary play arrives as an
    /// admitted real duration and is scaled; rest, travel and prison arrive as game seconds their owner
    /// already decided - the donor's rest raises <c>minutesPerTick * 60</c>, its travel the journey's
    /// minutes, its training three hours - so the two paths meet here in the unit the calendar counts.
    /// The fraction of a game second this clock holds is applied first, so an interval does not lose or
    /// repeat the part of a second that was already bought.
    /// <para>
    /// What the caller gets back is where the advance stopped, which consequence stopped it and how
    /// much of the interval is still theirs. Applying the consequence and resuming is the caller's
    /// work: nothing here dispatches anything.
    /// </para>
    /// </remarks>
    /// <param name="gameSeconds">The interval, in game seconds.</param>
    /// <param name="consequences">The deadlines the caller owns, as an identity and game seconds from now.</param>
    internal DaggerfallCalendarAdvance AdvanceInterval(
        long gameSeconds,
        IReadOnlyList<(int Identity, long SecondsFromNow)> consequences)
    {
        ArgumentNullException.ThrowIfNull(consequences);
        if (gameSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gameSeconds), gameSeconds, "An interval is not negative; nothing in the corpus shortens the world's clock.");
        }

        // The clock's own unapplied fraction is spent first, so the interval is applied to a calendar
        // that already accounts for everything the world was given.
        long whole = (long)Math.Floor(_remainder + 1e-9);
        _remainder -= whole;
        DaggerfallCalendarAdvance advance = Calendar.AdvanceToFirstConsequence(gameSeconds + whole, consequences);
        Calendar = advance.Calendar;
        return advance with { AppliedSeconds = advance.AppliedSeconds - whole, RemainingSeconds = advance.RemainingSeconds };
    }

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

        // The floor's tolerance can leave a remainder a hair below zero - a nanosecond at most - and the
        // save refuses a negative remainder, so the clock would emit a state it cannot load again. The
        // value is clamped at its source instead: what this clock reports is always a valid fraction,
        // and the refusal of a crafted remainder stays exactly as strict.
        if (_remainder < 0d)
        {
            _remainder = 0d;
        }
    }
}
