using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>
/// A body the real Engine will not step because it overlaps collision deeper than its bounded recovery resolves: a
/// forced teleport under a slab too low to stand beneath, or into a box. The Engine throws and leaves the choice to the
/// product, which stands the body at the nearest clear spot in reach or holds it, and never faults.
/// </summary>
public sealed class CharacterPenetrationTests
{
    /// <summary>Daggerfall's reach: the Engine's recovery may push a body a metre out of collision.</summary>
    private static readonly SpatialTuning Tuning = new(.5d, 8, 8, 1, new CharacterControllerTuning(RecoveryMaximumDistance: 1f));

    /// <summary>The centre of a standing body on the floor at zero: half its height and its skin and padding above it.</summary>
    private const float StandingCentre = .93f;

    private static readonly CharacterObstacle Floor = Box(1, new(-5, -1, -5), new(5, 0, 5));

    /// <summary>A slab at chest height, a metre square: no body can stand on the floor beneath it.</summary>
    private static readonly CharacterObstacle Slab = Box(2, new(-.5f, 1.2f, -.5f), new(.5f, 1.5f, .5f));

    [Fact]
    public void A_player_wedged_under_a_slab_is_stood_at_the_nearest_clear_spot_and_steps_on_from_there()
    {
        Run((system, _) =>
        {
            CharacterStepEnvironment environment = new(default, new[] { Floor, Slab });
            PlayerControlState player = new(new WorldPoint(0, StandingCentre, 0), 0, 0);

            Assert.Null(system.Step(player, Update(), environment));

            Vector3 stood = player.Position!.Value.ToVector();
            Assert.InRange(new Vector2(stood.X, stood.Z).Length(), .5f, 1f);
            Assert.Equal(StandingCentre, stood.Y, precision: 4);
            Assert.False(Overlaps(system, stood, environment));
            Assert.False(system.IsHeldInCollision);
            Assert.NotNull(system.Step(player, Update(new Vector2(0, 1)), environment));
        });
    }

    [Fact]
    public void A_player_buried_deeper_than_reach_is_held_where_it_is_without_faulting()
    {
        Run((system, _) =>
        {
            CharacterStepEnvironment environment = new(default, new[] { Floor, Box(2, new(-3, 0, -3), new(3, 3, 3)) });
            WorldPoint buried = new(0, StandingCentre, 0);
            PlayerControlState player = new(buried, 0, 0);

            for (int step = 0; step < 3; step++)
                Assert.Null(system.Step(player, Update(new Vector2(0, 1)), environment));

            Assert.Equal(buried, player.Position);
            Assert.True(system.IsHeldInCollision);
        });
    }

    /// <summary>
    /// Under a slab reaching away on every side but one, the only clear spot within reach is past a wall: the player is
    /// held rather than stood on its far side.
    /// </summary>
    [Fact]
    public void A_clear_spot_past_a_wall_is_not_taken()
    {
        Run((system, _) =>
        {
            CharacterObstacle slab = Box(2, new(-3, 1.2f, -3), new(.5f, 1.5f, 3));
            CharacterObstacle wall = Box(3, new(.55f, 0, -3), new(.6f, 3, 3));
            CharacterStepEnvironment environment = new(default, new[] { Floor, slab, wall });
            Vector3 beyond = new(1, StandingCentre, 0);
            Assert.False(Overlaps(system, beyond, environment));
            WorldPoint wedged = new(0, StandingCentre, 0);
            PlayerControlState player = new(wedged, 0, 0);

            Assert.Null(system.Step(player, Update(), environment));

            Assert.Equal(wedged, player.Position);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_actor_wedged_under_a_slab_is_stood_clear_as_the_player_is(bool thrown)
    {
        Run((system, spatial) =>
        {
            CharacterStepEnvironment environment = new(default, new[] { Floor, Slab });
            using ActorsState actors = new();
            actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
            ActorState actor = actors.CreateActor(42, new EntityTypeId("test"), new StatsComponent(), new ActorPose(new WorldPoint(0, 0, 0), 0), "health");
            ActorNavigationCoordinator navigation = new(spatial, system.Session, actors.Store, system.CurrentController, _ => environment);

            if (thrown)
                Assert.Null(navigation.Throw(actor, new Vector3(1, 0, 0), ActorNavigationMode.Ground, 1f / 60f));
            else
                Assert.Equal(NavigationPathOutcome.StartBlocked, navigation.Evaluate(actor,
                    new ActorNavigationRequest(new WorldPoint(0, 0, 1), .1f, 1, ActorNavigationMode.Flying)).Outcome);

            Vector3 feet = actor.Position.ToVector();
            Assert.InRange(new Vector2(feet.X, feet.Z).Length(), .5f, 1f);
            Assert.False(Overlaps(system, feet + Vector3.UnitY * StandingCentre, environment));
            Assert.Equal(NavigationPathOutcome.Reached, navigation.Evaluate(actor,
                new ActorNavigationRequest(new WorldPoint(feet.X, feet.Y, feet.Z + 1), .1f, 1, ActorNavigationMode.Flying)).Outcome);
        });
    }

    private static void Run(Action<SpatialMovementSystem, ISpatialService> test)
    {
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            using SpatialMovementSystem system = new(engine.Spatial, engine.Content, null, Tuning);
            test(system, engine.Spatial);
        });
    }

    private static ProductUpdateState Update(Vector2 intent = default) => new(1f / 60f) { PlanarIntent = intent };

    private static bool Overlaps(SpatialMovementSystem system, Vector3 centre, CharacterStepEnvironment environment)
    {
        CharacterShapeConfig shape = system.CurrentController.Shape;
        return system.OverlapCapsule(centre, (shape.StandingHeight * .5f) - shape.Radius, shape.Radius,
            ReadOnlyMemory<SpatialEntityCollider>.Empty, environment).Present;
    }

    private static CharacterObstacle Box(ulong entity, Vector3 min, Vector3 max) =>
        new(entity, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), min, max, true, Vector3.Zero, Vector3.Zero);
}
