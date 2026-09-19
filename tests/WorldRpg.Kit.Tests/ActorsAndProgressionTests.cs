using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class ActorsAndProgressionTests
{
    [Fact]
    public void Stats_component_retains_shared_stat_and_track_objects()
    {
        StatId healthMaximumId = StatId.Parse("health-maximum");
        TrackId healthId = TrackId.Parse("health");
        Stat healthMaximum = new(40, 0, 100);
        Track health = new(healthMaximum, 25, 0, TrackMaximumChangePolicy.PreserveMissingAmount);
        StatsComponent stats = new();
        stats.AddStat(healthMaximumId, healthMaximum);
        stats.AddTrack(healthId, health);

        Assert.Same(healthMaximum, stats.GetStat(healthMaximumId));
        Assert.Same(health, stats.GetTrack(healthId));
        Assert.Equal(10d, health.Spend(10));
        Assert.Equal(20d, health.Restore(20));
        health.SetCurrent(99, clamp: true);
        Assert.Equal(40d, health.Current);

        Assert.Throws<ArgumentException>(() => stats.AddStat(healthMaximumId, new Stat(40)));
        Assert.Throws<ArgumentException>(() => stats.AddTrack(healthId, new Track(40)));
    }

    [Fact]
    public void Shared_stat_updates_reconcile_the_live_track_synchronously()
    {
        StatId healthMaximumId = StatId.Parse("health-maximum");
        TrackId healthId = TrackId.Parse("health");
        Stat healthMaximum = new(40, 0, 100);
        Track health = new(healthMaximum, 25, 0, TrackMaximumChangePolicy.PreserveMissingAmount);
        StatsComponent stats = new();
        stats.AddStat(healthMaximumId, healthMaximum);
        stats.AddTrack(healthId, health);

        healthMaximum.SetSources(healthMaximumId,
        [
            new StatSource(
                new IntrinsicSourceIdentity(new EntityId(1), SourceInstanceId.Parse("level-two")),
                SourceDefinitionId.Parse("daggerfall.level-up"),
                priority: 0,
                [new StatContributionDefinition(
                    healthMaximumId,
                    StackingGroupId.Parse("level-up"),
                    MechanicsStackingPolicy.Sum,
                    new StatContribution.Add(10))]),
        ]);

        Assert.Equal(50d, stats.GetStat(healthMaximumId).Value);
        Assert.Equal(35d, stats.GetTrack(healthId).Current);
    }

    [Fact]
    public void Attached_stats_capture_rebuilds_shared_aliases_and_restores_sources_before_live_tracks()
    {
        StatId healthMaximumId = StatId.Parse("health-maximum");
        StatId healthAliasId = StatId.Parse("health-cap");
        TrackId healthId = TrackId.Parse("health");
        TrackId healthAliasTrackId = TrackId.Parse("health-current");
        Stat healthMaximum = new(40, 0, 100);
        Track health = new(healthMaximum, 25, 0, TrackMaximumChangePolicy.PreserveMissingAmount);
        StatsComponent stats = new();
        stats.AddStat(healthMaximumId, healthMaximum);
        stats.AddStat(healthAliasId, healthMaximum);
        stats.AddTrack(healthId, health);
        stats.AddTrack(healthAliasTrackId, health);
        using ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
        ActorState original = actors.CreateActor(101, new EntityTypeId("rat"), stats, Pose(1), "health");
        StatModifierHandle modifier = healthMaximum.AddModifier(10);
        StatSource levelUp = new(
            new IntrinsicSourceIdentity(original.Actor.Entity, SourceInstanceId.Parse("level-two")),
            SourceDefinitionId.Parse("daggerfall.level-up"),
            priority: 0,
            [new StatContributionDefinition(
                healthMaximumId,
                StackingGroupId.Parse("level-up"),
                MechanicsStackingPolicy.Sum,
                new StatContribution.Add(10))]);
        healthMaximum.SetSources(healthMaximumId, [levelUp]);

        StatsComponentSnapshot snapshot = StatsComponentCapture.Capture(original.Stats);
        StatModifierHandle? rebuiltModifier = null;
        ActorState restored = actors.CreateActor(102, new EntityTypeId("rat"), new StatsComponent(), Pose(2), "health");
        StatsComponent rebuilt = StatsComponentCapture.Rebuild(snapshot,
            (_, stat, handles) =>
            {
                stat.SetSources(healthMaximumId,
                [
                    new StatSource(
                        new IntrinsicSourceIdentity(restored.Actor.Entity, SourceInstanceId.Parse("level-two")),
                        SourceDefinitionId.Parse("daggerfall.level-up"),
                        priority: 0,
                        [new StatContributionDefinition(
                            healthMaximumId,
                            StackingGroupId.Parse("level-up"),
                            MechanicsStackingPolicy.Sum,
                            new StatContribution.Add(10))]),
                ]);
                rebuiltModifier = Assert.Single(handles);
            });
        restored.Actor.Replace(rebuilt);

        Assert.Same(rebuilt, restored.Stats);
        Assert.Same(restored.Stats.GetStat(healthMaximumId), restored.Stats.GetStat(healthAliasId));
        Assert.Same(restored.Stats.GetTrack(healthId), restored.Stats.GetTrack(healthAliasTrackId));
        Assert.Same(restored.Stats.GetStat(healthMaximumId), restored.Stats.GetTrack(healthId).Maximum);
        Assert.Equal(60d, restored.Stats.GetStat(healthMaximumId).Value);
        Assert.Equal(45d, restored.Stats.GetTrack(healthId).Current);
        Assert.Equal(restored.Actor.Entity, ((IntrinsicSourceIdentity)Assert.Single(
            restored.Stats.GetStat(healthMaximumId).Explain().Decisions).Source).Entity);
        Assert.True(restored.Stats.GetStat(healthMaximumId).RemoveModifier(rebuiltModifier!));
        Assert.Equal(50d, restored.Stats.GetStat(healthMaximumId).Value);
        Assert.Equal(35d, restored.Stats.GetTrack(healthId).Current);
        Assert.True(healthMaximum.RemoveModifier(modifier));
    }

    [Fact]
    public void Actors_state_maps_durable_identities_to_distinct_same_type_runtime_entities()
    {
        using ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
        ActorState first = actors.CreateActor(101, new EntityTypeId("rat"), Stats(), Pose(1), "health");
        ActorState second = actors.CreateActor(102, new EntityTypeId("rat"), Stats(), Pose(2), "health");

        Assert.NotEqual(first.Actor.Entity, second.Actor.Entity);
        Assert.Equal(101, first.DurableId);
        Assert.Equal(102, second.DurableId);
        Assert.Equal("rat", first.Actor.TypeId.Value);
        Assert.Equal([101L, 102L], actors.All.Select(actor => actor.DurableId).Order());
    }

    [Fact]
    public void Actor_wrapping_is_live_and_does_not_construct_components()
    {
        using ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
        StatsComponent stats = Stats();
        ActorState created = actors.CreateActor(101, new EntityTypeId("rat"), stats, Pose(1), "health");
        ulong revisionBeforeWrap = actors.Store.Revision;

        Actor wrapped = new(actors.Store, created.Actor.Entity);
        ActorState view = new(wrapped);

        Assert.Equal(revisionBeforeWrap, actors.Store.Revision);
        Assert.Same(stats, view.Stats);
        Assert.Equal(created.Pose, view.Pose);
        Assert.Same(created.Actor.Get<ActorBody>(), wrapped.Get<ActorBody>());
    }

    [Fact]
    public void Actors_state_lookup_destroy_and_recreate_replace_only_the_runtime_entity()
    {
        using ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
        ActorState original = actors.CreateActor(101, new EntityTypeId("rat"), Stats(), Pose(1), "health");

        Assert.True(actors.TryGet(101, out ActorState found));
        Assert.Equal(original.Actor.Entity, found.Actor.Entity);
        Assert.True(actors.Entities.Destroy(ActorsState.Identity(101)));
        Assert.False(actors.TryGet(101, out _));
        Assert.Throws<InvalidOperationException>(() => original.Actor.Get<ActorBody>());

        ActorState replacement = actors.CreateActor(101, new EntityTypeId("rat"), Stats(), Pose(3), "health");
        Assert.NotEqual(original.Actor.Entity, replacement.Actor.Entity);
        Assert.Equal(101, replacement.DurableId);
        Assert.Equal(replacement.Actor.Entity, actors.Get(101).Actor.Entity);
    }

    private static StatsComponent Stats()
    {
        Stat maximum = new(100);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(maximum, 100));
        return stats;
    }

    private static ActorPose Pose(float x) => new(new WorldRpg.Kit.Controls.WorldPoint(x, 0, 0), 0);
}
