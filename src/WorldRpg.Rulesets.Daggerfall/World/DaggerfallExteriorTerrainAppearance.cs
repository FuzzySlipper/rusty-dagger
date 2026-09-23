using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Owns the retained visual resources for the currently resident exterior terrain cells.
/// Spatial collision admission remains owned by <see cref="DaggerfallExteriorCellResidency"/>;
/// this coordinator only prepares Graphics resources and the facts that the complete product
/// appearance snapshot must publish.
/// </summary>
internal sealed class DaggerfallExteriorTerrainAppearance : IDisposable
{
    private const ulong AppearanceObjectPrefix = 0xD800_0000_0000_0000UL;
    private const int CoordinateBits = 24;
    private const ulong CoordinateMask = 0x00FF_FFFFUL;

    private readonly IGraphicsService _graphics;
    private readonly Material _material;
    private readonly Dictionary<DaggerfallExteriorCellId, TerrainVisual> _visuals = [];
    private readonly List<TerrainVisual> _retired = [];
    private DaggerfallExteriorWorldOrigin _origin;
    private bool _hasOrigin;
    private bool _disposed;

    /// <summary>
    /// Creates a neutral untextured terrain material. A later Daggerfall presentation owner can
    /// provide authored material policy without changing the retained mesh or collision contract.
    /// </summary>
    internal DaggerfallExteriorTerrainAppearance(IGraphicsService graphics)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _material = _graphics.CreateMaterial(new MaterialRequest(
            new Color(1F, 1F, 1F, 1F),
            default(RenderResourceReference),
            1F,
            new Color(1F, 1F, 1F, 1F),
            Vector3.Zero,
            0F,
            false));
    }

    internal int ActiveCellCount => _visuals.Count;

    internal int RetiredCellCount => _retired.Count;

    /// <summary>
    /// Appends the current terrain facts to the complete product snapshot assembled by the
    /// presentation owner. Duplicate object identities are rejected before Engine publication.
    /// </summary>
    internal void AppendFacts(List<AppearanceFact> baseFacts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(baseFacts);
        HashSet<ulong> ids = [];
        foreach (AppearanceFact fact in baseFacts)
        {
            if (!ids.Add(fact.ObjectId))
                throw new InvalidOperationException($"Complete appearance snapshot contains duplicate object {fact.ObjectId}.");
        }

        foreach (AppearanceFact fact in BuildFacts())
        {
            if (!ids.Add(fact.ObjectId))
            {
                throw new InvalidOperationException(
                    $"Complete appearance snapshot already owns exterior terrain object {fact.ObjectId}.");
            }
            baseFacts.Add(fact);
        }
    }

    /// <summary>Completes the resource transition after the owning Graphics snapshot is accepted.</summary>
    internal void CompleteAcceptedSnapshot() => DisposeRetired();

    /// <summary>The stable object identity used by one exterior terrain cell's visual fact.</summary>
    internal static ulong ObjectId(DaggerfallExteriorCellId cell)
    {
        if ((ulong)(uint)cell.X > CoordinateMask || (ulong)(uint)cell.Y > CoordinateMask)
        {
            throw new ArgumentOutOfRangeException(nameof(cell),
                "Exterior terrain visual coordinates exceed the stable ID range.");
        }

        return AppearanceObjectPrefix
            | ((ulong)(uint)cell.X << CoordinateBits)
            | (ulong)(uint)cell.Y;
    }

    /// <summary>
    /// Reconciles retained visual resources to the collision residency's durable cell set.
    /// Newly created resources are staged before active state changes; a failed admission leaves
    /// the previous visual set intact. Removed resources wait in <see cref="_retired"/> until a
    /// complete snapshot containing the new set has been accepted by Engine.
    /// </summary>
    internal void Reconcile(
        IEnumerable<DaggerfallExteriorCellId> residentCells,
        DaggerfallExteriorWorldOrigin origin,
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(residentCells);
        ArgumentNullException.ThrowIfNull(surfaceFactory);

        DaggerfallExteriorCellId[] desired = residentCells
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToArray();
        if (desired.Distinct().Count() != desired.Length)
            throw new ArgumentException("Exterior visual residency contains a duplicate cell.", nameof(residentCells));

        HashSet<DaggerfallExteriorCellId> desiredSet = desired.ToHashSet();
        List<(DaggerfallExteriorCellId Cell, TerrainVisual Visual)> additions = [];
        try
        {
            foreach (DaggerfallExteriorCellId cell in desired)
            {
                if (_visuals.ContainsKey(cell)) continue;
                additions.Add((cell, CreateVisual(cell, surfaceFactory(cell))));
            }
        }
        catch
        {
            foreach ((DaggerfallExteriorCellId _, TerrainVisual visual) in additions)
                DisposeVisual(visual);
            throw;
        }

        foreach (DaggerfallExteriorCellId cell in _visuals.Keys.Where(cell => !desiredSet.Contains(cell)).ToArray())
        {
            _retired.Add(_visuals[cell]);
            _visuals.Remove(cell);
        }

        foreach ((DaggerfallExteriorCellId cell, TerrainVisual visual) in additions)
            _visuals.Add(cell, visual);

        _origin = origin;
        _hasOrigin = true;
    }

    /// <summary>Marks all current visual cells for removal from the next complete snapshot.</summary>
    internal void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (TerrainVisual visual in _visuals.Values)
            _retired.Add(visual);
        _visuals.Clear();
        _hasOrigin = false;
    }

    /// <summary>Returns the terrain facts that the next complete snapshot must include.</summary>
    internal AppearanceFact[] BuildFacts()
    {
        if (!_hasOrigin) return [];

        return _visuals
            .OrderBy(pair => pair.Key.Y)
            .ThenBy(pair => pair.Key.X)
            .Select(pair => new AppearanceFact(
                ObjectId(pair.Key),
                false,
                0,
                new Transform(
                    _origin.LocalTranslation(pair.Key),
                    Quaternion.Identity,
                    Vector3.One),
                pair.Value.Appearance,
                true,
                RenderLayer.Scene))
            .ToArray();
    }

    /// <summary>
    /// Releases retained Engine resources after the caller has published a snapshot that no
    /// longer references them. The presentation owner invokes this after its complete snapshot
    /// has been accepted.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        foreach (TerrainVisual visual in _visuals.Values.Reverse())
            DisposeVisual(visual, ref failures);
        foreach (TerrainVisual visual in _retired.AsEnumerable().Reverse())
            DisposeVisual(visual, ref failures);
        _visuals.Clear();
        _retired.Clear();
        try { _material.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private TerrainVisual CreateVisual(DaggerfallExteriorCellId cell, DaggerfallTerrainSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.MapPixelX != cell.X || surface.MapPixelY != cell.Y)
        {
            throw new InvalidOperationException(
                $"Terrain visual surface ({surface.MapPixelX},{surface.MapPixelY}) does not match cell ({cell.X},{cell.Y}).");
        }
        if (surface.Vertices.Length < 3 || surface.Triangles.Length == 0)
            throw new InvalidOperationException($"Exterior terrain cell ({cell.X},{cell.Y}) has no renderable geometry.");

        Vector3[] normals = BuildNormals(surface);
        uint[] indices = new uint[checked(surface.Triangles.Length * 3)];
        for (int index = 0; index < surface.Triangles.Length; index++)
        {
            Triangle triangle = surface.Triangles[index];
            indices[index * 3] = triangle.A;
            indices[index * 3 + 1] = triangle.B;
            indices[index * 3 + 2] = triangle.C;
        }

        MeshResource? mesh = null;
        try
        {
            mesh = _graphics.CreateMeshResource(new MeshResourceCreateRequest(
                surface.Vertices,
                normals,
                ReadOnlyMemory<Vector2>.Empty,
                indices,
                new[] { new MeshGroup(0, 0, checked((uint)indices.Length)) },
                new[] { new MeshMaterialBinding(0, _material) }));
            Appearance appearance = _graphics.CreateMeshAppearance(mesh);
            return new(mesh, appearance);
        }
        catch
        {
            mesh?.Dispose();
            throw;
        }
    }

    private static Vector3[] BuildNormals(DaggerfallTerrainSurface surface)
    {
        Vector3[] normals = new Vector3[surface.Vertices.Length];
        for (int index = 0; index < surface.Vertices.Length; index++)
        {
            Vector3 vertex = surface.Vertices[index];
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
            {
                throw new InvalidOperationException($"Terrain visual vertex {index} is non-finite.");
            }
        }

        foreach (Triangle triangle in surface.Triangles)
        {
            uint vertexCount = checked((uint)surface.Vertices.Length);
            if (triangle.A >= vertexCount
                || triangle.B >= vertexCount
                || triangle.C >= vertexCount)
            {
                throw new InvalidOperationException("Terrain visual triangle references a vertex outside its surface.");
            }

            Vector3 normal = Vector3.Cross(
                surface.Vertices[triangle.B] - surface.Vertices[triangle.A],
                surface.Vertices[triangle.C] - surface.Vertices[triangle.A]);
            if (normal.LengthSquared() <= float.Epsilon) continue;
            normals[triangle.A] += normal;
            normals[triangle.B] += normal;
            normals[triangle.C] += normal;
        }

        for (int index = 0; index < normals.Length; index++)
        {
            normals[index] = normals[index].LengthSquared() <= float.Epsilon
                ? Vector3.UnitY
                : Vector3.Normalize(normals[index]);
        }

        return normals;
    }

    private void DisposeRetired()
    {
        List<Exception>? failures = null;
        foreach (TerrainVisual visual in _retired.AsEnumerable().Reverse())
            DisposeVisual(visual, ref failures);
        _retired.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static void DisposeVisual(TerrainVisual visual)
    {
        List<Exception>? failures = null;
        DisposeVisual(visual, ref failures);
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static void DisposeVisual(TerrainVisual visual, ref List<Exception>? failures)
    {
        try { visual.Appearance.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { visual.Mesh.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private sealed record TerrainVisual(MeshResource Mesh, Appearance Appearance);
}
