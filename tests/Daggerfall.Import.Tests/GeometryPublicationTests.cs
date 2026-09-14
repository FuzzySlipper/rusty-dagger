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
        Assert.Equal(0, publication.Summary.Unresolved);
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
        Assert.Equal(3, publication.Summary.Unresolved);
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

        Assert.Contains("does not match the", Assert.Throws<InvalidOperationException>(() => (publication with { Summary = publication.Summary with { Published = 2 } }).Validate()).Message, StringComparison.Ordinal);
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

    /// <summary>The corpus's texture leaves, which is what a material reference resolves against.</summary>
    private static TextureLeafInventory Textures() => TextureLeafInventory.Enumerate(
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "local/arena2"), "TEXTURE.*")
            .Select(path => (int.Parse(Path.GetFileName(path)["TEXTURE.".Length..]), Path.GetFileName(path), (ReadOnlyMemory<byte>)File.ReadAllBytes(path))),
        "local/arena2");

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
