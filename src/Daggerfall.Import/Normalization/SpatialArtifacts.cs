using System.Globalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>One byte-exact generated spatial artifact, held in memory until the caller elects to publish it.</summary>
public sealed class GeneratedSpatialArtifact
{
    private readonly byte[] bytes;

    public GeneratedSpatialArtifact(string id, string relativePath, ReadOnlySpan<byte> bytes, IReadOnlyList<string> dependsOnArtifactIds)
    {
        NormalizedImportDocument.RequireLogicalId(id, nameof(id));
        NormalizedImportDocument.RequireLogicalPath(relativePath, nameof(relativePath));
        ArgumentNullException.ThrowIfNull(dependsOnArtifactIds);
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A generated spatial artifact cannot be empty.", nameof(bytes));
        }

        Id = id;
        RelativePath = relativePath;
        this.bytes = bytes.ToArray();
        DependsOnArtifactIds = dependsOnArtifactIds.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        NormalizedImportDocument.ValidateUnique(DependsOnArtifactIds, value => value, "generated spatial artifact dependency");
    }

    public string Id { get; }

    public string RelativePath { get; }

    public ReadOnlyMemory<byte> Bytes => bytes;

    public ContentDigest ContentDigest => ContentDigest.Compute(bytes);

    public IReadOnlyList<string> DependsOnArtifactIds { get; }
}

/// <summary>Offline derivation of sparse multi-level navigation supports from normalized collision triangles.</summary>
public static class OfflineNavigationDeriver
{
    private const float EdgeTolerance = 0.0001F;
    private const float ParallelTolerance = 0.00001F;

    public static NormalizedNavigationSurface Derive(
        string id,
        string artifactId,
        IReadOnlyList<NormalizedMesh> meshes,
        NavigationDerivationConfig config)
    {
        NormalizedImportDocument.RequireLogicalId(id, nameof(id));
        NormalizedImportDocument.RequireLogicalId(artifactId, nameof(artifactId));
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        CollisionTriangle[] collision = meshes
            .OrderBy(mesh => mesh.Id, StringComparer.Ordinal)
            .SelectMany(CollisionTriangles)
            .ToArray();
        if (collision.Length == 0)
        {
            return new(id, artifactId, config, []);
        }

        float minimumUp = MathF.Cos(config.MaximumSlopeDegrees * (MathF.PI / 180F));
        Dictionary<(int Column, int Row, int Level), float> supports = [];
        foreach (CollisionTriangle triangle in collision)
        {
            if (NormalY(triangle) < minimumUp)
            {
                continue;
            }

            (int minColumn, int maxColumn, int minRow, int maxRow) = CoveredCells(triangle, config.CellSize);
            for (int column = minColumn; column <= maxColumn; column++)
            {
                for (int row = minRow; row <= maxRow; row++)
                {
                    float x = (column + 0.5F) * config.CellSize;
                    float z = (row + 0.5F) * config.CellSize;
                    if (!TryHeightAt(triangle, x, z, out float supportHeight))
                    {
                        continue;
                    }

                    int level = QuantizeLevel(supportHeight, config.LevelQuantum);
                    (int Column, int Row, int Level) key = (column, row, level);
                    if (!supports.TryGetValue(key, out float prior) || supportHeight < prior)
                    {
                        supports[key] = supportHeight;
                    }
                }
            }
        }

        // A city's supports and triangles can number in the hundreds of thousands. Restrict
        // each headroom query to triangles whose horizontal bounds cover its spatial bucket;
        // the exact intersection and height test below still decides the resulting artifact.
        float bucketSize = config.CellSize * 8F;
        Dictionary<(int X, int Z), List<CollisionTriangle>> headroomBuckets = BuildHeadroomBuckets(collision, bucketSize);
        NormalizedNavigationCell[] cells = supports
            .Where(candidate => HasHeadroom(candidate.Key.Column, candidate.Key.Row, candidate.Value,
                HeadroomCandidates(candidate.Key.Column, candidate.Key.Row), config)
                && HoldsFootprint((candidate.Key.Column + 0.5F) * config.CellSize, (candidate.Key.Row + 0.5F) * config.CellSize,
                    candidate.Value, config, Near))
            .OrderBy(candidate => candidate.Key.Column).ThenBy(candidate => candidate.Key.Row).ThenBy(candidate => candidate.Key.Level)
            .Select(candidate => new NormalizedNavigationCell(candidate.Key.Column, candidate.Key.Row, candidate.Key.Level, candidate.Value, true))
            .ToArray();
        return new(id, artifactId, config, cells);

        IReadOnlyList<CollisionTriangle> HeadroomCandidates(int column, int row)
        {
            int x = CellCoordinate(MathF.Floor((column + 0.5F) * config.CellSize / bucketSize));
            int z = CellCoordinate(MathF.Floor((row + 0.5F) * config.CellSize / bucketSize));
            return headroomBuckets.TryGetValue((x, z), out List<CollisionTriangle>? triangles) ? triangles : [];
        }

        // The triangles whose horizontal bounds may cover any point between two corners: almost always one bucket's.
        IReadOnlyList<CollisionTriangle> Near(float firstX, float firstZ, float lastX, float lastZ)
        {
            int fromX = CellCoordinate(MathF.Floor(MathF.Min(firstX, lastX) / bucketSize));
            int toX = CellCoordinate(MathF.Floor(MathF.Max(firstX, lastX) / bucketSize));
            int fromZ = CellCoordinate(MathF.Floor(MathF.Min(firstZ, lastZ) / bucketSize));
            int toZ = CellCoordinate(MathF.Floor(MathF.Max(firstZ, lastZ) / bucketSize));
            if (fromX == toX && fromZ == toZ)
                return headroomBuckets.TryGetValue((fromX, fromZ), out List<CollisionTriangle>? only) ? only : [];
            List<CollisionTriangle> union = [];
            for (int x = fromX; x <= toX; x++)
            for (int z = fromZ; z <= toZ; z++)
                if (headroomBuckets.TryGetValue((x, z), out List<CollisionTriangle>? triangles)) union.AddRange(triangles);
            return union;
        }
    }

