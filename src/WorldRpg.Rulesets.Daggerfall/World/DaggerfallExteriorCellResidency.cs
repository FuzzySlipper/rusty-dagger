using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>Durable identity for one Daggerfall wilderness map pixel.</summary>
internal readonly record struct DaggerfallExteriorCellId(int X, int Y);

/// <summary>
/// The map-pixel origin used when projecting durable exterior cells into the
/// Engine's current local frame. The compensation is product state used for
/// teleport offsets and vertical placement; the Engine WorldOrigin service
/// remains the owner of native origin rebases.
/// </summary>
internal readonly record struct DaggerfallExteriorWorldOrigin(
    int MapPixelX,
    int MapPixelY,
    Vector3 Compensation)
{
    internal static DaggerfallExteriorWorldOrigin At(DaggerfallExteriorCellId cell) =>
        new(cell.X, cell.Y, Vector3.Zero);

    internal Vector3 LocalTranslation(DaggerfallExteriorCellId cell)
    {
        float x = checked((cell.X - MapPixelX) * DaggerfallExteriorCellResidency.CellSize);
        float z = checked((MapPixelY - cell.Y) * DaggerfallExteriorCellResidency.CellSize);
        return new Vector3(x, 0F, z) + Compensation;
    }
}

/// <summary>Bounds of the normalized Daggerfall wilderness map-pixel catalog.</summary>
internal readonly record struct DaggerfallExteriorWorldBounds(int Width, int Height)
{
    internal static DaggerfallExteriorWorldBounds Daggerfall => new(1000, 500);

    internal void Validate()
    {
        if (Width <= 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height <= 0) throw new ArgumentOutOfRangeException(nameof(Height));
        if (Width > 0x00FF_FFFF) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height > 0x00FF_FFFF) throw new ArgumentOutOfRangeException(nameof(Height));
    }

    internal bool Contains(DaggerfallExteriorCellId cell) =>
        (uint)cell.X < (uint)Width && (uint)cell.Y < (uint)Height;

    internal void Require(DaggerfallExteriorCellId cell, string parameterName)
    {
        if (!Contains(cell))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                cell,
                $"Exterior cell ({cell.X},{cell.Y}) is outside the {Width}x{Height} map.");
        }
    }
}

/// <summary>
/// Saveable product state for exterior streaming. Engine handles and mesh
/// buffers are deliberately absent; re-admission rebuilds them from the
/// durable map-pixel identity and current normalized content.
/// </summary>
internal readonly record struct DaggerfallExteriorCellResidencySave(
    DaggerfallExteriorCellId Center,
    DaggerfallExteriorCellId Origin,
    float CompensationX,
    float CompensationY,
    float CompensationZ);

/// <summary>Result of one incremental exterior residency operation.</summary>
internal readonly record struct DaggerfallExteriorCellResidencyUpdate(
    DaggerfallExteriorCellId Center,
    DaggerfallExteriorWorldOrigin Origin,
    int AddedCellCount,
    int RemovedCellCount,
    int UpsertedInstanceCount,
    bool OriginChanged,
    bool Applied,
    CollisionReplaceReceipt EngineReceipt);

/// <summary>
/// Selects and incrementally admits the donor's seven-by-seven exterior cell
/// window. Product map-pixel identity stays global while Engine collision
/// instances use the current local origin frame.
/// </summary>
internal sealed class DaggerfallExteriorCellResidency
{
    internal const int StreamingRadius = 3;
    internal const int StreamingDimension = (StreamingRadius * 2) + 1;
    internal const float CellSize = DaggerfallTerrainSurfaceBuilder.HorizontalSize;

    // The high byte reserves separate, deterministic namespaces for this
    // ruleset's exterior assets and instances. The remaining 48 bits pack the
    // bounded map-pixel coordinates without using runtime Engine identities.
    private const ulong AssetIdPrefix = 0xD600_0000_0000_0000UL;
    private const ulong InstanceIdPrefix = 0xD700_0000_0000_0000UL;
    private const int CoordinateBits = 24;
    private const uint CoordinateMask = 0x00FF_FFFF;

    private readonly ISpatialService _spatial;
    private readonly SpatialSession _session;
    private readonly DaggerfallExteriorWorldBounds _bounds;
    private readonly Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> _surfaceFactory;
    private readonly HashSet<DaggerfallExteriorCellId> _resident = [];
    private DaggerfallExteriorCellId _center;
    private DaggerfallExteriorWorldOrigin _origin;
    private bool _initialized;

