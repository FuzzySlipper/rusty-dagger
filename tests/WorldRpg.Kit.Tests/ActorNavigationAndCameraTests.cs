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
        spatial.TriggerRows.Add(SpatialMovementSystem.ProjectWaterCollider(water));
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
    public void A_swimming_actor_finds_its_water_through_the_engine_trigger_query_only()
    {
        using SpatialSession session = new(new SpatialSessionHandle(12), () => { });
        SpatialDouble spatial = SpatialDouble.Create();
        using ActorsState actors = CreateActors(new ActorPose(new WorldPoint(0f, 0f, 0f), 0f));
        ActorState actor = actors.Get(42);
        CharacterWaterVolume reconciled = new(1, new Vector3(-4f, -4f, -4f), new Vector3(4f, 4f, 4f));
        CharacterWaterVolume unregistered = new(2, new Vector3(-8f, -8f, -8f), new Vector3(8f, 8f, 8f));
        ActorNavigationCoordinator navigation = new(spatial.Service, session, actors.Store, default,
            _ => new CharacterStepEnvironment(default, ReadOnlyMemory<CharacterObstacle>.Empty,
                ReadOnlyMemory<CharacterMeshInstance>.Empty, new[] { unregistered }));
        ActorNavigationRequest swim = new(new WorldPoint(0f, 1f, 0f), 1f, 8, ActorNavigationMode.Swimming, .1f);

        // A volume the environment offers but the Engine holds no trigger row for is not water.
        Assert.Equal(NavigationPathOutcome.ProjectionUnavailable, navigation.Evaluate(actor, swim).Outcome);
        Assert.Empty(spatial.CharacterRequests);

        // A trigger row the environment does not offer is not this actor's water either.
        spatial.TriggerRows.Add(SpatialMovementSystem.ProjectWaterCollider(reconciled));
        Assert.Equal(NavigationPathOutcome.ProjectionUnavailable, navigation.Evaluate(actor, swim).Outcome);

        // An offered volume with a current row is, and its containment is the Engine's rule.
        spatial.TriggerRows.Add(SpatialMovementSystem.ProjectWaterCollider(unregistered));
        Assert.Equal(NavigationPathOutcome.Reached, navigation.Evaluate(actor, swim).Outcome);
        Assert.Equal(CharacterMovementMode.Swimming, Assert.Single(spatial.CharacterRequests).Command.Movement.Mode);
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
    public void Camera_draws_out_as_far_as_a_place_names_and_returns_to_its_tuned_plane()
    {
        CameraDouble camera = CameraDouble.Create();
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);
        using FirstPersonCameraSystem system = new(camera.Service, player, new FirstPersonCameraTuning(1.5f, 75d, .1d, 100d));

        system.DrawOutTo(6_000);
        system.Update(player);
        Assert.Equal(6_000, camera.LastDescriptor.Projection.Far);

        system.DrawOutTo(null);
        system.Update(player);
        Assert.Equal(100, camera.LastDescriptor.Projection.Far);
        Assert.Throws<ArgumentOutOfRangeException>(() => system.DrawOutTo(.05));
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

    [Fact]
    public void Camera_punch_turns_the_view_without_moving_the_aim_and_settles_back()
    {
        CameraDouble camera = CameraDouble.Create();
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);
        using FirstPersonCameraSystem system = new(camera.Service, player, new FirstPersonCameraTuning(1.5f, 75d, .1d, 100d));

        system.Punch(-2d, 1d);
        system.Update(player);
        Assert.Equal((-2d, 1d), (camera.LastDescriptor.Pose.PitchDegrees, camera.LastDescriptor.Pose.YawDegrees));
        Assert.Equal((0f, 0f), (player.PitchRadians, player.YawRadians));

        system.SettlePunch(10d, 14d);
        system.Update(player);
        Assert.Equal(0d, camera.LastDescriptor.Pose.PitchDegrees, 6);
    }

    [Fact]
    public void A_thrown_actor_carries_its_velocity_through_the_engine_until_it_slows_and_its_hold_ends()
    {
        using SpatialSession session = new(new SpatialSessionHandle(12), () => { });
        (ActorReactions reactions, SpatialDouble spatial, ActorsState actors) = Thrown(session);
        using ActorsState disposable = actors;
        List<ActorThrowLanding> landings = [];

        reactions.Begin(42, new Vector3(4f, 0f, 0f), .3d, 20f, ActorNavigationMode.Ground);
        reactions.Step(actors, .1f, landings.Add);
        reactions.Step(actors, .1f, landings.Add);
        Assert.True(reactions.Holds(42)); // Stopped, but its hold is still running.
        Assert.Equal([new Vector3(4f, 0f, 0f), new Vector3(2f, 0f, 0f)], spatial.CharacterRequests.Select(request => request.Motion.ExternalVelocity));
        reactions.Step(actors, .1f, landings.Add);

        Assert.False(reactions.Holds(42));
        Assert.Equal(2, spatial.CharacterRequests.Count);
        Assert.Empty(landings);
        Assert.Equal(Vector3.Zero, actors.Store.Get(actors.Get(42).Actor.Entity, EngineComponentTypes.CharacterMotion).ExternalVelocity);
    }

    [Fact]
    public void A_wall_stops_a_throw_dead()
    {
        using SpatialSession session = new(new SpatialSessionHandle(12), () => { });
        (ActorReactions reactions, SpatialDouble spatial, ActorsState actors) = Thrown(session);
        using ActorsState disposable = actors;
        spatial.CharacterDisplacement = Vector3.Zero;
        spatial.CharacterBlocks = CharacterBlockFlags.Wall;

        reactions.Begin(42, new Vector3(4f, 0f, 0f), .1d, 1f, ActorNavigationMode.Ground);
        reactions.Step(actors, .1f, _ => { });

        Assert.False(reactions.Holds(42));
    }

    [Fact]
    public void A_thrown_actor_held_past_its_hold_keeps_falling_and_reports_how_far_it_fell()
    {
        using SpatialSession session = new(new SpatialSessionHandle(12), () => { });
        (ActorReactions reactions, SpatialDouble spatial, ActorsState actors) = Thrown(session);
        using ActorsState disposable = actors;
        List<ActorThrowLanding> landings = [];
        spatial.CharacterDisplacement = new Vector3(0f, -3f, 0f);
        spatial.CharacterGrounded = false;

        reactions.Begin(42, new Vector3(1f, 0f, 0f), .1d, 100f, ActorNavigationMode.Ground);
        reactions.Step(actors, .1f, landings.Add);
        reactions.Step(actors, .1f, landings.Add);
        Assert.True(reactions.Holds(42)); // Velocity spent and hold over, but still in the air.

        spatial.CharacterGrounded = true;
        reactions.Step(actors, .1f, landings.Add);

        Assert.False(reactions.Holds(42));
        Assert.Equal(42L, Assert.Single(landings).ActorId);
        Assert.Equal(9f, landings[0].Distance, 3);
    }

    [Fact]
    public void A_thrown_flyer_drifts_with_no_gravity_and_a_defeated_or_absent_actor_drops_out()
    {
        using SpatialSession session = new(new SpatialSessionHandle(12), () => { });
        (ActorReactions reactions, SpatialDouble spatial, ActorsState actors) = Thrown(session);
        using ActorsState disposable = actors;

        reactions.Begin(42, new Vector3(1f, 0f, 0f), .1d, 100f, ActorNavigationMode.Flying);
        reactions.Step(actors, .1f, _ => { });
        Assert.Equal((CharacterMovementMode.Flying, 0f), (spatial.CharacterRequests[0].Command.Movement.Mode, spatial.CharacterRequests[0].Command.Movement.GravityScale));

        reactions.Begin(404, Vector3.UnitX, 1d, 1f, ActorNavigationMode.Ground);
        reactions.Step(actors, .1f, _ => { });
        Assert.False(reactions.Holds(404));
    }

    [Theory]
    [InlineData(4f, true)] // A kick's push, which nothing could spend.
    [InlineData(0f, false)] // A hold with no push, off the ground as swimmers are.
    public void A_swimmer_is_held_for_its_time_and_released_whatever_the_blow_gave_it(float push, bool grounded)
    {
        using SpatialSession session = new(new SpatialSessionHandle(12), () => { });
        (ActorReactions reactions, SpatialDouble spatial, ActorsState actors) = Thrown(session);
        using ActorsState disposable = actors;
        ActorState swimmer = actors.Get(42);
        CharacterMotion motion = actors.Store.Get(swimmer.Actor.Entity, EngineComponentTypes.CharacterMotion);
        actors.Store.Set(swimmer.Actor.Entity, EngineComponentTypes.CharacterMotion, motion with { Grounded = grounded });

        reactions.Begin(42, new Vector3(push, 0f, 0f), .15d, 9f, ActorNavigationMode.Swimming);
        reactions.Step(actors, .1f, _ => { });
        Assert.True(reactions.Holds(42));
        reactions.Step(actors, .1f, _ => { });

        Assert.False(reactions.Holds(42));
        Assert.Empty(spatial.CharacterRequests); // Held on the spot: never thrown.
    }

    [Fact]
    public void An_actor_wedged_in_the_air_is_released_rather_than_held_for_ever()
    {
        using SpatialSession session = new(new SpatialSessionHandle(12), () => { });
        (ActorReactions reactions, SpatialDouble spatial, ActorsState actors) = Thrown(session);
        using ActorsState disposable = actors;
        ActorState wedged = actors.Get(42);
        CharacterMotion motion = actors.Store.Get(wedged.Actor.Entity, EngineComponentTypes.CharacterMotion);
        actors.Store.Set(wedged.Actor.Entity, EngineComponentTypes.CharacterMotion, motion with { Grounded = false });
        spatial.Unresolved = true;

        reactions.Begin(42, new Vector3(4f, 0f, 0f), .05d, 1f, ActorNavigationMode.Ground);
        reactions.Step(actors, .1f, _ => { });

        Assert.False(reactions.Holds(42));
    }

    private static (ActorReactions, SpatialDouble, ActorsState) Thrown(SpatialSession session)
    {
        SpatialDouble spatial = SpatialDouble.Create();
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Living(), "health");
        actors.CreateActor(42, new EntityTypeId("test"), Living(), new ActorPose(new WorldPoint(0f, 0f, 0f), 0f), "health");
        ActorNavigationCoordinator navigation = new(spatial.Service, session, actors.Store, default,
            _ => new CharacterStepEnvironment(default, ReadOnlyMemory<CharacterObstacle>.Empty, ReadOnlyMemory<CharacterMeshInstance>.Empty, ReadOnlyMemory<CharacterWaterVolume>.Empty));
        return (new ActorReactions(navigation), spatial, actors);
    }

    private static StatsComponent Living()
    {
        Stat maximum = new(10);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(maximum, 10d, 0d));
        return stats;
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
        internal CharacterBlockFlags CharacterBlocks { get; set; } = CharacterBlockFlags.None;
        internal bool? CharacterGrounded { get; set; }
        internal bool Unresolved { get; set; }
        /// <summary>The trigger rows the Engine would hold from the product's last trigger reconcile.</summary>
        internal List<SpatialEntityCollider> TriggerRows { get; } = [];

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
                if (Unresolved) throw new EngineCallException("Spatial", "ProposeCharacterStep", 1,
                    new[] { new EngineDiagnostic("unresolved-character-controller-penetration", "Wedged.", "test") });
                Transform before = new(request.Position, Quaternion.Identity, Vector3.One);
                Transform after = before with { Translation = before.Translation + CharacterDisplacement };
                CharacterMotion motion = request.Motion with
                {
                    LastCommandSequence = request.Command.Sequence,
                    Grounded = CharacterGrounded ?? request.Motion.Grounded,
                };
                return default(CharacterStepReceipt) with
                {
                    Generation = 1,
                    CommandSequence = request.Command.Sequence,
                    TransformBefore = before,
                    Transform = after,
                    Motion = motion,
                    WishVelocity = CharacterDisplacement,
                    Displacement = CharacterDisplacement,
                    BlockFlags = CharacterBlocks,
                };
            }

            if (method?.Name == nameof(ISpatialService.QueryTriggersAtPoints))
            {
                // Engine rule: a trigger contains a point strictly inside its row; a point on a face is outside.
                SpatialTriggerPointQueryRequest query = (SpatialTriggerPointQueryRequest)arguments![0]!;
                List<SpatialTriggerPointHit> hits = [];
                Vector3[] points = query.Points.ToArray();
                for (int index = 0; index < points.Length; index++)
                    foreach (SpatialEntityCollider row in TriggerRows.OrderBy(row => row.Entity))
                        if (points[index].X > row.Min.X && points[index].X < row.Max.X
                            && points[index].Y > row.Min.Y && points[index].Y < row.Max.Y
                            && points[index].Z > row.Min.Z && points[index].Z < row.Max.Z)
                            hits.Add(new SpatialTriggerPointHit(checked((uint)index), row.Entity));
                return new SpatialTriggerPointQueryResult(hits.ToArray(), checked((uint)points.Length));
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