    /// <summary>The four directions a footprint is crossed in: the axes and the diagonals.</summary>
    private static readonly (float X, float Z)[] FootprintDirections =
        [(1F, 0F), (0F, 1F), (MathF.Sqrt(0.5F), MathF.Sqrt(0.5F)), (MathF.Sqrt(0.5F), -MathF.Sqrt(0.5F))];

    /// <summary>
    /// Whether an actor standing at a cell's centre stands on its support rather than balancing across it. A walkable
    /// face can be narrower than any actor: Privateer's Hold's first room has a pile of logs whose tops are ridges of
    /// faces within the slope limit, 0.1 to 0.3 m across, with steeper faces either side, and a support at a cell's
    /// centre there sent routes along a log the player slides off. So across each of four directions the ground
    /// within the footprint must not fall away on both sides. A side holds where up-facing ground at the footprint's
    /// edge lies within what a walkable slope rises or falls over it, or where collision crosses at that rise (a wall
    /// or a riser): a step down on one side and a wall on the other is a stair beside a wall, not a ridge.
    /// </summary>
    private static bool HoldsFootprint(float x, float z, float support, NavigationDerivationConfig config,
        Func<float, float, float, float, IReadOnlyList<CollisionTriangle>> near)
    {
        float reach = config.FootprintRadius;
        float rise = (reach * MathF.Tan(config.MaximumSlopeDegrees * (MathF.PI / 180F))) + config.SupportProbeDrop;
        foreach ((float dx, float dz) in FootprintDirections)
        {
            if (FallsAway(dx, dz) && FallsAway(-dx, -dz)) return false;
        }

        return true;

        bool FallsAway(float dx, float dz)
        {
            float edgeX = x + (dx * reach), edgeZ = z + (dz * reach);
            foreach (CollisionTriangle triangle in near(edgeX, edgeZ, edgeX, edgeZ))
            {
                if (NormalY(triangle) > 0F && TryHeightAt(triangle, edgeX, edgeZ, out float ground) && MathF.Abs(ground - support) <= rise)
                    return false;
            }

            float y = support + rise;
            foreach (CollisionTriangle triangle in near(x, z, edgeX, edgeZ))
            {
                if (CrossesLevel(triangle, x, z, edgeX, edgeZ, y)) return false;
            }

            return true;
        }
    }

