using System.Globalization;
using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalization;

/// <summary>How a mesh's reference to a texture resolved.</summary>
public enum GeometryMaterialDisposition
{
    /// <summary>The texture archive is supplied and carries the record the mesh selects.</summary>
    Resolved,

    /// <summary>The corpus does not supply the texture archive the mesh selects.</summary>
    TextureNotSupplied,

    /// <summary>The texture archive is supplied but could not be parsed, so its records cannot be addressed.</summary>
    TextureMalformed,

    /// <summary>The archive is supplied and parsed, but it carries no such record.</summary>
    TextureRecordMissing,
}

/// <summary>One texture a mesh selects, and whether the corpus can serve it.</summary>
/// <param name="Archive">The texture archive the mesh's planes select.</param>
/// <param name="Record">The record within it.</param>
/// <param name="MaterialResourceId">The material resource the reference names, resolved or not.</param>
/// <param name="Disposition">Whether the corpus can serve it.</param>
/// <param name="Note">Why it cannot, empty when it can.</param>
public sealed record GeometryMaterialLink(
    ushort Archive,
    ushort Record,
    string MaterialResourceId,
    GeometryMaterialDisposition Disposition,
    string Note);

/// <summary>One published mesh artifact, keyed by the mesh number the source archive states.</summary>
/// <param name="SchemaVersion">Shape version of this record.</param>
/// <param name="MeshId">The mesh number, spelled as a decimal number.</param>
/// <param name="SourceRecordId">The number the archive indexes the record by.</param>
/// <param name="SourceOrdinal">The record's position in the archive directory.</param>
/// <param name="ArtifactId">The artifact's identity.</param>
/// <param name="RelativePath">Where the artifact is published relative to the content root.</param>
/// <param name="Vertices">How many vertices the artifact carries.</param>
/// <param name="Triangles">How many triangles it carries.</param>
/// <param name="Materials">The textures its planes select, in first-use order.</param>
/// <param name="ContentDigest">The artifact's content address.</param>
public sealed record GeometryMeshArtifact(
    int SchemaVersion,
    string MeshId,
    long SourceRecordId,
    int SourceOrdinal,
    string ArtifactId,
    string RelativePath,
    int Vertices,
    int Triangles,
    IReadOnlyList<GeometryMaterialLink> Materials,
    string ContentDigest);

/// <summary>A mesh number a normalized pack references that the archive cannot serve.</summary>
/// <param name="MeshId">The mesh number, spelled as the source spells it.</param>
/// <param name="Reason">Why the archive cannot serve it.</param>
public sealed record GeometryUnresolvedMeshReference(string MeshId, string Reason);

/// <summary>
/// How the published set relates to every record the inventory classifies: the four record classes
/// partition the archive, and the numbers a pack names that no record carries are counted beside them
/// because they are not records at all.
/// </summary>
/// <param name="Records">Every record the archive declares.</param>
/// <param name="Published">Records a normalized pack references and the archive serves.</param>
/// <param name="Unresolvable">Records a lookup reaches that cannot serve the number a pack names.</param>
/// <param name="Missing">Numbers a pack names that the archive carries no record for.</param>
/// <param name="Duplicate">Records carrying a number an earlier record already answers.</param>
/// <param name="Unused">Records a lookup reaches that no normalized pack references.</param>
public sealed record GeometryPublicationSummary(int Records, int Published, int Unresolvable, int Missing, int Duplicate, int Unused);

