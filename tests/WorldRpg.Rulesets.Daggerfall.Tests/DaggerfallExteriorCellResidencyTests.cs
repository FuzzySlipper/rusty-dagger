using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallExteriorCellResidencyTests
{
    [Fact]
    public void Selects_a_clipped_seven_by_seven_window()
    {
        DaggerfallExteriorCellResidency residency = Create();

        DaggerfallExteriorCellResidencyUpdate update = residency.Update(new(10, 10));

        Assert.Equal(49, update.AddedCellCount);
        Assert.Equal(49, residency.ResidentCells.Count);
        Assert.Equal(new DaggerfallExteriorCellId(7, 7), residency.ResidentCells.First());
        Assert.Equal(new Vector3(-2457.6F, 0F, -2457.6F), residency.Origin.LocalTranslation(new(7, 7)));

        DaggerfallExteriorCellResidency edge = Create();
        edge.Update(new(0, 0));
        Assert.Equal(16, edge.ResidentCells.Count);
    }

    [Fact]
    public void The_cells_bordering_the_window_ring_it_one_past_its_edge_within_the_world()
    {
        DaggerfallExteriorCellResidency residency = Create();
        Assert.Empty(residency.Bordering());

        residency.Update(new(10, 10));
        DaggerfallExteriorCellId[] bordering = [.. residency.Bordering()];
        Assert.Equal(32, bordering.Length);
        Assert.All(bordering, cell => Assert.Equal(4, Math.Max(Math.Abs(cell.X - 10), Math.Abs(cell.Y - 10))));
        Assert.DoesNotContain(bordering, residency.ResidentCells.Contains);

        DaggerfallExteriorCellResidency edge = Create();
        edge.Update(new(0, 0));
        // Only the row and column past the window's inner edges, and their corner, lie inside the world.
        Assert.Equal(9, edge.Bordering().Count());
    }

    [Fact]
    public void A_pose_past_the_map_belongs_to_the_nearest_edge_pixel()
    {
        DaggerfallExteriorWorldBounds bounds = DaggerfallExteriorWorldBounds.Daggerfall;
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(new(990, 490));
        float cell = DaggerfallExteriorCellResidency.CellSize;

        Assert.Equal(new DaggerfallExteriorCellId(999, 497), DaggerfallExteriorSessionOrigin.CellForLocalPosition(
            new WorldPoint(16 * cell, 0F, 7 * cell), origin, bounds));
        Assert.Equal(new DaggerfallExteriorCellId(992, 499), DaggerfallExteriorSessionOrigin.CellForLocalPosition(
            new WorldPoint(2.5F * cell, 0F, 40 * cell), origin, bounds));
        Assert.Equal(new DaggerfallExteriorCellId(0, 0), DaggerfallExteriorSessionOrigin.CellForLocalPosition(
            new WorldPoint(-2000 * cell, 0F, -2000 * cell), origin, bounds));
    }

    [Fact]
    public void The_world_edge_stops_each_axis_at_the_donors_boundary_in_any_local_frame()
    {
        DaggerfallExteriorWorldBounds bounds = DaggerfallExteriorWorldBounds.Daggerfall;
        float cell = DaggerfallExteriorCellResidency.CellSize;

        // PlayerGPS's limits: world X 32,750,000 and world Z 10,000 of 32,768,000 and 16,384,000, at 40 units to the metre.
        DaggerfallExteriorWorldOrigin southEast = new(990, 490, new Vector3(-3000F, 0F, 1500F));
        WorldPoint east = DaggerfallExteriorSessionOrigin.InsideWorldEdge(new WorldPoint(100000F, 7F, 12F), southEast, bounds);
        Assert.Equal(-3000F + (10 * cell) - 450F, east.X, 2);
        Assert.Equal(7F, east.Y);
        Assert.Equal(12F, east.Z);
        WorldPoint south = DaggerfallExteriorSessionOrigin.InsideWorldEdge(new WorldPoint(12F, 7F, 100000F), southEast, bounds);
        Assert.Equal(12F, south.X);
        Assert.Equal(1500F + (9 * cell) - 250F, south.Z, 2);

        // And world X 10,000 and world Z 16,370,000 on the other two sides.
        DaggerfallExteriorWorldOrigin northWest = new(5, 5, new Vector3(800F, 0F, -600F));
        WorldPoint corner = DaggerfallExteriorSessionOrigin.InsideWorldEdge(new WorldPoint(-100000F, 7F, -100000F), northWest, bounds);
        Assert.Equal(800F - (5 * cell) + 250F, corner.X, 2);
        Assert.Equal(-600F - (6 * cell) + 350F, corner.Z, 2);

        WorldPoint inside = new(801F, 7F, -601F);
        Assert.Equal(inside, DaggerfallExteriorSessionOrigin.InsideWorldEdge(inside, northWest, bounds));
    }

    [Fact]
    public void Adjacent_traversal_removes_and_adds_only_the_crossed_column()
    {
        DaggerfallExteriorCellResidency residency = Create();
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(new(10, 10));

        residency.Update(new(10, 10), origin);
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(new(11, 10), origin);

        Assert.Equal(7, update.AddedCellCount);
        Assert.Equal(7, update.RemovedCellCount);
        Assert.DoesNotContain(residency.ResidentCells, cell => cell.X == 7);
        Assert.Contains(new(14, 10), residency.ResidentCells);
        Assert.Equal(new Vector3(3276.8F, 0F, 0F), residency.Origin.LocalTranslation(new(14, 10)));
    }

    [Fact]
    public void An_origin_change_moves_the_frame_and_keeps_the_cells()
    {
        DaggerfallExteriorCellResidency residency = Create();
        residency.Update(new(10, 10), DaggerfallExteriorWorldOrigin.At(new(10, 10)));

        DaggerfallExteriorCellResidencyUpdate update = residency.Update(
            new(10, 10),
            new DaggerfallExteriorWorldOrigin(11, 10, new Vector3(2F, 3F, 4F)));

        Assert.True(update.OriginChanged);
        Assert.Equal(0, update.AddedCellCount);
        Assert.Equal(0, update.RemovedCellCount);
        Assert.Equal(new Vector3(2F, 3F, 4F), residency.Origin.LocalTranslation(new(11, 10)));
    }

    [Fact]
    public void An_adopted_engine_rebase_is_not_an_origin_change()
    {
        DaggerfallExteriorCellResidency residency = Create();
        DaggerfallExteriorCellId center = new(10, 10);
        residency.Update(center, DaggerfallExteriorWorldOrigin.At(center));
        DaggerfallExteriorWorldOrigin rebased = new(11, 10, new Vector3(2F, 3F, 4F));

        residency.AdoptRebasedOrigin(rebased);
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(center, rebased);

        Assert.Equal(rebased, residency.Origin);
        Assert.False(update.OriginChanged);
    }

    [Fact]
    public void Teleport_away_and_restore_returns_to_the_saved_window()
    {
        DaggerfallExteriorCellResidency residency = Create();
        residency.Update(new(20, 20));
        DaggerfallExteriorCellResidencySave saved = residency.Capture(new WorldOriginReadout(0, 0, 0, 0, 8192F, 0, 0));

        residency.Update(new(900, 400));
        DaggerfallExteriorCellResidencyUpdate restored = residency.Restore(saved);

        Assert.Equal(new(20, 20), restored.Center);
        Assert.Equal(49, residency.ResidentCells.Count);
        Assert.Contains(new(20, 20), residency.ResidentCells);
    }

    [Fact]
    public void Clear_forgets_the_window_and_restore_admits_it_again()
    {
        DaggerfallExteriorCellResidency residency = Create();
        residency.Update(new(20, 20));
        DaggerfallExteriorCellResidencySave saved = residency.Capture(new WorldOriginReadout(0, 0, 0, 0, 8192F, 0, 0));

        DaggerfallExteriorCellResidencyUpdate cleared = residency.Clear();

        Assert.Equal(49, cleared.RemovedCellCount);
        Assert.False(residency.IsInitialized);
        Assert.Empty(residency.ResidentCells);
        Assert.Equal(49, residency.Restore(saved).AddedCellCount);
    }

    [Fact]
    public void A_deferred_batch_reads_as_validating_then_by_its_baked_share_then_live()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(53), static () => { });
        DaggerfallDeferredCollision deferred = new(spatial.Service, session);
        spatial.Readout = Readout(0, 0);
        deferred.Track(1, deferred.Read(), static () => { });

        spatial.Readout = Readout(0, 1);
        Assert.Equal(new DaggerfallDeferredCollisionReading(DaggerfallDeferredCollisionState.Preparing), deferred.Poll());
        spatial.Readout = Readout(0, 1, baked: 1, planned: 4);
        Assert.Equal(new DaggerfallDeferredCollisionReading(DaggerfallDeferredCollisionState.Preparing, .25d), deferred.Poll());
        spatial.Readout = Readout(1, 0);
        Assert.Equal(DaggerfallDeferredCollisionReading.Live, deferred.Poll());
        Assert.False(deferred.Awaiting);
    }

    [Fact]
    public void A_placement_batch_reads_by_its_composed_cells_and_only_the_batch_the_Engine_names_refused_is_reverted()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(55), static () => { });
        DaggerfallDeferredCollision deferred = new(spatial.Service, session);
        List<ulong> reverted = [];
        spatial.Readout = Readout(10, 0, refused: 4);
        deferred.Track(11, deferred.Read(), () => reverted.Add(11));
        deferred.Track(12, deferred.Read(), () => reverted.Add(12));

        // The placements compose a quarter of their cells; the window waits behind them.
        spatial.Readout = Readout(10, 2, composed: 250, cells: 1000, refused: 4);
        Assert.Equal(new DaggerfallDeferredCollisionReading(DaggerfallDeferredCollisionState.Preparing, .125d), deferred.Poll());
        // The placements refused, the window still baking: nothing is settled until both are done.
        spatial.Readout = Readout(11, 1, baked: 1, planned: 2, refused: 5, lastRefused: 11);
        Assert.Equal(new DaggerfallDeferredCollisionReading(DaggerfallDeferredCollisionState.Preparing, .75d), deferred.Poll());
        Assert.Empty(reverted);
        // Both done with one refusal, the one the Engine names: only the placements go back.
        spatial.Readout = Readout(12, 0, refused: 5, lastRefused: 11);
        Assert.Equal(DaggerfallDeferredCollisionState.Refused, deferred.Poll().State);
        Assert.Equal([11UL], reverted);
        Assert.False(deferred.Awaiting);

        // Two batches and two refusals: both go back.
        reverted.Clear();
        deferred.Track(13, deferred.Read(), () => reverted.Add(13));
        deferred.Track(14, deferred.Read(), () => reverted.Add(14));
        spatial.Readout = Readout(14, 0, refused: 7, lastRefused: 14);
        Assert.Equal(DaggerfallDeferredCollisionState.Refused, deferred.Poll().State);
        Assert.Equal([13UL, 14UL], reverted);
    }

    private static CollisionResidencyReadout Readout(ulong completed, uint pending, ulong baked = 0, ulong planned = 0,
        ulong composed = 0, ulong cells = 0, ulong refused = 0, ulong lastRefused = 0, ulong revision = 0) =>
        new(completed, pending, baked, planned, composed, cells, refused, lastRefused, revision);

    private static DaggerfallExteriorCellResidency Create() => new(new DaggerfallExteriorWorldBounds(1000, 500));
}
