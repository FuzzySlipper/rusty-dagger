using System.Text.Json;
using System.Text.Json.Serialization;
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

    public NormalizedArtifactDescriptor ToDescriptor() => new(
        NormalizedArtifactDescriptor.CurrentSchemaVersion,
        Id,
        RelativePath,
        ContentDigest,
        bytes.Length,
        DependsOnArtifactIds);
}

/// <summary>
/// Deterministic spatial closure for one normalized location.  The static mesh
/// is shaped exactly for Engine's content-backed static-mesh admission while
/// collision and navigation remain purpose-neutral offline facts.
/// </summary>
public sealed record DungeonSpatialPublication(
    GeneratedSpatialArtifact StaticMesh,
    GeneratedSpatialArtifact CollisionNavigation,
    GeneratedSpatialArtifact ResourceCatalog)
{
    public IReadOnlyList<GeneratedSpatialArtifact> Artifacts => [StaticMesh, CollisionNavigation, ResourceCatalog];

    public IReadOnlyList<NormalizedArtifactDescriptor> ArtifactDescriptors => Artifacts
        .Select(artifact => artifact.ToDescriptor())
        .OrderBy(artifact => artifact.Id, StringComparer.Ordinal)
        .ToArray();

    public static DungeonSpatialPublication Create(
        string staticMeshArtifactId,
        string staticMeshRelativePath,
        string collisionNavigationArtifactId,
        string collisionNavigationRelativePath,
        string resourceCatalogArtifactId,
        string resourceCatalogRelativePath,
        string visualMeshAssetId,
        NormalizedBounds bounds,
        IReadOnlyList<NormalizedMesh> meshes,
        NormalizedWorld world,
        NormalizedNavigationSurface navigation,
        IReadOnlyList<NormalizedResourceCatalogEntry> resources)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(resources);
        bounds.Validate();
        NormalizedImportDocument.RequireLogicalId(staticMeshArtifactId, nameof(staticMeshArtifactId));
        NormalizedImportDocument.RequireLogicalId(collisionNavigationArtifactId, nameof(collisionNavigationArtifactId));
        NormalizedImportDocument.RequireLogicalId(resourceCatalogArtifactId, nameof(resourceCatalogArtifactId));
        NormalizedImportDocument.RequireLogicalId(visualMeshAssetId, nameof(visualMeshAssetId));
        if (!StringComparer.Ordinal.Equals(navigation.ArtifactId, collisionNavigationArtifactId))
        {
            throw new InvalidOperationException("The navigation surface must refer to the generated collision/navigation artifact.");
        }

        Dictionary<string, NormalizedMesh> meshById = meshes.ToDictionary(mesh => mesh.Id, StringComparer.Ordinal);
        HashSet<string> worldMeshIds = world.MeshIds.ToHashSet(StringComparer.Ordinal);
        if (meshById.Count != meshes.Count || worldMeshIds.Count != world.MeshIds.Count || meshById.Count != worldMeshIds.Count || !meshById.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(worldMeshIds))
        {
            throw new InvalidOperationException("Generated spatial publication meshes must exactly match the world visual mesh collection.");
        }

        NormalizedMesh[] worldMeshes = world.MeshIds.Select(meshId => meshById[meshId]).ToArray();
        if (worldMeshes.Any(mesh => !StringComparer.Ordinal.Equals(mesh.ArtifactId, staticMeshArtifactId)))
        {
            throw new InvalidOperationException("Every published visual mesh must refer to the generated static-mesh artifact.");
        }

        foreach (NormalizedResourceCatalogEntry resource in resources)
        {
            resource.Validate();
            if (!StringComparer.Ordinal.Equals(resource.ArtifactId, resourceCatalogArtifactId))
            {
                throw new InvalidOperationException("Generated resource metadata must identify its generated resource catalog, never a source archive or unrelated artifact.");
            }
        }

        byte[] staticMesh = StaticMeshJson.Serialize(visualMeshAssetId, bounds, worldMeshes);
        byte[] collisionNavigation = CollisionNavigationJson.Serialize(staticMeshArtifactId, bounds, worldMeshes, navigation);
        byte[] resourceCatalog = ResourceCatalogJson.Serialize(resources);
        return new(
            new GeneratedSpatialArtifact(staticMeshArtifactId, staticMeshRelativePath, staticMesh, []),
            new GeneratedSpatialArtifact(collisionNavigationArtifactId, collisionNavigationRelativePath, collisionNavigation, [staticMeshArtifactId]),
            new GeneratedSpatialArtifact(resourceCatalogArtifactId, resourceCatalogRelativePath, resourceCatalog, []));
    }

    public void ValidateAgainst(NormalizedImportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        foreach (GeneratedSpatialArtifact artifact in Artifacts)
        {
            NormalizedArtifactDescriptor descriptor = document.Artifacts.SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Id, artifact.Id))
                ?? throw new InvalidOperationException($"Normalized document does not describe generated spatial artifact '{artifact.Id}'.");
            if (!StringComparer.Ordinal.Equals(descriptor.RelativePath, artifact.RelativePath)
                || descriptor.ContentDigest != artifact.ContentDigest
                || descriptor.ByteLength != artifact.Bytes.Length
                || !descriptor.DependsOnArtifactIds.SequenceEqual(artifact.DependsOnArtifactIds, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Normalized document descriptor for '{artifact.Id}' does not match the generated bytes and dependency closure.");
            }
        }
    }
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
            return new(NormalizedNavigationSurface.CurrentSchemaVersion, id, artifactId, config, []);
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

        NormalizedNavigationCell[] cells = supports
            .Where(candidate => HasHeadroom(candidate.Key.Column, candidate.Key.Row, candidate.Value, collision, config))
            .OrderBy(candidate => candidate.Key.Column).ThenBy(candidate => candidate.Key.Row).ThenBy(candidate => candidate.Key.Level)
            .Select(candidate => new NormalizedNavigationCell(candidate.Key.Column, candidate.Key.Row, candidate.Key.Level, candidate.Value, true))
            .ToArray();
        return new(NormalizedNavigationSurface.CurrentSchemaVersion, id, artifactId, config, cells);
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
        return float.IsFinite(height);
    }

    private readonly record struct CollisionTriangle(NormalizedVector3 A, NormalizedVector3 B, NormalizedVector3 C);
}

