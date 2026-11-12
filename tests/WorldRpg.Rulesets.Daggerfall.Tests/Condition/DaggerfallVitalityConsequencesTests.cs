using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallVitalityConsequencesTests
{
    [Fact]
    public void Landing_uses_canonical_health_loss_and_only_the_first_landing_can_defeat()
    {
        using ActorsState actors = new();
        StatsComponent stats = Stats(health: 3.75d, fatigue: 100d);
        PlayerActorState player = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerActorFactory.PlayerCapabilities);
        DaggerfallVitalityConsequences consequences = new(new CombatResolution());

        DamageResult? lethal = consequences.ResolveLanding(player.Actor, new DaggerfallLanding(7f), preventsFallDamage: false);
        DamageResult? repeated = consequences.ResolveLanding(player.Actor, new DaggerfallLanding(7f), preventsFallDamage: false);

        Assert.Equal((10, 3.75d, true), (lethal!.Value.CalculatedDamage, lethal.Value.ActualHealthLost, lethal.Value.Defeated));
        Assert.Equal((0d, false), (repeated!.Value.ActualHealthLost, repeated.Value.Defeated));
        Assert.Equal(0d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);
    }

    [Fact]
    public void A_thrown_body_takes_the_players_fall_rule_and_the_fall_is_the_players_doing()
    {
        using ActorsState actors = new();
        PlayerActorState player = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), Stats(health: 20d, fatigue: 100d), "health", DaggerActorFactory.PlayerCapabilities);
        ActorState thrown = actors.CreateActor(2, new EntityTypeId("orc"), Stats(health: 20d, fatigue: 0d), new ActorPose(new WorldPoint(0f, 0f, 0f), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
        DaggerfallVitalityConsequences consequences = new(new CombatResolution());

        Assert.Null(consequences.ResolveThrownLanding(player.Actor, thrown.Actor, 5f));
        DamageResult? fall = consequences.ResolveThrownLanding(player.Actor, thrown.Actor, 7f);

        Assert.Equal(10, fall!.Value.CalculatedDamage);
        Assert.Equal(player.Actor.Entity, fall.Value.Source.Entity);
        Assert.Equal(10d, thrown.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);
    }

    [Fact]
    public void Grace_distance_has_no_health_application()
    {
        using ActorsState actors = new();
        StatsComponent stats = Stats(health: 20d, fatigue: 100d);
        PlayerActorState player = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerActorFactory.PlayerCapabilities);

        DamageResult? result = new DaggerfallVitalityConsequences(new CombatResolution())
            .ResolveLanding(player.Actor, new DaggerfallLanding(5f), preventsFallDamage: false);

        Assert.Null(result);
        Assert.Equal(20d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);
    }

    [Fact]
    public void Compiled_movement_protection_rejects_an_otherwise_damaging_landing()
    {
        using ActorsState actors = new();
        StatsComponent stats = Stats(health: 20d, fatigue: 100d);
        PlayerActorState player = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerActorFactory.PlayerCapabilities);

        DamageResult? result = new DaggerfallVitalityConsequences(new CombatResolution())
            .ResolveLanding(player.Actor, new DaggerfallLanding(7f), preventsFallDamage: true);

        Assert.Null(result);
        Assert.Equal(20d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);
    }

    private static StatsComponent Stats(double health, double fatigue)
    {
        Stat healthMaximum = new(100d);
        Stat fatigueMaximum = new(100d);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), healthMaximum);
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.FatigueMaximum.Value), fatigueMaximum);
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value), new Track(healthMaximum, health, 0d));
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value), new Track(fatigueMaximum, fatigue, 0d));
        return stats;
    }
}
