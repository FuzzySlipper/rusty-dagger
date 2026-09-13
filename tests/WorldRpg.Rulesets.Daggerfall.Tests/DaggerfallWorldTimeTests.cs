using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The clock the admitted update advances, and the fraction it must not lose.</summary>
public sealed class DaggerfallWorldTimeTests
{
    [Fact]
    public void Advances_by_the_admitted_duration_at_the_corpus_scale()
    {
        // The donor's scale: one admitted real second is twelve game seconds.
        DaggerfallWorldTime time = new(DaggerfallCalendar.Start, 0d, 12d);
        time.Advance(1d);
        Assert.Equal(new DaggerfallCalendar(405, 5, 0, 0, 0, 12), time.Calendar);
        Assert.Equal(0d, time.RemainderSeconds);

        // An hour of admitted time is five minutes of the world's, which is what the scale means.
        DaggerfallWorldTime hour = new(DaggerfallCalendar.Start, 0d, 12d);
        hour.Advance(3600d);
        Assert.Equal(new DaggerfallCalendar(405, 5, 0, 12, 0, 0), hour.Calendar);

        // A scale that does not divide evenly keeps the part of a game second it has not applied, rather
        // than dropping it: three tenths of a second three times is nine tenths, and the tenth advance
        // is what moves the second hand.
        DaggerfallWorldTime fractional = new(DaggerfallCalendar.Start, 0d, 1d);
        for (int step = 0; step < 3; step++)
        {
            fractional.Advance(0.3d);
        }

        Assert.Equal(0, fractional.Calendar.Second);
        Assert.Equal(0.9d, fractional.RemainderSeconds, 10);
        fractional.Advance(0.1d);
        Assert.Equal(1, fractional.Calendar.Second);
        Assert.Equal(0d, fractional.RemainderSeconds, 10);

        // A resumed clock starts where the save left it, including the part of a second it had not
        // applied, so save and resume cannot lose or duplicate a deadline.
        // A step small enough not to move the hand on its own: the second stays where the save left it
        // and the unapplied part grows by exactly what the scale bought.
        DaggerfallWorldTime resumed = new(fractional.Calendar, 0.4d, 12d);
        resumed.Advance(0.01d);
        Assert.Equal(1, resumed.Calendar.Second);
        Assert.Equal(0.52d, resumed.RemainderSeconds, 10);
    }

    [Fact]
    public void Takes_every_kind_of_interval_through_one_operation()
    {
        // A rest of five game minutes with nothing to interrupt it: the interval is the caller's unit,
        // so the clock does not scale it a second time.
        DaggerfallWorldTime time = new(DaggerfallCalendar.Start, 0d, 12d);
        DaggerfallCalendarAdvance rest = time.AdvanceInterval(300, []);
        Assert.False(rest.Interrupted);
        Assert.Equal((300, 0), (rest.AppliedSeconds, rest.RemainingSeconds));
        Assert.Equal(new DaggerfallCalendar(405, 5, 0, 0, 5, 0), time.Calendar);

        // Travel that something interrupts after ten game minutes: the advance stops there, the caller
        // is told which consequence fired, and the rest of the journey is still theirs to resume.
        DaggerfallCalendarAdvance travel = time.AdvanceInterval(3600, [(11, 600), (12, 900)]);
        Assert.True(travel.Interrupted);
        Assert.Equal(11, travel.Consequence);
        Assert.Equal((600, 3000), (travel.AppliedSeconds, travel.RemainingSeconds));
        Assert.Equal(new DaggerfallCalendar(405, 5, 0, 0, 15, 0), time.Calendar);

        // An interval that is interrupted immediately leaves the clock where it was and the whole
        // interval outstanding, which is how a caller applies a consequence it already owes.
        DaggerfallCalendarAdvance due = time.AdvanceInterval(60, [(3, 0)]);
        Assert.Equal((3, 0, 60), (due.Consequence, due.AppliedSeconds, due.RemainingSeconds));
        Assert.Equal(new DaggerfallCalendar(405, 5, 0, 0, 15, 0), time.Calendar);

        // The two kinds interleave on one clock: an interval of one game second applies exactly one,
        // and a fraction smaller than a second stays unapplied until admitted play buys enough to make
        // it whole - a calendar counts seconds, so three quarters of one cannot move it and must not be
        // rounded into a second the world did not have.
        DaggerfallWorldTime carrying = new(DaggerfallCalendar.Start, 0.75d, 12d);
        DaggerfallCalendarAdvance interval = carrying.AdvanceInterval(1, []);
        Assert.Equal(1, interval.AppliedSeconds);
        Assert.Equal(new DaggerfallCalendar(405, 5, 0, 0, 0, 1), carrying.Calendar);
        Assert.Equal(0.75d, carrying.RemainderSeconds, 10);

        carrying.Advance(0.05d);
        Assert.Equal(new DaggerfallCalendar(405, 5, 0, 0, 0, 2), carrying.Calendar);
        Assert.Equal(0.35d, carrying.RemainderSeconds, 10);

        // A negative interval is refused: nothing in the corpus shortens the world's clock.
        Assert.Throws<ArgumentOutOfRangeException>(() => time.AdvanceInterval(-1, []));
    }

    [Fact]
    public void Emits_only_remainders_a_save_accepts()
    {
        // The floor's tolerance can leave the remainder a nanosecond below zero, and a save refuses a
        // negative remainder. That would make a just-saved game unloadable, so the clock clamps at the
        // source and this asserts the property directly rather than the one value that showed it.
        DaggerfallWorldTime time = new(DaggerfallCalendar.Start, 0d, 12d);
        time.Advance(0.083333333325d);
        Assert.Equal(1, time.Calendar.Second);
        Assert.InRange(time.RemainderSeconds, 0d, 0.9999999999d);

        // A duration chosen to land as close to a whole second as the tolerance allows, then one that
        // does not: neither may leave a negative remainder.
        DaggerfallWorldTime near = new(DaggerfallCalendar.Start, 0d, 1d);
        near.Advance(0.9999999999999d);
        Assert.InRange(near.RemainderSeconds, 0d, 1d);
        near.Advance(1.0000000001d);
        Assert.InRange(near.RemainderSeconds, 0d, 1d);

        // A save carrying what the clock reported is a save the payload accepts, which is the property
        // the two sides have to agree on.
        DaggerfallCalendarSave saved = new(
            time.Calendar.Year, time.Calendar.Month, time.Calendar.Day,
            time.Calendar.Hour, time.Calendar.Minute, time.Calendar.Second,
            time.RemainderSeconds);
        Assert.InRange(saved.RemainderSeconds, 0d, 1d);
    }

    [Fact]
    public void Refuses_a_duration_or_scale_that_is_not_one()
    {
        DaggerfallWorldTime time = new(DaggerfallCalendar.Start, 0d, 12d);
        Assert.Throws<ArgumentOutOfRangeException>(() => time.Advance(-1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => time.Advance(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallWorldTime(DaggerfallCalendar.Start, 0d, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallWorldTime(DaggerfallCalendar.Start, 0d, double.PositiveInfinity));

        // A duration of nothing leaves the clock exactly where it was, which is what a rejected or
        // paused update has to mean.
        time.Advance(0d);
        Assert.Equal(DaggerfallCalendar.Start, time.Calendar);
        Assert.Equal(0d, time.RemainderSeconds);
    }
}