/// <summary>
/// The geometry a normalized dungeon or exterior pack references, published as one deterministic artifact
/// per mesh with the material references its planes select.
/// </summary>
/// <remarks>
/// <para>
/// This is the geometry side of the pack: the inventory says which mesh numbers exist and what the corpus
/// does with them, and this publishes the bytes of the ones a pack actually references, keyed by the number
/// the source archive states rather than by a name a placement invented. The mesh format stays with
/// <see cref="Arch3dDecoder"/>, the artifact container and mesh serialization stay with the established
/// offline publication, and nothing here builds a Unity mesh or a combined model: combining is the
/// consumer's business, and the donor's combiner is not ported.
/// </para>
/// <para>
/// A material reference that cannot be served is published as itself — the archive and record the mesh
/// selects, with the reason the corpus cannot serve it — and never replaced by another texture. The mesh
/// keeps its geometry either way, because the geometry is a fact about the mesh and the material is a fact
/// about the corpus.
/// </para>
/// </remarks>
public sealed record GeometryPublication(
    int SchemaVersion,
    string InventorySource,
    IReadOnlyList<GeneratedSpatialArtifact> Artifacts,
    IReadOnlyList<GeometryMeshArtifact> Meshes,
    IReadOnlyList<GeometryUnresolvedMeshReference> UnresolvedMeshes,
    IReadOnlyList<GeometryMaterialLink> UnresolvedMaterials,
    GeometryPublicationSummary Summary)
{
    /// <summary>Shape version this publication writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The artifact that lists what was published and what could not be.</summary>
    public const string IndexRelativePath = "geometry/index.json";

    /// <summary>The index artifact's identity.</summary>
    public const string IndexArtifactId = "geometry/index";

    /// <summary>Checks that every claim this publication makes about its own artifacts holds together.</summary>
    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Geometry publication schema must be {CurrentSchemaVersion} but is {SchemaVersion}.");
        }

        NormalizedImportDocument.RequireLogicalPath(InventorySource, nameof(InventorySource));
        ArgumentNullException.ThrowIfNull(Meshes);
        ArgumentNullException.ThrowIfNull(UnresolvedMeshes);
        ArgumentNullException.ThrowIfNull(UnresolvedMaterials);
        NormalizedImportDocument.ValidateUnique(Artifacts, artifact => artifact.Id, "geometry artifact");
        NormalizedImportDocument.ValidateUnique(Artifacts, artifact => artifact.RelativePath, "geometry artifact path");
        NormalizedImportDocument.ValidateUnique(Meshes, mesh => mesh.MeshId, "geometry mesh");
        NormalizedImportDocument.ValidateUnique(UnresolvedMeshes, mesh => mesh.MeshId, "unresolved mesh number");

        // The index is how a consumer learns what the publication holds, so it has to be there and it has
        // to describe the set the artifacts actually are.
        GeneratedSpatialArtifact index = Artifacts.SingleOrDefault(artifact => artifact.RelativePath == IndexRelativePath)
            ?? throw new InvalidOperationException($"Published geometry must carry its index at '{IndexRelativePath}'.");
        if (index.Id != IndexArtifactId)
        {
            throw new InvalidOperationException($"Published geometry index carries the identity '{index.Id}' where it is '{IndexArtifactId}'.");
        }

        Dictionary<string, GeneratedSpatialArtifact> byId = Artifacts.ToDictionary(artifact => artifact.Id, StringComparer.Ordinal);
        uint previous = 0;
        bool first = true;
        foreach (GeometryMeshArtifact mesh in Meshes)
        {
            if (!uint.TryParse(mesh.MeshId, NumberStyles.None, CultureInfo.InvariantCulture, out uint number))
            {
                throw new InvalidOperationException($"Published geometry mesh '{mesh.MeshId}' is not a mesh number.");
            }

            // Meshes are published in number order so the set reads the same however the pack listed them.
            if (!first && number <= previous)
            {
                throw new InvalidOperationException($"Published geometry mesh '{mesh.MeshId}' follows '{previous}' where the set is ordered by number.");
            }

            first = false;
            previous = number;
            if (mesh.SourceRecordId != number || mesh.SourceOrdinal < 0 || mesh.Vertices <= 0 || mesh.Triangles <= 0)
            {
                throw new InvalidOperationException($"Published geometry mesh '{mesh.MeshId}' states record {mesh.SourceRecordId} at ordinal {mesh.SourceOrdinal} with {mesh.Vertices} vertices and {mesh.Triangles} triangles.");
            }

            if (mesh.Materials.Count == 0)
            {
                throw new InvalidOperationException($"Published geometry mesh '{mesh.MeshId}' selects no texture, so it states no material reference.");
            }

            foreach (GeometryMaterialLink material in mesh.Materials)
            {
                material.Validate(mesh.MeshId);
            }

            if (!byId.TryGetValue(mesh.ArtifactId, out GeneratedSpatialArtifact? artifact) || artifact.RelativePath != mesh.RelativePath)
            {
                throw new InvalidOperationException($"Published geometry mesh '{mesh.MeshId}' names artifact '{mesh.ArtifactId}' at '{mesh.RelativePath}', which the publication does not carry.");
            }

            // The digest is recomputed rather than trusted: an artifact whose address does not describe its
            // own bytes would let a consumer cache, deduplicate or compare against something that is not there.
            string digest = artifact.ContentDigest.Value;
            if (!StringComparer.Ordinal.Equals(digest, mesh.ContentDigest) || !StringComparer.Ordinal.Equals(digest, ContentDigest.Compute(artifact.Bytes.Span).Value))
            {
                throw new InvalidOperationException($"Published geometry mesh '{mesh.MeshId}' states the content address '{mesh.ContentDigest}' where artifact '{mesh.ArtifactId}' hashes to '{digest}'.");
            }
        }

        foreach (GeometryUnresolvedMeshReference unresolved in UnresolvedMeshes)
        {
            if (Meshes.Any(mesh => StringComparer.Ordinal.Equals(mesh.MeshId, unresolved.MeshId)))
            {
                throw new InvalidOperationException($"Published geometry publishes mesh '{unresolved.MeshId}' and also reports it unresolved.");
            }

            if (string.IsNullOrWhiteSpace(unresolved.Reason))
            {
                throw new InvalidOperationException($"Published geometry reports mesh '{unresolved.MeshId}' unresolved without a reason.");
            }
        }

        foreach (GeometryMaterialLink material in UnresolvedMaterials)
        {
            if (material.Disposition == GeometryMaterialDisposition.Resolved)
            {
                throw new InvalidOperationException($"Published geometry reports the material '{material.MaterialResourceId}' unresolved where it resolved.");
            }
        }

        // Every record the archive declares is in exactly one class, so the summary is a partition of the
        // corpus rather than a claim about the published subset alone.
        if (Summary.Published != Meshes.Count)
        {
            throw new InvalidOperationException($"Published geometry summary {Summary} does not match the {Meshes.Count} published meshes it carries.");
        }

        if (Summary.Published < 0 || Summary.Unresolvable < 0 || Summary.Missing < 0 || Summary.Duplicate < 0 || Summary.Unused < 0)
        {
            throw new InvalidOperationException($"Published geometry summary {Summary} carries a class count no set of records can have.");
        }

        if (Summary.Unresolvable + Summary.Missing != UnresolvedMeshes.Count)
        {
            throw new InvalidOperationException($"Published geometry summary {Summary} reports {Summary.Unresolvable + Summary.Missing} unresolved numbers where the section carries {UnresolvedMeshes.Count}.");
        }

        // Four record classes, one archive: every record is published, unresolvable, a reused number, or
        // unused, and the numbers no record carries are counted beside the classes rather than in one.
        if (Summary.Published + Summary.Unresolvable + Summary.Duplicate + Summary.Unused != Summary.Records)
        {
            throw new InvalidOperationException($"Published geometry summary {Summary} does not account for every record the archive declares.");
        }
    }
}

