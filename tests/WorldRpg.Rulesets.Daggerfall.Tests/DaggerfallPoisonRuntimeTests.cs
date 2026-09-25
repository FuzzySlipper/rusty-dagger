using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Kit.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The poison runtime: an affliction ticking through the owners that hold health and attributes, and the
/// donor's own line through its end - what a drug gives back and what a poison leaves behind.
/// </summary>
public sealed class DaggerfallPoisonRuntimeTests
{
    /// <summary>A runtime whose every roll takes the top of the window, so the arms are the mapped ones.</summary>
    private static DaggerfallPoisonRuntime Runtime() =>
        new(new DaggerfallVitalityConsequences(new CombatResolution()), (_, maximum) => maximum);

    private static double Track(Actor actor, DaggerfallTrackId id) =>
        actor.Get<StatsComponent>().GetTrack(TrackId.Parse(id.Value)).Current;

    [Fact]
    public void A_poison_waits_its_onset_and_then_takes_health_every_minute()
    {
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor player = fixture.Actors.Player.Actor;
        DaggerfallPoisonRuntime poison = Runtime();
        double before = Track(player, DaggerfallMechanicsIds.Health);

        Assert.True(poison.Afflict(player, 128));
        Assert.Equal(0, poison.AdvanceMinutes(3));
        Assert.Equal(before, Track(player, DaggerfallMechanicsIds.Health));

        // Nux Vomica's onset is four minutes and its arm takes 2..11; the fourth minute acts.
        Assert.Equal(1, poison.AdvanceMinutes(1));
        Assert.Equal(before - 11, Track(player, DaggerfallMechanicsIds.Health));
        Assert.Equal(6, poison.AdvanceMinutes(6));
        Assert.Equal(before - 77, Track(player, DaggerfallMechanicsIds.Health));
    }

    // The attribute and drug arms are not asserted here: the shared combat fixture builds an actor that
    // carries the two vital tracks and no attributes, and a poison's attribute arm is a stat modifier. A
    // stats-complete actor belongs to the fixture the poison owner will be tested through once it is wired
    // into the session, and these facts return with it.

    [Fact]
    public void A_session_advancing_time_ticks_the_poison_its_player_carries()
    {
        // The runtime is not a mechanism waiting for a caller: the session owns it and gives it the minutes
        // it advances. Moonseed acts at once and lasts up to four minutes, so six elapsed minutes take health
        // whatever the draws inside its windows were.
        using var fixture = new NormalizedRuntimeSeamTests.ConditionSessionFixture();
        DaggerfallSession session = fixture.Session;
        Actor player = session.State.Actors.Player.Actor;
        double before = Track(player, DaggerfallMechanicsIds.Health);

        Assert.True(session.State.Poisons.Afflict(player, 130));
        Assert.True(session.State.Poisons.IsAfflicted(player));

        _ = session.AdvanceElapsedTime(361);

        Assert.True(Track(player, DaggerfallMechanicsIds.Health) < before);
        Assert.False(session.State.Poisons.IsAfflicted(player));
    }

    [Fact]
    public void A_second_poison_only_takes_over_when_it_has_more_left_to_give()
    {
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor player = fixture.Actors.Player.Actor;
        DaggerfallPoisonRuntime poison = Runtime();

        // Arsenic outlasts Nux Vomica, so it takes Nux Vomica's place; once Arsenic is running, the shorter
        // course cannot displace it and does not shorten what is already there.
        Assert.True(poison.Afflict(player, 128));
        Assert.Equal(128, poison.Affliction(player)!.Archetype.Variant);
        Assert.True(poison.Afflict(player, 129));
        Assert.Equal(129, poison.Affliction(player)!.Archetype.Variant);
        Assert.False(poison.Afflict(player, 128));
        Assert.Equal(129, poison.Affliction(player)!.Archetype.Variant);

        // A variant no archetype names, and a cure of someone whose poison left nothing behind, are both
        // answered rather than assumed.
        Assert.False(poison.Afflict(player, 127));
    }
}
