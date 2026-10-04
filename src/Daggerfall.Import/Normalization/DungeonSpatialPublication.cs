using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

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
        Id,
        RelativePath,
        ContentDigest,
        bytes.Length,
        DependsOnArtifactIds);
}

/// <summary>
/// One static-mesh material binding. The slot is published from the same mesh
/// assembly that writes the Engine static-mesh artifact; runtime consumers must
/// not derive it by parsing that artifact or repeating its ordering rule.
/// </summary>
public sealed record DungeonMaterialSlot(string MaterialResourceId, uint Slot);

/// <summary>One action-door visual emitted apart from the immutable world mesh.</summary>
public sealed record DungeonDoorVisual(string DoorId, GeneratedSpatialArtifact Artifact, IReadOnlyList<DungeonMaterialSlot> MaterialSlots);

/// <summary>One action-model visual emitted in model-local coordinates.</summary>
public sealed record DungeonActionModelVisual(string ActionId, GeneratedSpatialArtifact Artifact, IReadOnlyList<DungeonMaterialSlot> MaterialSlots);

/// <summary>
/// Deterministic spatial closure for one normalized location.  The static mesh
/// is shaped exactly for Engine's content-backed static-mesh admission while
/// collision and navigation remain purpose-neutral offline facts. The
/// collision/navigation artifact is the navigation surface's one published
/// home; <see cref="Navigation"/> is the surface it was written from.
/// </summary>
public sealed record DungeonSpatialPublication(
    GeneratedSpatialArtifact StaticMesh,
    GeneratedSpatialArtifact CollisionNavigation,
    GeneratedSpatialArtifact ResourceCatalog,
    IReadOnlyList<DungeonMaterialSlot> MaterialSlots,
    IReadOnlyList<DungeonDoorVisual> DoorVisuals,
    IReadOnlyList<DungeonActionModelVisual> ActionModelVisuals,
    NormalizedNavigationSurface Navigation)
{
    public IReadOnlyList<GeneratedSpatialArtifact> Artifacts
    {
        get
        {
            List<GeneratedSpatialArtifact> artifacts = [StaticMesh, CollisionNavigation, ResourceCatalog];
            HashSet<string> seen = artifacts.Select(artifact => artifact.Id).ToHashSet(StringComparer.Ordinal);
            foreach (DungeonActionModelVisual visual in ActionModelVisuals.OrderBy(value => value.ActionId, StringComparer.Ordinal))
                if (seen.Add(visual.Artifact.Id)) artifacts.Add(visual.Artifact);
            foreach (DungeonDoorVisual visual in DoorVisuals.OrderBy(value => value.DoorId, StringComparer.Ordinal))
                if (seen.Add(visual.Artifact.Id)) artifacts.Add(visual.Artifact);
            return artifacts;
        }
    }

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
        navigation.Validate(new HashSet<string>([collisionNavigationArtifactId], StringComparer.Ordinal));
        if (!StringComparer.Ordinal.Equals(world.NavigationId, navigation.Id))
        {
            throw new InvalidOperationException($"World navigation '{world.NavigationId}' does not identify the published navigation surface '{navigation.Id}'.");
        }

        Dictionary<string, NormalizedMesh> meshById = meshes.ToDictionary(mesh => mesh.Id, StringComparer.Ordinal);
        HashSet<string> worldMeshIds = world.MeshIds.ToHashSet(StringComparer.Ordinal);
        if (meshById.Count != meshes.Count || worldMeshIds.Count != world.MeshIds.Count || meshById.Count != worldMeshIds.Count || !meshById.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(worldMeshIds))
        {
            throw new InvalidOperationException("Generated spatial publication meshes must exactly match the world visual mesh collection.");
        }

        NormalizedMesh[] worldMeshes = world.MeshIds.Select(meshId => meshById[meshId]).ToArray();
        Dictionary<string, NormalizedDoorPlacement> doorsByMeshId = [];
        foreach (NormalizedDoorPlacement door in world.Doors)
            foreach (string meshId in door.VisualMeshIds)
                if (!doorsByMeshId.TryAdd(meshId, door)) throw new InvalidOperationException($"Action visual mesh '{meshId}' belongs to more than one door.");
        HashSet<string> actionMeshIds = world.ActionModels.SelectMany(model => model.MeshIds).ToHashSet(StringComparer.Ordinal);
        HashSet<string> movableMeshIds = doorsByMeshId.Keys.Concat(actionMeshIds).ToHashSet(StringComparer.Ordinal);
        string[] staticMeshIds = world.StaticMeshIds?.ToArray()
            ?? worldMeshes.Select(mesh => mesh.Id).Where(meshId => !movableMeshIds.Contains(meshId)).ToArray();
        if (staticMeshIds.Intersect(movableMeshIds, StringComparer.Ordinal).Any()
            || !staticMeshIds.Concat(movableMeshIds).ToHashSet(StringComparer.Ordinal).SetEquals(worldMeshIds))
        {
            throw new InvalidOperationException("Generated static and movable mesh admissions must partition the normalized world mesh collection.");
        }
        NormalizedMesh[] staticMeshes = staticMeshIds.Select(meshId => meshById[meshId]).ToArray();

        foreach (NormalizedResourceCatalogEntry resource in resources)
        {
            resource.Validate();
            if (!StringComparer.Ordinal.Equals(resource.ArtifactId, resourceCatalogArtifactId))
            {
                throw new InvalidOperationException("Generated resource metadata must identify its generated resource catalog, never a source archive or unrelated artifact.");
            }
        }

        string[] materialUniverse = worldMeshes.SelectMany(mesh => mesh.MaterialGroups)
            .Select(group => group.MaterialResourceId).Distinct(StringComparer.Ordinal).OrderBy(material => material, StringComparer.Ordinal).ToArray();
        MeshAssembly assembly = MeshAssembly.Create(staticMeshes, materialUniverse: materialUniverse);
        DungeonMaterialSlot[] materialSlots = assembly.MaterialSlots
            .Select(binding => new DungeonMaterialSlot(binding.Material, checked((uint)binding.Slot)))
            .ToArray();
        if (materialSlots.Length != materialSlots.Select(binding => binding.MaterialResourceId).Distinct(StringComparer.Ordinal).Count()
            || materialSlots.Length != materialSlots.Select(binding => binding.Slot).Distinct().Count())
        {
            throw new InvalidOperationException("Generated static-mesh material slots must be unique by resource and slot.");
        }

        // A valid RDB can contain only action-door visual geometry. Its static bundle is intentionally
        // empty; the world bounds remain the truthful content bounds for that inline asset.
        NormalizedBounds staticBounds = staticMeshes.Length == 0 ? bounds : MeshGeometry.Bounds(staticMeshes.SelectMany(mesh => mesh.Vertices).ToArray());
        byte[] staticMesh = StaticMeshArtifact.Serialize(visualMeshAssetId, staticBounds, assembly);
        byte[] collisionNavigation = CollisionNavigationArtifact.Serialize(staticMeshArtifactId, bounds, staticMeshes, navigation);
        byte[] resourceCatalog = ResourceCatalogJson.Serialize(resources);
        string directory = staticMeshRelativePath[..staticMeshRelativePath.LastIndexOf('/')];
        List<DungeonActionModelVisual> actionVisuals = [];
        Dictionary<string, DungeonActionModelVisual> actionVisualsById = new(StringComparer.Ordinal);
        foreach (NormalizedActionModelPlacement model in world.ActionModels.OrderBy(value => value.ActionId, StringComparer.Ordinal))
        {
            NormalizedMesh[] localMeshes = model.MeshIds.Select(meshId => meshById[meshId]).ToArray();
            // The world's material universe, as door visuals use: each local slot is the world slot of its
            // material, so a consumer binds the visual from the normalized meshes without reading its bytes.
            MeshAssembly localAssembly = MeshAssembly.Create(localMeshes, materialUniverse: materialUniverse);
            string fileName = ActionArtifactFileName(model.ActionId);
            GeneratedSpatialArtifact artifact = new(
                model.VisualArtifactId,
                $"{directory}/actions/{fileName}{StaticMeshBinary.Extension}",
                StaticMeshArtifact.Serialize(
                    $"mesh/action/{fileName}",
                    model.LocalBounds,
                    localAssembly),
                []);
            DungeonActionModelVisual visual = new(model.ActionId, artifact, localAssembly.MaterialSlots
                .Select(binding => new DungeonMaterialSlot(binding.Material, checked((uint)binding.Slot))).ToArray());
            actionVisuals.Add(visual);
            actionVisualsById.Add(model.ActionId, visual);
        }

        List<DungeonDoorVisual> doorVisuals = [];
        foreach (NormalizedDoorPlacement door in world.Doors.OrderBy(door => door.Id, StringComparer.Ordinal))
        {
            NormalizedActionModelPlacement? actionModel = world.ActionModels.SingleOrDefault(model => StringComparer.Ordinal.Equals(model.DoorId, door.Id));
            if (actionModel is not null)
            {
                DungeonActionModelVisual sharedVisual = actionVisualsById[actionModel.ActionId];
                doorVisuals.Add(new(door.Id, sharedVisual.Artifact, sharedVisual.MaterialSlots));
                continue;
            }

            NormalizedMesh[] localMeshes = door.VisualMeshIds.Select(meshId => Localize(meshById[meshId], door)).ToArray();
            MeshAssembly doorAssembly = MeshAssembly.Create(localMeshes, materialUniverse: materialUniverse);
            string suffix = door.Id["door/".Length..].Replace('/', '-');
            string artifactId = $"{staticMeshArtifactId}/door/{suffix}";
            string relativePath = $"{directory}/doors/{suffix}{StaticMeshBinary.Extension}";
            GeneratedSpatialArtifact artifact = new(artifactId, relativePath,
                StaticMeshArtifact.Serialize($"mesh/{door.Id}", MeshGeometry.Bounds(localMeshes.SelectMany(mesh => mesh.Vertices).ToArray()), doorAssembly), []);
            doorVisuals.Add(new DungeonDoorVisual(door.Id, artifact, doorAssembly.MaterialSlots
                .Select(binding => new DungeonMaterialSlot(binding.Material, checked((uint)binding.Slot))).ToArray()));
        }
        return new(
            new GeneratedSpatialArtifact(staticMeshArtifactId, staticMeshRelativePath, staticMesh, []),
            new GeneratedSpatialArtifact(collisionNavigationArtifactId, collisionNavigationRelativePath, collisionNavigation, [staticMeshArtifactId]),
            new GeneratedSpatialArtifact(resourceCatalogArtifactId, resourceCatalogRelativePath, resourceCatalog, []),
            Array.AsReadOnly(materialSlots),
            doorVisuals,
            actionVisuals,
            navigation.Canonicalize());
    }

    private static string ActionArtifactFileName(string actionId)
    {
        const string prefix = "action/";
        if (!actionId.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException($"Action model ID '{actionId}' must use the normalized action namespace.", nameof(actionId));
        string suffix = actionId[prefix.Length..].Replace('/', '-');
        if (suffix.Length == 0 || suffix.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException($"Action model ID '{actionId}' cannot form a generated artifact filename.", nameof(actionId));
        return suffix.ToLowerInvariant();
    }

    private static NormalizedMesh Localize(NormalizedMesh mesh, NormalizedDoorPlacement door)
    {
        if (door.RotationDegrees.X != 0F || door.RotationDegrees.Z != 0F)
            throw new InvalidOperationException($"Action door '{door.Id}' has a non-yaw rotation that the static-mesh door publisher cannot represent.");
        float radians = -door.RotationDegrees.Y * (MathF.PI / 180F);
        float sine = MathF.Sin(radians), cosine = MathF.Cos(radians);
        NormalizedVector3 Convert(NormalizedVector3 value, bool normal)
        {
            float x = value.X - (normal ? 0F : door.Position.X);
            float y = value.Y - (normal ? 0F : door.Position.Y);
            float z = value.Z - (normal ? 0F : door.Position.Z);
            return new((x * cosine) - (z * sine), y, (x * sine) + (z * cosine));
        }
        return mesh with { Vertices = mesh.Vertices.Select(value => Convert(value, false)).ToArray(), Normals = mesh.Normals.Select(value => Convert(value, true)).ToArray() };
    }

    public void ValidateAgainst(NormalizedImportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        if (!StringComparer.Ordinal.Equals(document.World.NavigationId, Navigation.Id))
        {
            throw new InvalidOperationException($"World navigation '{document.World.NavigationId}' does not identify the published navigation surface '{Navigation.Id}'.");
        }
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
                HeadroomCandidates(candidate.Key.Column, candidate.Key.Row), config))
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
        return float.IsFinite(height);
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

/// <summary>
/// A generated metadata catalog. It records resource identity, dependencies,
/// and sprite-frame facts only; it deliberately does not pretend that source
/// texture pixels or audio bytes have been published by the spatial slice.
/// </summary>
internal static class ResourceCatalogJson
{
    private static readonly JsonSerializerOptions Options = PublishedJson.Section;

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
            new ResourceCatalogDocument(canonical),
            Options);
        return [.. bytes, (byte)'\n'];
    }

    private sealed record ResourceCatalogDocument(IReadOnlyList<NormalizedResourceCatalogEntry> Resources)
    {
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

    public static MeshAssembly Create(IReadOnlyList<NormalizedMesh> meshes, bool collisionOnly = false, IReadOnlyList<string>? materialUniverse = null)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        NormalizedMesh[] orderedMeshes = meshes.OrderBy(mesh => mesh.Id, StringComparer.Ordinal).ToArray();
        foreach (NormalizedMesh mesh in orderedMeshes) mesh.Validate();
        string[] materials = (materialUniverse ?? orderedMeshes.SelectMany(mesh => mesh.MaterialGroups)
            .Where(group => !collisionOnly || group.ParticipatesInCollision)
            .Select(group => group.MaterialResourceId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(material => material, StringComparer.Ordinal)
            .ToArray()).ToArray();
        if ((materials.Length == 0 && !collisionOnly) || materials.Distinct(StringComparer.Ordinal).Count() != materials.Length
            || !materials.SequenceEqual(materials.OrderBy(material => material, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new ArgumentException("A mesh material universe must be non-empty, unique, and sorted.", nameof(materialUniverse));
        Dictionary<string, int> slots = materials.Select((material, slot) => (material, slot)).ToDictionary(value => value.material, value => value.slot, StringComparer.Ordinal);
        List<NormalizedVector3> vertices = [];
        List<NormalizedVector3> normals = [];
        List<NormalizedVector2> uvs = [];
        List<uint> indices = [];
        List<StaticMeshGroup> groups = [];
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
