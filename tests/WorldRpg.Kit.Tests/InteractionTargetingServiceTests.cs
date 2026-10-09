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

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Larger_authored_reach_expands_the_query_but_keeps_default_target_reach(bool authored)
    {
        using ActorsState actors = Actors();
        PerceptionDouble perception = PerceptionDouble.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2, 3d, 1d, PerceptionPairKind.Visible, 3d));
        using SpatialMovementSystem spatial = Spatial();
        InteractionTargetingService targeting = new(perception.Service, spatial, actors.Entities);
        var ordinary = Candidate(actors, 2, 0) with { Position = new WorldPoint(3, 0, 0) };
        var extended = Candidate(actors, 3, 1) with { Position = new WorldPoint(3.1f, 0, 0), ReachDistance = 3.2d };
        InteractionUseReceipt use = targeting.Activate(actors.Player.Actor.Entity, new WorldPoint(0, 0, 0),
            Vector3.UnitX, 2.25d, .5d, [authored ? ordinary with { ReachDistance = 3.2d } : ordinary, extended],
            _ => new(true, "Admitted."));
        Assert.Equal(authored, use.Performed);
        Assert.Equal(3.2d, Assert.Single(perception.Requests).Observers.Span[0].MaximumDistance, precision: 6);
        Assert.Equal(3.2f, targeting.LastEvidence!.Query.MaximumDistance);
    }

    /// <summary>
    /// A door plane set into a wall: the target point lies on the wall, which Engine line of sight hits at the
    /// point itself (its ray runs the whole distance, endpoint included). Sighted as a surface target, the
    /// door is visible and usable from its open side and hidden from behind the wall.
    /// </summary>
    [Fact]
    public void A_surface_target_is_sighted_off_its_own_wall_from_its_open_side_only()
    {
        using ActorsState actors = Actors();
        PerceptionDouble perception = PerceptionDouble.Create();
        // A wall in the plane x = 1, as Engine occlusion sees it: any ray reaching or crossing the plane is stopped.
        perception.Responder = request => Receipt([.. request.Targets.ToArray().Select(target =>
        {
            float from = request.Observers.Span[0].Origin.X - 1f, to = target.Center.X - 1f;
            bool blocked = to == 0f || MathF.Sign(from) != MathF.Sign(to);
            return new PerceptionPair(1, target.Entity, 1d, 1d, blocked ? PerceptionPairKind.Occluded : PerceptionPairKind.Visible, 1d);
        })]);
        using SpatialMovementSystem spatial = Spatial();
        InteractionTargetingService targeting = new(perception.Service, spatial, actors.Entities);
        InteractionTargetCandidate onWall = Candidate(actors, 2, precedence: 0);
        InteractionTargetCandidate door = onWall with { SurfaceNormal = -Vector3.UnitX };

        // A point target on the wall is hidden by the wall from every side.
        Assert.False(targeting.Activate(actors.Player.Actor.Entity, new WorldPoint(0, 0, 0), Vector3.UnitX, 2.25d, .5d, [onWall],
            _ => throw new Xunit.Sdk.XunitException("A target hidden by its own wall must not be activated.")).Performed);
        Assert.Equal(InteractionVisibility.Occluded, Assert.Single(targeting.LastEvidence!.Focus.Candidates.ToArray()).Candidate.Visibility);

        // From its open side the door is sighted just off the wall, and is used.
        bool used = false;
        InteractionUseReceipt front = targeting.Activate(actors.Player.Actor.Entity, new WorldPoint(0, 0, 0), Vector3.UnitX, 2.25d, .5d, [door],
            _ => { used = true; return new(true, "Passed through."); });
        Assert.True(front.Performed);
        Assert.True(used);
        Assert.Equal(InteractionReason.Ready, front.Reason);
        Assert.Equal(1f - InteractionTargetingService.SurfaceSeparation, perception.Requests[^1].Targets.Span[0].Center.X, 6);

        // From behind the wall it is hidden, even where the wall's back face would not stop a ray.
        perception.Responder = request => Receipt([.. request.Targets.ToArray().Select(target =>
            new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);
        InteractionUseReceipt back = targeting.Activate(actors.Player.Actor.Entity, new WorldPoint(2, 0, 0), -Vector3.UnitX, 2.25d, .5d, [door],
            _ => throw new Xunit.Sdk.XunitException("A door must not be used through the back of its wall."));
        Assert.False(back.Performed);
        Assert.Equal(InteractionVisibility.Occluded, Assert.Single(targeting.LastEvidence!.Focus.Candidates.ToArray()).Candidate.Visibility);
        Assert.Throws<ArgumentOutOfRangeException>(() => (door with { SurfaceNormal = Vector3.Zero }).Validate());
    }

    private static InteractionTargetCandidate Candidate(ActorsState actors, long durableId, int precedence)
    {
        ActorState actor = actors.Get(durableId);
        return new InteractionTargetCandidate(actor.Actor.Entity, ActorsState.Identity(durableId), checked((ulong)durableId), actor.Position, precedence, $"actor-{durableId}");
    }

    private static ActorsState Actors()
    {
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health", ActorCapabilities.Targeting);
        actors.CreateActor(2, new EntityTypeId("door"), Stats(), new ActorPose(new WorldPoint(1, 0, 0), 0), "health", ActorCapabilities.Targeting);
        actors.CreateActor(3, new EntityTypeId("corpse"), Stats(), new ActorPose(new WorldPoint(2, 0, 0), 0), "health", ActorCapabilities.Targeting);
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

    private static PerceptionReadoutResult Receipt(params PerceptionPair[] pairs) => new(
        pairs, ReadOnlyMemory<PerceptionAggregate>.Empty, checked((uint)pairs.Length), false, 0, 1, 1,
        checked((uint)pairs.Length), checked((ulong)pairs.Length), 0, 0, 0, 0);

    private class PerceptionDouble : DispatchProxy
    {
        public IPerceptionService Service { get; private set; } = null!;
        public List<PerceptionQueryRequest> Requests { get; } = [];
        public PerceptionReadoutResult Receipt { get; set; }
        public Func<PerceptionQueryRequest, PerceptionReadoutResult>? Responder { get; set; }
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
            PerceptionQueryRequest request = (PerceptionQueryRequest)arguments![0]!;
            Requests.Add(request);
            return Responder?.Invoke(request) ?? Receipt;
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
