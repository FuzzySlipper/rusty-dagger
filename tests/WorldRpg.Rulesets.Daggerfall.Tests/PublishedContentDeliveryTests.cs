using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published classic media as admitted content a caller reads by name.
/// </summary>
/// <remarks>
/// The delivery half of the UI-art work: artifacts are published into the same content tree the
/// product admits, so a consumer resolves one by its content-relative name instead of by a filesystem
/// convention or a hand-kept index. The inventory beside them is generated from the publication, and
/// this checks it against the bytes on disk rather than trusting either side alone.
/// </remarks>
public sealed class PublishedContentDeliveryTests
{
    [Fact]
    public void Reads_a_published_artifact_by_name_from_admitted_content()
    {
        ProductContent content = AdmittedContent();

        // The death screen is the artifact this work started from, and it is reachable by its
        // content-relative name with no filesystem convention in the caller.
        Assert.True(content.TryReadFile("worldrpg/media/ui/screen-death.png", out ProductContentFile screen));
        Assert.NotEmpty(screen.Bytes.ToArray());
        Assert.Equal("worldrpg/media/ui/screen-death.png", Encoding.UTF8.GetString(screen.Path.Span));

        // The images the product already drew are inside the admitted tree too, which is what makes the
        // bundled-path fallback unnecessary rather than merely unfashionable.
        Assert.True(content.TryReadFile("worldrpg/media/ui/hud-chrome-main.png", out ProductContentFile chrome));
        Assert.NotEmpty(chrome.Bytes.ToArray());

        // A name the content does not carry fails as a miss rather than resolving to something else.
        Assert.False(content.TryReadFile("worldrpg/media/ui/screen-title.png", out _));

        // The directory read sees the published group without being told its members: this is what the
        // generated inventory replaces a hand-maintained list with.
        Assert.Contains(content.ReadDirectory("worldrpg/media/ui"), file => Encoding.UTF8.GetString(file.Path.Span) == "worldrpg/media/ui/screen-death.png");
        Assert.Contains(content.ReadDirectory("worldrpg/media/audio"), file => Encoding.UTF8.GetString(file.Path.Span).StartsWith("worldrpg/media/audio/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_generated_inventory_agrees_with_the_bytes_it_indexes()
    {
        ProductContent content = AdmittedContent();
        JsonElement inventory = JsonDocument.Parse(content.ReadBytes("worldrpg/media/classic-media-inventory.json").ToArray()).RootElement;
        Assert.Equal(1, inventory.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("daggerfall-import-tool classic-media", inventory.GetProperty("generator").GetString());

        // Every entry the inventory lists is present and hashes to what it says, and every published
        // artifact is listed: an index that drifts from its content is worse than none, because a
        // consumer trusts it.
        // The inventory names artifacts relative to the group it publishes into, while the admitted
        // snapshot names them relative to the content root, so the group appears in one and not the
        // other. That difference is the open question on this work - which root is the declared group -
        // and this test states it rather than hiding it: the closure below is checked across it.
        HashSet<string> listed = [];
        foreach (JsonElement artifact in inventory.GetProperty("artifacts").EnumerateArray())
        {
            string path = artifact.GetProperty("path").GetString()!;
            byte[] bytes = content.ReadBytes(GroupPrefix + path).ToArray();
            Assert.Equal(artifact.GetProperty("byteLength").GetInt64(), bytes.Length);
            Assert.Equal(artifact.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            listed.Add(GroupPrefix + path);
        }

        HashSet<string> published = [.. content.ReadDirectory("worldrpg/media", recursive: true)
            .Select(file => Encoding.UTF8.GetString(file.Path.Span))
            .Where(path => !path.EndsWith("classic-media-inventory.json", StringComparison.Ordinal))];
        Assert.Equal(published.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
        Assert.Equal(58, listed.Count);
    }

    /// <summary>The group's path prefix in the admitted snapshot, which the inventory does not carry.</summary>
    private const string GroupPrefix = "worldrpg/";

    private static ProductContent AdmittedContent()
    {
        string contentRoot = Path.Combine(RepositoryRoot(), "content");
        string selected = Path.Combine(contentRoot, "worldrpg");
        ProductContentFile[] files = [.. Directory.GetFiles(selected, "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(
                Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')),
                File.ReadAllBytes(path)))];
        return new ProductContent(files);
    }

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AGENTS.md")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }
}