internal static class StaticMeshJson
{
    public static byte[] Serialize(string asset, NormalizedBounds bounds, IReadOnlyList<NormalizedMesh> meshes)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        MeshAssembly assembly = MeshAssembly.Create(meshes);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("asset", asset);
            writer.WritePropertyName("payload");
            writer.WriteStartObject();
            writer.WritePropertyName("layout");
            writer.WriteStartObject();
            writer.WriteNumber("vertexCount", assembly.Vertices.Count);
            writer.WriteNumber("indexCount", assembly.Indices.Count);
            writer.WriteString("indexWidth", "u32");
            writer.WritePropertyName("attributes");
            writer.WriteStartArray();
            WriteAttribute(writer, "position", 3);
            WriteAttribute(writer, "normal", 3);
            WriteAttribute(writer, "uv", 2);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WritePropertyName("groups");
            writer.WriteStartArray();
            foreach (MeshAssemblyGroup group in assembly.Groups)
            {
                writer.WriteStartObject();
                writer.WriteNumber("materialSlot", group.MaterialSlot);
                writer.WriteNumber("start", group.Start);
                writer.WriteNumber("count", group.Count);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("bounds");
            WriteBounds(writer, bounds);
            writer.WritePropertyName("source");
            writer.WriteStartObject();
            writer.WriteString("kind", "inline");
            WriteVector3Stream(writer, "positions", assembly.Vertices);
            WriteVector3Stream(writer, "normals", assembly.Normals);
            WriteVector2Stream(writer, "uvs", assembly.Uvs);
            writer.WritePropertyName("indices");
            writer.WriteStartArray();
            foreach (uint index in assembly.Indices) writer.WriteNumberValue(index);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteString("provenance", "staticAsset");
            writer.WriteEndObject();
            writer.WritePropertyName("materialSlots");
            writer.WriteStartArray();
            foreach ((string material, int slot) in assembly.MaterialSlots)
            {
                writer.WriteStartObject();
                writer.WriteNumber("slot", slot);
                writer.WriteString("material", material);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("collision");
            writer.WriteStartObject();
            writer.WriteString("kind", "visualOnly");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return [.. stream.ToArray(), (byte)'\n'];
    }

    private static void WriteAttribute(Utf8JsonWriter writer, string name, int components)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteNumber("components", components);
        writer.WriteString("kind", "f32");
        writer.WriteEndObject();
    }

    internal static void WriteBounds(Utf8JsonWriter writer, NormalizedBounds bounds)
    {
        writer.WriteStartObject();
        WriteVector3(writer, "min", bounds.Minimum);
        WriteVector3(writer, "max", bounds.Maximum);
        writer.WriteEndObject();
    }

    internal static void WriteVector3(Utf8JsonWriter writer, string name, NormalizedVector3 value)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }

    private static void WriteVector3Stream(Utf8JsonWriter writer, string name, IReadOnlyList<NormalizedVector3> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (NormalizedVector3 value in values)
        {
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
        }
        writer.WriteEndArray();
    }

    private static void WriteVector2Stream(Utf8JsonWriter writer, string name, IReadOnlyList<NormalizedVector2> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (NormalizedVector2 value in values)
        {
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
        }
        writer.WriteEndArray();
    }
}

