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

        long total = ToAbsoluteSeconds() + seconds;
        DaggerfallCalendar advanced = FromAbsoluteSeconds(total);
        elapsed = new DaggerfallCalendarElapsed(
            seconds,
            Days(advanced.DayNumber - DayNumber),
            Months(advanced, this),
            advanced.Year - Year,
            advanced.Day < Day ? 1 : 0,
            advanced.Hour < Hour || (advanced.Hour == Hour && advanced.Minute < Minute) ? 1 : 0,
            advanced.IsDay != IsDay);
        return advanced;
    }

    /// <summary>This instant as seconds from the calendar's own start, which is what arithmetic uses.</summary>
    public long ToAbsoluteSeconds() => (DayNumber * SecondsPerDay) + SecondOfDay;

    /// <summary>The instant a count of seconds from the calendar's start names.</summary>
    public static DaggerfallCalendar FromAbsoluteSeconds(long seconds)
    {
        int day = (int)(seconds / SecondsPerDay);
        int secondOfDay = (int)(seconds % SecondsPerDay);
        int year = FirstYear + (day / DaysPerYear);
        int dayOfYear = day % DaysPerYear;
        int month = FirstMonth + (dayOfYear / DaysPerMonth);
        // A start month later than the first wraps the year forward rather than producing a month
        // beyond December, which is where a single division would land.
        year += month / MonthsPerYear;
        month %= MonthsPerYear;
        return new DaggerfallCalendar(
            year,
            month,
            dayOfYear % DaysPerMonth,
            secondOfDay / (MinutesPerHour * SecondsPerMinute),
            (secondOfDay / SecondsPerMinute) % MinutesPerHour,
            secondOfDay % SecondsPerMinute);
    }

    private static int Days(long days) => (int)days;

    private static int Months(DaggerfallCalendar advanced, DaggerfallCalendar origin) =>
        (int)(((advanced.Year - origin.Year) * MonthsPerYear) + advanced.Month - origin.Month);
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
