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
/// A walker on ground no navigation covers, as in the wilds past a location's navigation: the real Engine has no grid
/// to start it from, so it walks straight for its target under the character controller, over the floor and under
/// gravity, rather than standing still.
/// </summary>
public sealed class OffGridWalkingTests
{
    private static readonly CharacterObstacle Floor = new(1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One),
        new(-20, -1, -20), new(20, 0, 20), true, Vector3.Zero, Vector3.Zero);

    [Fact]
    public void A_walker_off_every_grid_walks_toward_its_target_on_the_floor()
    {
        Run((navigation, actor) =>
        {
            for (int step = 0; step < 30; step++)
                Assert.Equal(NavigationPathOutcome.Reached, navigation.Evaluate(actor, new ActorNavigationRequest(new WorldPoint(0, 0, -10), .1f, 64)).Outcome);
            Vector3 feet = actor.Position.ToVector();
            Assert.True(feet.Z < -.5f, $"The walker stayed at {feet}.");
            Assert.InRange(feet.Y, -.05f, .05f);
            Assert.InRange(MathF.Abs(feet.X), 0f, .01f);
        }, new WorldPoint(0, 0, 0));
    }

    [Fact]
    public void A_walker_standing_above_the_floor_comes_down_onto_it_as_it_walks()
    {
        Run((navigation, actor) =>
        {
            for (int step = 0; step < 90; step++)
                navigation.Evaluate(actor, new ActorNavigationRequest(new WorldPoint(0, 0, -10), .1f, 64));
            Assert.InRange(actor.Position.Y, -.05f, .05f);
            Assert.True(actor.Position.Z < -.5f);
        }, new WorldPoint(0, .7f, 0));
    }

    private static void Run(Action<ActorNavigationCoordinator, ActorState> test, WorldPoint start)
    {
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            using SpatialMovementSystem system = new(engine.Spatial, engine.Content, null, new SpatialTuning(.5d, 8, 8, 1, new CharacterControllerTuning()));
            CharacterStepEnvironment environment = new(default, new[] { Floor });
            using ActorsState actors = new();
            actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
            ActorState actor = actors.CreateActor(42, new EntityTypeId("test"), new StatsComponent(), new ActorPose(start, 0), "health");
            test(new ActorNavigationCoordinator(engine.Spatial, system.Session, actors.Store, system.CurrentController, _ => environment), actor);
        });
    }
}