/// <summary>The input one geometry publication is derived from.</summary>
/// <param name="Inventory">The mesh inventory of the archive the geometry is read from.</param>
/// <param name="MeshArchiveBytes">The archive's own bytes.</param>
/// <param name="ReferencedMeshIds">The mesh numbers the normalized packs reference, spelled as they spell them.</param>
/// <param name="Textures">The texture-leaf inventory the material references are resolved against.</param>
public sealed record GeometryPublicationRequest(
    Arch3dMeshInventory Inventory,
    ReadOnlyMemory<byte> MeshArchiveBytes,
    IReadOnlyList<string> ReferencedMeshIds,
    TextureLeafInventory Textures);

/// <summary>Builds the geometry a normalized pack references from the archive and the inventory.</summary>
public static class GeometryPublicationBuilder
{
    /// <summary>The relative path one mesh's artifact is published at.</summary>
    public static string MeshRelativePath(uint meshNumber) => $"geometry/mesh-{meshNumber.ToString(CultureInfo.InvariantCulture)}.json";

    /// <summary>The artifact identity one mesh's artifact carries.</summary>
    public static string MeshArtifactId(uint meshNumber) => $"geometry/mesh-{meshNumber.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Reads the referenced meshes out of the archive and publishes them with their materials.</summary>
    public static GeometryPublication Create(GeometryPublicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Inventory);
        ArgumentNullException.ThrowIfNull(request.ReferencedMeshIds);
        ArgumentNullException.ThrowIfNull(request.Textures);
        Arch3dMeshInventory inventory = request.Inventory;