internal static class CollisionNavigationJson
{
    public static byte[] Serialize(string staticMeshArtifactId, NormalizedBounds bounds, IReadOnlyList<NormalizedMesh> meshes, NormalizedNavigationSurface navigation)
    {
        MeshAssembly collision = MeshAssembly.Create(meshes, collisionOnly: true);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("staticMeshArtifactId", staticMeshArtifactId);
            writer.WritePropertyName("bounds");
            StaticMeshJson.WriteBounds(writer, bounds);
            writer.WritePropertyName("collision");
            writer.WriteStartObject();
            WriteVector3Collection(writer, "positions", collision.Vertices);
            writer.WritePropertyName("triangles");
            writer.WriteStartArray();
            for (int index = 0; index < collision.Indices.Count; index += 3)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(collision.Indices[index]);
                writer.WriteNumberValue(collision.Indices[index + 1]);
                writer.WriteNumberValue(collision.Indices[index + 2]);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WritePropertyName("navigation");
            WriteNavigation(writer, navigation);
            writer.WriteEndObject();
        }

        return [.. stream.ToArray(), (byte)'\n'];
    }

    private static void WriteVector3Collection(Utf8JsonWriter writer, string name, IReadOnlyList<NormalizedVector3> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (NormalizedVector3 value in values)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }

    private static void WriteNavigation(Utf8JsonWriter writer, NormalizedNavigationSurface navigation)
    {
        writer.WriteStartObject();
        writer.WriteString("id", navigation.Id);
        writer.WritePropertyName("config");
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", navigation.Config.SchemaVersion);
        writer.WriteNumber("cellSize", navigation.Config.CellSize);
        writer.WriteNumber("levelQuantum", navigation.Config.LevelQuantum);
        writer.WriteNumber("maximumSlopeDegrees", navigation.Config.MaximumSlopeDegrees);
        writer.WriteNumber("requiredHeadroom", navigation.Config.RequiredHeadroom);
        writer.WriteNumber("supportProbeDrop", navigation.Config.SupportProbeDrop);
        writer.WriteEndObject();
        writer.WritePropertyName("cells");
        writer.WriteStartArray();
        foreach (NormalizedNavigationCell cell in navigation.Cells.OrderBy(cell => cell.Column).ThenBy(cell => cell.Row).ThenBy(cell => cell.Level))
        {
            writer.WriteStartObject();
            writer.WriteNumber("column", cell.Column);
            writer.WriteNumber("row", cell.Row);
            writer.WriteNumber("level", cell.Level);
            writer.WriteNumber("supportHeight", cell.SupportHeight);
            writer.WriteBoolean("walkable", cell.Walkable);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

/// <summary>
/// A generated metadata catalog. It records resource identity, dependencies,
/// and sprite-frame facts only; it deliberately does not pretend that source
/// texture pixels or audio bytes have been published by the spatial slice.
/// </summary>
internal static class ResourceCatalogJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static byte[] Serialize(IReadOnlyList<NormalizedResourceCatalogEntry> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        NormalizedResourceCatalogEntry[] canonical = resources
            .OrderBy(resource => resource.Id, StringComparer.Ordinal)
            .Select(resource => resource.Canonicalize())
            .ToArray();
        foreach (NormalizedResourceCatalogEntry resource in canonical)
        {
            resource.Validate();
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            new ResourceCatalogDocument(ResourceCatalogDocument.CurrentSchemaVersion, canonical),
            Options);
        return [.. bytes, (byte)'\n'];
    }

    private sealed record ResourceCatalogDocument(int SchemaVersion, IReadOnlyList<NormalizedResourceCatalogEntry> Resources)
    {
        public const int CurrentSchemaVersion = 1;
    }
}

internal sealed class MeshAssembly
{
    private MeshAssembly(
        IReadOnlyList<NormalizedVector3> vertices,
        IReadOnlyList<NormalizedVector3> normals,
        IReadOnlyList<NormalizedVector2> uvs,
        IReadOnlyList<uint> indices,
        IReadOnlyList<MeshAssemblyGroup> groups,
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
    public IReadOnlyList<MeshAssemblyGroup> Groups { get; }
    public IReadOnlyList<(string Material, int Slot)> MaterialSlots { get; }

    public static MeshAssembly Create(IReadOnlyList<NormalizedMesh> meshes, bool collisionOnly = false)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        NormalizedMesh[] orderedMeshes = meshes.OrderBy(mesh => mesh.Id, StringComparer.Ordinal).ToArray();
        foreach (NormalizedMesh mesh in orderedMeshes) mesh.Validate();
        string[] materials = orderedMeshes.SelectMany(mesh => mesh.MaterialGroups)
            .Where(group => !collisionOnly || group.ParticipatesInCollision)
            .Select(group => group.MaterialResourceId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(material => material, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, int> slots = materials.Select((material, slot) => (material, slot)).ToDictionary(value => value.material, value => value.slot, StringComparer.Ordinal);
        List<NormalizedVector3> vertices = [];
        List<NormalizedVector3> normals = [];
        List<NormalizedVector2> uvs = [];
        List<uint> indices = [];
        List<MeshAssemblyGroup> groups = [];
        foreach (NormalizedMesh mesh in orderedMeshes)
        {
            NormalizedMaterialGroup[] selectedGroups = mesh.MaterialGroups
                .Where(group => !collisionOnly || group.ParticipatesInCollision)
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

internal readonly record struct MeshAssemblyGroup(int MaterialSlot, int Start, int Count);
