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
/// A walker's step over the real Engine's navigation, tried grid by grid. A wall stands between a walker and its
/// target on open ground: a grid derived from that collision on its own id (as the wild grid about the player is)
/// takes the walker around the wall, where the straight walk off every grid runs into it.
/// </summary>
public sealed class GridRoutingTests
{
    /// <summary>The first grid a step tries, as a location's composed grid is; unpublished out in the wilds.</summary>
    private const ulong Absent = 1;

    /// <summary>The grid derived from the scene's collision.</summary>
    private const ulong Derived = 2;

    private static readonly Vector3 Start = new(0, 0, 5), Goal = new(0, 0, -5);

    [Fact]
    public void A_walker_paths_around_a_wall_on_the_derived_grid_when_the_first_grid_does_not_hold_it()
    {
        Run([Absent, Derived], (navigation, actor) =>
        {
            float widest = 0;
            for (int step = 0; step < 200 && Vector3.Distance(actor.Position.ToVector() with { Y = 0 }, Goal) > .6f; step++)
            {
                navigation.Evaluate(actor, new ActorNavigationRequest(WorldPoint.From(Goal), .25f, 4096));
                widest = MathF.Max(widest, MathF.Abs(actor.Position.X));
            }
            Assert.True(Vector3.Distance(actor.Position.ToVector() with { Y = 0 }, Goal) <= .6f, $"The walker stopped at {actor.Position}.");
            Assert.True(widest > 3f, $"The walker went no wider than {widest} m, through the wall's 3 m half-width.");
            Assert.True(navigation.TryRoute(actor.Actor.Entity, out ulong? grid));
            Assert.Equal(Derived, grid);
        });
    }

