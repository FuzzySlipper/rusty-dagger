using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The calendar an admitted interval advances, in the classic calendar's own units.</summary>
public sealed class DaggerfallCalendarTests
{
    [Fact]
    public void Keeps_the_classic_units_and_starts_where_the_corpus_does()
    {
        Assert.Equal(30, DaggerfallCalendar.DaysPerMonth);
        Assert.Equal(12, DaggerfallCalendar.MonthsPerYear);
        Assert.Equal(360, DaggerfallCalendar.DaysPerYear);
        Assert.Equal(86400, DaggerfallCalendar.SecondsPerDay);
        Assert.Equal((405, 5, 0), (DaggerfallCalendar.Start.Year, DaggerfallCalendar.Start.Month, DaggerfallCalendar.Start.Day));
        Assert.Equal(0, DaggerfallCalendar.Start.ToAbsoluteSeconds());

        // The calendar round-trips through its own seconds, which is what makes an interval additive.
        DaggerfallCalendar date = new(412, 2, 17, 13, 45, 30);
        Assert.Equal(date, DaggerfallCalendar.FromAbsoluteSeconds(date.ToAbsoluteSeconds()));
    }

    [Fact]
    public void Crosses_the_day_month_and_year_boundaries_in_order()
    {
        DaggerfallCalendar start = new(405, 5, 29, 23, 59, 30);

        // One minute crosses the day, the month and the year, and each is reported: the first of the
        // month, the first of the sixth month of the year, and the wrap into the next year.
        DaggerfallCalendar advanced = start.Advance(30, out DaggerfallCalendarElapsed elapsed);
        Assert.Equal(new DaggerfallCalendar(405, 6, 0, 0, 0, 0), advanced);
        Assert.Equal(1, elapsed.Days);
        Assert.Equal(30, elapsed.Seconds);
        Assert.Equal(1, elapsed.DayOfMonth);
        Assert.Equal(1, elapsed.Hour);
        Assert.True(elapsed.Days >= 1);

        // A whole year is twelve month boundaries and one year boundary, and the weekday has cycled.
        DaggerfallCalendar year = new(405, 5, 0, 12, 0, 0);
        DaggerfallCalendar same = year.Advance(DaggerfallCalendar.SecondsPerYear, out DaggerfallCalendarElapsed yearElapsed);
        Assert.Equal((406, 5, 0), (same.Year, same.Month, same.Day));
        Assert.Equal(1, yearElapsed.Years);
        Assert.Equal(12, yearElapsed.Months);

        // A year is three hundred and sixty days, which is fifty-one weeks and three days, so the
        // weekday advances by three rather than repeating: this is a fact about the classic calendar,
        // not a rounding artefact, and a calendar that repeated the weekday would be wrong.
        Assert.Equal(3, DaggerfallCalendar.DaysPerYear % DaggerfallCalendar.DaysPerWeek);
        Assert.Equal((year.DayOfWeek + 3) % DaggerfallCalendar.DaysPerWeek, same.DayOfWeek);

        // The year rolls December to January rather than producing a thirteenth month. Days are
        // zero-based, so the last day of a month is twenty-nine and not thirty: the first version of
        // this test asked for a day that does not exist.
        DaggerfallCalendar december = new(405, 11, DaggerfallCalendar.DaysPerMonth - 1, 0, 0, 0);
        Assert.Equal(new DaggerfallCalendar(406, 0, 0, 0, 0, 0), december.Advance(DaggerfallCalendar.SecondsPerDay, out _));
    }

    [Fact]
    public void A_non_positive_interval_changes_nothing_and_crosses_nothing()
    {
        DaggerfallCalendar date = new(405, 5, 10, 9, 30, 0);
        Assert.Equal(date, date.Advance(0, out DaggerfallCalendarElapsed none));
        Assert.Equal(default, none);
        Assert.Equal(date, date.Advance(-3600, out DaggerfallCalendarElapsed backwards));
        Assert.Equal(default, backwards);
    }

    [Fact]
    public void Reads_the_season_and_the_daylight_the_hour_implies()
    {
        // Seasons divide the twelve months three at a time, and daylight is the donor's dawn-to-dusk.
        Assert.Equal(DaggerfallSeason.Spring, new DaggerfallCalendar(405, 1, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Summer, new DaggerfallCalendar(405, 4, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Autumn, new DaggerfallCalendar(405, 7, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Winter, new DaggerfallCalendar(405, 10, 0, 0, 0, 0).Season);

        Assert.False(new DaggerfallCalendar(405, 5, 0, DaggerfallCalendar.DawnHour - 1, 0, 0).IsDay);
        Assert.True(new DaggerfallCalendar(405, 5, 0, DaggerfallCalendar.DawnHour, 0, 0).IsDay);
        Assert.True(new DaggerfallCalendar(405, 5, 0, DaggerfallCalendar.DuskHour - 1, 0, 0).IsDay);
        Assert.False(new DaggerfallCalendar(405, 5, 0, DaggerfallCalendar.DuskHour, 0, 0).IsDay);

        // Crossing dawn or dusk is reported, which is what a light or a schedule reacts to.
        DaggerfallCalendar beforeDawn = new(405, 5, 0, DaggerfallCalendar.DawnHour - 1, 59, 0);
        _ = beforeDawn.Advance(120, out DaggerfallCalendarElapsed crossed);
        Assert.True(crossed.DaylightChanged);
        _ = new DaggerfallCalendar(405, 5, 0, 12, 0, 0).Advance(600, out DaggerfallCalendarElapsed steady);
        Assert.False(steady.DaylightChanged);
    }
}
