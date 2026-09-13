using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
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
        HashSet<string> listed = [];
        foreach (JsonElement artifact in inventory.GetProperty("artifacts").EnumerateArray())
        {
            string path = artifact.GetProperty("path").GetString()!;
            byte[] bytes = content.ReadBytes(path).ToArray();
            Assert.Equal(artifact.GetProperty("byteLength").GetInt64(), bytes.Length);
            Assert.Equal(artifact.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            listed.Add(path);
        }

        HashSet<string> published = [.. content.ReadDirectory("worldrpg/media", recursive: true)
            .Select(file => Encoding.UTF8.GetString(file.Path.Span))
            .Where(path => !path.EndsWith("classic-media-inventory.json", StringComparison.Ordinal))];
        Assert.Equal(published.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
        // The published group carries the sixty-one media artifacts and the sound catalog that
        // describes the whole archive, and the inventory indexes both because both are content.
        Assert.Contains("worldrpg/media/audio/classic-sound-catalog.json", listed);
        Assert.Equal(62, listed.Count);
    }

    /// <summary>
    /// The inventory states the media identity of every artifact that has one, which is how a consumer
    /// resolves a published name to bytes: the UI art the DOM draws is named here rather than staged
    /// beside the UI, and the identity is the one the pack's own manifest publishes for the same bytes.
    /// </summary>
    [Fact]
    public void The_generated_inventory_states_the_media_identity_of_each_published_artifact()
    {
        ProductContent content = AdmittedContent();
        JsonElement inventory = JsonDocument.Parse(content.ReadBytes(DaggerfallUiArt.InventoryPath).ToArray()).RootElement;
        Dictionary<string, (string Path, long ByteLength)> identified = new(StringComparer.Ordinal);
        foreach (JsonElement artifact in inventory.GetProperty("artifacts").EnumerateArray())
        {
            if (!artifact.TryGetProperty("mediaId", out JsonElement mediaId)) continue;
            string path = artifact.GetProperty("path").GetString()!;
            long byteLength = artifact.GetProperty("byteLength").GetInt64();
            Assert.True(identified.TryAdd(mediaId.GetString()!, (path, byteLength)), $"The inventory names '{mediaId}' twice.");
            // Every stated identity resolves to admitted bytes of the stated length under that name.
            Assert.Equal(byteLength, content.ReadBytes(path).Length);
        }

        // The six classic UI images the group publishes, the authored skins, and every item icon the
        // pack names for its catalog: the death screen is the artifact this delivery path exists for.
        Assert.Equal(("worldrpg/media/ui/screen-death.png", 63124L), identified["screen.death"]);
        Assert.Equal("worldrpg/media/ui/window-character-sheet-chrome.png", identified["window.character-sheet.chrome"].Path);
        Assert.Equal("worldrpg/media/ui/inventory-icons/inventory-icon-iron-dagger.png", identified["inventory.icon.iron-dagger"].Path);
        Assert.Equal("worldrpg/media/ui/authored/inventory-skin-panel-slate-v1.png", identified["inventory.skin.panel-slate.v1"].Path);
        Assert.Equal(61, identified.Count);

        // The identities the group states are the identities the pack publishes for the same images,
        // so a consumer that asks by media name cannot be answered with a different artifact.
        JsonElement classic = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/imports/privateers-hold/media/classic/manifest.json"))).RootElement;
        Dictionary<string, string> packPaths = [];
        foreach (JsonElement resource in classic.GetProperty("media").GetProperty("resources").EnumerateArray())
            packPaths[resource.GetProperty("id").GetString()!] = resource.GetProperty("relativePath").GetString()!;
        Assert.Equal("media/ui/inventory-icons/inventory-icon-iron-dagger.png", packPaths["inventory.icon.iron-dagger"]);
        Assert.Equal("media/ui/window-character-sheet-chrome.png", packPaths["window.character-sheet.chrome"]);
        Assert.Equal(
            Path.GetFileName(packPaths["inventory.icon.iron-dagger"]),
            Path.GetFileName(identified["inventory.icon.iron-dagger"].Path));
    }

    [Fact]
    public void The_published_sound_catalog_names_clips_the_admitted_media_carries()
    {
        ProductContent content = AdmittedContent();
        JsonElement catalog = JsonDocument.Parse(content.ReadBytes("worldrpg/media/audio/classic-sound-catalog.json").ToArray()).RootElement;

        // The catalog is the availability record for the whole archive, delivered by name like any
        // other artifact, so a consumer can see every clip and its disposition rather than assuming
        // the six the product plays today.
        Assert.Equal(1, catalog.GetProperty("schemaVersion").GetInt32());
        JsonElement[] clips = [.. catalog.GetProperty("clips").EnumerateArray()];
        Assert.Equal(459, clips.Length);
        JsonElement[] admitted = [.. clips.Where(clip => clip.GetProperty("disposition").GetString() == "admitted")];
        Assert.Equal(6, admitted.Length);
        Assert.All(clips.Where(clip => clip.GetProperty("disposition").GetString() != "admitted"), clip => Assert.Equal(JsonValueKind.Null, clip.GetProperty("mediaId").ValueKind));

        // Which archive clip each cue is belongs to the product rather than to the producer that just
        // stated it, so the binding is pinned here as literals: a republish that swapped two identities
        // would move the file and the importer's table together and otherwise stay green.
        Assert.Equal([106, 108, 109, 110, 111, 112], admitted.Select(clip => clip.GetProperty("ordinal").GetInt32()));
        Assert.Equal(
            ["audio.melee.dagger.swing", "audio.melee.hit.1", "audio.melee.hit.2", "audio.melee.hit.3", "audio.melee.hit.4", "audio.melee.hit.5"],
            admitted.Select(clip => clip.GetProperty("mediaId").GetString()));

        // Every admitted reference resolves inside admitted content: the classic media manifest the
        // pack reads carries exactly those media identities, so the catalog's references are the ones
        // the product can follow rather than names only the importer knows.
        JsonElement manifest = JsonDocument.Parse(content.ReadBytes("worldrpg/imports/privateers-hold/media/classic/manifest.json").ToArray()).RootElement;
        HashSet<string> carried = [.. manifest.GetProperty("media").GetProperty("resources").EnumerateArray().Select(resource => resource.GetProperty("id").GetString()!)];
        Assert.All(admitted, clip => Assert.Contains(clip.GetProperty("mediaId").GetString()!, carried));
        Assert.Equal(admitted.Length, manifest.GetProperty("audio").EnumerateArray().Count());
    }

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
