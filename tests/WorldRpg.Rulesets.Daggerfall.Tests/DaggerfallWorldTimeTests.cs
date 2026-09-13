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
