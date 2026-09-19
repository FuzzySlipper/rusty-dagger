using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class PassiveTrackRecoveryTests
{
    private static readonly StatId StaminaMaximum = StatId.Parse("stamina-maximum");
    private static readonly TrackId Stamina = TrackId.Parse("stamina");

    [Fact]
    public void Quiet_period_fractional_carry_and_full_track_discard_follow_admitted_time()
    {
        StatsComponent stats = StaminaStats();
        PassiveTrackRecovery recovery = new(5d, 2d);
        Track stamina = stats.GetTrack(Stamina);

        recovery.RestartQuietPeriod();
        recovery.Advance(stamina, 1.5d);
        Assert.Equal(80d, stamina.Current);
        recovery.Advance(stamina, .5d);
        Assert.Equal(80d, stamina.Current);
        recovery.Advance(stamina, .2d);
        Assert.Equal(81d, stamina.Current);
        recovery.Advance(stamina, .8d);
        Assert.Equal(85d, stamina.Current);

        stamina.SetCurrent(90d);
        recovery.Advance(stamina, 20d);
        stamina.Spend(5d);
        recovery.Advance(stamina, .1d);
        Assert.Equal(95d, stamina.Current);

        recovery.RestartQuietPeriod();
        recovery.Advance(stamina, 2d);
        recovery.Advance(stamina, .1d);
        Assert.Equal(95d, stamina.Current);
    }

    [Fact]
    public void Advance_requires_a_positive_finite_admitted_delta()
    {
        PassiveTrackRecovery recovery = new(5d, 2d);
        Track stamina = StaminaStats().GetTrack(Stamina);

        Assert.Throws<ArgumentOutOfRangeException>(() => recovery.Advance(stamina, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => recovery.Advance(stamina, double.NaN));
    }

    private static StatsComponent StaminaStats()
    {
        Stat maximum = new(100d, 0d, 100d);
        StatsComponent stats = new();
        stats.AddStat(StaminaMaximum, maximum);
        stats.AddTrack(Stamina, new Track(maximum, 80d, 0d, quantum: 1d));
        return stats;
    }
}