        SortedDictionary<uint, string> referenced = [];
        foreach (string meshId in request.ReferencedMeshIds)
        {
            if (!uint.TryParse(meshId, NumberStyles.None, CultureInfo.InvariantCulture, out uint number))
            {
                throw new InvalidOperationException($"A normalized pack references the mesh '{meshId}', which is not a mesh number.");
            }

            referenced.TryAdd(number, meshId);
        }

        List<GeneratedSpatialArtifact> artifacts = [];
        List<GeometryMeshArtifact> meshes = [];
        List<GeometryUnresolvedMeshReference> unresolved = [];
        List<GeometryMaterialLink> unresolvedMaterials = [];
        int unresolvable = 0;
        int missing = 0;
        foreach ((uint number, string spelling) in referenced)
        {
            // The record a lookup reaches is the inventory's own answer, so it is read from the column
            // that states it rather than derived again from list order.
            Arch3dMeshRecord? record = inventory.Records.FirstOrDefault(candidate => candidate.RecordId == number && candidate.DuplicateOf is null);
            if (record is null)
            {
                missing++;
                unresolved.Add(new GeometryUnresolvedMeshReference(spelling, $"the archive carries no record numbered {number}"));
                continue;
            }

            if (record.State != Arch3dRecordState.Read)
            {
                unresolvable++;
                unresolved.Add(new GeometryUnresolvedMeshReference(spelling, $"record {record.Ordinal} carries number {number} and could not be decoded: {record.Reason}"));
                continue;
            }

            Arch3dMesh mesh = Arch3dDecoder.Decode(Payload(request.MeshArchiveBytes, record), inventory.Source, number);
            List<GeometryMaterialLink> materials = [];
            Dictionary<(ushort Archive, ushort Record), GeometryMaterialLink> byTexture = [];
            List<NormalizedVector3> vertices = [];
            List<NormalizedVector3> normals = [];
            List<NormalizedVector2> uvs = [];
            List<NormalizedTriangle> triangles = [];
            List<NormalizedMaterialGroup> groups = [];
            foreach (Arch3dPlane plane in mesh.Planes)
            {
                if (plane.Points.Count < 3)
                {
                    continue;
                }

                (ushort archive, ushort textureRecord) = (plane.TextureArchive, plane.TextureRecord);
                if (!byTexture.TryGetValue((archive, textureRecord), out GeometryMaterialLink? material))
                {
                    material = Resolve(archive, textureRecord, request.Textures);
                    byTexture.Add((archive, textureRecord), material);
                    materials.Add(material);
                    if (material.Disposition != GeometryMaterialDisposition.Resolved)
                    {
                        unresolvedMaterials.Add(material);
                    }
                }

                int vertexStart = vertices.Count;
                int triangleStart = triangles.Count;
                List<NormalizedVector3> polygon = new(plane.Points.Count);
                foreach (Arch3dPoint point in plane.Points)
                {
                    Arena2ImportPoint placed = Arena2SourceTransform.ToImportPoint(point);
                    NormalizedVector3 vertex = new(placed.XMetres, placed.YMetres, -placed.ZMetres);
                    polygon.Add(vertex);
                    vertices.Add(vertex);

                    // The source's own corrected coordinates travel with the mesh: a texture's extent is a
                    // fact about the material, and binding the two is the consumer's business.
                    uvs.Add(new(point.U, point.V));
                }

                NormalizedVector3 normal = Normal(polygon[0], polygon[1], polygon[2]);
                normals.AddRange(Enumerable.Repeat(normal, polygon.Count));
                for (int index = 1; index < polygon.Count - 1; index++)
                {
                    triangles.Add(new(vertexStart, vertexStart + index, vertexStart + index + 1));
                }

                groups.Add(new NormalizedMaterialGroup(material.MaterialResourceId, triangleStart, polygon.Count - 2, false));
            }

            if (triangles.Count == 0 || groups.Count == 0)
            {
                unresolvable++;
                unresolved.Add(new GeometryUnresolvedMeshReference(spelling, $"record {record.Ordinal} carries number {number} and declares no drawable plane"));
                continue;
            }

            string meshId = number.ToString(CultureInfo.InvariantCulture);
            NormalizedMesh normalized = new NormalizedMesh(NormalizedMesh.CurrentSchemaVersion, meshId, MeshArtifactId(number), vertices, normals, uvs, triangles, groups).Canonicalize();
            normalized.Validate();
            byte[] bytes = StaticMeshJson.Serialize(meshId, Bounds(vertices), MeshAssembly.Create([normalized]));
            GeneratedSpatialArtifact artifact = new(MeshArtifactId(number), MeshRelativePath(number), bytes, []);
            artifacts.Add(artifact);
            meshes.Add(new GeometryMeshArtifact(
                GeometryMeshArtifactSchema,
                meshId,
                number,
                record.Ordinal,
                artifact.Id,
                artifact.RelativePath,
                vertices.Count,
                triangles.Count,
                materials,
                artifact.ContentDigest.Value));
        }

