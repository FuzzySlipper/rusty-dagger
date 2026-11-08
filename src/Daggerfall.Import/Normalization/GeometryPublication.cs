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

    /// <summary>
    /// The archive carries the record, but the record declares nothing a material can bind: no frame, or
    /// an extent with no area. The donor's own reader returns nothing for such a record.
    /// </summary>
    TextureRecordUnusable,
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
/// <param name="MeshId">The mesh number, spelled as a decimal number.</param>
/// <param name="SourceRecordId">The number the archive indexes the record by.</param>
/// <param name="SourceOrdinal">The record's position in the archive directory.</param>
/// <param name="ArtifactId">The artifact's identity.</param>
/// <param name="RelativePath">Where the artifact is published relative to the content root.</param>
/// <param name="Vertices">How many vertices the artifact carries.</param>
/// <param name="Triangles">How many triangles it carries.</param>
/// <param name="Materials">The textures its planes select, in first-use order.</param>
/// <param name="ContentDigest">The artifact's content address.</param>
/// <param name="MaterialSlots">
/// The static mesh material slot each material is drawn with, published from the assembly that wrote the
/// artifact so a consumer binds textures to slots without reading the mesh bytes.
/// </param>
public sealed record GeometryMeshArtifact(
    string MeshId,
    long SourceRecordId,
    int SourceOrdinal,
    string ArtifactId,
    string RelativePath,
    int Vertices,
    int Triangles,
    IReadOnlyList<GeometryMaterialLink> Materials,
    string ContentDigest,
    IReadOnlyList<StaticMeshMaterialBinding> MaterialSlots);

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
    string InventorySource,
    IReadOnlyList<GeneratedSpatialArtifact> Artifacts,
    IReadOnlyList<GeometryMeshArtifact> Meshes,
    IReadOnlyList<GeometryUnresolvedMeshReference> UnresolvedMeshes,
    IReadOnlyList<GeometryMaterialLink> UnresolvedMaterials,
    GeometryPublicationSummary Summary)
{
    /// <summary>
    /// The trimmed meshes of block placements an earlier placement partly covers (<see cref="RmbPlacementTrims"/>),
    /// beside the archive's records: each is cut from a published record's mesh and names it.
    /// </summary>
    public IReadOnlyList<GeometryMeshArtifact> PlacementMeshes { get; init; } = [];

    /// <summary>The artifact that lists what was published and what could not be.</summary>
    public const string IndexRelativePath = "geometry/index.json";

    /// <summary>The index artifact's identity.</summary>
    public const string IndexArtifactId = "geometry/index";

    /// <summary>
    /// Checks that every claim this publication makes about its own artifacts holds together.
    /// </summary>
    /// <remarks>
    /// This is self-consistency, not provenance: the publication does not carry the inventory it classified
    /// through, so the duplicate and unused counts are checked against each other and against the records it
    /// publishes rather than re-derived from the archive. A summary that lied consistently, in the section
    /// and in the index both, would satisfy every rule here; the inventory is the authority for those two
    /// columns, and re-deriving them would mean carrying it.
    /// </remarks>
    public void Validate()
    {
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

        NormalizedImportDocument.ValidateUnique(PlacementMeshes, mesh => mesh.MeshId, "geometry placement mesh");
        foreach (GeometryMeshArtifact mesh in PlacementMeshes)
        {
            GeometryMeshArtifact? model = Meshes.SingleOrDefault(candidate => candidate.SourceRecordId == mesh.SourceRecordId);
            if (model is null || model.SourceOrdinal != mesh.SourceOrdinal || mesh.Vertices <= 0 || mesh.Triangles <= 0
                || !byId.TryGetValue(mesh.ArtifactId, out GeneratedSpatialArtifact? artifact) || artifact.RelativePath != mesh.RelativePath
                || !StringComparer.Ordinal.Equals(mesh.ContentDigest, ContentDigest.Compute(artifact.Bytes.Span).Value))
            {
                throw new InvalidOperationException($"Published geometry placement mesh '{mesh.MeshId}' trims model {mesh.SourceRecordId}, which the publication does not carry, or its artifact does not agree with it.");
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

        // The unresolved list is what the meshes select and cannot have: dropping a reference would leave a
        // consumer binding a material nothing states is missing.
        GeometryMaterialLink[] selectedUnresolved = [.. Meshes
            .SelectMany(mesh => mesh.Materials)
            .Where(material => material.Disposition != GeometryMaterialDisposition.Resolved)];
        if (selectedUnresolved.Length != UnresolvedMaterials.Count || selectedUnresolved.Any(material => !UnresolvedMaterials.Contains(material)))
        {
            throw new InvalidOperationException($"Published geometry carries {UnresolvedMaterials.Count} unresolved materials where its {Meshes.Count} meshes select {selectedUnresolved.Length}.");
        }

        // The index is what a consumer reads, so it is parsed and compared rather than trusted: bytes that
        // name a mesh the section does not carry, or a summary the records do not support, are refused.
        GeometryIndex document = JsonSerializer.Deserialize<GeometryIndex>(index.Bytes.Span, PublishedJson.SectionRead)
            ?? throw new InvalidOperationException($"Published geometry index at '{IndexRelativePath}' could not be read.");
        if (!StringComparer.Ordinal.Equals(document.InventorySource, InventorySource)
            || document.Summary != Summary
            || !document.Meshes.Select(mesh => (mesh.MeshId, mesh.SourceRecordId, mesh.SourceOrdinal, mesh.ArtifactId, mesh.RelativePath, mesh.Vertices, mesh.Triangles, mesh.ContentDigest))
                .SequenceEqual(Meshes.Select(mesh => (mesh.MeshId, mesh.SourceRecordId, mesh.SourceOrdinal, mesh.ArtifactId, mesh.RelativePath, mesh.Vertices, mesh.Triangles, mesh.ContentDigest)))
            || !document.Meshes.SelectMany(mesh => mesh.MaterialSlots.Select(slot => (mesh.MeshId, slot)))
                .SequenceEqual(Meshes.SelectMany(mesh => mesh.MaterialSlots.Select(slot => (mesh.MeshId, slot))))
            || !document.PlacementMeshes.Select(mesh => (mesh.MeshId, mesh.SourceRecordId, mesh.ArtifactId, mesh.RelativePath, mesh.ContentDigest))
                .SequenceEqual(PlacementMeshes.Select(mesh => (mesh.MeshId, mesh.SourceRecordId, mesh.ArtifactId, mesh.RelativePath, mesh.ContentDigest)))
            || !document.UnresolvedMeshes.Select(mesh => (mesh.MeshId, mesh.Reason))
                .SequenceEqual(UnresolvedMeshes.Select(mesh => (mesh.MeshId, mesh.Reason))))
        {
            throw new InvalidOperationException("Published geometry index does not describe the records the section carries.");
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
/// <param name="ReferencedMeshIds">
/// The mesh numbers to publish, spelled as the caller spells them: the ones the normalized packs
/// reference, plus any a site's own world visuals name — a missile's mesh is placed by no block, so
/// nothing in a pack references it.
/// </param>
/// <param name="Textures">The texture-leaf inventory the material references are resolved against.</param>
public sealed record GeometryPublicationRequest(
    Arch3dMeshInventory Inventory,
    ReadOnlyMemory<byte> MeshArchiveBytes,
    IReadOnlyList<string> ReferencedMeshIds,
    TextureLeafInventory Textures)
{
    /// <summary>The block placements to publish trimmed meshes of; each trims a referenced mesh.</summary>
    public IReadOnlyList<RmbPlacementTrim> Placements { get; init; } = [];
}

/// <summary>Builds the geometry a normalized pack and a site's world visuals reference from the archive and the inventory.</summary>
public static class GeometryPublicationBuilder
{
    /// <summary>The relative path one mesh's artifact is published at.</summary>
    public static string MeshRelativePath(uint meshNumber) => $"geometry/mesh-{meshNumber.ToString(CultureInfo.InvariantCulture)}{StaticMeshBinary.Extension}";

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

            // One number, one spelling in the publication, whichever order the pack listed them in: the
            // lexicographically smaller wins so two runs over one set cannot disagree on bytes.
            if (!referenced.TryAdd(number, meshId) && StringComparer.Ordinal.Compare(meshId, referenced[number]) < 0)
            {
                referenced[number] = meshId;
            }
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
            string meshId = number.ToString(CultureInfo.InvariantCulture);
            GeometryMeshArtifact? published = Publish(meshId, MeshArtifactId(number), MeshRelativePath(number), number, record.Ordinal,
                [.. mesh.Planes.Where(plane => plane.Points.Count >= 3).Select(plane => new RmbTrimFace(plane.TextureArchive, plane.TextureRecord,
                    [.. plane.Points.Select(point => MeshGeometry.ToRightHanded(Arena2SourceTransform.ToImportPoint(point)))],
                    [.. plane.Points.Select(point => PlaneUv(plane, point, request.Textures))]))],
                request.Textures, artifacts, unresolvedMaterials);
            if (published is null)
            {
                unresolvable++;
                unresolved.Add(new GeometryUnresolvedMeshReference(spelling, $"record {record.Ordinal} carries number {number} and declares no drawable plane"));
                continue;
            }

            meshes.Add(published);
        }

        // A trimmed placement's materials are its model's, already counted among the publication's unresolved ones.
        List<GeometryMeshArtifact> placements = [];
        foreach (RmbPlacementTrim trim in request.Placements.Where(trim => trim.MeshArtifactId is not null).OrderBy(trim => trim.MeshArtifactId, StringComparer.Ordinal))
        {
            string id = trim.MeshArtifactId!;
            GeometryMeshArtifact model = meshes.SingleOrDefault(mesh => mesh.SourceRecordId == trim.ModelNumber)
                ?? throw new InvalidOperationException($"Block placement '{trim.PlacementId}' trims model {trim.ModelNumber}, which the publication does not publish.");
            placements.Add(Publish(id["geometry/".Length..], id, $"{id}{StaticMeshBinary.Extension}", trim.ModelNumber, model.SourceOrdinal,
                trim.Faces, request.Textures, artifacts, null)
                ?? throw new InvalidOperationException($"Block placement '{trim.PlacementId}' keeps faces with no drawable fan."));
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
            inventory.Source,
            [.. artifacts, Index(inventory.Source, summary, meshes, placements, unresolved)],
            meshes,
            unresolved,
            unresolvedMaterials,
            summary)
        {
            PlacementMeshes = placements,
        };
        publication.Validate();
        return publication;
    }


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

        if (record >= leaf.Records || !textures.TryGetRecord(archive, record, out TextureRecordFacts? facts) || facts is null)
        {
            return new GeometryMaterialLink(archive, record, materialId, GeometryMaterialDisposition.TextureRecordMissing, $"texture archive {archive} carries {leaf.Records} records, so record {record} is missing");
        }

        // The leaf parsed and carries the record, which is not the same as the record being bindable:
        // eight records in the corpus declare no frame or no extent, and calling those resolved would
        // state that a material exists where the corpus has nothing to draw.
        if (facts.Frames == 0)
        {
            return new GeometryMaterialLink(archive, record, materialId, GeometryMaterialDisposition.TextureRecordUnusable, $"texture archive {archive} record {record} declares no frame");
        }

        if (facts.Width <= 0 || facts.Height <= 0)
        {
            return new GeometryMaterialLink(archive, record, materialId, GeometryMaterialDisposition.TextureRecordUnusable, $"texture archive {archive} record {record} declares the extent {facts.Width}x{facts.Height}");
        }

        // The record states a frame and an extent, which is still not the same as the corpus carrying the
        // picture: the leaf owner proved the frame decodes where the bytes were in hand, and a record it
        // could not read is reported with the decoder's own reason rather than called bindable.
        return facts.UnreadableReason.Length == 0
            ? new GeometryMaterialLink(archive, record, materialId, GeometryMaterialDisposition.Resolved, string.Empty)
            : new GeometryMaterialLink(archive, record, materialId, GeometryMaterialDisposition.TextureRecordUnusable, $"texture archive {archive} record {record} cannot be read: {facts.UnreadableReason}");
    }

    private static GeneratedSpatialArtifact Index(
        string inventorySource,
        GeometryPublicationSummary summary,
        IReadOnlyList<GeometryMeshArtifact> meshes,
        IReadOnlyList<GeometryMeshArtifact> placements,
        IReadOnlyList<GeometryUnresolvedMeshReference> unresolved)
    {
        GeometryIndex index = new(inventorySource, summary, meshes, unresolved) { PlacementMeshes = placements };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(index, PublishedJson.Section);
        return new GeneratedSpatialArtifact(GeometryPublication.IndexArtifactId, GeometryPublication.IndexRelativePath, bytes,
            [.. meshes.Concat(placements).Select(mesh => mesh.ArtifactId).OrderBy(id => id, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// The texture coordinate a source point takes on its plane. The mesh is drawn with whole repeats of its
    /// texture per uv unit, so the source's sub-texel coordinates become fractions of the record's own extent,
    /// as the donor scales them by the base archive's record. A climate swap draws a record of the same extent.
    /// A record with no extent has no picture to place, and its material is reported unresolved.
    /// </summary>
    public static NormalizedVector2 PlaneUv(Arch3dPlane plane, Arch3dPoint point, TextureLeafInventory textures)
    {
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(textures);
        if (!textures.TryGetRecord(plane.TextureArchive, plane.TextureRecord, out TextureRecordFacts? facts) || facts is not { Width: > 0, Height: > 0 })
            return default;
        Arena2TextureUv uv = Arena2SourceTransform.ToTextureUv(point, facts.Width, facts.Height);
        return new(uv.U, uv.V);
    }

    /// <summary>
    /// Publishes one mesh from its faces, a material group per face, or nothing when no face has a drawable fan.
    /// A material the faces select first is resolved and, when <paramref name="unresolvedMaterials"/> is given,
    /// recorded there if the corpus cannot serve it.
    /// </summary>
    private static GeometryMeshArtifact? Publish(string meshId, string artifactId, string relativePath, long recordId, int ordinal,
        IReadOnlyList<RmbTrimFace> faces, TextureLeafInventory textures, List<GeneratedSpatialArtifact> artifacts, List<GeometryMaterialLink>? unresolvedMaterials)
    {
        List<GeometryMaterialLink> materials = [];
        Dictionary<(ushort Archive, ushort Record), GeometryMaterialLink> byTexture = [];
        List<NormalizedVector3> vertices = [];
        List<NormalizedVector3> normals = [];
        List<NormalizedVector2> uvs = [];
        List<NormalizedTriangle> triangles = [];
        List<NormalizedMaterialGroup> groups = [];
        foreach (RmbTrimFace face in faces)
        {
            if (!byTexture.TryGetValue((face.TextureArchive, face.TextureRecord), out GeometryMaterialLink? material))
            {
                material = Resolve(face.TextureArchive, face.TextureRecord, textures);
                byTexture.Add((face.TextureArchive, face.TextureRecord), material);
                materials.Add(material);
                if (material.Disposition != GeometryMaterialDisposition.Resolved) unresolvedMaterials?.Add(material);
            }

            (int fanStart, int fanCount) = MeshGeometry.AppendPolygon(vertices, normals, uvs, triangles, face.Polygon, face.Uvs, MeshGeometry.Normal(face.Polygon));
            groups.Add(new NormalizedMaterialGroup(material.MaterialResourceId, fanStart, fanCount, false));
        }

        if (triangles.Count == 0 || groups.Count == 0) return null;
        NormalizedMesh normalized = new NormalizedMesh(meshId, artifactId, vertices, normals, uvs, triangles, groups).Canonicalize();
        normalized.Validate();
        MeshAssembly assembly = MeshAssembly.Create([normalized]);
        byte[] bytes = StaticMeshArtifact.Serialize(meshId, MeshGeometry.Bounds(vertices), assembly);
        GeneratedSpatialArtifact artifact = new(artifactId, relativePath, bytes, []);
        artifacts.Add(artifact);
        return new GeometryMeshArtifact(meshId, recordId, ordinal, artifact.Id, artifact.RelativePath, vertices.Count, triangles.Count, materials,
            artifact.ContentDigest.Value, [.. assembly.MaterialSlots.Select(binding => new StaticMeshMaterialBinding(binding.Material, binding.Slot))]);
    }

}

/// <summary>What one geometry publication holds, written as the index artifact.</summary>
/// <param name="InventorySource">The logical path of the inventory this publication classified through.</param>
/// <param name="Summary">How the published set relates to every record the archive declares.</param>
/// <param name="Meshes">Every published mesh, in number order.</param>
/// <param name="UnresolvedMeshes">Every referenced number the archive could not serve.</param>
public sealed record GeometryIndex(
    string InventorySource,
    GeometryPublicationSummary Summary,
    IReadOnlyList<GeometryMeshArtifact> Meshes,
    IReadOnlyList<GeometryUnresolvedMeshReference> UnresolvedMeshes)
{
    /// <summary>Every trimmed block placement mesh, in identity order.</summary>
    public IReadOnlyList<GeometryMeshArtifact> PlacementMeshes { get; init; } = [];
}

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