    /// <summary>Whether the horizontal segment from (x, y, z) to (endX, y, endZ) meets the triangle (Möller–Trumbore).</summary>
    private static bool CrossesLevel(CollisionTriangle triangle, float x, float z, float endX, float endZ, float y)
    {
        float dx = endX - x, dz = endZ - z;
        float e1x = triangle.B.X - triangle.A.X, e1y = triangle.B.Y - triangle.A.Y, e1z = triangle.B.Z - triangle.A.Z;
        float e2x = triangle.C.X - triangle.A.X, e2y = triangle.C.Y - triangle.A.Y, e2z = triangle.C.Z - triangle.A.Z;
        // p = d × e2, with d = (dx, 0, dz).
        float px = -dz * e2y, py = (dz * e2x) - (dx * e2z), pz = dx * e2y;
        float determinant = (e1x * px) + (e1y * py) + (e1z * pz);
        if (MathF.Abs(determinant) <= ParallelTolerance * ParallelTolerance) return false;
        float sx = x - triangle.A.X, sy = y - triangle.A.Y, sz = z - triangle.A.Z;
        float u = ((sx * px) + (sy * py) + (sz * pz)) / determinant;
        if (u < -EdgeTolerance || u > 1F + EdgeTolerance) return false;
        // q = s × e1.
        float qx = (sy * e1z) - (sz * e1y), qy = (sz * e1x) - (sx * e1z), qz = (sx * e1y) - (sy * e1x);
        float v = ((dx * qx) + (dz * qz)) / determinant;
        if (v < -EdgeTolerance || u + v > 1F + EdgeTolerance) return false;
        float along = ((e2x * qx) + (e2y * qy) + (e2z * qz)) / determinant;
        return along is >= 0F and <= 1F;
    }

