using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
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

        using ActorState actor = CreateActor(new ActorPose(new WorldPoint(1f, 2f, 3f), .25f));
        ActorPose next = new(new WorldPoint(4f, 5f, 6f), -.5f);
        actor.ApplyPose(next);

        Assert.Equal(next, actor.Pose);
        Assert.Equal(next.Position, actor.Position);
        Assert.Equal(next.HeadingYawRadians, actor.Heading);
    }

    [Fact]
    public void Navigation_uses_the_supplied_session_and_exact_engine_request_shape()
    {
        using SpatialSession session = new(new SpatialSessionHandle(7), () => { });
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.Receipt = Receipt(NavigationPathOutcome.Reached, new Vector3(3f, 2f, 1f));
        using ActorState actor = CreateActor(new ActorPose(new WorldPoint(1f, 2f, 3f), 0f));
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
        using ActorState actor = CreateActor(new ActorPose(new WorldPoint(0f, 1f, 0f), 1f));

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
        using ActorState actor = CreateActor(before);

        new ActorNavigationCoordinator(spatial.Service, session).Evaluate(actor, new ActorNavigationRequest(new WorldPoint(4f, 5f, 6f), 1f, 8));

        Assert.Equal(before, actor.Pose);
    }

    [Fact]
    public void Reached_zero_horizontal_displacement_retains_heading()
    {
        using SpatialSession session = new(new SpatialSessionHandle(10), () => { });
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.Receipt = Receipt(NavigationPathOutcome.Reached, new Vector3(2f, 9f, -4f));
        using ActorState actor = CreateActor(new ActorPose(new WorldPoint(2f, 1f, -4f), -.75f));

        new ActorNavigationCoordinator(spatial.Service, session).Evaluate(actor, new ActorNavigationRequest(new WorldPoint(2f, 9f, -4f), 1f, 8));

        Assert.Equal(new WorldPoint(2f, 9f, -4f), actor.Position);
        Assert.Equal(-.75f, actor.HeadingYawRadians);
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

    private static NavigationStepReceipt Receipt(NavigationPathOutcome outcome, Vector3 waypoint) => new(
        outcome, waypoint, default, 0, 0, 0, 0, 0, 0);

    private static ActorState CreateActor(ActorPose pose) => new(
        42,
        new ActorMechanicsState(
            new EntityId(42),
            Array.Empty<(ExactStatDefinition Definition, ExactValue Base)>(),
            [new ExactTrack(new ExactTrackDefinition(TrackId.Parse("health"), ExactValue.Zero, new ExactTrackMaximum.Fixed(new ExactValue(1))), new ExactValue(1))]),
        pose,
        "health");

    private class SpatialDouble : DispatchProxy
    {
        internal ISpatialService Service { get; private set; } = null!;
        internal List<NavigationStepRequest> Requests { get; } = [];
        internal NavigationStepReceipt Receipt { get; set; }

        internal static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            SpatialDouble proxy = (SpatialDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(ISpatialService.EvaluateNavigationStep))
            {
                throw new NotSupportedException(method?.Name);
            }

            Requests.Add((NavigationStepRequest)arguments![0]!);
            return Receipt;
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
