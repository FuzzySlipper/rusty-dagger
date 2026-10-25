using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallExteriorCellResidencyTests
{
    [Fact]
    public void Selects_a_clipped_seven_by_seven_window_with_stable_mesh_identity()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(41), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);

        DaggerfallExteriorCellResidencyUpdate update = residency.Update(new(10, 10));

        Assert.True(update.Applied);
        Assert.Equal(49, update.AddedCellCount);
        Assert.Equal(49, residency.ResidentCells.Count);
        CollisionResidencyRequest request = Assert.Single(spatial.Requests);
        Assert.Equal(49, request.Assets.Length);
        Assert.Equal(49, request.Instances.Length);
        Assert.Empty(request.RemovedAssets.ToArray());
        Assert.Empty(request.RemovedInstances.ToArray());

        StaticMeshAsset firstAsset = request.Assets.Span[0];
        StaticMeshInstance firstInstance = request.Instances.Span[0];
        Assert.Equal(DaggerfallExteriorCellResidency.AssetId(new(7, 7)), firstAsset.Id);
        Assert.Equal(DaggerfallExteriorCellResidency.InstanceId(new(7, 7)), firstInstance.Id);
        Assert.Equal(firstAsset.Id, firstInstance.Asset);
        Assert.Equal(new Vector3(-2457.6F, 0F, -2457.6F), firstInstance.Transform.Translation);
        StaticMeshAsset secondAsset = request.Assets.Span[1];
        Assert.Equal(
            new Triangle(0, 2, 1),
            request.Triangles.Span[(int)secondAsset.FirstTriangle]);

        CollisionResidencySpatialFake edgeSpatial = CollisionResidencySpatialFake.Create();
        using SpatialSession edgeSession = new(new SpatialSessionHandle(42), static () => { });
        DaggerfallExteriorCellResidency edge = Create(edgeSpatial.Service, edgeSession);

        edge.Update(new(0, 0));

        Assert.Equal(16, edge.ResidentCells.Count);
        Assert.Equal(16, Assert.Single(edgeSpatial.Requests).Instances.Length);
    }

    [Fact]
    public void Adjacent_traversal_removes_and_adds_only_the_crossed_column()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(43), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(new(10, 10));

        residency.Update(new(10, 10), origin);
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(new(11, 10), origin);

        Assert.Equal(7, update.AddedCellCount);
        Assert.Equal(7, update.RemovedCellCount);
        Assert.Equal(7, update.UpsertedInstanceCount);
        CollisionResidencyRequest request = spatial.Requests[^1];
        Assert.Equal(7, request.Assets.Length);
        Assert.Equal(7, request.Instances.Length);
        Assert.Equal(7, request.RemovedAssets.Length);
        Assert.Equal(7, request.RemovedInstances.Length);
        Assert.Equal(
            Enumerable.Range(7, 7).Select(y => DaggerfallExteriorCellResidency.AssetId(new(7, y))),
            request.RemovedAssets.ToArray());
        StaticMeshInstance crossed = request.Instances.ToArray().Single(instance =>
            instance.Id == DaggerfallExteriorCellResidency.InstanceId(new(14, 10)));
        Assert.Equal(new Vector3(3276.8F, 0F, 0F), crossed.Transform.Translation);
    }

    [Fact]
    public void Origin_change_reprojects_instances_without_rebuilding_geometry()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(44), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);

        residency.Update(new(10, 10), DaggerfallExteriorWorldOrigin.At(new(10, 10)));
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(
            new(10, 10),
            new DaggerfallExteriorWorldOrigin(11, 10, new Vector3(2F, 3F, 4F)));

        Assert.True(update.OriginChanged);
        Assert.Equal(49, update.UpsertedInstanceCount);
        CollisionResidencyRequest request = spatial.Requests[^1];
        Assert.Empty(request.Assets.ToArray());
        Assert.Empty(request.Vertices.ToArray());
        Assert.Empty(request.Triangles.ToArray());
        Assert.Empty(request.RemovedAssets.ToArray());
        Assert.Equal(49, request.Instances.Length);
        StaticMeshInstance cell = request.Instances.ToArray().Single(instance =>
            instance.Id == DaggerfallExteriorCellResidency.InstanceId(new(11, 10)));
        Assert.Equal(new Vector3(2F, 3F, 4F), cell.Transform.Translation);
    }

    [Fact]
    public void Origin_change_during_traversal_does_not_reupsert_removed_cells()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(47), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        residency.Update(new(10, 10), DaggerfallExteriorWorldOrigin.At(new(10, 10)));

        residency.Update(new(11, 10), DaggerfallExteriorWorldOrigin.At(new(11, 10)));

        CollisionResidencyRequest request = spatial.Requests[^1];
        Assert.Equal(49, request.Instances.Length);
        Assert.DoesNotContain(
            request.Instances.ToArray(),
            instance => request.RemovedInstances.Span.Contains(instance.Id));
    }

    [Fact]
    public void Adopted_engine_rebase_updates_product_origin_without_native_upsert()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(49), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        DaggerfallExteriorCellId center = new(10, 10);
        residency.Update(center, DaggerfallExteriorWorldOrigin.At(center));
        int requestCount = spatial.Requests.Count;
        DaggerfallExteriorWorldOrigin rebased = new(11, 10, new Vector3(2F, 3F, 4F));

        residency.AdoptRebasedOrigin(rebased);
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(center, rebased);

        Assert.Equal(rebased, residency.Origin);
        Assert.False(update.OriginChanged);
        Assert.False(update.Applied);
        Assert.Equal(requestCount, spatial.Requests.Count);
    }

    [Fact]
    public void Teleport_away_and_restore_reuses_durable_cell_ids()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(45), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        residency.Update(new(20, 20));
        DaggerfallExteriorCellResidencySave saved = residency.Capture(new WorldOriginReadout(0, 0, 0, 0, 8192F, 0, 0));
        ulong originalAsset = DaggerfallExteriorCellResidency.AssetId(new(20, 20));

        residency.Update(new(900, 400));
        DaggerfallExteriorCellResidencyUpdate restored = residency.Restore(saved);

        Assert.Equal(new(20, 20), restored.Center);
        Assert.Equal(49, residency.ResidentCells.Count);
        Assert.Contains(new(20, 20), residency.ResidentCells);
        Assert.Equal(originalAsset, DaggerfallExteriorCellResidency.AssetId(new(20, 20)));
        Assert.Equal(49, spatial.Requests[^1].Assets.Length);
        Assert.Equal(49, spatial.Requests[^1].Instances.Length);
    }

    [Fact]
    public void Refresh_upserts_the_same_asset_and_instance_identity()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(46), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        DaggerfallExteriorCellId cell = new(20, 20);
        residency.Update(cell);

        DaggerfallExteriorCellResidencyUpdate update = residency.Refresh(cell);

        Assert.Equal(1, update.UpsertedInstanceCount);
        CollisionResidencyRequest request = spatial.Requests[^1];
        Assert.Single(request.Assets.ToArray());
        Assert.Single(request.Instances.ToArray());
        Assert.Equal(DaggerfallExteriorCellResidency.AssetId(cell), request.Assets.Span[0].Id);
        Assert.Equal(DaggerfallExteriorCellResidency.InstanceId(cell), request.Instances.Span[0].Id);
    }

    [Fact]
    public void Clear_removes_all_owned_colliders_and_restore_rehydrates_after_content_transition()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(48), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        residency.Update(new(20, 20));
        DaggerfallExteriorCellResidencySave saved = residency.Capture(new WorldOriginReadout(0, 0, 0, 0, 8192F, 0, 0));

        DaggerfallExteriorCellResidencyUpdate cleared = residency.Clear();

        Assert.Equal(49, cleared.RemovedCellCount);
        Assert.False(residency.IsInitialized);
        CollisionResidencyRequest clearRequest = spatial.Requests[^1];
        Assert.Empty(clearRequest.Assets.ToArray());
        Assert.Empty(clearRequest.Instances.ToArray());
        Assert.Equal(49, clearRequest.RemovedAssets.Length);
        Assert.Equal(49, clearRequest.RemovedInstances.Length);

        DaggerfallExteriorCellResidencyUpdate restored = residency.Restore(saved);

        Assert.Equal(49, restored.AddedCellCount);
        Assert.Equal(49, restored.UpsertedInstanceCount);
        CollisionResidencyRequest restoreRequest = spatial.Requests[^1];
        Assert.Equal(49, restoreRequest.Assets.Length);
        Assert.Equal(49, restoreRequest.Instances.Length);
        Assert.Empty(restoreRequest.RemovedAssets.ToArray());
        Assert.Empty(restoreRequest.RemovedInstances.ToArray());
    }

    [Fact]
    public void A_held_window_reaches_the_Engine_only_when_released_and_then_as_one_deferred_batch()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(51), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);

        DaggerfallExteriorCellResidencyUpdate update = residency.Update(new(10, 10), DaggerfallExteriorWorldOrigin.At(new(10, 10)), hold: true);

        // The window records its cells at once, but nothing reaches the Engine until the change hands it over.
        Assert.True(update.Applied);
        Assert.Equal(49, residency.ResidentCells.Count);
        Assert.True(residency.HoldsBatch);
        Assert.Empty(spatial.Requests);

        CollisionReplaceReceipt receipt = residency.ReleaseHeld()!.Value;
        CollisionResidencyRequest request = Assert.Single(spatial.Requests);
        Assert.True(request.Deferred);
        Assert.Equal(49, request.Instances.Length);
        Assert.Equal(1UL, receipt.DeferredBatch);
        Assert.False(residency.HoldsBatch);
        Assert.Null(residency.ReleaseHeld());
    }

    [Fact]
    public void A_synchronous_change_releases_a_held_window_first_so_the_Engine_keeps_the_products_order()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(52), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(new(10, 10));
        residency.Update(new(10, 10), origin, hold: true);

        // A second window change while one is held is not held: the first goes deferred, then this one at once.
        residency.Update(new(11, 10), origin, hold: true);
        Assert.Equal([true, false], spatial.Requests.Select(request => request.Deferred));
        Assert.False(residency.HoldsBatch);

        residency.Update(new(12, 10), origin, hold: true);
        residency.Clear();
        Assert.Equal([true, false, true, false], spatial.Requests.Select(request => request.Deferred));
        Assert.Equal(7, spatial.Requests[^1].RemovedInstances.Length - 42);
    }

    [Fact]
    public void The_deferred_window_reads_as_validating_then_by_its_baked_share_then_live()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(53), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        // One batch already pending ahead of the window's when it is made.
        spatial.Readout = new CollisionResidencyReadout(4, 1, 0, 0, 0, 9);
        residency.Update(new(10, 10), DaggerfallExteriorWorldOrigin.At(new(10, 10)), hold: true);
        ulong batch = residency.ReleaseHeld()!.Value.DeferredBatch;

        // Both pending, the one ahead still validating: nothing measures it yet.
        spatial.Readout = new CollisionResidencyReadout(4, 2, 0, 0, 0, 9);
        Assert.Equal(new DaggerfallExteriorAdmission(DaggerfallExteriorAdmissionState.Baking), residency.ReadAdmission());
        // The one ahead half baked: a quarter of the two.
        spatial.Readout = new CollisionResidencyReadout(4, 2, 10, 20, 0, 9);
        Assert.Equal(new DaggerfallExteriorAdmission(DaggerfallExteriorAdmissionState.Baking, .25d), residency.ReadAdmission());
        // That one done and the window's 49 instances 7 baked.
        spatial.Readout = new CollisionResidencyReadout(5, 1, 7, 49, 0, 10);
        Assert.Equal(new DaggerfallExteriorAdmission(DaggerfallExteriorAdmissionState.Baking, (1d + (7d / 49d)) / 2d), residency.ReadAdmission());
        // Every batch up to the window's is done.
        spatial.Readout = new CollisionResidencyReadout(batch, 0, 0, 0, 0, 11);
        Assert.Equal(DaggerfallExteriorAdmission.Live, residency.ReadAdmission());
        // Nothing is deferred now, so the Engine is not asked again.
        spatial.Readout = default;
        Assert.Equal(DaggerfallExteriorAdmission.Live, residency.ReadAdmission());
    }

    [Fact]
    public void A_refused_window_batch_returns_the_window_to_its_cells_before_it_so_the_next_update_admits_them_again()
    {
        CollisionResidencySpatialFake spatial = CollisionResidencySpatialFake.Create();
        using SpatialSession session = new(new SpatialSessionHandle(54), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(new(10, 10));
        residency.Update(new(10, 10), origin);
        spatial.Readout = new CollisionResidencyReadout(0, 0, 0, 0, 2, 3);
        residency.Update(new(11, 10), origin, hold: true);
        ulong batch = residency.ReleaseHeld()!.Value.DeferredBatch;

        spatial.Readout = new CollisionResidencyReadout(batch, 0, 0, 0, 3, 3);
        Assert.Equal(DaggerfallExteriorAdmissionState.Refused, residency.ReadAdmission().State);
        Assert.Contains(new DaggerfallExteriorCellId(7, 10), residency.ResidentCells);
        Assert.DoesNotContain(new DaggerfallExteriorCellId(14, 10), residency.ResidentCells);

        // The Engine kept the old column and lacks the new one: the next update moves the window again, at once.
        DaggerfallExteriorCellResidencyUpdate retry = residency.Update(new(11, 10), origin);
        Assert.Equal((7, 7), (retry.AddedCellCount, retry.RemovedCellCount));
        Assert.False(spatial.Requests[^1].Deferred);
    }

    private static DaggerfallExteriorCellResidency Create(
        ISpatialService spatial,
        SpatialSession session) => new(
            spatial,
            session,
            new DaggerfallExteriorWorldBounds(1000, 500),
            cell => new DaggerfallTerrainSurface(
                cell.X,
                cell.Y,
                [new Vector3(0F, 0F, 0F), new Vector3(1F, 0F, 0F), new Vector3(0F, 0F, 1F)],
                [new Triangle(0, 2, 1)],
                [0F, 0F, 0F]));
}
