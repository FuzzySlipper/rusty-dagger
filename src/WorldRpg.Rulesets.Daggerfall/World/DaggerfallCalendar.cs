namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>Which part of the year a date falls in, as the game's own seasons divide it.</summary>
public enum DaggerfallSeason
{
    /// <summary>Morning Star through First Seed.</summary>
    Spring,

    /// <summary>Rain's Hand through Last Seed.</summary>
    Summer,

    /// <summary>Hearth Fire through Frost Fall.</summary>
    Autumn,

    /// <summary>Sun's Height's end through Evening Star.</summary>
    Winter,
}

/// <summary>
/// The instant the world's calendar reads: a date and a time of day in explicit units.
/// </summary>
/// <remarks>
/// The units are the classic ones: sixty seconds to a minute, sixty minutes to an hour, twenty-four
/// hours to a day, thirty days to a month and twelve months to a year, so a year is exactly three
/// hundred and sixty days and a month always has thirty. There are no leap years and no calendar
/// drift to model, which is why this is a value rather than a clock: it is advanced by whoever owns
/// an admitted interval, never by itself.
/// </remarks>
/// <param name="Year">The year, which the corpus starts at 405.</param>
/// <param name="Month">The month, zero-based, twelve to a year.</param>
/// <param name="Day">The day of the month, zero-based, thirty to a month.</param>
/// <param name="Hour">The hour of the day, zero-based.</param>
/// <param name="Minute">The minute of the hour.</param>
/// <param name="Second">The second of the minute.</param>
public readonly record struct DaggerfallCalendar(int Year, int Month, int Day, int Hour, int Minute, int Second)
{
    /// <summary>Seconds in a minute.</summary>
    public const int SecondsPerMinute = 60;

    /// <summary>Minutes in an hour.</summary>
    public const int MinutesPerHour = 60;

    /// <summary>Hours in a day.</summary>
    public const int HoursPerDay = 24;

    /// <summary>Days in a week, which is what cycles the weekday.</summary>
    public const int DaysPerWeek = 7;

    /// <summary>Days in a month: every month, since none is longer.</summary>
    public const int DaysPerMonth = 30;

    /// <summary>Months in a year.</summary>
    public const int MonthsPerYear = 12;

    /// <summary>Days in a year, which is the product and not an approximation.</summary>
    public const int DaysPerYear = DaysPerMonth * MonthsPerYear;

    /// <summary>Seconds in a day.</summary>
    public const int SecondsPerDay = SecondsPerMinute * MinutesPerHour * HoursPerDay;

    /// <summary>Seconds in a year.</summary>
    public const int SecondsPerYear = SecondsPerDay * DaysPerYear;

    /// <summary>The year the corpus's calendar starts in.</summary>
    public const int FirstYear = 405;

    /// <summary>The month the corpus's calendar starts in, zero-based.</summary>
    public const int FirstMonth = 5;

    /// <summary>The hour dawn begins, which is when the world's day changes character.</summary>
    public const int DawnHour = 6;

    /// <summary>The hour dusk begins.</summary>
    public const int DuskHour = 18;

    /// <summary>The calendar the corpus starts a game on.</summary>
    public static DaggerfallCalendar Start => new(FirstYear, FirstMonth, 0, 0, 0, 0);

    /// <summary>The second of the day this instant falls on, from zero at midnight.</summary>
    public int SecondOfDay => (Hour * MinutesPerHour * SecondsPerMinute) + (Minute * SecondsPerMinute) + Second;

    /// <summary>Whether it is day: dawn to dusk, as the world's own hours divide it.</summary>
    public bool IsDay => Hour >= DawnHour && Hour < DuskHour;

    /// <summary>The season the date falls in, by its month.</summary>
    public DaggerfallSeason Season => Month switch
    {
        >= 0 and <= 2 => DaggerfallSeason.Spring,
        >= 3 and <= 5 => DaggerfallSeason.Summer,
        >= 6 and <= 8 => DaggerfallSeason.Autumn,
        _ => DaggerfallSeason.Winter,
    };

    /// <summary>
    /// The weekday, which the classic calendar counts from the start date rather than from a
    /// historical anchor.
    /// </summary>
    public int DayOfWeek
    {
        get
        {
            long days = DayNumber;
            int weekday = (int)(days % DaysPerWeek);
            return weekday < 0 ? weekday + DaysPerWeek : weekday;
        }
    }

    /// <summary>
    /// The day of the year, counted from one at the year's first day, which is what the holiday
    /// table is keyed by.
    /// </summary>
    public int DayOfYear => (Month * DaysPerMonth) + Day + 1;

    /// <summary>
    /// Which holiday the date is, or zero when it is not one, for the region that would celebrate it.
    /// </summary>
    /// <remarks>
    /// The classic rule (the donor's <c>FormulaHelper.GetHolidayId</c>) is a table of fifty-three
    /// holidays, each on one day of the year and each celebrated either everywhere or in one region.
    /// A region is named one-based here, which is the donor's own convention: its table stores the
    /// region's index plus one, or 0xFF for a holiday every region keeps. Days past the table's last
    /// entry are never a holiday, which is why the check on the day comes before the search rather
    /// than after it.
    /// </remarks>
    /// <param name="regionIndex">The zero-based source region index whose calendar this is.</param>
    public int GetHolidayId(int regionIndex)
    {
        if (regionIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(regionIndex), regionIndex, "A region index is not negative.");
        }

        int dayOfYear = DayOfYear;
        if (dayOfYear > HolidayLastDay)
        {
            return 0;
        }

        for (int holiday = 0; holiday < HolidaysCelebratedIn.Length; holiday++)
        {
            bool kept = HolidaysCelebratedIn[holiday] == EveryRegion || HolidaysCelebratedIn[holiday] == regionIndex + 1;
            if (kept && dayOfYear == HolidayDaysOfYear[holiday])
            {
                return holiday + 1;
            }
        }

        return 0;
    }

    /// <summary>The value the holiday table uses for a holiday every region keeps.</summary>
    private const byte EveryRegion = 0xFF;

    /// <summary>The last day of the year the holiday table reaches; later days are never a holiday.</summary>
    private const int HolidayLastDay = 355;

    /// <summary>
    /// Which regions celebrate each holiday, by holiday: a region's index plus one, or every region.
    /// </summary>
    private static readonly byte[] HolidaysCelebratedIn =
    [
        0xFF, 0x19, 0x01, 0xFF, 0x1D, 0x05, 0x19, 0x06, 0x3C, 0xFF, 0x29, 0x1A,
        0xFF, 0x02, 0x19, 0x01, 0x0E, 0x12, 0x14, 0xFF, 0xFF, 0x1C, 0x21, 0x1F, 0x2C, 0xFF, 0x12,
        0x23, 0xFF, 0x38, 0xFF, 0x01, 0x30, 0x29, 0x0B, 0x16, 0xFF, 0xFF, 0x11, 0x17, 0x14, 0x01,
        0xFF, 0x13, 0xFF, 0x33, 0x3C, 0x2E, 0xFF, 0xFF, 0x01, 0x2D, 0x18,
    ];

    /// <summary>The day of the year each holiday falls on, by holiday.</summary>
    private static readonly short[] HolidayDaysOfYear =
    [
        0x01, 0x02, 0x0C, 0x0F, 0x10, 0x12, 0x20, 0x23, 0x26, 0x2E, 0x39, 0x3A,
        0x43, 0x45, 0x55, 0x56, 0x5B, 0x67, 0x6E, 0x76, 0x7F, 0x81, 0x8C, 0x96, 0x97, 0xA6, 0xAD,
        0xAE, 0xBE, 0xC0, 0xC8, 0xD1, 0xD4, 0xDD, 0xE0, 0xE7, 0xED, 0xF3, 0xF6, 0xFC, 0x103, 0x113,
        0x11B, 0x125, 0x12C, 0x12F, 0x134, 0x13E, 0x140, 0x159, 0x15C, 0x162, 0x163,
    ];

    /// <summary>The day of the year each holiday falls on, by its one-based identity.</summary>
    public static IReadOnlyList<int> HolidayDays => [.. HolidayDaysOfYear.Select(day => (int)day)];

    /// <summary>How many holidays the table carries.</summary>
    public static int HolidayCount => HolidayDaysOfYear.Length;

    /// <summary>How many whole days have passed since the calendar's first day.</summary>
    public long DayNumber => (((long)Year - FirstYear) * DaysPerYear) + ((Month - FirstMonth) * DaysPerMonth) + Day;

    /// <summary>
    /// Advances the calendar by an interval, and reports what boundaries it crossed.
    /// </summary>
    /// <remarks>
    /// One operation covers every kind of elapsed time the game has - ordinary play, rest, travel and
    /// prison - because they differ in who owns the interval, not in how the calendar moves. A
    /// non-positive interval changes nothing and crosses nothing, which is how a rejected duration is
    /// kept from looking like a successful advance.
    /// </remarks>
    /// <param name="seconds">The elapsed seconds to advance by.</param>
    public DaggerfallCalendar Advance(long seconds, out DaggerfallCalendarElapsed elapsed)
    {
        elapsed = default;
        if (seconds <= 0) return this;
        if (seconds > MaximumIntervalSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds,
                $"An interval beyond {MaximumIntervalSeconds} seconds cannot be represented as a date, so it is refused rather than returned as a year no calendar describes.");
        }

        long total = ToAbsoluteSeconds() + seconds;
        DaggerfallCalendar advanced = FromAbsoluteSeconds(total);
        elapsed = new DaggerfallCalendarElapsed(
            seconds,
            Days(advanced.DayNumber - DayNumber),
            Months(advanced, this),
            advanced.Year - Year,
            advanced.Day < Day ? 1 : 0,
            advanced.Hour < Hour || (advanced.Hour == Hour && advanced.Minute < Minute) ? 1 : 0,
            CrossesDaylight(seconds));
        return advanced;
    }

    /// <summary>
    /// The largest interval an advance accepts, which is a million years of game seconds.
    /// </summary>
    /// <remarks>
    /// The calendar's fields are 32-bit and its elapsed counts are 32-bit, so an interval beyond this
    /// could not be represented even in principle. Refusing it is the difference between a caller's
    /// arithmetic error and a returned year that no date can describe.
    /// </remarks>
    public const long MaximumIntervalSeconds = (long)SecondsPerYear * 1_000_000;

    /// <summary>The consequence identity that means no consequence stopped an advance.</summary>
    public const int NoConsequence = -1;

    /// <summary>
    /// Advances by an interval, stopping at the first consequence it meets.
    /// </summary>
    /// <remarks>
    /// Every kind of elapsed time the game has - ordinary play, rest, travel and prison - comes through
    /// here as one interval, because they differ in who owns the interval rather than in how the
    /// calendar moves, and the caller's own deadlines decide where it stops. The consequence that
    /// fires is the earliest one inside the interval; two at the same second are ordered by identity so
    /// that the same interval and the same deadlines always stop in the same place. A consequence at
    /// exactly the end of the interval is still a stop, and one beyond it is not reached.
    /// </remarks>
    /// <param name="seconds">The elapsed seconds to advance by.</param>
    /// <param name="consequences">The deadlines the caller owns, as an identity and seconds from now.</param>
    public DaggerfallCalendarAdvance AdvanceToFirstConsequence(
        long seconds,
        IReadOnlyList<(int Identity, long SecondsFromNow)> consequences)
    {
        ArgumentNullException.ThrowIfNull(consequences);
        if (seconds <= 0)
        {
            // Nothing is owed by a negative interval: reporting it back as remaining would hand the
            // caller a negative amount of time to resume with, which no interval can be.
            return new DaggerfallCalendarAdvance(this, 0, 0, NoConsequence, default);
        }

        // An identity below zero would be indistinguishable from "no consequence", so it is refused
        // rather than silently reported as none.
        int fired = NoConsequence;
        long applied = seconds;
        foreach ((int identity, long secondsFromNow) in consequences)
        {
            if (identity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(consequences), identity, "A consequence identity is not negative.");
            }

            if (secondsFromNow < 0 || secondsFromNow > seconds)
            {
                continue;
            }

            if (secondsFromNow < applied || (secondsFromNow == applied && (fired == NoConsequence || identity < fired)))
            {
                applied = secondsFromNow;
                fired = identity;
            }
        }

        DaggerfallCalendar advanced = Advance(applied, out DaggerfallCalendarElapsed crossed);
        return new DaggerfallCalendarAdvance(advanced, applied, seconds - applied, fired, crossed);
    }

    /// <summary>This instant as seconds from the calendar's own start, which is what arithmetic uses.</summary>
    public long ToAbsoluteSeconds() => (DayNumber * SecondsPerDay) + SecondOfDay;

    /// <summary>The instant a count of seconds from the calendar's start names.</summary>
    public static DaggerfallCalendar FromAbsoluteSeconds(long seconds)
    {
        // Floor division rather than truncation: an instant before the calendar's first day is a real
        // instant - the classic corpus starts in month five of year 405, so the first four months of
        // that year are behind it - and truncation would answer with a negative second and a day in
        // the previous month instead of the date that instant actually is.
        long day = (long)Math.Floor((double)seconds / SecondsPerDay);
        int secondOfDay = (int)(seconds - (day * SecondsPerDay));
        long years = (long)Math.Floor((double)day / DaysPerYear);
        int dayOfYear = (int)(day - (years * DaysPerYear));
        int year = checked(FirstYear + (int)years);
        int month = FirstMonth + (dayOfYear / DaysPerMonth);
        // A start month later than the first wraps the year forward rather than producing a month
        // beyond December, which is where a single division would land.
        year = checked(year + (month / MonthsPerYear));
        month %= MonthsPerYear;
        return new DaggerfallCalendar(
            year,
            month,
            dayOfYear % DaysPerMonth,
            secondOfDay / (MinutesPerHour * SecondsPerMinute),
            (secondOfDay / SecondsPerMinute) % MinutesPerHour,
            secondOfDay % SecondsPerMinute);
    }

    /// <summary>
    /// Whether an advance of this many seconds covers dawn or dusk, including when both ends of it are
    /// on the same side of the boundary: a jump from noon past the next noon passes both.
    /// </summary>
    private bool CrossesDaylight(long seconds)
    {
        if (seconds >= SecondsPerDay)
        {
            return true;
        }

        int start = SecondOfDay;
        int end = start + (int)seconds;
        int dawn = DawnHour * MinutesPerHour * SecondsPerMinute;
        int dusk = DuskHour * MinutesPerHour * SecondsPerMinute;
        return (start < dawn && end >= dawn)
            || (start < dusk && end >= dusk)
            || (start >= dawn && end >= SecondsPerDay + dawn)
            || (start >= dusk && end >= SecondsPerDay + dusk);
    }

    private static int Days(long days) => checked((int)days);

    private static int Months(DaggerfallCalendar advanced, DaggerfallCalendar origin) =>
        (int)(((advanced.Year - origin.Year) * MonthsPerYear) + advanced.Month - origin.Month);
}

