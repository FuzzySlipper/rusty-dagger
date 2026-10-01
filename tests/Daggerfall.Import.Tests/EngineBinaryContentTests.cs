using System.Text.Json;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The importer writes the Engine's binary content forms itself; there is no SDK writer. The interface is
/// the Engine's documented layout and its pinned fixtures, copied unchanged into Fixtures/engine from
/// rusty-engine: fixtures/csharp-spatial-artifact (valid.json, valid.rspatial) and
/// fixtures/csharp-static-mesh (triangle.static-mesh.json, triangle.rstatmsh). Each binary fixture is its
/// JSON fixture in the binary form, so encoding the JSON fixture's facts must reproduce it byte for byte.
/// </summary>
public sealed class EngineBinaryContentTests
{
    private static string Fixture(string name) => Path.Combine(TestData.RepositoryRoot, "tests/Daggerfall.Import.Tests/Fixtures/engine", name);

    [Fact]
    public void Encodes_the_engine_spatial_fixture_byte_for_byte()
    {
        Assert.Equal(File.ReadAllBytes(Fixture("valid.rspatial")), SpatialArtifactBinary.Write(SpatialFacts(File.ReadAllBytes(Fixture("valid.json")))));
    }

    [Fact]
    public void Encodes_the_engine_static_mesh_fixture_byte_for_byte()
    {
        Assert.Equal(File.ReadAllBytes(Fixture("triangle.rstatmsh")), StaticMeshBinary.Write(StaticMeshFacts(File.ReadAllBytes(Fixture("triangle.static-mesh.json")))));
    }

    [Fact]
    public void The_spatial_fixture_reads_back_as_its_json_facts()
    {
        EngineBinaryContent.SpatialArtifact artifact = EngineBinaryContent.ReadSpatial(File.ReadAllBytes(Fixture("valid.rspatial")));
        Assert.Equal("mesh/generated-floor", artifact.StaticMeshArtifactId);
        Assert.Equal("navigation/generated-floor", artifact.NavigationId);
        Assert.Equal([1, 0.25, 45, 1, 0.1], artifact.Config);
        Assert.Equal([(0u, 1u, 2u), (2u, 1u, 3u)], artifact.Triangles);
        Assert.Equal([0, 1, 2], artifact.Cells.Select(cell => cell.Column));
        Assert.All(artifact.Cells, cell => Assert.True(cell.Walkable));
    }

    [Fact]
    public void Refuses_a_cell_whose_level_the_engine_would_derive_differently()
    {
        SpatialArtifactFacts facts = SpatialFacts(File.ReadAllBytes(Fixture("valid.json")));
        SpatialArtifactFacts wrong = facts with { Cells = [.. facts.Cells.Select((cell, index) => index == 1 ? cell with { Level = 3 } : cell)] };
        Assert.Contains("derives a different level", Assert.Throws<InvalidOperationException>(() => SpatialArtifactBinary.Write(wrong)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_static_mesh_with_uvs_packs_them_after_the_normals()
    {
        StaticMeshFacts facts = StaticMeshFacts(File.ReadAllBytes(Fixture("triangle.static-mesh.json"))) with
        {
            Uvs = [new(0F, 0F), new(1F, 0F), new(0F, 1F)],
        };
        using EngineBinaryContent.StaticMesh mesh = EngineBinaryContent.ReadStaticMesh(StaticMeshBinary.Write(facts));
        JsonElement source = mesh.Root.GetProperty("payload").GetProperty("source");
        Assert.Equal("packedStreamsLeV2", source.GetProperty("encoding").GetString());
        Assert.Equal(16 + 36 + 36, source.GetProperty("uvsByteOffset").GetInt32());
        Assert.Equal(16 + 36 + 36 + 24, source.GetProperty("indicesByteOffset").GetInt32());
        Assert.Equal("uv", mesh.Root.GetProperty("payload").GetProperty("layout").GetProperty("attributes")[2].GetProperty("name").GetString());
        Assert.Equal([0F, 0F, 1F, 0F, 0F, 1F], mesh.Uvs!);
        Assert.Equal([0u, 1u, 2u], mesh.Indices);
        Assert.Equal(
            $"mesh-resource/{source.GetProperty("contentHash").GetString()!["sha256:".Length..]}",
            source.GetProperty("resource").GetString());
    }

    private static SpatialArtifactFacts SpatialFacts(byte[] json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement navigation = root.GetProperty("navigation");
        JsonElement config = navigation.GetProperty("config");
        SpatialArtifactPosition Position(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
        return new(
            root.GetProperty("staticMeshArtifactId").GetString()!,
            Position(root.GetProperty("bounds").GetProperty("min")),
            Position(root.GetProperty("bounds").GetProperty("max")),
            [.. root.GetProperty("collision").GetProperty("positions").EnumerateArray().Select(Position)],
            [.. root.GetProperty("collision").GetProperty("triangles").EnumerateArray().Select(value => new SpatialArtifactTriangle(value[0].GetUInt32(), value[1].GetUInt32(), value[2].GetUInt32()))],
            navigation.GetProperty("id").GetString()!,
            new(
                config.GetProperty("cellSize").GetDouble(),
                config.GetProperty("levelQuantum").GetDouble(),
                config.GetProperty("maximumSlopeDegrees").GetDouble(),
                config.GetProperty("requiredHeadroom").GetDouble(),
                config.GetProperty("supportProbeDrop").GetDouble()),
            [.. navigation.GetProperty("cells").EnumerateArray().Select(cell => new SpatialArtifactCell(
                cell.GetProperty("column").GetInt32(),
                cell.GetProperty("row").GetInt32(),
                cell.GetProperty("level").GetInt64(),
                cell.GetProperty("supportHeight").GetDouble(),
                cell.GetProperty("walkable").GetBoolean()))]);
    }

    private static StaticMeshFacts StaticMeshFacts(byte[] json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement payload = root.GetProperty("payload");
        JsonElement source = payload.GetProperty("source");
        NormalizedVector3[] Vectors(string name)
        {
            float[] values = [.. source.GetProperty(name).EnumerateArray().Select(value => value.GetSingle())];
            return [.. Enumerable.Range(0, values.Length / 3).Select(index => new NormalizedVector3(values[index * 3], values[(index * 3) + 1], values[(index * 3) + 2]))];
        }
        NormalizedVector3 Vector(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
        return new(
            root.GetProperty("asset").GetString()!,
            new(Vector(payload.GetProperty("bounds").GetProperty("min")), Vector(payload.GetProperty("bounds").GetProperty("max"))),
            Vectors("positions"),
            Vectors("normals"),
            null,
            [.. source.GetProperty("indices").EnumerateArray().Select(value => value.GetUInt32())],
            [.. payload.GetProperty("groups").EnumerateArray().Select(group => new StaticMeshGroup(
                group.GetProperty("materialSlot").GetInt32(), group.GetProperty("start").GetInt32(), group.GetProperty("count").GetInt32()))],
            [.. root.GetProperty("materialSlots").EnumerateArray().Select(slot => new StaticMeshMaterialBinding(
                slot.GetProperty("material").GetString()!, slot.GetProperty("slot").GetInt32()))]);
    }
}
