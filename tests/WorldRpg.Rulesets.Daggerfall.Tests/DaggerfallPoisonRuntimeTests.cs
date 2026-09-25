using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
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

    private static double Stat(Actor actor, DaggerfallStatId id) =>
        actor.Get<StatsComponent>().GetStat(StatId.Parse(id.Value)).Value;

    /// <summary>
    /// A second actor that carries the attributes, which a poison's attribute arm needs: the shared combat
    /// fixture's actor has the two vital tracks and no attributes, because combat never reads them there.
    /// </summary>
    private static Actor AttributedActor(DaggerCombatFixture fixture)
    {
        DaggerfallActorDefinition player = fixture.Definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallMechanicsState mechanics = new();
        StatsComponent stats = mechanics.CreateStats(
            player,
            DaggerfallPlayerVitals.Initial(player.Stats, fixture.Definitions.Catalogs.RequireCareer("class00")));
        return fixture.Actors.CreateActor(3, new EntityTypeId("nymph"), stats, new ActorPose(new WorldPoint(0f, 0f, 0f), 0f), "health").Actor;
    }

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
    public void Attribute_damage_a_poison_did_stays_after_it_runs_its_course()
    {
        // The donor says it plainly: attribute damage persists until the victim heals or is cured. So a
        // completed poison is still carried, holding what it has to take back.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        DaggerfallPoisonRuntime poison = Runtime();
        double strength = Stat(victim, DaggerfallMechanicsIds.Strength);

        Assert.True(poison.Afflict(victim, 131));
        // The answer counts the arms that acted, not the ticks: Drothweed waits up to ten minutes and then
        // drains three attributes a minute for up to thirty, so fifty minutes spend the whole window.
        Assert.Equal(90, poison.AdvanceMinutes(50));

        // Every tick of the window drains strength, so the stat is below where it started; what matters
        // here is that it stays there when the poison completes.
        Assert.True(Stat(victim, DaggerfallMechanicsIds.Strength) < strength);
        Assert.Equal(DaggerfallPoisonPhase.Complete, poison.Affliction(victim)!.Phase);
        Assert.True(poison.HasPersistingDamage(victim));

        Assert.True(poison.Cure(victim));
        Assert.Equal(strength, Stat(victim, DaggerfallMechanicsIds.Strength));
        Assert.Equal(0, poison.Count);
    }

    [Fact]
    public void A_drug_gives_back_what_it_gave_when_its_course_ends()
    {
        // The other half of the donor's line: a drug's helping arms are removed when the poison completes,
        // while the harm it did stands.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        DaggerfallPoisonRuntime poison = Runtime();
        double luck = Stat(victim, DaggerfallMechanicsIds.Luck);
        double stamina = Track(victim, DaggerfallMechanicsIds.Stamina);

        Assert.True(poison.Afflict(victim, 136));
        // Indulcet waits up to twelve minutes and acts for up to six, two arms to the minute.
        Assert.Equal(12, poison.AdvanceMinutes(20));

        Assert.Equal(luck, Stat(victim, DaggerfallMechanicsIds.Luck));
        Assert.True(Track(victim, DaggerfallMechanicsIds.Stamina) < stamina);
        Assert.False(poison.IsAfflicted(victim));
    }

    [Fact]
    public void A_second_poison_does_not_heal_the_damage_the_first_one_left()
    {
        // The reason the runtime holds a list per actor rather than one poison: the damage a completed poison
        // did persists, so taking a second poison must not quietly give it back. The older poison stops
        // running and keeps its own arms, which a cure then takes off with everything else.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        DaggerfallPoisonRuntime poison = Runtime();
        double strength = Stat(victim, DaggerfallMechanicsIds.Strength);

        Assert.True(poison.Afflict(victim, 131));
        Assert.Equal(90, poison.AdvanceMinutes(50));
        double drained = Stat(victim, DaggerfallMechanicsIds.Strength);
        Assert.True(drained < strength);
        Assert.Equal(DaggerfallPoisonPhase.Complete, poison.Affliction(victim)!.Phase);

        // Arsenic outlasts what is left of Drothweed, so it takes over; the drain stays.
        Assert.True(poison.Afflict(victim, 129));
        Assert.Equal(129, poison.Affliction(victim)!.Archetype.Variant);
        Assert.Equal(drained, Stat(victim, DaggerfallMechanicsIds.Strength));
        Assert.Equal(2, poison.Count);

        Assert.True(poison.Cure(victim));
        Assert.Equal(strength, Stat(victim, DaggerfallMechanicsIds.Strength));
        Assert.False(poison.IsAfflicted(victim));
    }

    [Fact]
    public void A_poison_a_save_carries_comes_back_where_it_stood_and_for_what_it_left()
    {
        // What a save carries is the affliction and the instance its arms own; the arms themselves come back
        // from the stats save, which is what the restore reads to find the damage a completed poison left.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        DaggerfallPoisonRuntime source = Runtime();
        double strength = Stat(victim, DaggerfallMechanicsIds.Strength);

        // One poison completed with a drain behind it, and one still running with most of its course left.
        Assert.True(source.Afflict(victim, 131));
        Assert.Equal(90, source.AdvanceMinutes(50));
        Assert.True(source.Afflict(victim, 129));
        DaggerfallPoisonsSave saved = source.Capture();
        double drained = Stat(victim, DaggerfallMechanicsIds.Strength);

        Assert.Equal(2, saved.Records.Length);
        Assert.Equal(2, source.Count);

        DaggerfallPoisonRuntime restored = Runtime();
        restored.Restore(saved, entity => entity == checked((long)victim.Entity.Value) ? victim : null);

        Assert.Equal(2, restored.Count);
        Assert.Equal(129, restored.Affliction(victim)!.Archetype.Variant);
        Assert.True(restored.Affliction(victim)!.MinutesRemaining > 1);
        Assert.True(restored.HasPersistingDamage(victim));

        // And the drain it came back with is still there to be cured, not silently re-applied.
        Assert.Equal(drained, Stat(victim, DaggerfallMechanicsIds.Strength));
        Assert.True(restored.Cure(victim));
        Assert.Equal(strength, Stat(victim, DaggerfallMechanicsIds.Strength));
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
