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
    public void Stops_at_the_first_consequence_the_interval_meets()
    {
        // A rest of an hour with a consequence in twenty minutes: the calendar advances twenty minutes,
        // reports which consequence stopped it, and leaves the rest of the interval to the caller -
        // which is what keeps each consumer owning its own periodic work rather than a scheduler.
        DaggerfallCalendar start = new(405, 5, 0, 8, 0, 0);
        DaggerfallCalendarAdvance advance = start.AdvanceToFirstConsequence(3600, [(7, 1200), (3, 4000)]);
        Assert.True(advance.Interrupted);
        Assert.Equal(7, advance.Consequence);
        Assert.Equal(1200, advance.AppliedSeconds);
        Assert.Equal(2400, advance.RemainingSeconds);
        Assert.Equal(new DaggerfallCalendar(405, 5, 0, 8, 20, 0), advance.Calendar);

        // Two consequences at the same second are ordered by identity, so the same interval and the
        // same deadlines always stop in the same place.
        Assert.Equal(3, start.AdvanceToFirstConsequence(3600, [(7, 1200), (3, 1200)]).Consequence);
        Assert.Equal(3, start.AdvanceToFirstConsequence(3600, [(3, 1200), (7, 1200)]).Consequence);

        // A consequence exactly at the end of the interval is still a stop; one beyond it is not reached,
        // and an interval with none spends itself.
        Assert.Equal(9, start.AdvanceToFirstConsequence(600, [(9, 600)]).Consequence);
        DaggerfallCalendarAdvance none = start.AdvanceToFirstConsequence(600, [(9, 601)]);
        Assert.False(none.Interrupted);
        Assert.Equal(DaggerfallCalendar.NoConsequence, none.Consequence);
        Assert.Equal((600, 0), (none.AppliedSeconds, none.RemainingSeconds));
        Assert.Equal(new DaggerfallCalendar(405, 5, 0, 8, 10, 0), none.Calendar);

        // A consequence already due stops the advance without moving the calendar, and the remaining
        // interval is the whole of it.
        DaggerfallCalendarAdvance due = start.AdvanceToFirstConsequence(3600, [(1, 0)]);
        Assert.Equal((1, 0, 3600), (due.Consequence, due.AppliedSeconds, due.RemainingSeconds));
        Assert.Equal(start, due.Calendar);

        // A non-positive interval changes nothing and reaches no consequence, and a negative identity is
        // refused rather than reported as none.
        Assert.Equal(DaggerfallCalendar.NoConsequence, start.AdvanceToFirstConsequence(0, [(1, 0)]).Consequence);
        Assert.Equal(0, start.AdvanceToFirstConsequence(-60, [(1, 0)]).AppliedSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => start.AdvanceToFirstConsequence(600, [(-1, 60)]));

        // The applied part still reports what it crossed, in the calendar's own order: an interval that
        // runs into the next day reports the day boundary rather than only the hour.
        DaggerfallCalendarAdvance midnight = new DaggerfallCalendar(405, 5, 0, 23, 59, 30)
            .AdvanceToFirstConsequence(120, [(4, 120)]);
        Assert.Equal(1, midnight.Crossed.Days);
        Assert.Equal(new DaggerfallCalendar(405, 5, 1, 0, 1, 30), midnight.Calendar);
    }

    [Fact]
    public void Converts_an_instant_before_the_first_day_back_to_itself()
    {
        // The classic corpus starts in month five of year 405, so the first four months of that year
        // are behind the calendar's first day. Truncating division answered those with a negative
        // second and a day in the previous month; floor division answers with the date they are.
        Assert.Equal(new DaggerfallCalendar(405, 4, 29, 23, 59, 59), DaggerfallCalendar.FromAbsoluteSeconds(-1));

        // Every instant in the first four months round-trips to itself, which is what "the calendar can
        // describe a date before its own first day" has to mean.
        foreach (DaggerfallCalendar date in new[]
        {
            new DaggerfallCalendar(405, 0, 0, 0, 0, 0),
            new DaggerfallCalendar(405, 2, 15, 12, 30, 30),
            new DaggerfallCalendar(405, 4, 29, 23, 59, 59),
            new DaggerfallCalendar(404, 11, 29, 6, 0, 0),
        })
        {
            Assert.Equal(date, DaggerfallCalendar.FromAbsoluteSeconds(date.ToAbsoluteSeconds()));
        }

        // An interval no date can describe is refused rather than returned as a year that does not
        // exist, and a negative interval owes nothing rather than a negative amount of time.
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallCalendar.Start.Advance(DaggerfallCalendar.MaximumIntervalSeconds + 1, out _));
        DaggerfallCalendarAdvance backwards = new DaggerfallCalendar(405, 5, 0, 0, 0, 0).AdvanceToFirstConsequence(-60, [(1, 0)]);
        Assert.Equal((0L, 0L), (backwards.AppliedSeconds, backwards.RemainingSeconds));
    }

    [Fact]
    public void Reports_daylight_crossed_even_when_the_advance_encloses_it()
    {
        // Noon to the next noon passes dusk and dawn, so the flag is set even though both ends are
        // daytime: comparing the endpoints alone would miss both boundaries.
        DaggerfallCalendarAdvance full = new DaggerfallCalendar(405, 5, 0, 12, 0, 0).AdvanceToFirstConsequence(DaggerfallCalendar.SecondsPerDay, []);
        Assert.True(full.Crossed.DaylightChanged);

        // The same for an advance that stays inside the day but steps over dusk.
        DaggerfallCalendarAdvance dusk = new DaggerfallCalendar(405, 5, 0, DaggerfallCalendar.DuskHour - 1, 0, 0).AdvanceToFirstConsequence(7200, []);
        Assert.True(dusk.Crossed.DaylightChanged);

        // An advance inside one stretch of daylight crosses none.
        DaggerfallCalendarAdvance inside = new DaggerfallCalendar(405, 5, 0, 9, 0, 0).AdvanceToFirstConsequence(600, []);
        Assert.False(inside.Crossed.DaylightChanged);
    }

    [Fact]
    public void Reads_the_holiday_a_date_is_for_the_region_that_keeps_it()
    {
        // The classic table: fifty-three holidays, each on one day of the year and each kept either
        // everywhere or in a single region, whose identity is the table's value minus one.
        Assert.Equal(53, DaggerfallCalendar.HolidayCount);
        Assert.Equal(53, DaggerfallCalendar.HolidayDays.Count);

        // The first holiday is the year's first day and every region keeps it; the twelfth day is the
        // third holiday, kept by region 0x01 - the first region - and by no other.
        DaggerfallCalendar firstDay = new(405, 0, 0, 0, 0, 0);
        Assert.Equal(1, firstDay.DayOfYear);
        Assert.Equal(1, firstDay.GetHolidayId(0));
        Assert.Equal(1, firstDay.GetHolidayId(60));

        DaggerfallCalendar twelfthDay = new(405, 0, 11, 0, 0, 0);
        Assert.Equal(12, twelfthDay.DayOfYear);
        Assert.Equal(3, twelfthDay.GetHolidayId(0));
        Assert.Equal(0, twelfthDay.GetHolidayId(1));

        // A day that is not a holiday is none, in every region, and a day past the table's last entry
        // is never one even when the table has no entry beyond it.
        DaggerfallCalendar ordinary = new(405, 0, 3, 0, 0, 0);
        Assert.Equal(0, ordinary.GetHolidayId(0));
        Assert.Equal(0, ordinary.GetHolidayId(24));
        Assert.Equal(0, new DaggerfallCalendar(405, 11, 29, 0, 0, 0).GetHolidayId(0));
        Assert.Equal(360, new DaggerfallCalendar(405, 11, 29, 0, 0, 0).DayOfYear);

        // The region the holiday table names is the donor's own convention: its value is the region
        // index plus one, so the holiday kept by region 0x19 is found at index 0x19 - 1 and nowhere
        // else. Holiday 2 carries 0x19.
        DaggerfallCalendar secondHoliday = new(405, 0, 1, 0, 0, 0);
        Assert.Equal(2, secondHoliday.DayOfYear);
        Assert.Equal(2, secondHoliday.GetHolidayId(0x19 - 1));
        Assert.Equal(0, secondHoliday.GetHolidayId(0x19));
        Assert.Equal(0, secondHoliday.GetHolidayId(0x19 - 2));

        // A negative region is not a region.
        Assert.Throws<ArgumentOutOfRangeException>(() => firstDay.GetHolidayId(-1));
    }

    [Fact]
    public void Reads_the_season_and_the_daylight_the_hour_implies()
    {
        // Seasons divide the twelve months three at a time from the donor's own table, where winter
        // wraps the year's end rather than starting it, and daylight is the donor's dawn-to-dusk.
        Assert.Equal(DaggerfallSeason.Winter, new DaggerfallCalendar(405, 11, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Winter, new DaggerfallCalendar(405, 0, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Winter, new DaggerfallCalendar(405, 1, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Spring, new DaggerfallCalendar(405, 2, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Spring, new DaggerfallCalendar(405, 4, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Summer, new DaggerfallCalendar(405, 5, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Summer, new DaggerfallCalendar(405, 7, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Autumn, new DaggerfallCalendar(405, 8, 0, 0, 0, 0).Season);
        Assert.Equal(DaggerfallSeason.Autumn, new DaggerfallCalendar(405, 10, 0, 0, 0, 0).Season);

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
