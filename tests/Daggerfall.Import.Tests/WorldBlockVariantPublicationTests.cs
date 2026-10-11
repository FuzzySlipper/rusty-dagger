using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class WorldBlockVariantPublicationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "block-variants-" + Guid.NewGuid().ToString("N"));
    public WorldBlockVariantPublicationTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void Authored_variant_is_in_the_same_closure_with_normalized_identity_and_provenance()
    {
        var publication = Publication();
        string descriptor = Write("variants.json", """{"variants":[{"variant":"lit","document":"lit.json"}]}""");
        Write("lit.json", JsonSerializer.Serialize(Block() with { Lights = [new("light/test/torch", new(1, 2, 3), 10, 17, new(1, 1, 1))] }, PublishedJson.Section));
        var result = WorldBlockVariantPublication.Append(publication, descriptor);
        var index = JsonNode.Parse(result.Plan.Artifacts.Single(value => value.RelativePath == "blocks.json").Bytes.Span)!;
        var variant = Assert.Single(index["variants"]!.AsArray())!;
        Assert.Equal("TEST.RDB", variant["sourceKey"]!.GetValue<string>());
        Assert.Equal("rdb/test", variant["key"]!.GetValue<string>());
        Assert.Contains(result.Plan.Manifest.Artifacts, value => value.RelativePath == "lit.json");
        Assert.Contains(result.Plan.Manifest.AuthoredOverlays, value => value.Path == "world-block-variants/lit.json");
        Assert.Contains(result.Plan.Manifest.Artifacts.Single(value => value.RelativePath == "blocks.json").DependsOnPaths, value => value == "lit.json");
    }

    [Theory]
    [InlineData("-")]
    [InlineData("lit;dim")]
    public void Invalid_variant_names_are_rejected(string name)
    {
        string descriptor = Write("variants.json", $$"""{"variants":[{"variant":"{{name}}","document":"lit.json"}]}""");
        Write("lit.json", JsonSerializer.Serialize(Block(), PublishedJson.Section));
        Assert.NotNull(Record.Exception(() => WorldBlockVariantPublication.Append(Publication(), descriptor)));
    }

    [Fact]
    public void Artifact_path_collision_cannot_replace_base_geometry()
    {
        string descriptor = Write("variants.json", """{"variants":[{"variant":"lit","document":"base.json"}]}""");
        Write("base.json", JsonSerializer.Serialize(Block() with { Lights = [new("light/test/torch", new(1, 2, 3), 10, 17, new(1, 1, 1))] }, PublishedJson.Section));
        Assert.Contains("collides", Assert.Throws<InvalidOperationException>(() => WorldBlockVariantPublication.Append(Publication(), descriptor)).Message);
    }

    private static DaggerfallWorldBlock Block() => new("rdb/test", DaggerfallWorldBlockKind.Rdb, "TEST.RDB", 0, null, null, null, []);
    private static WorldBlocksPublication Publication()
    {
        var index = new DaggerfallWorldBlockIndex("shared", new(0, 0, 1), [new("rdb/test", DaggerfallWorldBlockKind.Rdb, "TEST.RDB", null, "base.json", null)], [], []);
        var plan = ImportPublicationPlan.Create(new("daggerfall-import/world-blocks", "test", [new("arena2/BLOCKS.BSA", ContentDigest.Compute("test"u8), 4)]),
            [new("base.json", JsonSerializer.SerializeToUtf8Bytes(Block(), PublishedJson.Section)), new("blocks.json", JsonSerializer.SerializeToUtf8Bytes(index, PublishedJson.Section))]);
        return new(plan, index, []);
    }
    private string Write(string path, string text) { string file = Path.Combine(root, path); File.WriteAllText(file, text); return file; }
}
