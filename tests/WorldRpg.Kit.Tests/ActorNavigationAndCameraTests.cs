using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class ActorNavigationAndCameraTests
{
    [Fact]
    public void Actor_pose_validates_and_actor_applies_authoritative_pose()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActorPose(new WorldPoint(float.NaN, 0f, 0f), 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActorPose(new WorldPoint(0f, 0f, 0f), float.PositiveInfinity));

        using ActorsState actors = CreateActors(new ActorPose(new WorldPoint(1f, 2f, 3f), .25f));
        ActorState actor = actors.Get(42);
        ActorPose next = new(new WorldPoint(4f, 5f, 6f), -.5f);
        actor.ApplyPose(next);

        Assert.Equal(next, actor.Pose);
        Assert.Equal(next.Position, actor.Position);
        Assert.Equal(next.HeadingYawRadians, actor.Heading);
    }

    [Fact]
    public void Actor_facade_reads_external_engine_transform_with_actor_heading_convention()
    {
        using ActorsState actors = CreateActors(new ActorPose(new WorldPoint(1f, 2f, 3f), 0f));
        ActorState actor = actors.Get(42);
        WorldPoint externalPosition = new(7f, 8f, 9f);
        float externalHeading = .75f;
        Vector3 externalScale = new(2f, 3f, 4f);
        actors.Store.Set(actor.Actor.Entity, EngineComponentTypes.Transform, new Transform(
            externalPosition.ToVector(),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, -externalHeading),
            externalScale));

        Assert.Equal(externalPosition, actor.Position);
        Assert.Equal(externalPosition, actor.Pose.Position);
        Assert.Equal(externalHeading, actor.HeadingYawRadians, precision: 5);

        actor.ApplyPose(new ActorPose(new WorldPoint(-1f, 0f, 2f), -.5f));
        Transform applied = actors.Store.Get(actor.Actor.Entity, EngineComponentTypes.Transform);
        Assert.Equal(externalScale, applied.Scale);
        Assert.Equal(new Vector3(-1f, 0f, 2f), applied.Translation);
        Assert.Equal(-.5f, actor.HeadingYawRadians, precision: 5);
    }

    [Fact]
    public void Navigation_uses_the_supplied_session_and_exact_engine_request_shape()
    {
        using SpatialSession session = new(new SpatialSessionHandle(7), () => { });
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.Receipt = Receipt(NavigationPathOutcome.Reached, new Vector3(3f, 2f, 1f));
        using ActorsState actors = CreateActors(new ActorPose(new WorldPoint(1f, 2f, 3f), 0f));
        ActorState actor = actors.Get(42);
        ActorNavigationCoordinator navigation = new(spatial.Service, session);
        ActorNavigationRequest request = new(new WorldPoint(8f, 9f, 10f), 2.5f, 17);

        navigation.Evaluate(actor, request);
        navigation.Evaluate(actor, request);

        Assert.Equal(2, spatial.Requests.Count);
        foreach (NavigationStepRequest submitted in spatial.Requests)
        {
            Assert.Same(session, submitted.Session);
            Assert.Equal(request.Target.ToVector(), submitted.Target);
            Assert.Equal(request.MaximumStepUnits, submitted.MaxStepUnits);
            Assert.Equal(request.MaximumVisited, submitted.MaxVisited);
        }
        Assert.Equal(new Vector3(1f, 2f, 3f), spatial.Requests[0].From);
        Assert.Equal(new Vector3(3f, 2f, 1f), spatial.Requests[1].From);
    }

    [Fact]
    public void Reached_navigation_step_applies_intermediate_waypoint_and_engine_heading_convention()
    {
        using SpatialSession session = new(new SpatialSessionHandle(8), () => { });
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.Receipt = Receipt((NavigationPathOutcome)0, new Vector3(2f, 1f, -3f));
        using ActorsState actors = CreateActors(new ActorPose(new WorldPoint(0f, 1f, 0f), 1f));
        ActorState actor = actors.Get(42);

        new ActorNavigationCoordinator(spatial.Service, session).Evaluate(actor, new ActorNavigationRequest(new WorldPoint(20f, 1f, -30f), 1f, 32));

        Assert.Equal(new WorldPoint(2f, 1f, -3f), actor.Position);
        Assert.Equal(MathF.Atan2(2f, 3f), actor.HeadingYawRadians);
    }

    [Theory]
    [InlineData(NavigationPathOutcome.NoPath)]
    [InlineData(NavigationPathOutcome.BudgetExhausted)]
    [InlineData(NavigationPathOutcome.InvalidQueryBudget)]
    [InlineData(NavigationPathOutcome.StartNotWalkable)]
    [InlineData(NavigationPathOutcome.GoalNotWalkable)]
    [InlineData(NavigationPathOutcome.StartNotTraversable)]
    [InlineData(NavigationPathOutcome.GoalNotTraversable)]
    [InlineData(NavigationPathOutcome.InvalidAgentVolume)]
    [InlineData(NavigationPathOutcome.InvalidStep)]
    [InlineData(NavigationPathOutcome.NonFinitePosition)]
    [InlineData(NavigationPathOutcome.ProjectionUnavailable)]
    [InlineData(NavigationPathOutcome.InvalidAgentHeight)]
    [InlineData(NavigationPathOutcome.StartBlocked)]
    [InlineData(NavigationPathOutcome.GoalBlocked)]
    [InlineData(NavigationPathOutcome.CostOverflow)]
    public void Nonreached_navigation_outcomes_leave_actor_pose_immutable(NavigationPathOutcome outcome)
    {
        using SpatialSession session = new(new SpatialSessionHandle(9), () => { });
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.Receipt = Receipt(outcome, new Vector3(99f, 99f, 99f));
        ActorPose before = new(new WorldPoint(1f, 2f, 3f), .75f);
        using ActorsState actors = CreateActors(before);
        ActorState actor = actors.Get(42);

        new ActorNavigationCoordinator(spatial.Service, session).Evaluate(actor, new ActorNavigationRequest(new WorldPoint(4f, 5f, 6f), 1f, 8));

        Assert.Equal(before.Position, actor.Pose.Position);
        Assert.Equal(before.HeadingYawRadians, actor.Pose.HeadingYawRadians, precision: 5);
    }

    [Fact]
    public void Reached_zero_horizontal_displacement_retains_heading()
    {
        using SpatialSession session = new(new SpatialSessionHandle(10), () => { });
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.Receipt = Receipt(NavigationPathOutcome.Reached, new Vector3(2f, 9f, -4f));
        using ActorsState actors = CreateActors(new ActorPose(new WorldPoint(2f, 1f, -4f), -.75f));
        ActorState actor = actors.Get(42);

        new ActorNavigationCoordinator(spatial.Service, session).Evaluate(actor, new ActorNavigationRequest(new WorldPoint(2f, 9f, -4f), 1f, 8));

        Assert.Equal(new WorldPoint(2f, 9f, -4f), actor.Position);
        Assert.Equal(-.75f, actor.HeadingYawRadians, precision: 5);
    }

    [Fact]
    public void Non_ground_actor_navigation_uses_engine_character_step_and_updates_canonical_transform()
    {
        using SpatialSession session = new(new SpatialSessionHandle(12), () => { });
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.CharacterDisplacement = new Vector3(0f, 1f, 0f);
        using ActorsState actors = CreateActors(new ActorPose(new WorldPoint(0f, 0f, 0f), 0f));
        ActorState actor = actors.Get(42);
        CharacterWaterVolume water = new(1, new Vector3(-4f, -4f, -4f), new Vector3(4f, 4f, 4f));
        ActorNavigationCoordinator navigation = new(
            spatial.Service,
            session,
            actors.Store,
            default,
            _ => new CharacterStepEnvironment(default, ReadOnlyMemory<CharacterObstacle>.Empty,
                ReadOnlyMemory<CharacterMeshInstance>.Empty, new[] { water }));

        NavigationStepResult receipt = navigation.Evaluate(actor,
            new ActorNavigationRequest(new WorldPoint(0f, 1f, 0f), 1f, 8, ActorNavigationMode.Swimming, .1f));

        Assert.Equal(NavigationPathOutcome.Reached, receipt.Outcome);
        Assert.Single(spatial.CharacterRequests);
        Assert.Empty(spatial.Requests);
        Assert.Equal(CharacterMovementMode.Swimming, spatial.CharacterRequests[0].Command.Movement.Mode);
        Assert.Equal(.1f, spatial.CharacterRequests[0].Command.StepSeconds);
        Assert.Equal(new WorldPoint(0f, 1f, 0f), actor.Position);
        Assert.Equal(new Vector3(0f, 1f, 0f), actors.Store.Get(actor.Actor.Entity, EngineComponentTypes.Transform).Translation);
    }

    [Fact]
    public void Near_horizontal_character_intent_is_bounded_after_float_normalization()
    {
        using SpatialSession session = new(new SpatialSessionHandle(13), () => { });
        SpatialDouble spatial = SpatialDouble.Create();
        using ActorsState actors = CreateActors(new ActorPose(new WorldPoint(0f, 0f, 0f), 0f));
        ActorState actor = actors.Get(42);
        Vector3 delta = new(.001f, 1e-8f, .068f);
        float distance = delta.Length();
        Vector3 direction = delta / distance;
        float rawPlanarDistance = MathF.Sqrt((direction.X * direction.X) + (direction.Z * direction.Z));
        Assert.True(rawPlanarDistance > 1f);

        ActorNavigationCoordinator navigation = new(
            spatial.Service,
            session,
            actors.Store,
            default,
            _ => CharacterStepEnvironment.Empty);

        navigation.Evaluate(actor, new ActorNavigationRequest(
            WorldPoint.From(delta), 1f, 8, ActorNavigationMode.Flying, .1f));

        CharacterStepRequest request = Assert.Single(spatial.CharacterRequests);
        Assert.Equal(1f, request.Command.PlanarIntent.Y);
    }

    [Fact]
    public void Wander_uses_engine_receipts_and_enters_blocked_or_unloaded_states_explicitly()
    {
        using SpatialSession session = new(new SpatialSessionHandle(11), () => { });
        SpatialDouble spatial = SpatialDouble.Create();
        using ActorsState actors = CreateActors(new ActorPose(new WorldPoint(1f, 2f, 3f), 0f));
        ActorState actor = actors.Get(42);
        actor.Stats.AddTrack(TrackId.Parse("health"), new Track(new Stat(100), 100));
        actor.Actor.Add(new WanderMemoryComponent());
        ActorWanderCoordinator wander = new(new ActorNavigationCoordinator(spatial.Service, session));
        WanderPolicy policy = new(.5f, 2.5f, 0f, 8, 2, ActorNavigationMode.Swimming);

        spatial.Receipt = Receipt(NavigationPathOutcome.Reached, new Vector3(-1.5f, 2f, 3f));
        WanderEvidence reached = wander.Update(actor, actor.Wander, policy, 1, .1f);
        Assert.Equal(WanderState.Idle, reached.Current);
        Assert.Single(spatial.Requests);
        Assert.Equal(new Vector3(-1.5f, 2f, 3f), spatial.Requests[0].Target);

        ActorPose beforeBlocked = actor.Pose;
        spatial.Receipt = Receipt(NavigationPathOutcome.NoPath, new Vector3(99f, 99f, 99f));
        _ = wander.Update(actor, actor.Wander, policy, 2, .1f);
        WanderEvidence blocked = wander.Update(actor, actor.Wander, policy, 3, .1f);
        Assert.Equal(WanderState.Blocked, blocked.Current);
        Assert.Equal(beforeBlocked, actor.Pose);

        actor.Wander.MarkUnloaded();
        WanderEvidence unloaded = wander.Update(actor, actor.Wander, policy, 4, .1f);
        Assert.Equal(WanderState.Unloaded, unloaded.Current);
        Assert.Equal(3, spatial.Requests.Count);
    }

    [Fact]
    public void Camera_viewpoint_equals_the_exact_position_in_its_engine_descriptor()
    {
        CameraDouble camera = CameraDouble.Create();
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);
        using FirstPersonCameraSystem system = new(camera.Service, player, new FirstPersonCameraTuning(1.5f, 75d, .1d, 100d));

        Assert.Equal(system.Viewpoint.ToVector(), camera.LastDescriptor.Pose.Position);
        player.MoveTo(new Vector3(4f, 5f, 6f));
        system.Update(player);

        Assert.Equal(new WorldPoint(4f, 6.5f, 6f), system.Viewpoint);
        Assert.Equal(system.Viewpoint.ToVector(), camera.LastDescriptor.Pose.Position);
    }

    [Fact]
    public void Camera_presentation_offset_moves_the_existing_camera_without_moving_the_player()
    {
        CameraDouble camera = CameraDouble.Create();
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);
        using FirstPersonCameraSystem system = new(camera.Service, player, new FirstPersonCameraTuning(1.5f, 75d, .1d, 100d));

        system.Update(player, -1f);

        Assert.Equal(new Vector3(1f, 2.5f, 3f), camera.LastDescriptor.Pose.Position);
        Assert.Equal(new WorldPoint(1f, 2f, 3f), player.Position);
        Assert.Equal(new WorldPoint(1f, 3.5f, 3f), system.Viewpoint);
    }

    private static NavigationStepResult Receipt(NavigationPathOutcome outcome, Vector3 waypoint) => new(
        ReadOnlyMemory<PlanarNavCell>.Empty, ReadOnlyMemory<NavigationPathEdge>.Empty, outcome, waypoint, default, default, 0f, 0, 0, 0, 0, 0, false, default, default);

    private static ActorsState CreateActors(ActorPose pose)
    {
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
        actors.CreateActor(42, new EntityTypeId("test"), new StatsComponent(), pose, "health");
        return actors;
    }

    private class SpatialDouble : DispatchProxy
    {
        internal ISpatialService Service { get; private set; } = null!;
        internal List<NavigationStepRequest> Requests { get; } = [];
        internal List<CharacterStepRequest> CharacterRequests { get; } = [];
        internal NavigationStepResult Receipt { get; set; }
        internal Vector3 CharacterDisplacement { get; set; } = Vector3.UnitX;

        internal static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            SpatialDouble proxy = (SpatialDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == nameof(ISpatialService.EvaluateNavigationStep))
            {
                Requests.Add((NavigationStepRequest)arguments![0]!);
                return Receipt;
            }

            if (method?.Name == nameof(ISpatialService.ProposeCharacterStep))
            {
                CharacterStepRequest request = (CharacterStepRequest)arguments![0]!;
                CharacterRequests.Add(request);
                Transform before = new(request.Position, Quaternion.Identity, Vector3.One);
                Transform after = before with { Translation = before.Translation + CharacterDisplacement };
                CharacterMotion motion = request.Motion with { LastCommandSequence = request.Command.Sequence };
                return default(CharacterStepReceipt) with
                {
                    Generation = 1,
                    CommandSequence = request.Command.Sequence,
                    TransformBefore = before,
                    Transform = after,
                    Motion = motion,
                    WishVelocity = CharacterDisplacement,
                    Displacement = CharacterDisplacement,
                    BlockFlags = CharacterBlockFlags.None,
                };
            }

            throw new NotSupportedException(method?.Name);
        }
    }

    private class CameraDouble : DispatchProxy
    {
        internal ICameraViewService Service { get; private set; } = null!;
        internal CameraDescriptor LastDescriptor { get; private set; }

        internal static CameraDouble Create()
        {
            ICameraViewService service = DispatchProxy.Create<ICameraViewService, CameraDouble>();
            CameraDouble proxy = (CameraDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ICameraViewService.CreateCamera) => Create((CameraDescriptor)arguments![0]!),
            nameof(ICameraViewService.UpdateCamera) => Update((CameraUpdateRequest)arguments![0]!),
            nameof(ICameraViewService.SetActiveCamera) or nameof(ICameraViewService.ClearActiveCamera) => null,
            _ => throw new NotSupportedException(method?.Name),
        };

        private Camera Create(CameraDescriptor descriptor)
        {
            LastDescriptor = descriptor;
            return new Camera(new CameraHandle(1), () => { });
        }

        private object? Update(CameraUpdateRequest request)
        {
            LastDescriptor = request.Descriptor;
            return null;
        }
    }
}
