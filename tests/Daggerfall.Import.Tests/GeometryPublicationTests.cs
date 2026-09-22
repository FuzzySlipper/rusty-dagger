using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The geometry a normalized pack references: one deterministic artifact per mesh, the material
/// references its planes select, and the references the corpus cannot serve.
/// </summary>
public sealed class GeometryPublicationTests
{
    private static readonly Lazy<Arch3dMeshInventory> Archive = new(() =>
        Arch3dInventoryReader.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/ARCH3D.BSA")), "local/arena2/ARCH3D.BSA"));

    [Fact]
    public void Publishes_one_artifact_per_referenced_mesh_with_its_material_links()
    {
        // Mesh 55000 is a door model the shipped plan references; it is read from the real archive so the
        // geometry, its planes and its textures are the corpus's own.
        GeometryPublication publication = Publish(["55000"], Textures());

        GeometryMeshArtifact mesh = Assert.Single(publication.Meshes);
        Assert.Equal("55000", mesh.MeshId);
        Assert.Equal(55000, mesh.SourceRecordId);
        Assert.Equal(GeometryPublicationBuilder.MeshRelativePath(55000), mesh.RelativePath);
        Assert.True(mesh.Vertices > 0 && mesh.Triangles > 0);
        Assert.NotEmpty(mesh.Materials);
        Assert.All(mesh.Materials, material => Assert.Equal(GeometryMaterialDisposition.Resolved, material.Disposition));

        // The artifact is the mesh: it carries its own bytes, and its address is the digest of them.
        GeneratedSpatialArtifact artifact = publication.Artifacts.Single(value => value.RelativePath == mesh.RelativePath);
        Assert.Equal(ContentDigest.Compute(artifact.Bytes.Span).Value, mesh.ContentDigest);
        Assert.Equal(2, publication.Artifacts.Count);

        // Meshes 55000 and 55001 differ, and each artifact says so.
        GeometryPublication pair = Publish(["55001", "55000"], Textures());
        Assert.Equal(["55000", "55001"], pair.Meshes.Select(value => value.MeshId));
        Assert.Equal(2, pair.Meshes.Select(value => value.ContentDigest).Distinct().Count());

        // Every record the archive declares is classified exactly once, whatever the pack referenced.
        Assert.Equal(10251, publication.Summary.Records);
        Assert.Equal(1, publication.Summary.Published);
        Assert.Equal(0, publication.Summary.Unresolvable);
        Assert.Equal(0, publication.Summary.Missing);
        Assert.Equal(14, publication.Summary.Duplicate);
        Assert.Equal(10251 - 1 - 14, publication.Summary.Unused);
    }

    [Fact]
    public void Preserves_a_material_reference_the_corpus_cannot_serve()
    {
        // Texture archive 34 is one of the leaves the corpus does not supply, and no leaf carries a record
        // this high. Neither becomes another texture: the reference stays what it is, with the reason.
        TextureLeafInventory textures = TextureLeafInventory.Enumerate(
            [(0, "TEXTURE.000", File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/TEXTURE.000")))],
            "local/arena2");
        GeometryPublication missing = Publish(["55000"], textures);

        GeometryMaterialLink[] unresolved = [.. missing.Meshes[0].Materials.Where(material => material.Disposition != GeometryMaterialDisposition.Resolved)];
        Assert.NotEmpty(unresolved);
        Assert.All(unresolved, material =>
        {
            Assert.Equal($"material/texture-{material.Archive}-{material.Record}", material.MaterialResourceId);
            Assert.NotEmpty(material.Note);
        });
        Assert.Contains(unresolved, material => material.Disposition == GeometryMaterialDisposition.TextureNotSupplied);
        Assert.Equal(unresolved.Length, missing.UnresolvedMaterials.Count);
        Assert.True(missing.Meshes[0].Triangles > 0);
    }

    [Fact]
    public void Reports_a_referenced_mesh_the_archive_cannot_serve()
    {
        // A number no record carries, a number whose record cannot be decoded, and a number the archive
        // carries but the mesh declares no drawable plane for: each is a reference, not a substitute.
        // The published case carries a real mesh record; the other two are the corpus's own bytes with the
        // record made unreadable, and with its plane count zeroed so nothing is drawable.
        byte[] real = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/ARCH3D.BSA"));
        Arch3dMeshRecord source = Archive.Value.Records.First(value => value.RecordId == 55000);
        byte[] payload = real.AsSpan((int)source.Offset, source.ByteLength).ToArray();
        byte[] planeless = (byte[])payload.Clone();
        BitConverter.GetBytes(0).CopyTo(planeless, 8);
        byte[] archive = NumericArchive((9004, payload), (9005, new byte[32]), (9006, planeless));
        Arch3dMeshInventory inventory = Arch3dInventoryReader.Read(archive, "local/arena2/ARCH3D.BSA");
        GeometryPublication publication = GeometryPublicationBuilder.Create(new GeometryPublicationRequest(
            inventory,
            archive,
            ["9004", "09005", "9006", "999999"],
            Textures()));

        GeometryMeshArtifact mesh = Assert.Single(publication.Meshes);
        Assert.Equal("9004", mesh.MeshId);
        Assert.Equal(
            ["09005", "9006", "999999"],
            publication.UnresolvedMeshes.Select(value => value.MeshId));
        Assert.Contains("could not be decoded", publication.UnresolvedMeshes[0].Reason, StringComparison.Ordinal);
        Assert.Contains("declares no drawable plane", publication.UnresolvedMeshes[1].Reason, StringComparison.Ordinal);
        Assert.Contains("carries no record numbered 999999", publication.UnresolvedMeshes[2].Reason, StringComparison.Ordinal);
        Assert.Equal(2, publication.Summary.Unresolvable);
        Assert.Equal(1, publication.Summary.Missing);
        Assert.Equal(0, publication.Summary.Unused);

        // The classes partition the records and no class can be negative, which is what the mixed-unit
        // arithmetic this replaced could state while still validating.
        Assert.Equal(publication.Summary.Records, publication.Summary.Published + publication.Summary.Unresolvable + publication.Summary.Duplicate + publication.Summary.Unused);
        Assert.Equal(1, publication.Summary.Published);
    }

    [Fact]
    public void The_index_states_what_was_published_and_what_could_not_be()
    {
        GeometryPublication publication = Publish(["55000", "999999"], Textures());
        GeneratedSpatialArtifact index = publication.Artifacts.Single(artifact => artifact.RelativePath == GeometryPublication.IndexRelativePath);

        using JsonDocument document = JsonDocument.Parse(index.Bytes);
        JsonElement root = document.RootElement;
        Assert.Equal("local/arena2/ARCH3D.BSA", root.GetProperty("inventorySource").GetString());
        Assert.Equal(10251, root.GetProperty("summary").GetProperty("records").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("published").GetInt32());
        Assert.Single(root.GetProperty("meshes").EnumerateArray());
        Assert.Equal("55000", root.GetProperty("meshes")[0].GetProperty("meshId").GetString());
        Assert.Equal("999999", root.GetProperty("unresolvedMeshes")[0].GetProperty("meshId").GetString());

        // The index names every artifact it lists, so a consumer can reach them from it.
        Assert.Equal(publication.Meshes.Select(mesh => $"{mesh.RelativePath}"), index.DependsOnArtifactIds.Select(id => publication.Artifacts.Single(artifact => artifact.Id == id).RelativePath));
    }

    [Fact]
    public void Publishes_the_same_bytes_and_addresses_every_time()
    {
        // Repeated imports have to agree, because a pack that changes its addresses without changing its
        // source would invalidate every consumer's cache for no reason.
        GeometryPublication first = Publish(["55000", "55001", "55002"], Textures());
        GeometryPublication second = Publish(["55002", "55000", "55001"], Textures());

        Assert.Equal(
            first.Meshes.Select(mesh => (mesh.MeshId, mesh.ContentDigest)),
            second.Meshes.Select(mesh => (mesh.MeshId, mesh.ContentDigest)));
        Assert.Equal(
            first.Artifacts.Select(artifact => (artifact.RelativePath, artifact.ContentDigest.Value)),
            second.Artifacts.Select(artifact => (artifact.RelativePath, artifact.ContentDigest.Value)));
    }

    [Fact]
    public void Every_triangle_indexes_vertices_the_artifact_carries()
    {
        // Bounds are a property of the mesh contract, and the publication has to hold its own output to
        // them: an index past the vertex list would make the artifact unloadable rather than wrong-looking.
        foreach (string meshId in new[] { "55000", "41313", "21141" })
        {
            GeometryMeshArtifact mesh = Assert.Single(Publish([meshId], Textures()).Meshes);
            using JsonDocument document = JsonDocument.Parse(
                Publish([meshId], Textures()).Artifacts.Single(artifact => artifact.RelativePath == mesh.RelativePath).Bytes);
            // The artifact is the mesh the contract validated when it was assembled — every triangle index
            // is one the vertex list carries, which the mesh contract refuses otherwise — and it names the
            // source mesh it came from.
            string raw = System.Text.Encoding.UTF8.GetString(
                Publish([meshId], Textures()).Artifacts.Single(artifact => artifact.RelativePath == mesh.RelativePath).Bytes.Span);
            JsonDocument.Parse(raw).Dispose();
            Assert.Contains($"\"{mesh.MeshId}\"", raw, StringComparison.Ordinal);
            Assert.True(mesh.Vertices > 0 && mesh.Triangles > 0);
        }
    }

    [Fact]
    public void Reports_a_texture_record_that_declares_nothing_to_bind()
    {
        // The leaf parsed, which is not the same as its record being bindable: eight records in the corpus
        // declare no frame, and the donor's own reader returns nothing for them. A material that called
        // those resolved would state a texture exists where the corpus has nothing to draw.
        TextureLeafInventory textures = Textures();

        Assert.True(textures.TryGetRecord(81, 4, out TextureRecordFacts? empty));
        Assert.Equal(0, empty!.Frames);
        Assert.True(textures.TryGetRecord(436, 0, out TextureRecordFacts? first));
        Assert.Equal(0, first!.Frames);

        // Every record the leaves carry states its own facts, and a record the leaf does not carry is not
        // answerable at all rather than answered by an aggregate.
        Assert.All(textures.Decoded, leaf => Assert.Equal(leaf.Records, leaf.RecordFacts.Count));

        // The per-record read flag is derived from an actual decode rather than from the header, so every
        // decoded leaf is checked against its own source bytes: a record the owner calls readable has a
        // frame that decodes, and one it cannot read says why.
        ReadOnlyMemory<byte> ReadTextureBytes(TextureLeafRecord leaf) =>
            File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2", leaf.Path));
        TextureLeafRecord[] decoded = [.. textures.Decoded];
        int checkedRecords = CrossCheckTextureReadability(decoded, ReadTextureBytes);
        int expectedCheckedRecords = decoded.Sum(leaf => leaf.RecordFacts.Count);
        Assert.Equal(expectedCheckedRecords, checkedRecords);

        // A lie after the old 24-leaf sample reaches the same cross-check. The copied record stays tied to
        // its real decoded leaf and bytes, so this proves the regression without guessing a source offset.
        Assert.True(decoded.Length > 24, $"the real corpus has {decoded.Length} decoded leaves");
        TextureLeafRecord beyondSample = decoded
            .Skip(24)
            .First(leaf => leaf.RecordFacts.Any(facts => facts.Frames > 0 && facts.UnreadableReason.Length == 0));
        TextureRecordFacts readable = beyondSample.RecordFacts
            .First(facts => facts.Frames > 0 && facts.UnreadableReason.Length == 0);
        TextureLeafRecord corrupted = beyondSample with
        {
            RecordFacts = [.. beyondSample.RecordFacts.Select(facts => facts.RecordIndex == readable.RecordIndex
                ? facts with { UnreadableReason = "the test deliberately lies" }
                : facts)],
        };
        Assert.Throws<Xunit.Sdk.EqualException>(() => CrossCheckTextureReadability([corrupted], ReadTextureBytes));

        Assert.All(textures.Decoded.SelectMany(leaf => leaf.RecordFacts), facts => Assert.True(facts.Width >= 0 && facts.Height >= 0));
        Assert.False(textures.TryGetRecord(81, 999, out _));
        Assert.False(textures.TryGetRecord(34, 0, out _));
    }

    [Fact]
    public void Reports_a_material_whose_texture_record_cannot_be_read()
    {
        // The leaf proves a record's first frame decodes before a material is called bindable, and the corpus
        // carries no record that fails that proof, so this fixture builds one through the decoder itself: a
        // leaf cut short of the frame its record declares. The corpus's own frames-less record rides on the
        // second plane. Both are reported with what their owner established rather than as a material a
        // consumer could bind.
        const int LeafId = 34;
        GeometryPublication publication = Publish(
            ["9004"],
            Textures((LeafId, $"TEXTURE.{LeafId:000}", TextureLeaf(frameDecodes: false))),
            NumericArchive((9004, MeshFixture((81, 4), (LeafId, 0)))));

        GeometryMeshArtifact mesh = Assert.Single(publication.Meshes);
        Assert.Equal(2, mesh.Materials.Count);
        GeometryMaterialLink frameLess = mesh.Materials.Single(material => material.Archive == 81 && material.Record == 4);
        Assert.Equal(GeometryMaterialDisposition.TextureRecordUnusable, frameLess.Disposition);
        Assert.Contains("declares no frame", frameLess.Note, StringComparison.Ordinal);
        GeometryMaterialLink unreadable = mesh.Materials.Single(material => material.Archive == LeafId && material.Record == 0);
        Assert.Equal(GeometryMaterialDisposition.TextureRecordUnusable, unreadable.Disposition);
        Assert.Contains("cannot be read", unreadable.Note, StringComparison.Ordinal);
        Assert.Equal(2, publication.UnresolvedMaterials.Count);
        Assert.Equal(
            publication.UnresolvedMaterials.Count,
            publication.Meshes.Sum(value => value.Materials.Count(material => material.Disposition != GeometryMaterialDisposition.Resolved)));

        // The control: the same fixture leaf, carrying the frame its record declares, resolves. The
        // disposition is the decoder's proof about the bytes and not the leaf id or the reference spelling.
        GeometryPublication readable = Publish(
            ["9004"],
            Textures((LeafId, $"TEXTURE.{LeafId:000}", TextureLeaf(frameDecodes: true))),
            NumericArchive((9004, MeshFixture((LeafId, 0)))));
        Assert.Equal(GeometryMaterialDisposition.Resolved, Assert.Single(Assert.Single(readable.Meshes).Materials).Disposition);
        Assert.Empty(readable.UnresolvedMaterials);
    }

    [Fact]
    public void Reports_a_material_whose_texture_record_declares_no_extent()
    {
        // The guard's sub-branches are told apart by what the record itself states, so the record that
        // states a frame and an extent no frame can have is refused for the extent it declares rather than
        // falling through to the decoder's reason for refusing the frame. The corpus carries no such
        // record, so this fixture builds one through the decoder the inventory reads, as the frame the
        // fixture cuts short is built.
        const int LeafId = 34;
        TextureLeafInventory textures = Textures((LeafId, $"TEXTURE.{LeafId:000}", TextureLeaf(frameDecodes: false, width: 0)));
        Assert.True(textures.TryGetRecord(LeafId, 0, out TextureRecordFacts? facts));
        Assert.Equal(1, facts!.Frames);
        Assert.Equal(0, facts.Width);
        Assert.Equal(2, facts.Height);

        GeometryPublication publication = Publish(
            ["9004"],
            textures,
            NumericArchive((9004, MeshFixture((LeafId, 0)))));

        GeometryMaterialLink extentless = Assert.Single(Assert.Single(publication.Meshes).Materials);
        Assert.Equal(GeometryMaterialDisposition.TextureRecordUnusable, extentless.Disposition);
        Assert.Contains("declares the extent 0x2", extentless.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot be read", extentless.Note, StringComparison.Ordinal);
        Assert.Equal(extentless, Assert.Single(publication.UnresolvedMaterials));
    }

    [Fact]
    public void Publishes_a_number_once_however_its_spelling_arrives()
    {
        // One number, one spelling in the publication: a pack that spells a missing number two ways cannot
        // make two runs publish different bytes.
        GeometryPublication first = Publish(["999998", "0999998"], Textures());
        GeometryPublication second = Publish(["0999998", "999998"], Textures());

        Assert.Single(first.UnresolvedMeshes);
        Assert.Equal(first.UnresolvedMeshes[0].MeshId, second.UnresolvedMeshes[0].MeshId);
        Assert.Equal(
            first.Artifacts.Select(artifact => (artifact.RelativePath, artifact.ContentDigest.Value)),
            second.Artifacts.Select(artifact => (artifact.RelativePath, artifact.ContentDigest.Value)));
    }

    [Fact]
    public void Refuses_an_index_that_does_not_describe_the_records()
    {
        // The index is what a consumer reads, so it is parsed and compared rather than trusted.
        GeometryPublication publication = Publish(["55000"], Textures());
        GeneratedSpatialArtifact index = publication.Artifacts.Single(artifact => artifact.RelativePath == GeometryPublication.IndexRelativePath);
        GeometryIndex document = System.Text.Json.JsonSerializer.Deserialize<GeometryIndex>(index.Bytes.Span, Daggerfall.Import.Publication.PublishedJson.SectionRead)!;

        GeneratedSpatialArtifact tampered = new(
            index.Id,
            index.RelativePath,
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(document with { Meshes = [document.Meshes[0] with { SourceOrdinal = document.Meshes[0].SourceOrdinal + 100 }] }, Daggerfall.Import.Publication.PublishedJson.Section),
            index.DependsOnArtifactIds);
        GeometryPublication moved = publication with { Artifacts = [.. publication.Artifacts.Where(artifact => artifact.RelativePath != GeometryPublication.IndexRelativePath), tampered] };

        Assert.Contains("index does not describe the records", Assert.Throws<InvalidOperationException>(() => moved.Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Takes_a_polygons_normal_from_a_triangle_that_has_area()
    {
        // A polygon whose leading points are collinear still states its own plane: substituting an up vector
        // for a polygon that spans a plane is publishing a geometric fact the source never stated.
        List<NormalizedVector3> collinear = [new(0F, 0F, 0F), new(1F, 0F, 0F), new(2F, 0F, 0F), new(1F, 1F, 0F)];
        NormalizedVector3 normal = MeshGeometry.Normal(collinear);

        Assert.Equal(0F, normal.X, 0.0001F);
        Assert.Equal(0F, normal.Y, 0.0001F);
        Assert.Equal(1F, MathF.Abs(normal.Z), 0.0001F);

        // A polygon with no area at all keeps the documented fallback.
        Assert.Equal(new NormalizedVector3(0F, 1F, 0F), MeshGeometry.Normal([new(0F, 0F, 0F), new(1F, 0F, 0F), new(2F, 0F, 0F)]));

        // The fan, the frame and the bounds are the same owner the dungeon's own geometry uses.
        List<NormalizedVector3> vertices = [];
        List<NormalizedVector3> normals = [];
        List<NormalizedVector2> uvs = [];
        List<NormalizedTriangle> triangles = [];
        (int first, int count) = MeshGeometry.AppendPolygon(vertices, normals, uvs, triangles, collinear, [new(0F, 0F), new(1F, 0F), new(1F, 1F), new(0F, 1F)], normal);
        Assert.Equal(0, first);
        Assert.Equal(2, count);
        Assert.Equal(4, vertices.Count);
        Assert.Equal(4, normals.Count);
        Assert.Equal(4, uvs.Count);
        Assert.All(triangles, triangle => Assert.InRange(triangle.FirstVertex, 0, 3));
        Assert.All(triangles, triangle => Assert.InRange(triangle.ThirdVertex, 0, 3));
        Assert.Equal(0F, MeshGeometry.Bounds(vertices).Minimum.X);
        Assert.Equal(2F, MeshGeometry.Bounds(vertices).Maximum.X);
    }

    [Fact]
    public void Refuses_a_summary_the_records_do_not_support_even_when_the_index_agrees()
    {
        // The index comparison fires first for an index that disagrees with the section, so the rules that
        // read the summary itself are pinned by rebuilding a consistent index around a wrong summary.
        GeometryPublication publication = Publish(["55000"], Textures());
        GeometryPublicationSummary wrong = publication.Summary with { Unused = publication.Summary.Unused + 1 };
        GeneratedSpatialArtifact index = publication.Artifacts.Single(artifact => artifact.RelativePath == GeometryPublication.IndexRelativePath);
        GeometryIndex document = System.Text.Json.JsonSerializer.Deserialize<GeometryIndex>(index.Bytes.Span, Daggerfall.Import.Publication.PublishedJson.SectionRead)!;
        GeneratedSpatialArtifact agreeing = new(
            index.Id,
            index.RelativePath,
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(document with { Summary = wrong }, Daggerfall.Import.Publication.PublishedJson.Section),
            index.DependsOnArtifactIds);
        GeometryPublication tampered = publication with
        {
            Summary = wrong,
            Artifacts = [.. publication.Artifacts.Where(artifact => artifact.RelativePath != GeometryPublication.IndexRelativePath), agreeing],
        };

        Assert.Contains("does not account for every record", Assert.Throws<InvalidOperationException>(() => tampered.Validate()).Message, StringComparison.Ordinal);

        // The index's own schema and inventory claims are checked too, not only its records.
        GeneratedSpatialArtifact foreign = new(
            index.Id,
            index.RelativePath,
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(document with { InventorySource = "elsewhere/ARCH3D.BSA" }, Daggerfall.Import.Publication.PublishedJson.Section),
            index.DependsOnArtifactIds);
        Assert.Contains("index does not describe the records", Assert.Throws<InvalidOperationException>(() => (publication with { Artifacts = [.. publication.Artifacts.Where(artifact => artifact.RelativePath != GeometryPublication.IndexRelativePath), foreign] }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_reference_that_is_not_a_mesh_number()
    {
        byte[] archive = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/ARCH3D.BSA"));
        GeometryPublicationBuilder.Create(new GeometryPublicationRequest(Archive.Value, archive, ["55000"], Textures()))
            .Validate();

        Assert.Contains("which is not a mesh number", Assert.Throws<InvalidOperationException>(() => GeometryPublicationBuilder.Create(
            new GeometryPublicationRequest(Archive.Value, archive, ["mesh/9004"], Textures()))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_publication_whose_claims_do_not_hold_together()
    {
        GeometryPublication publication = Publish(["55000"], Textures());
        GeometryMeshArtifact mesh = publication.Meshes[0];

        Assert.Contains("index does not describe the records", Assert.Throws<InvalidOperationException>(() => (publication with { Summary = publication.Summary with { Published = 2 } }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("index does not describe the records", Assert.Throws<InvalidOperationException>(() => (publication with { Summary = publication.Summary with { Unused = -1, Duplicate = publication.Summary.Duplicate + 1 } }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("index does not describe the records", Assert.Throws<InvalidOperationException>(() => (publication with { Summary = publication.Summary with { Missing = 1, Unused = publication.Summary.Unused - 1 } }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("hashes to", Assert.Throws<InvalidOperationException>(() => (publication with { Meshes = [mesh with { ContentDigest = new string('a', 64) }] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("does not carry", Assert.Throws<InvalidOperationException>(() => (publication with { Meshes = [mesh with { ArtifactId = "geometry/mesh-999" }] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("publishes mesh", Assert.Throws<InvalidOperationException>(() => (publication with { UnresolvedMeshes = [new GeometryUnresolvedMeshReference(mesh.MeshId, "the fixture says so")] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("without a reason", Assert.Throws<InvalidOperationException>(() => (publication with { UnresolvedMeshes = [new GeometryUnresolvedMeshReference("999", " ")] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("must carry its index", Assert.Throws<InvalidOperationException>(() => (publication with { Artifacts = [.. publication.Artifacts.Where(artifact => artifact.RelativePath != GeometryPublication.IndexRelativePath)] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("the set is ordered by number", Assert.Throws<InvalidOperationException>(() => (publication with { Meshes = [mesh, mesh with { MeshId = "1", SourceRecordId = 1, ArtifactId = "geometry/mesh-1", RelativePath = GeometryPublicationBuilder.MeshRelativePath(1) }] }).Validate()).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The meshes the shipped plan's own blocks reference: the five RDB blocks the published normalized
    /// dungeon is assembled from, read out of the block inventory the same way the tool reads them.
    /// </summary>
    private static string[] ReferencedByTheShippedPlan()
    {
        using JsonDocument pack = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        string[] blocks = ["B0000003.RDB", "B0000006.RDB", "B0000009.RDB", "B0000012.RDB", "S0000999.RDB"];
        return [.. pack.RootElement.GetProperty("blocks").GetProperty("records").EnumerateArray()
            .Where(record => blocks.Contains(record.GetProperty("sourceKey").GetString(), StringComparer.Ordinal))
            .SelectMany(record => record.GetProperty("objects").GetProperty("modelIds").EnumerateArray())
            .Select(model => model.GetString()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(model => model, StringComparer.Ordinal)];
    }

    [Fact]
    public void Publishes_the_geometry_the_shipped_plan_references()
    {
        // The corpus-level check: the meshes the published dungeon's own blocks name are all servable, and
        // the publication's own numbers agree with the inventory and its classification.
        string[] referenced = ReferencedByTheShippedPlan();
        Assert.True(referenced.Length > 100, $"the shipped plan references {referenced.Length} meshes");
        GeometryPublication publication = Publish(referenced, Textures());

        Assert.Equal(referenced.Length, publication.Meshes.Count);
        Assert.Empty(publication.UnresolvedMeshes);
        Assert.Equal(referenced.Length, publication.Summary.Published);
        Assert.Equal(10251 - referenced.Length - 14, publication.Summary.Unused);
        Assert.All(publication.Meshes, mesh => Assert.Equal(uint.Parse(mesh.MeshId), mesh.SourceRecordId));

        // The material links are the archive's own textures, and every one of them is either served or
        // reported: none is silently dropped.
        Assert.All(publication.Meshes, mesh => Assert.NotEmpty(mesh.Materials));
        Assert.Equal(
            publication.Meshes.Sum(mesh => mesh.Materials.Count(material => material.Disposition != GeometryMaterialDisposition.Resolved)),
            publication.UnresolvedMaterials.Count);

        // Determinism over the whole referenced set, which is what a pack's addresses depend on.
        GeometryPublication again = Publish([.. referenced.Reverse()], Textures());
        Assert.Equal(
            publication.Meshes.Select(mesh => (mesh.MeshId, mesh.ContentDigest)),
            again.Meshes.Select(mesh => (mesh.MeshId, mesh.ContentDigest)));
    }

    private static GeometryPublication Publish(IReadOnlyList<string> referenced, TextureLeafInventory textures) =>
        GeometryPublicationBuilder.Create(new GeometryPublicationRequest(Archive.Value, File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/ARCH3D.BSA")), referenced, textures));

    /// <summary>Checks each supplied leaf's recorded frame readability against bytes from its named source.</summary>
    private static int CrossCheckTextureReadability(
        IEnumerable<TextureLeafRecord> leaves,
        Func<TextureLeafRecord, ReadOnlyMemory<byte>> sourceBytes)
    {
        int checkedRecords = 0;
        foreach (TextureLeafRecord leaf in leaves)
        {
            TextureArchive archive = TextureArchive.Parse(sourceBytes(leaf).Span, leaf.Path, null);
            foreach (TextureRecordFacts facts in leaf.RecordFacts)
            {
                checkedRecords++;
                if (facts.Frames == 0)
                {
                    Assert.Empty(facts.UnreadableReason);
                    continue;
                }

                bool decodes = true;
                try
                {
                    _ = archive.DecodeFrame(facts.RecordIndex, 0);
                }
                catch (Exception failure) when (failure is Arena2FormatException or ArgumentOutOfRangeException)
                {
                    decodes = false;
                }

                Assert.Equal(decodes, facts.UnreadableReason.Length == 0);
            }
        }

        return checkedRecords;
    }

    /// <summary>Publishes the references against a mesh archive the test supplies.</summary>
    private static GeometryPublication Publish(IReadOnlyList<string> referenced, TextureLeafInventory textures, byte[] archive) =>
        GeometryPublicationBuilder.Create(new GeometryPublicationRequest(
            Arch3dInventoryReader.Read(archive, "local/arena2/ARCH3D.BSA"), archive, referenced, textures));

    /// <summary>The corpus's texture leaves, which is what a material reference resolves against.</summary>
    private static TextureLeafInventory Textures(params (int Id, string Path, ReadOnlyMemory<byte> Bytes)[] supplied) => TextureLeafInventory.Enumerate(
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "local/arena2"), "TEXTURE.*")
            .Select(path => (int.Parse(Path.GetFileName(path)["TEXTURE.".Length..]), Path.GetFileName(path), (ReadOnlyMemory<byte>)File.ReadAllBytes(path)))
            .Concat(supplied),
        "local/arena2");

    /// <summary>
    /// Builds one texture leaf holding a single record of the supplied extent. A single-frame record reads
    /// its rows at a 256-byte stride, so the frame a leaf cut short of the second row declares is one the
    /// decoder refuses; the full leaf carries both rows and decodes. A record of no extent is one no frame
    /// can have, whichever way the leaf is cut: the publication refuses it for the extent it declares, and
    /// the decoder here refuses the frame for the same reason.
    /// </summary>
    private static byte[] TextureLeaf(bool frameDecodes = true, short width = 2, short height = 2)
    {
        const int recordOffset = 46;
        const int dataOffset = 28;
        const int dataBytes = 258;
        byte[] bytes = new byte[recordOffset + (frameDecodes ? dataOffset + dataBytes : dataOffset + 2)];
        BitConverter.GetBytes((short)1).CopyTo(bytes, 0);
        BitConverter.GetBytes(recordOffset).CopyTo(bytes, 28);
        BitConverter.GetBytes(width).CopyTo(bytes, recordOffset + 4);
        BitConverter.GetBytes(height).CopyTo(bytes, recordOffset + 6);
        BitConverter.GetBytes((uint)dataOffset).CopyTo(bytes, recordOffset + 14);
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, recordOffset + 20);
        bytes[recordOffset + dataOffset] = 1;
        bytes[recordOffset + dataOffset + 1] = 2;
        if (frameDecodes)
        {
            bytes[recordOffset + dataOffset + 256] = 3;
            bytes[recordOffset + dataOffset + 257] = 4;
        }

        return bytes;
    }

    /// <summary>
    /// Builds an ARCH3D mesh record carrying one three-point plane per supplied texture reference, in the
    /// 64-byte header, twelve-byte point list and eight-byte plane headers the decoder reads.
    /// </summary>
    private static byte[] MeshFixture(params (ushort Archive, ushort Record)[] textures)
    {
        const int headerBytes = 64;
        const int planeHeaderBytes = 8;
        const int planePointBytes = 8;
        const int pointListBytes = 3 * 12;
        const int planeBytes = planeHeaderBytes + (3 * planePointBytes);
        byte[] bytes = new byte[headerBytes + pointListBytes + (textures.Length * planeBytes)];
        System.Text.Encoding.ASCII.GetBytes("v2.6").CopyTo(bytes, 0);
        BitConverter.GetBytes(3).CopyTo(bytes, 4);
        BitConverter.GetBytes(textures.Length).CopyTo(bytes, 8);
        BitConverter.GetBytes(headerBytes).CopyTo(bytes, 48);
        BitConverter.GetBytes(headerBytes + pointListBytes).CopyTo(bytes, 60);
        WriteMeshVector(bytes, 64, 0, 0, 0);
        WriteMeshVector(bytes, 76, 256, 0, 0);
        WriteMeshVector(bytes, 88, 0, 0, 256);
        for (int index = 0; index < textures.Length; index++)
        {
            int plane = headerBytes + pointListBytes + (index * planeBytes);
            bytes[plane] = 3;
            BitConverter.GetBytes((ushort)((textures[index].Archive << 7) | textures[index].Record)).CopyTo(bytes, plane + 2);
            for (int point = 0; point < 3; point++)
            {
                int entry = plane + planeHeaderBytes + (point * planePointBytes);
                BitConverter.GetBytes(point * 12).CopyTo(bytes, entry);
                BitConverter.GetBytes((short)(point == 1 ? 32 : 0)).CopyTo(bytes, entry + 4);
                BitConverter.GetBytes((short)(point == 2 ? 32 : 0)).CopyTo(bytes, entry + 6);
            }
        }

        return bytes;
    }

    private static void WriteMeshVector(byte[] bytes, int offset, int x, int y, int z)
    {
        BitConverter.GetBytes(x).CopyTo(bytes, offset);
        BitConverter.GetBytes(y).CopyTo(bytes, offset + 4);
        BitConverter.GetBytes(z).CopyTo(bytes, offset + 8);
    }

    /// <summary>Builds the numeric BSA variant around the supplied payloads.</summary>
    private static byte[] NumericArchive(params (uint Id, byte[] Payload)[] records)
    {
        int payloadBytes = records.Sum(record => record.Payload.Length);
        byte[] bytes = new byte[Arena2FormatConstants.BsaHeaderBytes + payloadBytes + (records.Length * Arena2FormatConstants.NumericBsaDirectoryEntryBytes)];
        bytes[0] = (byte)records.Length;
        bytes[1] = (byte)(records.Length >> 8);
        bytes[2] = (byte)(Arena2FormatConstants.NumericBsaDirectoryType & 0xff);
        bytes[3] = (byte)(Arena2FormatConstants.NumericBsaDirectoryType >> 8);
        int payload = Arena2FormatConstants.BsaHeaderBytes;
        int directory = Arena2FormatConstants.BsaHeaderBytes + payloadBytes;
        for (int index = 0; index < records.Length; index++)
        {
            records[index].Payload.CopyTo(bytes, payload);
            payload += records[index].Payload.Length;
            BitConverter.GetBytes(records[index].Id).CopyTo(bytes, directory + (index * 8));
            BitConverter.GetBytes(records[index].Payload.Length).CopyTo(bytes, directory + (index * 8) + 4);
        }

        return bytes;
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