        // Every record the archive declares is published, unresolved, a reused number, or unused: the
        // inventory already says which, so the summary counts its classes rather than judging them again.
        int duplicates = inventory.Records.Count(record => record.DuplicateOf is not null);
        GeometryPublicationSummary summary = new(
            inventory.Records.Count,
            meshes.Count,
            unresolvable,
            missing,
            duplicates,
            inventory.Records.Count - meshes.Count - unresolvable - duplicates);

        GeometryPublication publication = new(
            GeometryPublication.CurrentSchemaVersion,
            inventory.Source,
            [.. artifacts, Index(inventory.Source, summary, meshes, unresolved)],
            meshes,
            unresolved,
            unresolvedMaterials,
            summary);
        publication.Validate();
        return publication;
    }

    private const int GeometryMeshArtifactSchema = 1;

    private static ReadOnlySpan<byte> Payload(ReadOnlyMemory<byte> bytes, Arch3dMeshRecord record) =>
        bytes.Span.Slice((int)record.Offset, record.ByteLength);

    private static GeometryMaterialLink Resolve(ushort archive, ushort record, TextureLeafInventory textures)
    {
        string materialId = $"material/texture-{archive}-{record}";
        if (!textures.TryGet(archive, out TextureLeafRecord? leaf) || leaf is null || leaf.Disposition == TextureLeafDisposition.NotSupplied)
        {
            return new GeometryMaterialLink(archive, record, materialId, GeometryMaterialDisposition.TextureNotSupplied, $"the corpus does not supply texture archive {archive}");
        }

        if (leaf.Disposition != TextureLeafDisposition.Decoded)
        {
            return new GeometryMaterialLink(archive, record, materialId, GeometryMaterialDisposition.TextureMalformed, $"texture archive {archive} could not be parsed: {leaf.Note}");
        }

        return record >= leaf.Records
            ? new GeometryMaterialLink(archive, record, materialId, GeometryMaterialDisposition.TextureRecordMissing, $"texture archive {archive} carries {leaf.Records} records, so record {record} is missing")
            : new GeometryMaterialLink(archive, record, materialId, GeometryMaterialDisposition.Resolved, string.Empty);
    }

    private static GeneratedSpatialArtifact Index(
        string inventorySource,
        GeometryPublicationSummary summary,
        IReadOnlyList<GeometryMeshArtifact> meshes,
        IReadOnlyList<GeometryUnresolvedMeshReference> unresolved)
    {
        GeometryIndex index = new(GeometryPublication.CurrentSchemaVersion, inventorySource, summary, meshes, unresolved);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(index, PublishedJson.Section);
        return new GeneratedSpatialArtifact(GeometryPublication.IndexArtifactId, GeometryPublication.IndexRelativePath, bytes, [.. meshes.Select(mesh => mesh.ArtifactId).OrderBy(id => id, StringComparer.Ordinal)]);
    }

    private static NormalizedBounds Bounds(IReadOnlyList<NormalizedVector3> vertices) => new(
        NormalizedBounds.CurrentSchemaVersion,
        new(vertices.Min(vertex => vertex.X), vertices.Min(vertex => vertex.Y), vertices.Min(vertex => vertex.Z)),
        new(vertices.Max(vertex => vertex.X), vertices.Max(vertex => vertex.Y), vertices.Max(vertex => vertex.Z)));

    private static NormalizedVector3 Normal(NormalizedVector3 first, NormalizedVector3 second, NormalizedVector3 third)
    {
        float ax = second.X - first.X;
        float ay = second.Y - first.Y;
        float az = second.Z - first.Z;
        float bx = third.X - first.X;
        float by = third.Y - first.Y;
        float bz = third.Z - first.Z;
        float x = (ay * bz) - (az * by);
        float y = (az * bx) - (ax * bz);
        float z = (ax * by) - (ay * bx);
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        return length > 1E-12F ? new(x / length, y / length, z / length) : new(0F, 1F, 0F);
    }
}

