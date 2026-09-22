using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Interaction;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class InteractionTargetingServiceTests
{
    private static readonly ContentSha256 Hash = new(1, 2, 3, 4);

    [Fact]
    public void Overlapping_visible_targets_use_engine_priority_and_run_one_revalidated_action()
    {
        using ActorsState actors = Actors();
        PerceptionDouble perception = PerceptionDouble.Create();
        perception.Receipt = Receipt(
            new PerceptionPair(1, 3, 2d, 1d, PerceptionPairKind.Visible, 2d),
            new PerceptionPair(1, 2, 1d, 1d, PerceptionPairKind.Visible, 1d));
        using SpatialMovementSystem spatial = Spatial();
        InteractionTargetingService targeting = new(perception.Service, spatial, actors.Entities);
        InteractionTargetCandidate? applied = null;

        InteractionUseReceipt use = targeting.Activate(
            actors.Player.Actor.Entity,
            new WorldPoint(0, 0, 0),
            Vector3.UnitX,
            2.25d,
            .5d,
            [Candidate(actors, 2, precedence: 0), Candidate(actors, 3, precedence: 1)],
            target =>
            {
                applied = target;
                return new(true, "Opened.");
            });

        Assert.True(use.Performed);
        Assert.Equal(ActorsState.Identity(2), applied?.Identity);
        Assert.Single(perception.Requests);
        InteractionTargetingEvidence evidence = Assert.IsType<InteractionTargetingEvidence>(targeting.LastEvidence);
        Assert.Equal(2.25f, evidence.Query.MaximumDistance);
        Assert.Equal(ActorsState.Identity(2), evidence.SelectedIdentity);
        Assert.Equal(InteractionReason.Ready, evidence.Use.Reason);
    }

    [Fact]
    public void Stale_or_out_of_reach_candidates_do_not_run_the_product_action()
    {
        using ActorsState actors = Actors();
        PerceptionDouble perception = PerceptionDouble.Create();
        using SpatialMovementSystem spatial = Spatial();
        InteractionTargetingService targeting = new(perception.Service, spatial, actors.Entities);
        InteractionTargetCandidate stale = Candidate(actors, 2, precedence: 0);
        Assert.True(actors.Entities.Destroy(stale.Identity));

        InteractionUseReceipt staleUse = targeting.Activate(
            actors.Player.Actor.Entity, new WorldPoint(0, 0, 0), Vector3.UnitX, 2.25d, .5d, [stale],
            _ => throw new Xunit.Sdk.XunitException("A stale target must not be activated."));

        Assert.False(staleUse.Performed);
        Assert.Equal(InteractionReason.NoCandidate, staleUse.Reason);
        Assert.Empty(perception.Requests);
        Assert.Null(targeting.LastEvidence);

        InteractionTargetCandidate live = Candidate(actors, 3, precedence: 0) with { ReachDistance = 1d };
        perception.Receipt = Receipt(new PerceptionPair(1, 3, 2d, 1d, PerceptionPairKind.Visible, 2d));
        InteractionUseReceipt outOfReach = targeting.Activate(
            actors.Player.Actor.Entity, new WorldPoint(0, 0, 0), Vector3.UnitX, 2.25d, .5d, [live],
            _ => throw new Xunit.Sdk.XunitException("An out-of-reach target must not be activated."));

        Assert.False(outOfReach.Performed);
        Assert.Equal(InteractionReason.NoCandidate, outOfReach.Reason);
        Assert.Single(perception.Requests);
        Assert.Equal(InteractionReason.OutOfReach, Assert.IsType<InteractionTargetingEvidence>(targeting.LastEvidence).Focus.Reason);
        Assert.True(actors.Entities.TryResolve(ActorsState.Identity(3), out EntityId current));
        Assert.Equal(live.Entity, current);
    }

    private static InteractionTargetCandidate Candidate(ActorsState actors, long durableId, int precedence)
    {
        ActorState actor = actors.Get(durableId);
        return new InteractionTargetCandidate(actor.Actor.Entity, ActorsState.Identity(durableId), checked((ulong)durableId), actor.Position, precedence, $"actor-{durableId}");
    }

    private static ActorsState Actors()
    {
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health");
        actors.CreateActor(2, new EntityTypeId("door"), Stats(), new ActorPose(new WorldPoint(1, 0, 0), 0), "health");
        actors.CreateActor(3, new EntityTypeId("corpse"), Stats(), new ActorPose(new WorldPoint(2, 0, 0), 0), "health");
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

    private static SpatialMovementSystem Spatial()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        return new SpatialMovementSystem(spatial.Service, ContentDouble.Create().Service,
            new SpatialContentArtifact("spatial/test", Hash, 1), new SpatialTuning(.5, 8, 8, 1));
    }

    private static PerceptionReadoutLeaseReceipt Receipt(params PerceptionPair[] pairs) => new(
        pairs, ReadOnlyMemory<PerceptionAggregate>.Empty, checked((uint)pairs.Length), false, 0, 1, 1,
        checked((uint)pairs.Length), checked((ulong)pairs.Length), 0, 0, 0, 0);

    private class PerceptionDouble : DispatchProxy
    {
        public IPerceptionService Service { get; private set; } = null!;
        public List<PerceptionQueryRequest> Requests { get; } = [];
        public PerceptionReadoutLeaseReceipt Receipt { get; set; }
        public static PerceptionDouble Create()
        {
            IPerceptionService service = DispatchProxy.Create<IPerceptionService, PerceptionDouble>();
            PerceptionDouble result = (PerceptionDouble)(object)service;
            result.Service = service;
            return result;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IPerceptionService.QueryVisibility)) throw new NotSupportedException(method?.Name);
            Requests.Add((PerceptionQueryRequest)arguments![0]!);
            return Receipt;
        }
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
            ? new ContentReference(new ContentReferenceHandle(1), static () => { }) : throw new NotSupportedException(method?.Name);
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
