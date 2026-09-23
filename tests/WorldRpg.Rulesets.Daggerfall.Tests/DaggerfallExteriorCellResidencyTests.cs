using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallExteriorCellResidencyTests
{
    [Fact]
    public void Selects_a_clipped_seven_by_seven_window_with_stable_mesh_identity()
    {
        SpatialDouble spatial = SpatialDouble.Create();
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
        Assert.Equal(new Vector3(-2457.6F, 0F, 2457.6F), firstInstance.Transform.Translation);
        StaticMeshAsset secondAsset = request.Assets.Span[1];
        Assert.Equal(
            new Triangle(0, 2, 1),
            request.Triangles.Span[(int)secondAsset.FirstTriangle]);

        SpatialDouble edgeSpatial = SpatialDouble.Create();
        using SpatialSession edgeSession = new(new SpatialSessionHandle(42), static () => { });
        DaggerfallExteriorCellResidency edge = Create(edgeSpatial.Service, edgeSession);

        edge.Update(new(0, 0));

        Assert.Equal(16, edge.ResidentCells.Count);
        Assert.Equal(16, Assert.Single(edgeSpatial.Requests).Instances.Length);
    }

    [Fact]
    public void Adjacent_traversal_removes_and_adds_only_the_crossed_column()
    {
        SpatialDouble spatial = SpatialDouble.Create();
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
        SpatialDouble spatial = SpatialDouble.Create();
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
        SpatialDouble spatial = SpatialDouble.Create();
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
        SpatialDouble spatial = SpatialDouble.Create();
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
        SpatialDouble spatial = SpatialDouble.Create();
        using SpatialSession session = new(new SpatialSessionHandle(45), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        residency.Update(new(20, 20));
        DaggerfallExteriorCellResidencySave saved = residency.Capture();
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
        SpatialDouble spatial = SpatialDouble.Create();
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
        SpatialDouble spatial = SpatialDouble.Create();
        using SpatialSession session = new(new SpatialSessionHandle(48), static () => { });
        DaggerfallExteriorCellResidency residency = Create(spatial.Service, session);
        residency.Update(new(20, 20));
        DaggerfallExteriorCellResidencySave saved = residency.Capture();

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

    private class SpatialDouble : DispatchProxy
    {
        internal ISpatialService Service { get; private set; } = null!;
        internal List<CollisionResidencyRequest> Requests { get; } = [];

        internal static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            SpatialDouble proxy = (SpatialDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == nameof(ISpatialService.ApplyCollisionResidency))
            {
                Requests.Add((CollisionResidencyRequest)arguments![0]!);
                return new CollisionReplaceReceipt();
            }
            if (method?.ReturnType == typeof(void)) return null;
            if (method?.ReturnType.IsValueType == true) return Activator.CreateInstance(method.ReturnType);
            throw new NotSupportedException(method?.Name);
        }
    }
}
