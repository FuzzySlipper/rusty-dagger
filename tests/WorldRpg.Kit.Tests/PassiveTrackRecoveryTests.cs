using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class PassiveTrackRecoveryTests
{
    private static readonly StatId FatigueMaximum = StatId.Parse("fatigue-maximum");
    private static readonly TrackId Fatigue = TrackId.Parse("fatigue");

    [Fact]
    public void Quiet_period_fractional_carry_and_full_track_discard_follow_admitted_time()
    {
        StatsComponent stats = FatigueStats();
        PassiveTrackRecovery recovery = new(5d, 2d);
        Track fatigue = stats.GetTrack(Fatigue);

        recovery.RestartQuietPeriod();
        recovery.Advance(fatigue, 1.5d);
        Assert.Equal(80d, fatigue.Current);
        recovery.Advance(fatigue, .5d);
        Assert.Equal(80d, fatigue.Current);
        recovery.Advance(fatigue, .2d);
        Assert.Equal(81d, fatigue.Current);
        recovery.Advance(fatigue, .8d);
        Assert.Equal(85d, fatigue.Current);

        fatigue.SetCurrent(90d);
        recovery.Advance(fatigue, 20d);
        fatigue.Spend(5d);
        recovery.Advance(fatigue, .1d);
        Assert.Equal(95d, fatigue.Current);

        recovery.RestartQuietPeriod();
        recovery.Advance(fatigue, 2d);
        recovery.Advance(fatigue, .1d);
        Assert.Equal(95d, fatigue.Current);
    }

    [Fact]
    public void Advance_requires_a_positive_finite_admitted_delta()
    {
        PassiveTrackRecovery recovery = new(5d, 2d);
        Track fatigue = FatigueStats().GetTrack(Fatigue);

        Assert.Throws<ArgumentOutOfRangeException>(() => recovery.Advance(fatigue, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => recovery.Advance(fatigue, double.NaN));
    }

    private static StatsComponent FatigueStats()
    {
        Stat maximum = new(100d, 0d, 100d);
        StatsComponent stats = new();
        stats.AddStat(FatigueMaximum, maximum);
        stats.AddTrack(Fatigue, new Track(maximum, 80d, 0d, quantum: 1d));
        return stats;
    }
}
