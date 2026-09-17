using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class ActorsAndProgressionTests
{
    [Fact]
    public void Actor_routes_product_ids_to_the_same_shared_stat_and_track_objects()
    {
        StatId healthMaximumId = StatId.Parse("health-maximum");
        TrackId healthId = TrackId.Parse("health");
        Stat healthMaximum = new(40, 0, 100);
        Track health = new(healthMaximum, 25, 0, TrackMaximumChangePolicy.PreserveMissingAmount);
        using ActorMechanicsState mechanics = new(new EntityId(1), [(healthMaximumId, healthMaximum)], [(healthId, health)]);

        Assert.Same(healthMaximum, mechanics.ReadStat(healthMaximumId));
        Assert.Same(health, mechanics.ReadTrack(healthId));
        Assert.Equal(10d, health.Spend(10));
        Assert.Equal(20d, health.Restore(20));
        health.SetCurrent(99, clamp: true);
        Assert.Equal(40d, health.Current);

        Assert.Throws<ArgumentException>(() => new ActorMechanicsState(
            new EntityId(2),
            [(healthMaximumId, healthMaximum), (healthMaximumId, new Stat(40))],
            [(healthId, health)]));
        Assert.Throws<ArgumentException>(() => new ActorMechanicsState(
            new EntityId(3),
            [(healthMaximumId, healthMaximum)],
            [(healthId, health), (healthId, new Track(40))]));
    }

    [Fact]
    public void Shared_stat_updates_reconcile_the_live_track_synchronously()
    {
        StatId healthMaximumId = StatId.Parse("health-maximum");
        TrackId healthId = TrackId.Parse("health");
        Stat healthMaximum = new(40, 0, 100);
        Track health = new(healthMaximum, 25, 0, TrackMaximumChangePolicy.PreserveMissingAmount);
        using ActorMechanicsState mechanics = new(new EntityId(1), [(healthMaximumId, healthMaximum)], [(healthId, health)]);

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

        Assert.Equal(50d, mechanics.ReadStat(healthMaximumId).Value);
        Assert.Equal(35d, mechanics.ReadTrack(healthId).Current);
    }
}