    internal DaggerfallExteriorCellResidency(
        ISpatialService spatial,
        SpatialSession session,
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory)
        : this(spatial, session, DaggerfallExteriorWorldBounds.Daggerfall, surfaceFactory)
    {
    }

    internal DaggerfallExteriorCellResidency(
        ISpatialService spatial,
        SpatialSession session,
        DaggerfallExteriorWorldBounds bounds,
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory)
    {
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _bounds = bounds;
        _bounds.Validate();
        _surfaceFactory = surfaceFactory ?? throw new ArgumentNullException(nameof(surfaceFactory));
    }

    internal bool IsInitialized => _initialized;

    internal DaggerfallExteriorCellId Center => _initialized
        ? _center
        : throw new InvalidOperationException("Exterior residency has not been initialized.");

    internal DaggerfallExteriorWorldOrigin Origin => _initialized
        ? _origin
        : throw new InvalidOperationException("Exterior residency has not been initialized.");

    internal IReadOnlyCollection<DaggerfallExteriorCellId> ResidentCells => _resident
        .OrderBy(cell => cell.Y)
        .ThenBy(cell => cell.X)
        .ToArray();

    /// <summary>Updates the window while keeping the current map-pixel origin.</summary>
    internal DaggerfallExteriorCellResidencyUpdate Update(DaggerfallExteriorCellId center)
    {
        DaggerfallExteriorWorldOrigin origin = _initialized
            ? _origin
            : DaggerfallExteriorWorldOrigin.At(center);
        return Update(center, origin);
    }

    /// <summary>
    /// Updates the target window and local-origin projection in one operation.
    /// Existing geometry is left resident and only newly selected cells are
    /// rebuilt. When the origin changes, existing instances are upserted with
    /// their new local translations; Engine WorldOrigin rebases retained
    /// colliders through its own safe prepare/commit API.
    /// </summary>
    internal DaggerfallExteriorCellResidencyUpdate Update(
        DaggerfallExteriorCellId center,
        DaggerfallExteriorWorldOrigin origin)
    {
        _bounds.Require(center, nameof(center));
        ValidateOrigin(origin);

        List<DaggerfallExteriorCellId> desired = Select(center);
        List<DaggerfallExteriorCellId> additions = desired
            .Where(cell => !_resident.Contains(cell))
            .ToList();
        List<DaggerfallExteriorCellId> removals = _resident
            .Where(cell => !desired.Contains(cell))
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToList();
        bool originChanged = _initialized && _origin != origin;

        var assets = new List<StaticMeshAsset>(additions.Count);
        var vertices = new List<Vector3>();
        var triangles = new List<Triangle>();
        var instances = new List<StaticMeshInstance>(additions.Count + (originChanged ? _resident.Count : 0));

        foreach (DaggerfallExteriorCellId cell in additions)
        {
            DaggerfallTerrainSurface surface = _surfaceFactory(cell)
                ?? throw new InvalidOperationException($"Exterior cell ({cell.X},{cell.Y}) produced no terrain surface.");
            ValidateSurface(cell, surface);
            StaticMeshAsset asset = AppendAsset(cell, surface, vertices, triangles);
            assets.Add(asset);
            instances.Add(Instance(cell, origin));
        }

        if (originChanged)
        {
            foreach (DaggerfallExteriorCellId cell in _resident
                .Where(desired.Contains)
                .OrderBy(value => value.Y)
                .ThenBy(value => value.X))
                instances.Add(Instance(cell, origin));
        }

        ulong[] removedAssets = removals.Select(AssetId).ToArray();
        ulong[] removedInstances = removals.Select(InstanceId).ToArray();
        bool applied = assets.Count != 0 || instances.Count != 0 || removedAssets.Length != 0;
        CollisionReplaceReceipt receipt = default;
        if (applied)
        {
            receipt = _spatial.ApplyCollisionResidency(new CollisionResidencyRequest(
                _session,
                assets.ToArray(),
                vertices.ToArray(),
                triangles.ToArray(),
                instances.ToArray(),
                removedAssets,
                removedInstances));
        }

        foreach (DaggerfallExteriorCellId cell in removals)
            _resident.Remove(cell);
        foreach (DaggerfallExteriorCellId cell in additions)
            _resident.Add(cell);
        _center = center;
        _origin = origin;
        _initialized = true;

        return new DaggerfallExteriorCellResidencyUpdate(
            center,
            origin,
            additions.Count,
            removals.Count,
            instances.Count,
            originChanged,
            applied,
            receipt);
    }

