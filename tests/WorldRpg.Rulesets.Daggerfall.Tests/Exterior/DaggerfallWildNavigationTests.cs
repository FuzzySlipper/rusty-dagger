using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The wild grid over the real Engine: derived about the player off the update, installed on a later Engine call beside
/// whatever grid a location composed, derived again once the player has gone far enough, and cleared indoors.
/// </summary>
public sealed class DaggerfallWildNavigationTests
{
    private static readonly DaggerfallWildNavigationTuning Tuning = new(8F, 1F, 4F, 60d, 8F);

    [Fact]
    public void Outdoors_the_grid_is_derived_about_the_player_off_the_update_and_follows_it_and_indoors_it_is_cleared()
    {
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        SpatialMovementSystem? movement = null;
        DaggerfallWildNavigation? wild = null;
        try
        {
            host.Call(engine =>
            {
                movement = new(engine.Spatial, engine.Content, null, DaggerfallTuning.Defaults.Spatial);
                Vector3[] floor = [new(-60, 0, -60), new(-60, 0, 60), new(60, 0, 60), new(60, 0, -60)];
                _ = engine.Spatial.ApplyCollisionResidency(new CollisionResidencyRequest(movement.Session,
                    new[] { new StaticMeshAsset(1, 0, 4, 0, 2) }, floor, new Triangle[] { new(0, 1, 2), new(0, 2, 3) },
                    new[] { new StaticMeshInstance(2, 1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) },
                    ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<ulong>.Empty));
                wild = new(engine.Spatial, movement, Tuning);
                // A location's composed grid stays first; until the wild grid installs it is the only one.
                wild.Update(new WorldPoint(0, 0, 0), outdoors: true, 1d / 60);
                Assert.True(wild.Pending);
                Assert.Equal([1UL], wild.Grids(1));
            });
            Installed(host, () => wild!, new WorldPoint(0, 0, 0));
            host.Call(engine =>
            {
                Assert.Equal([1UL, DaggerfallWildNavigation.GridId], wild!.Grids(1));
                Assert.True(wild.Installed!.Value.WalkableCellCount > 200);
                NavigationGridInfo grid = Assert.Single(engine.Spatial.ReadNavigationGrids(new NavigationGridsRequest(movement!.Session)).ToArray(),
                    value => value.GridId == DaggerfallWildNavigation.GridId);
                Assert.InRange(grid.MaxX - grid.MinX, 14, 18);
                // A walker chases the player's centre, a standing body's half height above the floor: it stands on the grid.
                NavigationStepResult toCentre = engine.Spatial.EvaluateNavigationStep(new NavigationStepRequest(movement.Session,
                    new Vector3(-3, 0, 0), new Vector3(3, .93F, 0), 1F, 4096, DaggerfallWildNavigation.GridId));
                Assert.Equal(NavigationPathOutcome.Reached, toCentre.Outcome);
                // Open ground is crossed on the diagonal: six cells across and six along take six steps, not twelve.
                NavigationStepResult diagonal = engine.Spatial.EvaluateNavigationStep(new NavigationStepRequest(movement.Session,
                    new Vector3(-3, 0, -3), new Vector3(3, 0, 3), 1F, 4096, DaggerfallWildNavigation.GridId));
                Assert.InRange(diagonal.Path.Length, 6, 8);
                // Within the move distance nothing is derived again; past it, the grid follows the player.
                wild.Update(new WorldPoint(3, 0, 0), outdoors: true, 1d / 60);
                Assert.False(wild.Pending);
                wild.Update(new WorldPoint(10, 0, 0), outdoors: true, 1d / 60);
                Assert.True(wild.Pending);
                Assert.Equal(new Vector3(10, 0, 0), wild.Centre);
            });
            Installed(host, () => wild!, new WorldPoint(10, 0, 0));
            host.Call(engine =>
            {
                Assert.Equal(2, wild!.Derivations);
                // Indoors there is no wild grid.
                wild.Update(new WorldPoint(10, 0, 0), outdoors: false, 1d / 60);
                Assert.Equal([1UL], wild.Grids(1));
                Assert.DoesNotContain(engine.Spatial.ReadNavigationGrids(new NavigationGridsRequest(movement!.Session)).ToArray(),
                    value => value.GridId == DaggerfallWildNavigation.GridId);
            });
        }
        finally
        {
            host.Call(_ => movement?.Dispose());
        }
    }

    [Fact]
    public void The_wild_grid_stands_beside_a_locations_composed_grid_and_leaves_it_as_it_was()
    {
        DaggerfallSiteProfile hold = TestSessions.Assembled("17/179/dungeon");
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = TestSessions.SpatialFiles(hold) });
        SpatialMovementSystem? movement = null;
        DaggerfallWildNavigation? wild = null;
        NavigationGridInfo composed = default;
        try
        {
            host.Call(engine =>
            {
                movement = TestSessions.SpatialOf(engine, hold, DaggerfallTuning.Defaults.Spatial);
                composed = Assert.Single(engine.Spatial.ReadNavigationGrids(new NavigationGridsRequest(movement.Session)).ToArray());
                Assert.Equal(hold.Geometry.NavigationGridId, composed.GridId);
                wild = new(engine.Spatial, movement, Tuning);
                wild.Update(new WorldPoint(composed.MinX * (float)composed.CellSize, 0, composed.MinZ * (float)composed.CellSize), outdoors: true, 1d / 60);
            });
            Installed(host, () => wild!, new WorldPoint(composed.MinX * (float)composed.CellSize, 0, composed.MinZ * (float)composed.CellSize));
            host.Call(engine =>
            {
                NavigationGridInfo[] grids = engine.Spatial.ReadNavigationGrids(new NavigationGridsRequest(movement!.Session)).ToArray();
                NavigationGridInfo still = Assert.Single(grids, value => value.GridId == composed.GridId);
                Assert.Equal((composed.ProjectionHash, composed.WalkableCellCount), (still.ProjectionHash, still.WalkableCellCount));
                Assert.Contains(grids, value => value.GridId == DaggerfallWildNavigation.GridId);
                Assert.Equal([composed.GridId, DaggerfallWildNavigation.GridId], wild!.Grids(composed.GridId));
                // Clearing the wild grid leaves the composed one too.
                wild.Update(null, outdoors: false, 1d / 60);
                NavigationGridInfo after = Assert.Single(engine.Spatial.ReadNavigationGrids(new NavigationGridsRequest(movement.Session)).ToArray());
                Assert.Equal(composed.ProjectionHash, after.ProjectionHash);
            });
        }
        finally
        {
            host.Call(_ => movement?.Dispose());
        }
    }

    /// <summary>Updates call by call until the derivation running off the update has installed.</summary>
    private static void Installed(EngineTestHost host, Func<DaggerfallWildNavigation> wild, WorldPoint player)
    {
        for (int call = 0; call < 400 && wild().Pending; call++)
        {
            Thread.Sleep(5);
            host.Call(_ => wild().Update(player, outdoors: true, 1d / 60));
        }
        Assert.False(wild().Pending, "The wild grid never installed.");
    }
}