/// <summary>
/// One advance up to the first consequence it meets, and what is left of the interval.
/// </summary>
/// <param name="Calendar">The calendar at the point the advance stopped.</param>
/// <param name="AppliedSeconds">How much of the interval was applied before stopping.</param>
/// <param name="RemainingSeconds">How much of the interval the caller still owns.</param>
/// <param name="Consequence">The identity of the consequence that stopped it, or none.</param>
/// <param name="Crossed">What the applied part crossed, in boundary order.</param>
public readonly record struct DaggerfallCalendarAdvance(
    DaggerfallCalendar Calendar,
    long AppliedSeconds,
    long RemainingSeconds,
    int Consequence,
    DaggerfallCalendarElapsed Crossed)
{
    /// <summary>Whether a consequence stopped the advance before the interval was spent.</summary>
    public bool Interrupted => Consequence != DaggerfallCalendar.NoConsequence;
}

/// <summary>What one advance crossed, in the order the calendar crosses it.</summary>
/// <param name="Seconds">The interval that was applied.</param>
/// <param name="Days">How many days were crossed in whole.</param>
/// <param name="Months">How many month boundaries were crossed.</param>
/// <param name="Years">How many year boundaries were crossed.</param>
/// <param name="DayOfMonth">How many day-of-month boundaries were crossed, which is one when the day wrapped.</param>
/// <param name="Hour">How many hour boundaries were crossed, which is one when the clock wrapped.</param>
/// <param name="DaylightChanged">Whether the advance crossed dawn or dusk.</param>
public readonly record struct DaggerfallCalendarElapsed(
    long Seconds,
    int Days,
    int Months,
    int Years,
    int DayOfMonth,
    int Hour,
    bool DaylightChanged);
