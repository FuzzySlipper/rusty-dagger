using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class TargetingServiceTests
{
    private static readonly ContentSha256 Hash = new(1, 2, 3, 4);

    [Fact]
    public void Selection_uses_engine_visibility_with_stable_distance_order_and_clears_dead_or_destroyed_targets()
    {
        using ActorsState actors = Actors();
        PerceptionDouble perception = PerceptionDouble.Create();
        perception.Receipt = Receipt(
            new PerceptionPair(1, 3, 2d, 1d, PerceptionPairKind.Visible, 2d),
            new PerceptionPair(1, 2, 1d, 1d, PerceptionPairKind.Visible, 1d));
        using SpatialMovementSystem spatial = Spatial();
        TargetingService targeting = new(perception.Service, spatial, actors, new Policy());

        Assert.Equal(2L, targeting.Select(new WorldPoint(0, 0, 0), -Vector3.UnitZ, actionReach: 4d));
        TargetingEvidence evidence = Assert.IsType<TargetingEvidence>(targeting.LastEvidence);
        Assert.Equal(4d, evidence.Request.Observers.Span[0].MaximumDistance);
        Assert.Equal([2UL, 3UL], evidence.Request.Targets.Span.ToArray().Select(target => target.Entity));
        actors.Get(2).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        Assert.Null(targeting.Current);

        perception.Receipt = Receipt(new PerceptionPair(1, 3, 1d, 1d, PerceptionPairKind.Visible, 1d));
        Assert.Equal(3L, targeting.Select(new WorldPoint(0, 0, 0), -Vector3.UnitZ, actionReach: 8d));
        Assert.True(actors.Entities.Destroy(ActorsState.Identity(3)));
        Assert.Null(targeting.Current);
    }

    private static SpatialMovementSystem Spatial()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        return new SpatialMovementSystem(spatial.Service, ContentDouble.Create().Service,
            new SpatialContentArtifact("spatial/test", Hash, 1), new SpatialTuning(.5, 8, 8, 1));
    }

    private static ActorsState Actors()
    {
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health");
        actors.CreateActor(2, new EntityTypeId("near"), Stats(), new ActorPose(new WorldPoint(1, 0, 0), 0), "health");
        actors.CreateActor(3, new EntityTypeId("far"), Stats(), new ActorPose(new WorldPoint(2, 0, 0), 0), "health");
        return actors;
    }

    private static StatsComponent Stats()
    {
        Stat maximum = new(100);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(maximum, 100));
        return stats;
    }

    private static PerceptionReadoutLeaseReceipt Receipt(params PerceptionPair[] pairs) => new(
        pairs, ReadOnlyMemory<PerceptionAggregate>.Empty, checked((uint)pairs.Length), false, 0, 1, 1,
        checked((uint)pairs.Length), checked((ulong)pairs.Length), 0, 0, 0, 0);

    private sealed class Policy : ITargetingPolicy
    {
        public bool IsValidTarget(ActorState actor) => actor.DurableId is 2 or 3;
        public double MaximumDistance(double? actionReach) => actionReach ?? 10d;
        public double MinimumFacingCosine => .5d;
    }

    private class PerceptionDouble : DispatchProxy
    {
        public IPerceptionService Service { get; private set; } = null!;
        public PerceptionReadoutLeaseReceipt Receipt { get; set; }
        public static PerceptionDouble Create()
        {
            IPerceptionService service = DispatchProxy.Create<IPerceptionService, PerceptionDouble>();
            PerceptionDouble result = (PerceptionDouble)(object)service;
            result.Service = service;
            return result;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IPerceptionService.QueryVisibility)
            ? Receipt : throw new NotSupportedException(method?.Name);
    }

    private class ContentDouble : DispatchProxy
    {
        public IContentService Service { get; private set; } = null!;
        public static ContentDouble Create()
        {
            IContentService service = DispatchProxy.Create<IContentService, ContentDouble>();
            ContentDouble result = (ContentDouble)(object)service;
            result.Service = service;
            return result;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IContentService.ResolveReference)
            ? new ContentReference(new ContentReferenceHandle(1), static () => { })
            : throw new NotSupportedException(method?.Name);
    }

    private class SpatialDouble : DispatchProxy
    {
        public ISpatialService Service { get; private set; } = null!;
        public static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            SpatialDouble result = (SpatialDouble)(object)service;
            result.Service = service;
            return result;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ISpatialService.DefaultCharacterControllerConfig) => default(CharacterControllerConfig),
            nameof(ISpatialService.ValidateCharacterControllerConfig) => null,
            nameof(ISpatialService.CreateSession) => new SpatialSession(new SpatialSessionHandle(1), static () => { }),
            nameof(ISpatialService.ReplaceContentArtifact) => new SpatialContentArtifactReplaceReceipt(),
            _ => throw new NotSupportedException(method?.Name),
        };
    }
}
