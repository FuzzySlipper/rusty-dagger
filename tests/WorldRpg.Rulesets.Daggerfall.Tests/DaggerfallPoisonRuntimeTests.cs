using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Kit.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The poison owner: afflictions carried as active effects, ticking through the owners that hold health and
/// attributes, and the donor's own line through a poison's end - what a drug gives back and what a poison
/// leaves behind.
/// </summary>
public sealed class DaggerfallPoisonRuntimeTests
{
    /// <summary>
    /// One poison owner over a lifecycle that compiles only the twelve poisons, over the fixture's actors.
    /// The fixture's random service answers the minimum of every keyed window, so an arm's magnitude is its
    /// archetype minimum; the onset and duration rolls are the caller's, and the top of the window keeps
    /// each course long enough for its whole window to be spent.
    /// </summary>
    private static (DaggerfallPoisonRuntime Poison, DaggerfallEffectLifecycle Effects) Runtime(DaggerCombatFixture fixture)
    {
        DaggerfallEffectLifecycle effects = new(fixture.Actors, new DaggerfallEffectCatalog(
            DaggerfallPoisonEffects.Definitions(fixture.Random, new DaggerfallVitalityConsequences(new CombatResolution()))));
        return (new DaggerfallPoisonRuntime(effects, (_, maximum) => maximum), effects);
    }

    /// <summary>Passes minutes the way the session does: one ordinary round per minute.</summary>
    private static void Tick(DaggerfallEffectLifecycle effects, int minutes)
    {
        for (int minute = 0; minute < minutes; minute++) effects.AdvanceOrdinaryRound();
    }

    private static double Track(Actor actor, DaggerfallTrackId id) =>
        actor.Get<StatsComponent>().GetTrack(TrackId.Parse(id.Value)).Current;

    private static double Stat(Actor actor, DaggerfallStatId id) =>
        actor.Get<StatsComponent>().GetStat(StatId.Parse(id.Value)).Value;

    /// <summary>
    /// A second actor that carries the attributes, which a poison's attribute arm needs: the shared combat
    /// fixture's player has the two vital tracks and no attributes, because combat never reads them there.
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
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        double before = Track(player, DaggerfallMechanicsIds.Health);

        Assert.True(poison.Afflict(player, 128));
        // Nux Vomica's onset is four minutes and its arm takes 2..11; the fourth minute acts, and the
        // assignment round is not one of the four.
        Tick(effects, 3);
        Assert.Equal(before, Track(player, DaggerfallMechanicsIds.Health));

        Tick(effects, 1);
        Assert.Equal(before - 2, Track(player, DaggerfallMechanicsIds.Health));

        // Its window is up to ten minutes, so a whole course takes twenty points at the arm's minimum.
        Tick(effects, 9);
        Assert.Equal(before - 20, Track(player, DaggerfallMechanicsIds.Health));
        Assert.False(poison.IsAfflicted(player));
    }

    [Fact]
    public void A_session_advancing_time_ticks_the_poison_its_player_carries()
    {
        // The owner is not a mechanism waiting for a caller: the session's effect lifecycle advances it.
        // Moonseed acts at once and lasts up to four minutes, so six elapsed minutes take health whatever
        // the draws inside its windows were.
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
        // completed poison is still active, holding the sources a cure has to take off.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        double strength = Stat(victim, DaggerfallMechanicsIds.Strength);

        Assert.True(poison.Afflict(victim, 131));
        // Drothweed waits up to ten minutes and then drains three attributes a minute for up to thirty.
        Tick(effects, 40);

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
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        double luck = Stat(victim, DaggerfallMechanicsIds.Luck);
        double stamina = Track(victim, DaggerfallMechanicsIds.Stamina);

        Assert.True(poison.Afflict(victim, 136));
        // Indulcet waits up to twelve minutes and acts for up to six, two arms to the minute.
        Tick(effects, 20);

        Assert.Equal(luck, Stat(victim, DaggerfallMechanicsIds.Luck));
        Assert.True(Track(victim, DaggerfallMechanicsIds.Stamina) < stamina);
        Assert.False(poison.IsAfflicted(victim));
    }

    [Fact]
    public void A_second_poison_does_not_heal_the_damage_the_first_one_left()
    {
        // The damage a completed poison did persists, so taking a second poison must not quietly give it
        // back. The older poison stops running and keeps its own arms, which a cure then takes off with
        // everything else.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        double strength = Stat(victim, DaggerfallMechanicsIds.Strength);

        Assert.True(poison.Afflict(victim, 131));
        Tick(effects, 40);
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
    public void A_session_with_an_active_poison_saves_and_comes_back_with_it()
    {
        // The case the parallel owner could not satisfy: a poison's arms are stat sources, and the stats
        // save refuses an effect source with no active effect to clean it up. A poison is an active effect
        // now, so the same save that used to fail succeeds and carries the course with it.
        using var fixture = new NormalizedRuntimeSeamTests.ConditionSessionFixture();
        DaggerfallSession session = fixture.Session;
        Actor player = session.State.Actors.Player.Actor;
        Assert.True(session.State.Poisons.Afflict(player, 130));
        Assert.True(session.State.Poisons.IsAfflicted(player));

        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(saved);

        Actor restoredPlayer = restored.State.Actors.Player.Actor;
        Assert.True(restored.State.Poisons.IsAfflicted(restoredPlayer));
        Assert.Equal(130, restored.State.Poisons.Affliction(restoredPlayer)!.Archetype.Variant);

        // And it is still running rather than merely present: Moonseed acts at once and lasts up to four
        // minutes, so six elapsed minutes take health from where the reload left it.
        double before = Track(restoredPlayer, DaggerfallMechanicsIds.Health);
        _ = restored.AdvanceElapsedTime(361);
        Assert.True(Track(restoredPlayer, DaggerfallMechanicsIds.Health) < before);
        Assert.False(restored.State.Poisons.IsAfflicted(restoredPlayer));
    }

    [Fact]
    public void A_second_poison_only_takes_over_when_it_has_more_left_to_give()
    {
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor player = fixture.Actors.Player.Actor;
        (DaggerfallPoisonRuntime poison, _) = Runtime(fixture);

        // Arsenic outlasts Nux Vomica, so it takes Nux Vomica's place; once Arsenic is running, the shorter
        // course cannot displace it and does not shorten what is already there.
        Assert.True(poison.Afflict(player, 128));
        Assert.Equal(128, poison.Affliction(player)!.Archetype.Variant);
        Assert.True(poison.Afflict(player, 129));
        Assert.Equal(129, poison.Affliction(player)!.Archetype.Variant);
        Assert.False(poison.Afflict(player, 128));
        Assert.Equal(129, poison.Affliction(player)!.Archetype.Variant);

        // A variant no archetype names is answered rather than assumed.
        Assert.False(poison.Afflict(player, 127));
    }
}