    [Fact]
    public void Off_every_grid_the_walker_walks_straight_and_the_wall_stops_it()
    {
        Run([Absent], (navigation, actor) =>
        {
            for (int step = 0; step < 200; step++)
                navigation.Evaluate(actor, new ActorNavigationRequest(WorldPoint.From(Goal), .25f, 4096));
            Assert.True(actor.Position.Z > 0f, $"The walker passed the wall to {actor.Position}.");
            Assert.True(navigation.TryRoute(actor.Actor.Entity, out ulong? grid));
            Assert.Null(grid);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_walker_crosses_from_the_grid_that_holds_it_to_an_overlapping_one_that_holds_its_target(bool boxOnTheWay)
    {
        // As at a town's edge: the town's grid holds the walker (x -150 to 0), the wild grid about the player its target
        // (x -76 to 116), and they overlap between; neither holds both. With a closed box on the straight way across the
        // whole overlap (x -80 to 4, z -2 to 2, 8 m tall), the crossing lies off that way, around either side of it.
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            using SpatialMovementSystem system = new(engine.Spatial, engine.Content, null, new SpatialTuning(.5d, 8, 8, 1, new CharacterControllerTuning()));
            List<Vector3> vertices = [new(-200, 0, -20), new(-200, 0, 20), new(200, 0, 20), new(200, 0, -20)];
            List<Triangle> triangles = [new(0, 1, 2), new(0, 2, 3)];
            if (boxOnTheWay) Box(vertices, triangles, new Vector3(-80, 0, -2), new Vector3(4, 8, 2));
            _ = engine.Spatial.ApplyCollisionResidency(new CollisionResidencyRequest(system.Session,
                new[] { new StaticMeshAsset(1, 0, (uint)vertices.Count, 0, (uint)triangles.Count) }, vertices.ToArray(), triangles.ToArray(),
                new[] { new StaticMeshInstance(2, 1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) },
                ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<ulong>.Empty));
            foreach ((ulong grid, float min, float max) in new[] { (Absent, -150f, 0f), (Derived, -76f, 116f) })
                _ = engine.Spatial.ReplaceCollisionNavigation(new CollisionNavigationReplaceRequest(system.Session,
                    new Vector3(min, -2, -6), new Vector3(max, 4, 6),
                    engine.Spatial.DefaultCollisionNavigationConfig() with { GridId = grid, CellSize = 1, Character = system.CurrentController }));
            using ActorsState actors = new();
            actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
            ActorState actor = actors.CreateActor(42, new EntityTypeId("test"), new StatsComponent(), new ActorPose(new WorldPoint(-100, 0, 0), 0), "health");
            ActorNavigationCoordinator navigation = new(engine.Spatial, system.Session, actors.Store, system.CurrentController,
                _ => CharacterStepEnvironment.Empty, () => [Absent, Derived]);
            Vector3 target = new(20, 0, 0);
            List<ulong?> routes = [];
            float widest = 0;
            for (int step = 0; step < 600 && Vector3.Distance(actor.Position.ToVector() with { Y = 0 }, target) > .6f; step++)
            {
                Assert.Equal(NavigationPathOutcome.Reached, navigation.Evaluate(actor, new ActorNavigationRequest(WorldPoint.From(target), .5f, 8192)).Outcome);
                Assert.True(navigation.TryRoute(actor.Actor.Entity, out ulong? grid));
                if (routes.Count == 0 || routes[^1] != grid) routes.Add(grid);
                widest = MathF.Max(widest, MathF.Abs(actor.Position.Z));
            }
            Assert.True(Vector3.Distance(actor.Position.ToVector() with { Y = 0 }, target) <= .6f, $"The walker stopped at {actor.Position}.");
            // It went by the town's grid to the overlap, then by the wild grid: never straight.
            Assert.Equal([Absent, Derived], routes);
            if (boxOnTheWay) Assert.True(widest > 2f, $"The walker went no wider than {widest} m, through the box's 2 m half-width.");
        });
    }

    [Fact]
    public void A_crossing_kept_while_the_target_creeps_away_is_checked_again_and_a_new_one_found_where_the_grids_now_meet()
    {
        // The town's grid holds the walker (x -150 to 0); the wild grid about the player first holds the way ahead of it
        // (x -76 to 116, z -6 to 6). The target creeps sideways under 2 m an update while the walker barely moves, and the
        // wild grid follows it (to z 30 to 50), so the crossing first found (near z 0) no longer leads anywhere.
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            using SpatialMovementSystem system = new(engine.Spatial, engine.Content, null, new SpatialTuning(.5d, 8, 8, 1, new CharacterControllerTuning()));
            Vector3[] floor = [new(-200, 0, -60), new(-200, 0, 60), new(200, 0, 60), new(200, 0, -60)];
            _ = engine.Spatial.ApplyCollisionResidency(new CollisionResidencyRequest(system.Session,
                new[] { new StaticMeshAsset(1, 0, 4, 0, 2) }, floor, new Triangle[] { new(0, 1, 2), new(0, 2, 3) },
                new[] { new StaticMeshInstance(2, 1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) },
                ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<ulong>.Empty));
            void Publish(ulong grid, Vector3 min, Vector3 max) => _ = engine.Spatial.ReplaceCollisionNavigation(new CollisionNavigationReplaceRequest(
                system.Session, min, max, engine.Spatial.DefaultCollisionNavigationConfig() with { GridId = grid, CellSize = 1, Character = system.CurrentController }));
            Publish(Absent, new(-150, -2, -50), new(0, 4, 50));
            Publish(Derived, new(-76, -2, -6), new(116, 4, 6));
            using ActorsState actors = new();
            actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
            ActorState actor = actors.CreateActor(42, new EntityTypeId("test"), new StatsComponent(), new ActorPose(new WorldPoint(-100, 0, 0), 0), "health");
            ActorNavigationCoordinator navigation = new(engine.Spatial, system.Session, actors.Store, system.CurrentController,
                _ => CharacterStepEnvironment.Empty, () => [Absent, Derived]);
            Vector3 target = new(20, 0, 0);
            Assert.Equal(NavigationPathOutcome.Reached, navigation.Evaluate(actor, new ActorNavigationRequest(WorldPoint.From(target), .01f, 8192)).Outcome);
            for (; target.Z < 40; target.Z = MathF.Min(40, target.Z + 1.5f))
                _ = navigation.Evaluate(actor, new ActorNavigationRequest(WorldPoint.From(target), .01f, 8192));
            Publish(Derived, new(-76, -2, 30), new(116, 4, 50));
            for (int step = 0; step < 600 && Vector3.Distance(actor.Position.ToVector() with { Y = 0 }, target) > .6f; step++)
                _ = navigation.Evaluate(actor, new ActorNavigationRequest(WorldPoint.From(target), .5f, 8192));
            Assert.True(Vector3.Distance(actor.Position.ToVector() with { Y = 0 }, target) <= .6f, $"The walker stopped at {actor.Position}.");
        });
    }

    private static void Run(ulong[] grids, Action<ActorNavigationCoordinator, ActorState> test)
    {
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            using SpatialMovementSystem system = new(engine.Spatial, engine.Content, null, new SpatialTuning(.5d, 8, 8, 1, new CharacterControllerTuning()));
            // A floor 40 m square and a wall 6 m wide, 3 m tall and 0.4 m thick across the way, at z 0.
            Vector3[] vertices =
            [
                new(-20, 0, -20), new(-20, 0, 20), new(20, 0, 20), new(20, 0, -20),
                new(-3, 0, -.2f), new(3, 0, -.2f), new(3, 3, -.2f), new(-3, 3, -.2f),
                new(-3, 0, .2f), new(3, 0, .2f), new(3, 3, .2f), new(-3, 3, .2f),
            ];
            Triangle[] triangles =
            [
                new(0, 1, 2), new(0, 2, 3),
                new(4, 6, 5), new(4, 7, 6), new(8, 9, 10), new(8, 10, 11),
                new(4, 5, 9), new(4, 9, 8), new(7, 11, 10), new(7, 10, 6),
                new(4, 8, 11), new(4, 11, 7), new(5, 6, 10), new(5, 10, 9),
            ];
            _ = engine.Spatial.ApplyCollisionResidency(new CollisionResidencyRequest(system.Session,
                new[] { new StaticMeshAsset(1, 0, (uint)vertices.Length, 0, (uint)triangles.Length) }, vertices, triangles,
                new[] { new StaticMeshInstance(2, 1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) },
                ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<ulong>.Empty));
            _ = engine.Spatial.ReplaceCollisionNavigation(new CollisionNavigationReplaceRequest(system.Session,
                new Vector3(-12, -2, -12), new Vector3(12, 4, 12),
                engine.Spatial.DefaultCollisionNavigationConfig() with { GridId = Derived, CellSize = .5, Character = system.CurrentController }));
            using ActorsState actors = new();
            actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
            ActorState actor = actors.CreateActor(42, new EntityTypeId("test"), new StatsComponent(), new ActorPose(WorldPoint.From(Start), 0), "health");
            test(new ActorNavigationCoordinator(engine.Spatial, system.Session, actors.Store, system.CurrentController,
                _ => CharacterStepEnvironment.Empty, () => grids), actor);
        });
    }

    /// <summary>A closed box from <paramref name="min"/> to <paramref name="max"/>: its sides and its roof.</summary>
    private static void Box(List<Vector3> vertices, List<Triangle> triangles, Vector3 min, Vector3 max)
    {
        uint b = (uint)vertices.Count;
        vertices.AddRange([
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)]);
        uint[][] faces = [[0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7], [0, 1, 5], [0, 5, 4], [3, 7, 6], [3, 6, 2], [0, 4, 7], [0, 7, 3], [1, 2, 6], [1, 6, 5]];
        foreach (uint[] face in faces) triangles.Add(new(b + face[0], b + face[1], b + face[2]));
    }
}
