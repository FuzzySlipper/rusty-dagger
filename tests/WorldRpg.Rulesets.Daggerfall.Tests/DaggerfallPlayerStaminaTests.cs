using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Combat Stamina: its pool, its refill, and how a rising Fatigue squeezes both.</summary>
public sealed class DaggerfallPlayerStaminaTests
{
    private static readonly DaggerfallStaminaTuning Tuning = DaggerfallStaminaTuning.Default;

    [Theory]
    [InlineData(0d, 110)] // 20 + .75 * 120
    [InlineData(.5d, 110)] // Shrinking starts past half.
    [InlineData(.75d, 77)] // Halfway from half to collapse: 110 * .7
    [InlineData(1d, 44)] // 110 * .4
    public void The_pool_follows_attributes_and_shrinks_past_half_fatigue(double fatigue, int maximum) =>
        Assert.Equal(maximum, Tuning.Maximum(60, 60, fatigue));

    [Theory]
    [InlineData(.75d, 1d)]
    [InlineData(.875d, .75d)]
    [InlineData(1d, .5d)]
    public void The_refill_slows_past_three_quarters_fatigue(double fatigue, double scale) =>
        Assert.Equal(scale, Tuning.RefillScale(fatigue), 6);

    [Fact]
    public void A_fresh_character_starts_full()
    {
        using DaggerfallSession session = FreshSession();
        Track stamina = Stamina(session);

        Assert.True(stamina.Current > 0d);
        Assert.Equal(stamina.MaximumValue, stamina.Current);
    }

    [Fact]
    public void Spending_restarts_the_quiet_period_and_the_pool_then_refills_while_fatigue_stays()
    {
        Rig rig = new();
        rig.Stamina.SetCurrent(50d);
        double fatigue = rig.Fatigue.Current;

        rig.Step(.5d); // The drop is seen here: half of the second's quiet passes.
        Assert.Equal(50d, rig.Stamina.Current);
        rig.Step(.5d);
        Assert.Equal(50d, rig.Stamina.Current);
        rig.Step(1d);
        Assert.Equal(58d, rig.Stamina.Current);

        rig.Stamina.SetCurrent(40d);
        rig.Step(.9d);
        Assert.Equal(40d, rig.Stamina.Current);
        Assert.Equal(fatigue, rig.Fatigue.Current);
    }

    [Fact]
    public void A_rising_fatigue_takes_the_pool_down_with_what_was_already_spent_and_rest_gives_it_back()
    {
        Rig rig = new();
        double full = rig.Stamina.MaximumValue;
        rig.Stamina.SetCurrent(full - 30d);
        rig.Step(.1d);

        rig.Fatigue.SetCurrent(0d);
        rig.Step(.1d);
        Assert.InRange(rig.Stamina.MaximumValue, full * Tuning.ShrunkScale - 1d, full * Tuning.ShrunkScale + 1d);
        Assert.Equal(rig.Stamina.MaximumValue - 30d, rig.Stamina.Current);

        rig.Fatigue.SetCurrent(rig.Fatigue.MaximumValue);
        rig.Owner.Fill(rig.Stats);
        Assert.Equal((full, full), (rig.Stamina.MaximumValue, rig.Stamina.Current));
    }

    [Fact]
    public void Time_outside_updates_refills_stamina_and_no_update_refills_fatigue()
    {
        using DaggerfallSession session = FreshSession();
        Track fatigue = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value));
        fatigue.SetCurrent(fatigue.MaximumValue - 640d);
        Stamina(session).SetCurrent(0d);

        _ = session.AdvanceElapsedTime(60, idleFatigue: false);

        Assert.Equal(Stamina(session).MaximumValue, Stamina(session).Current);
        Assert.Equal(fatigue.MaximumValue - 640d, fatigue.Current);
    }

    private static Track Stamina(DaggerfallSession session) =>
        session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));

    private sealed class Rig
    {
        internal StatsComponent Stats { get; } = new DaggerfallMechanicsState().CreateStats(
            TestPayload.Definitions.RequireActor(new DaggerfallActorId("player")), new DaggerfallVitalValues(100, 6_400, 100));
        internal DaggerfallPlayerStamina Owner { get; } = new(Tuning);
        internal Track Stamina => Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));
        internal Track Fatigue => Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value));

        internal Rig()
        {
            Owner.Refresh(Stats);
            Owner.Step(Stats, .1d);
        }

        internal void Step(double seconds) => Owner.Step(Stats, seconds);
    }
}