    /// <summary>
    /// Adopts the product-side local origin after Engine has committed its native WorldOrigin
    /// rebase. Spatial already shifted retained colliders during that commit, so this bookkeeping
    /// operation deliberately emits no replacement request; a following <see cref="Update"/>
    /// admits only cells entering or leaving the selected window.
    /// </summary>
    internal void AdoptRebasedOrigin(DaggerfallExteriorWorldOrigin origin)
    {
        if (!_initialized)
            throw new InvalidOperationException("A rebased origin requires initialized exterior residency.");
        ValidateOrigin(origin);
        _origin = origin;
    }

    /// <summary>
    /// Rebuilds one currently resident cell after its normalized surface or
    /// product-owned terrain delta changes while preserving both Engine IDs.
    /// </summary>
    internal DaggerfallExteriorCellResidencyUpdate Refresh(DaggerfallExteriorCellId cell)
    {
        if (!_initialized)
            throw new InvalidOperationException("Exterior residency has not been initialized.");
        if (!_resident.Contains(cell))
            throw new InvalidOperationException($"Exterior cell ({cell.X},{cell.Y}) is not resident.");

        DaggerfallTerrainSurface surface = _surfaceFactory(cell)
            ?? throw new InvalidOperationException($"Exterior cell ({cell.X},{cell.Y}) produced no terrain surface.");
        ValidateSurface(cell, surface);
        var assets = new List<StaticMeshAsset>(1);
        var vertices = new List<Vector3>(surface.Vertices.Length);
        var triangles = new List<Triangle>(surface.Triangles.Length);
        assets.Add(AppendAsset(cell, surface, vertices, triangles));
        StaticMeshInstance instance = Instance(cell, _origin);
        CollisionReplaceReceipt receipt = _spatial.ApplyCollisionResidency(new CollisionResidencyRequest(
            _session,
            assets.ToArray(),
            vertices.ToArray(),
            triangles.ToArray(),
            new[] { instance },
            ReadOnlyMemory<ulong>.Empty,
            ReadOnlyMemory<ulong>.Empty));
        return new DaggerfallExteriorCellResidencyUpdate(
            _center,
            _origin,
            0,
            0,
            1,
            false,
            true,
            receipt);
    }

    /// <summary>
    /// Removes every exterior collider owned by this coordinator and forgets
    /// the admitted set. Call this before a site/content transition because
    /// Engine ReplaceContentArtifact replaces the complete collision set;
    /// Restore the captured state after a failed transition rollback.
    /// </summary>
    internal DaggerfallExteriorCellResidencyUpdate Clear()
    {
        if (!_initialized)
            return new(default, default, 0, 0, 0, false, false, default);

        DaggerfallExteriorCellId center = _center;
        DaggerfallExteriorWorldOrigin origin = _origin;
        List<DaggerfallExteriorCellId> cells = _resident
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToList();
        ulong[] removedAssets = cells.Select(AssetId).ToArray();
        ulong[] removedInstances = cells.Select(InstanceId).ToArray();
        CollisionReplaceReceipt receipt = _spatial.ApplyCollisionResidency(new CollisionResidencyRequest(
            _session,
            ReadOnlyMemory<StaticMeshAsset>.Empty,
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Triangle>.Empty,
            ReadOnlyMemory<StaticMeshInstance>.Empty,
            removedAssets,
            removedInstances));

        _resident.Clear();
        _initialized = false;
        return new(
            center,
            origin,
            0,
            cells.Count,
            0,
            false,
            true,
            receipt);
    }

    internal DaggerfallExteriorCellResidencySave Capture()
    {
        if (!_initialized)
            throw new InvalidOperationException("Exterior residency has not been initialized.");
        return new(
            _center,
            new DaggerfallExteriorCellId(_origin.MapPixelX, _origin.MapPixelY),
            _origin.Compensation.X,
            _origin.Compensation.Y,
            _origin.Compensation.Z);
    }