    private static Dictionary<(int X, int Z), List<CollisionTriangle>> BuildHeadroomBuckets(
        IReadOnlyList<CollisionTriangle> triangles, float bucketSize)
    {
        Dictionary<(int X, int Z), List<CollisionTriangle>> buckets = [];
        foreach (CollisionTriangle triangle in triangles)
        {
            float minX = MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X));
            float maxX = MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X));
            float minZ = MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z));
            float maxZ = MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z));
            // TryHeightAt permits each barycentric coordinate to extend by EdgeTolerance.
            // Two negative coordinates can extend the query beyond either bounding edge.
            float marginX = (maxX - minX) * (2F * EdgeTolerance);
            float marginZ = (maxZ - minZ) * (2F * EdgeTolerance);
            int firstX = CellCoordinate(MathF.Floor((minX - marginX) / bucketSize));
            int lastX = CellCoordinate(MathF.Floor((maxX + marginX) / bucketSize));
            int firstZ = CellCoordinate(MathF.Floor((minZ - marginZ) / bucketSize));
            int lastZ = CellCoordinate(MathF.Floor((maxZ + marginZ) / bucketSize));
            for (int x = firstX; x <= lastX; x++)
            for (int z = firstZ; z <= lastZ; z++)
            {
                if (!buckets.TryGetValue((x, z), out List<CollisionTriangle>? bucket))
                    buckets.Add((x, z), bucket = []);
                bucket.Add(triangle);
            }
        }
        return buckets;
    }

    private static IEnumerable<CollisionTriangle> CollisionTriangles(NormalizedMesh mesh)
    {
        mesh.Validate();
        foreach (NormalizedMaterialGroup group in mesh.MaterialGroups.Where(group => group.ParticipatesInCollision))
        {
            int end = checked(group.StartTriangle + group.TriangleCount);
            for (int index = group.StartTriangle; index < end; index++)
            {
                NormalizedTriangle indices = mesh.Triangles[index];
                yield return new(mesh.Vertices[indices.FirstVertex], mesh.Vertices[indices.SecondVertex], mesh.Vertices[indices.ThirdVertex]);
            }
        }
    }

    private static (int MinColumn, int MaxColumn, int MinRow, int MaxRow) CoveredCells(CollisionTriangle triangle, float cellSize) =>
        (CellCoordinate(MathF.Floor(MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X)) / cellSize)),
         CellCoordinate(MathF.Floor(MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X)) / cellSize)),
         CellCoordinate(MathF.Floor(MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z)) / cellSize)),
         CellCoordinate(MathF.Floor(MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z)) / cellSize)));

    private static int CellCoordinate(float value)
    {
        if (!float.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new InvalidOperationException("Collision geometry lies outside the supported signed navigation grid range.");
        }

        return (int)value;
    }

    private static int QuantizeLevel(float height, float quantum)
    {
        float quantized = MathF.Round(height / quantum, MidpointRounding.AwayFromZero);
        if (!float.IsFinite(quantized) || quantized < int.MinValue || quantized > int.MaxValue)
        {
            throw new InvalidOperationException("Collision support height lies outside the supported navigation level range.");
        }

        return (int)quantized;
    }

    private static bool HasHeadroom(int column, int row, float supportHeight, IReadOnlyList<CollisionTriangle> collision, NavigationDerivationConfig config)
    {
        float x = (column + 0.5F) * config.CellSize;
        float z = (row + 0.5F) * config.CellSize;
        float minimum = supportHeight + config.SupportProbeDrop;
        float maximum = supportHeight + config.RequiredHeadroom;
        foreach (CollisionTriangle triangle in collision)
        {
            if (TryHeightAt(triangle, x, z, out float intersection)
                && intersection > minimum + EdgeTolerance
                && intersection < maximum - EdgeTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static float NormalY(CollisionTriangle triangle)
    {
        float abx = triangle.B.X - triangle.A.X;
        float aby = triangle.B.Y - triangle.A.Y;
        float abz = triangle.B.Z - triangle.A.Z;
        float acx = triangle.C.X - triangle.A.X;
        float acy = triangle.C.Y - triangle.A.Y;
        float acz = triangle.C.Z - triangle.A.Z;
        float y = (abz * acx) - (abx * acz);
        float length = MathF.Sqrt(
            ((aby * acz) - (abz * acy)) * ((aby * acz) - (abz * acy))
            + (y * y)
            + ((abx * acy) - (aby * acx)) * ((abx * acy) - (aby * acx)));
        return length <= ParallelTolerance ? 0F : y / length;
    }

    private static bool TryHeightAt(CollisionTriangle triangle, float x, float z, out float height)
    {
        float denominator = ((triangle.B.Z - triangle.C.Z) * (triangle.A.X - triangle.C.X))
            + ((triangle.C.X - triangle.B.X) * (triangle.A.Z - triangle.C.Z));
        if (MathF.Abs(denominator) <= ParallelTolerance)
        {
            height = default;
            return false;
        }

        float first = (((triangle.B.Z - triangle.C.Z) * (x - triangle.C.X))
            + ((triangle.C.X - triangle.B.X) * (z - triangle.C.Z))) / denominator;
        float second = (((triangle.C.Z - triangle.A.Z) * (x - triangle.C.X))
            + ((triangle.A.X - triangle.C.X) * (z - triangle.C.Z))) / denominator;
        float third = 1F - first - second;
        if (first < -EdgeTolerance || second < -EdgeTolerance || third < -EdgeTolerance)
        {
            height = default;
            return false;
        }

        height = (first * triangle.A.Y) + (second * triangle.B.Y) + (third * triangle.C.Y);
        if (!float.IsFinite(height)) return false;
        // The support lies on the triangle, so within its corners' heights. Float rounding of the weights (or a centre
        // just past an edge, inside the tolerance) can put it a hair outside them: a flat floor at 12.8 would publish a
        // 12.799999 support below the artifact's bounds, and the Engine refuses such an artifact when it is admitted.
        height = Math.Clamp(height, MathF.Min(triangle.A.Y, MathF.Min(triangle.B.Y, triangle.C.Y)),
            MathF.Max(triangle.A.Y, MathF.Max(triangle.B.Y, triangle.C.Y)));
        return true;
    }

    private readonly record struct CollisionTriangle(NormalizedVector3 A, NormalizedVector3 B, NormalizedVector3 C);
}

/// <summary>The importer's static meshes, published in the Engine's binary static mesh form.</summary>
internal static class StaticMeshArtifact
{
    public static byte[] Serialize(string asset, NormalizedBounds bounds, MeshAssembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return StaticMeshBinary.Write(new(
            asset,
            bounds,
            assembly.Vertices,
            assembly.Normals,
            assembly.Uvs,
            assembly.Indices,
            assembly.Groups,
            [.. assembly.MaterialSlots.Select(binding => new StaticMeshMaterialBinding(binding.Material, binding.Slot))]));
    }
}

/// <summary>The importer's collision and navigation, published in the Engine's binary Spatial content artifact form.</summary>
internal static class CollisionNavigationArtifact
{
    public static byte[] Serialize(string staticMeshArtifactId, NormalizedBounds bounds, IReadOnlyList<NormalizedMesh> meshes, NormalizedNavigationSurface navigation)
    {
        (List<NormalizedVector3> positions, List<SpatialArtifactTriangle> triangles) = Weld(MeshAssembly.Create(meshes, collisionOnly: true));
        NavigationDerivationConfig config = navigation.Config;
        return SpatialArtifactBinary.Write(new(
            staticMeshArtifactId,
            Position(bounds.Minimum),
            Position(bounds.Maximum),
            [.. positions.Select(Position)],
            triangles,
            navigation.Id,
            new(Decimal(config.CellSize), Decimal(config.LevelQuantum), Decimal(config.MaximumSlopeDegrees), Decimal(config.RequiredHeadroom), Decimal(config.SupportProbeDrop)),
            [.. navigation.Cells.OrderBy(cell => cell.Column).ThenBy(cell => cell.Row).ThenBy(cell => cell.Level)
                .Select(cell => new SpatialArtifactCell(cell.Column, cell.Row, cell.Level, Decimal(cell.SupportHeight), cell.Walkable))]));
    }

    private static SpatialArtifactPosition Position(NormalizedVector3 value) => new(Decimal(value.X), Decimal(value.Y), Decimal(value.Z));

    /// <summary>
    /// The double a float's shortest round-trip decimal names (0.1F is 0.1, not 0.100000001490116). The
    /// artifact's f64 facts are the importer's f32 values as decimals state them, the values the JSON
    /// form carried, so both forms of one import admit the same collision and navigation.
    /// </summary>
    private static double Decimal(float value)
    {
        Span<char> text = stackalloc char[32];
        if (!value.TryFormat(text, out int written, provider: CultureInfo.InvariantCulture))
            throw new InvalidOperationException("A float did not format as a decimal.");
        return double.Parse(text[..written], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Collision needs each position once, not once per rendered face as the visual assembly carries it
    /// for its normals and UVs: a town's collision shares about two thirds of its vertices. Welding exact
    /// equal positions keeps the geometry identical. A triangle whose corners weld together had zero area
    /// and the Engine refuses repeated indices, so it is dropped.
    /// </summary>
    private static (List<NormalizedVector3> Positions, List<SpatialArtifactTriangle> Triangles) Weld(MeshAssembly collision)
    {
        Dictionary<(float X, float Y, float Z), uint> indexByPosition = [];
        List<NormalizedVector3> positions = [];
        uint[] welded = new uint[collision.Vertices.Count];
        for (int vertex = 0; vertex < collision.Vertices.Count; vertex++)
        {
            NormalizedVector3 value = collision.Vertices[vertex];
            // Negative zero is the same position as zero.
            (float X, float Y, float Z) key = (value.X + 0F, value.Y + 0F, value.Z + 0F);
            if (!indexByPosition.TryGetValue(key, out uint index))
            {
                index = checked((uint)positions.Count);
                indexByPosition.Add(key, index);
                positions.Add(new NormalizedVector3(key.X, key.Y, key.Z));
            }
            welded[vertex] = index;
        }

        List<SpatialArtifactTriangle> triangles = new(collision.Indices.Count / 3);
        for (int index = 0; index < collision.Indices.Count; index += 3)
        {
            uint a = welded[collision.Indices[index]], b = welded[collision.Indices[index + 1]], c = welded[collision.Indices[index + 2]];
            if (a != b && b != c && a != c) triangles.Add(new(a, b, c));
        }
        // The Engine admits positions only with triangles that use them.
        if (triangles.Count == 0) positions.Clear();
        return (positions, triangles);
    }
}

internal sealed class MeshAssembly
{
    private MeshAssembly(
        IReadOnlyList<NormalizedVector3> vertices,
        IReadOnlyList<NormalizedVector3> normals,
        IReadOnlyList<NormalizedVector2> uvs,
        IReadOnlyList<uint> indices,
        IReadOnlyList<StaticMeshGroup> groups,
        IReadOnlyList<(string Material, int Slot)> materialSlots)
    {
        Vertices = vertices;
        Normals = normals;
        Uvs = uvs;
        Indices = indices;
        Groups = groups;
        MaterialSlots = materialSlots;
    }

    public IReadOnlyList<NormalizedVector3> Vertices { get; }
    public IReadOnlyList<NormalizedVector3> Normals { get; }
    public IReadOnlyList<NormalizedVector2> Uvs { get; }
    public IReadOnlyList<uint> Indices { get; }
    public IReadOnlyList<StaticMeshGroup> Groups { get; }
    public IReadOnlyList<(string Material, int Slot)> MaterialSlots { get; }

    public static MeshAssembly Create(IReadOnlyList<NormalizedMesh> meshes, bool collisionOnly = false)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        NormalizedMesh[] orderedMeshes = meshes.OrderBy(mesh => mesh.Id, StringComparer.Ordinal).ToArray();
        foreach (NormalizedMesh mesh in orderedMeshes) mesh.Validate();
        string[] materials = [.. orderedMeshes.SelectMany(mesh => mesh.MaterialGroups)
            .Where(group => collisionOnly ? group.ParticipatesInCollision : !group.CollisionOnly)
            .Select(group => group.MaterialResourceId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(material => material, StringComparer.Ordinal)];
        if (materials.Length == 0 && !collisionOnly)
            throw new ArgumentException("A static mesh draws at least one material.", nameof(meshes));
        Dictionary<string, int> slots = materials.Select((material, slot) => (material, slot)).ToDictionary(value => value.material, value => value.slot, StringComparer.Ordinal);
        List<NormalizedVector3> vertices = [];
        List<NormalizedVector3> normals = [];
        List<NormalizedVector2> uvs = [];
        List<uint> indices = [];
        List<StaticMeshGroup> groups = [];
        foreach (NormalizedMesh mesh in orderedMeshes)
        {
            NormalizedMaterialGroup[] selectedGroups = mesh.MaterialGroups
                .Where(group => collisionOnly ? group.ParticipatesInCollision : !group.CollisionOnly)
                .OrderBy(group => group.StartTriangle)
                .ToArray();
            if (selectedGroups.Length == 0)
            {
                continue;
            }

            int vertexOffset = vertices.Count;
            vertices.AddRange(mesh.Vertices);
            normals.AddRange(mesh.Normals);
            uvs.AddRange(mesh.TextureCoordinates);
            foreach (NormalizedMaterialGroup group in selectedGroups)
            {
                int start = indices.Count;
                int end = checked(group.StartTriangle + group.TriangleCount);
                for (int triangleIndex = group.StartTriangle; triangleIndex < end; triangleIndex++)
                {
                    NormalizedTriangle triangle = mesh.Triangles[triangleIndex];
                    indices.Add(checked((uint)(vertexOffset + triangle.FirstVertex)));
                    indices.Add(checked((uint)(vertexOffset + triangle.SecondVertex)));
                    indices.Add(checked((uint)(vertexOffset + triangle.ThirdVertex)));
                }

                groups.Add(new(slots[group.MaterialResourceId], start, checked(indices.Count - start)));
            }
        }

        return new(
            vertices,
            normals,
            uvs,
            indices,
            groups,
            materials.Select((material, slot) => (material, slot)).ToArray());
    }
}
