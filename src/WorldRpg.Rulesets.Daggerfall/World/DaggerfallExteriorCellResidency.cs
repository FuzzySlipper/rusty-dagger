using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;

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

    /// <summary>
    /// The local-frame compensation an Engine WorldOrigin cell implies. Compensation is never stored
    /// beside the Engine origin; it is always derived from a WorldOrigin readout.
    /// </summary>
    internal static Vector3 CompensationFor(long cellX, long cellY, long cellZ) =>
        new(-cellX, -cellY, -cellZ);

    internal Vector3 LocalTranslation(DaggerfallExteriorCellId cell)
    {
        float x = checked((cell.X - MapPixelX) * DaggerfallExteriorCellResidency.CellSize);
        float z = checked((cell.Y - MapPixelY) * DaggerfallExteriorCellResidency.CellSize);
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
/// <param name="Center">The streaming window's center map pixel.</param>
/// <param name="Origin">The map pixel the local frame is anchored to.</param>
/// <param name="EngineOriginCellX">The Engine WorldOrigin cell read at capture; the Engine does not persist it.</param>
/// <param name="EngineOriginCellY">The Engine WorldOrigin cell read at capture.</param>
/// <param name="EngineOriginCellZ">The Engine WorldOrigin cell read at capture.</param>
internal readonly record struct DaggerfallExteriorCellResidencySave(
    DaggerfallExteriorCellId Center,
    DaggerfallExteriorCellId Origin,
    long EngineOriginCellX,
    long EngineOriginCellY,
    long EngineOriginCellZ)
{
    /// <summary>The saved local frame: the anchor map pixel and the compensation its Engine origin implies.</summary>
    internal DaggerfallExteriorWorldOrigin WorldOrigin => new(Origin.X, Origin.Y,
        DaggerfallExteriorWorldOrigin.CompensationFor(EngineOriginCellX, EngineOriginCellY, EngineOriginCellZ));
}

/// <summary>Durable active-site location state separate from the terrain cell window.</summary>
internal sealed record DaggerfallExteriorLocationResidencySave(
    DaggerfallWorldProfileKeySave Profile,
    DaggerfallExteriorCellId Cell,
    bool Loaded)
{
    internal DaggerfallExteriorLocationResidencySave Validate()
    {
        ArgumentNullException.ThrowIfNull(Profile);
        Profile.Validate();
        if (Profile.Require().Kind != DaggerfallWorldProfileKind.Exterior)
            throw new ArgumentException("Exterior location residency requires an exterior profile.", nameof(Profile));
        return this;
    }
}

/// <summary>Result of one incremental exterior residency operation.</summary>
internal readonly record struct DaggerfallExteriorCellResidencyUpdate(
    DaggerfallExteriorCellId Center,
    DaggerfallExteriorWorldOrigin Origin,
    int AddedCellCount,
    int RemovedCellCount,
    bool OriginChanged);

/// <summary>
/// Selects the donor's seven-by-seven exterior cell window. Product map-pixel identity stays global while the content
/// each cell brings (its flats, water, ground containers and locations) stands in the current local origin frame. The
/// window draws no collision: <see cref="DaggerfallExteriorGroundCollider"/> holds the ground.
/// </summary>
internal sealed class DaggerfallExteriorCellResidency
{
    internal const int StreamingRadius = 3;
    internal const int StreamingDimension = (StreamingRadius * 2) + 1;
    internal const float CellSize = DaggerfallTerrainSurfaceBuilder.HorizontalSize;

    private readonly DaggerfallExteriorWorldBounds _bounds;
    private readonly HashSet<DaggerfallExteriorCellId> _resident = [];
    private DaggerfallExteriorCellId _center;
    private DaggerfallExteriorWorldOrigin _origin;
    private bool _initialized;

    internal DaggerfallExteriorCellResidency(DaggerfallExteriorWorldBounds bounds)
    {
        _bounds = bounds;
        _bounds.Validate();
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

    /// <summary>The cells one past the window's edge, within the world: those the next crossing brings in.</summary>
    internal IEnumerable<DaggerfallExteriorCellId> Bordering()
    {
        if (!_initialized) yield break;
        const int reach = StreamingRadius + 1;
        for (int y = _center.Y - reach; y <= _center.Y + reach; y++)
        {
            for (int x = _center.X - reach; x <= _center.X + reach; x++)
            {
                DaggerfallExteriorCellId cell = new(x, y);
                if (Math.Max(Math.Abs(x - _center.X), Math.Abs(y - _center.Y)) == reach && _bounds.Contains(cell))
                    yield return cell;
            }
        }
    }

    /// <summary>Updates the window while keeping the current map-pixel origin.</summary>
    internal DaggerfallExteriorCellResidencyUpdate Update(DaggerfallExteriorCellId center)
    {
        DaggerfallExteriorWorldOrigin origin = _initialized
            ? _origin
            : DaggerfallExteriorWorldOrigin.At(center);
        return Update(center, origin);
    }

    /// <summary>Updates the target window and local-origin projection in one operation.</summary>
    internal DaggerfallExteriorCellResidencyUpdate Update(
        DaggerfallExteriorCellId center,
        DaggerfallExteriorWorldOrigin origin)
    {
        _bounds.Require(center, nameof(center));
        ValidateOrigin(origin);

        List<DaggerfallExteriorCellId> desired = Select(center);
        int additions = desired.Count(cell => !_resident.Contains(cell));
        int removals = _resident.Count(cell => !desired.Contains(cell));
        bool originChanged = _initialized && _origin != origin;

        _resident.Clear();
        _resident.UnionWith(desired);
        _center = center;
        _origin = origin;
        _initialized = true;
        return new DaggerfallExteriorCellResidencyUpdate(center, origin, additions, removals, originChanged);
    }

    /// <summary>
    /// Adopts the local frame read back from the Engine after it committed its native WorldOrigin
    /// rebase; the caller derives its compensation from the WorldOrigin readout, so this is the
    /// frame the window now stands in, not a second origin.
    /// </summary>
    internal void AdoptRebasedOrigin(DaggerfallExteriorWorldOrigin origin)
    {
        if (!_initialized)
            throw new InvalidOperationException("A rebased origin requires initialized exterior residency.");
        ValidateOrigin(origin);
        _origin = origin;
    }

    /// <summary>Forgets the admitted set, ahead of a site or content transition; a failed one restores what it captured.</summary>
    internal DaggerfallExteriorCellResidencyUpdate Clear()
    {
        if (!_initialized) return default;
        DaggerfallExteriorCellResidencyUpdate update = new(_center, _origin, 0, _resident.Count, false);
        _resident.Clear();
        _initialized = false;
        return update;
    }

    /// <summary>Captures the window and its anchor with the Engine origin cell the caller read.</summary>
    internal DaggerfallExteriorCellResidencySave Capture(WorldOriginReadout engineOrigin)
    {
        if (!_initialized)
            throw new InvalidOperationException("Exterior residency has not been initialized.");
        return new(
            _center,
            new DaggerfallExteriorCellId(_origin.MapPixelX, _origin.MapPixelY),
            engineOrigin.CellX,
            engineOrigin.CellY,
            engineOrigin.CellZ);
    }

    internal DaggerfallExteriorCellResidencyUpdate Restore(DaggerfallExteriorCellResidencySave save)
    {
        ValidateOrigin(save.WorldOrigin);
        return Update(save.Center, save.WorldOrigin);
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
}