    internal DaggerfallExteriorCellResidencyUpdate Restore(DaggerfallExteriorCellResidencySave save)
    {
        ValidateOrigin(new DaggerfallExteriorWorldOrigin(
            save.Origin.X,
            save.Origin.Y,
            new Vector3(save.CompensationX, save.CompensationY, save.CompensationZ)));
        return Update(
            save.Center,
            new DaggerfallExteriorWorldOrigin(
                save.Origin.X,
                save.Origin.Y,
                new Vector3(save.CompensationX, save.CompensationY, save.CompensationZ)));
    }

    internal static ulong AssetId(DaggerfallExteriorCellId cell) =>
        AssetIdPrefix | PackCoordinates(cell);

    internal static ulong InstanceId(DaggerfallExteriorCellId cell) =>
        InstanceIdPrefix | PackCoordinates(cell);

    private static ulong PackCoordinates(DaggerfallExteriorCellId cell)
    {
        if ((uint)cell.X > CoordinateMask || (uint)cell.Y > CoordinateMask)
            throw new ArgumentOutOfRangeException(nameof(cell), "Exterior identity coordinates exceed the stable ID range.");
        return ((ulong)(uint)cell.X << CoordinateBits) | (uint)cell.Y;
    }

    private List<DaggerfallExteriorCellId> Select(DaggerfallExteriorCellId center)
    {
        int minimumX = Math.Max(0, center.X - StreamingRadius);
        int maximumX = Math.Min(_bounds.Width - 1, center.X + StreamingRadius);
        int minimumY = Math.Max(0, center.Y - StreamingRadius);
        int maximumY = Math.Min(_bounds.Height - 1, center.Y + StreamingRadius);
        var cells = new List<DaggerfallExteriorCellId>(StreamingDimension * StreamingDimension);
        for (int y = minimumY; y <= maximumY; y++)
        {
            for (int x = minimumX; x <= maximumX; x++)
                cells.Add(new DaggerfallExteriorCellId(x, y));
        }
        return cells;
    }

    private static StaticMeshAsset AppendAsset(
        DaggerfallExteriorCellId cell,
        DaggerfallTerrainSurface surface,
        List<Vector3> vertices,
        List<Triangle> triangles)
    {
        int firstVertex = vertices.Count;
        int firstTriangle = triangles.Count;
        vertices.AddRange(surface.Vertices);
        triangles.AddRange(surface.Triangles);
        return new(
            AssetId(cell),
            checked((uint)firstVertex),
            checked((uint)surface.Vertices.Length),
            checked((uint)firstTriangle),
            checked((uint)surface.Triangles.Length));
    }

    private static StaticMeshInstance Instance(
        DaggerfallExteriorCellId cell,
        DaggerfallExteriorWorldOrigin origin) =>
        new(
            InstanceId(cell),
            AssetId(cell),
            new Transform(origin.LocalTranslation(cell), Quaternion.Identity, Vector3.One));

    private void ValidateOrigin(DaggerfallExteriorWorldOrigin origin)
    {
        _bounds.Require(new DaggerfallExteriorCellId(origin.MapPixelX, origin.MapPixelY), nameof(origin));
        if (!float.IsFinite(origin.Compensation.X)
            || !float.IsFinite(origin.Compensation.Y)
            || !float.IsFinite(origin.Compensation.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "Exterior origin compensation must be finite.");
        }
    }

    private static void ValidateSurface(DaggerfallExteriorCellId cell, DaggerfallTerrainSurface surface)
    {
        if (surface.MapPixelX != cell.X || surface.MapPixelY != cell.Y)
        {
            throw new InvalidOperationException(
                $"Terrain surface ({surface.MapPixelX},{surface.MapPixelY}) does not match exterior cell ({cell.X},{cell.Y}).");
        }
        if (surface.Vertices.Length == 0 || surface.Triangles.Length == 0)
            throw new InvalidOperationException($"Exterior cell ({cell.X},{cell.Y}) has no collision geometry.");

        uint vertexCount = checked((uint)surface.Vertices.Length);
        foreach (Triangle triangle in surface.Triangles)
        {
            if (triangle.A >= vertexCount || triangle.B >= vertexCount || triangle.C >= vertexCount)
            {
                throw new InvalidOperationException(
                    $"Exterior cell ({cell.X},{cell.Y}) has a triangle outside its vertex range.");
            }
        }
    }
}