/// <summary>What one geometry publication holds, written as the index artifact.</summary>
/// <param name="SchemaVersion">Shape version of the index.</param>
/// <param name="InventorySource">The logical path of the inventory this publication classified through.</param>
/// <param name="Summary">How the published set relates to every record the archive declares.</param>
/// <param name="Meshes">Every published mesh, in number order.</param>
/// <param name="UnresolvedMeshes">Every referenced number the archive could not serve.</param>
public sealed record GeometryIndex(
    int SchemaVersion,
    string InventorySource,
    GeometryPublicationSummary Summary,
    IReadOnlyList<GeometryMeshArtifact> Meshes,
    IReadOnlyList<GeometryUnresolvedMeshReference> UnresolvedMeshes);

/// <summary>Checks one material reference against what its disposition claims.</summary>
internal static class GeometryMaterialLinkValidation
{
    /// <summary>Checks that a material link states a reference and says whether the corpus serves it.</summary>
    internal static void Validate(this GeometryMaterialLink material, string meshId)
    {
        NormalizedImportDocument.RequireLogicalId(material.MaterialResourceId, nameof(material.MaterialResourceId));
        if (material.Disposition == GeometryMaterialDisposition.Resolved)
        {
            if (material.Note.Length != 0)
            {
                throw new InvalidOperationException($"Mesh '{meshId}' resolves the material '{material.MaterialResourceId}' and still states '{material.Note}'.");
            }
        }
        else if (string.IsNullOrWhiteSpace(material.Note))
        {
            throw new InvalidOperationException($"Mesh '{meshId}' reports the material '{material.MaterialResourceId}' as {material.Disposition} without a reason.");
        }
    }
}
