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
        Assert.False(content.TryReadFile("worldrpg/media/ui/screen-no-such-artifact.png", out _));

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
        // The published group carries the seventy-four media artifacts and the sound catalog that
        // describes the whole archive, and the inventory indexes both because both are content.
        Assert.Contains("worldrpg/media/audio/classic-sound-catalog.json", listed);
        Assert.Equal(80, listed.Count);

        // The families this repository cannot read at all are stated with their files and the donor
        // anchor, so a consumer that finds no artifact for one of them can tell "not published" from
        // "not readable" instead of guessing.
        (string Family, string[] Files, string Anchor)[] unreadable =
        [
            .. inventory.GetProperty("unreadableFamilies").EnumerateArray()
                .Select(family => (
                    family.GetProperty("family").GetString()!,
                    family.GetProperty("files").EnumerateArray().Select(file => file.GetString()!).ToArray(),
                    family.GetProperty("donorAnchor").GetString()!)),
        ];
        Assert.Equal([".CEL", ".BSS"], unreadable.Select(family => family.Family));
        Assert.Equal(["MAGE.CEL", "ROGUE.CEL", "WARRIOR.CEL"], unreadable.Single(family => family.Family == ".CEL").Files);
        Assert.Equal(["CMPA00I0.BSS", "CMPA01I0.BSS", "CMPA02I0.BSS"], unreadable.Single(family => family.Family == ".BSS").Files);
        Assert.All(unreadable, family => Assert.StartsWith("Assets/Scripts/API/", family.Anchor, StringComparison.Ordinal));

        // The six screens drawn with the palette inside their own file state that conversion fact with
        // its donor anchor, so a consumer can tell why their colours are what they are - and nothing
        // else in the inventory claims a palette it does not carry.
        (string MediaId, string Source, int Scale, string Anchor)[] palettes =
        [
            .. inventory.GetProperty("artifacts").EnumerateArray()
                .Where(entry => entry.TryGetProperty("palette", out _))
                .Select(entry => (
                    entry.GetProperty("mediaId").GetString()!,
                    entry.GetProperty("palette").GetProperty("source").GetString()!,
                    entry.GetProperty("palette").GetProperty("channelScale").GetInt32(),
                    entry.GetProperty("palette").GetProperty("donorAnchor").GetString()!)),
        ];
        Assert.Equal(
            ["screen.character-generation", "screen.death", "screen.intro", "screen.pick.02", "screen.pick.03", "screen.title"],
            palettes.Select(palette => palette.MediaId).Order(StringComparer.Ordinal));
        Assert.All(palettes, palette =>
        {
            Assert.Equal("embedded-in-source-file", palette.Source);
            Assert.Equal(4, palette.Scale);
            Assert.Equal("ImgFile.ReadPalette", palette.Anchor);
        });
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

        // The twelve classic UI images the group publishes, the authored skins, and every item icon
        // the pack names for its catalog: the death screen is the artifact this delivery path exists
        // for, and the service screens are the ones a UI task binds by slot.
        Assert.Equal(("worldrpg/media/ui/screen-death.png", 63124L), identified["screen.death"]);
        Assert.Equal("worldrpg/media/ui/window-character-sheet-chrome.png", identified["window.character-sheet.chrome"].Path);
        Assert.Equal("worldrpg/media/ui/window-book-reader.png", identified["window.book.reader"].Path);
        Assert.Equal("worldrpg/media/ui/window-rest-panel.png", identified["window.rest.panel"].Path);
        Assert.Equal("worldrpg/media/ui/window-merchant-cost.png", identified["window.merchant.cost"].Path);
        Assert.Equal("worldrpg/media/ui/window-guild-service.png", identified["window.guild.service"].Path);
        Assert.Equal("worldrpg/media/ui/window-bank-panel.png", identified["window.bank.panel"].Path);
        // The companions the donor composes those screens from are published beside the panels.
        Assert.Equal("worldrpg/media/ui/window-rest-hours-past.png", identified["window.rest.hours-past"].Path);
        Assert.Equal("worldrpg/media/ui/window-rest-hours-remaining.png", identified["window.rest.hours-remaining"].Path);
        Assert.Equal("worldrpg/media/ui/window-guild-member.png", identified["window.guild.member"].Path);
        Assert.Equal("worldrpg/media/ui/window-merchant-buttons-buy.png", identified["window.merchant.buttons.buy"].Path);
        Assert.Equal("worldrpg/media/ui/window-merchant-buttons-identify.png", identified["window.merchant.buttons.identify"].Path);
        Assert.Equal("worldrpg/media/ui/inventory-icons/inventory-icon-iron-dagger.png", identified["inventory.icon.iron-dagger"].Path);
        Assert.Equal("worldrpg/media/ui/authored/inventory-skin-panel-slate-v1.png", identified["inventory.skin.panel-slate.v1"].Path);
        Assert.Equal(79, identified.Count);

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

        // Every identity both sides name must be the same file, so a republish that moved one side and
        // not the other cannot pass on one sampled row. The identities the pack does not name yet are
        // the death screen and the five service screens: the committed bundle publication predates
        // them, and republishing that bundle is what adds them - so this line fails, loudly, the day
        // it does.
        Assert.Equal(
            [.. identified.Keys.Where(packPaths.ContainsKey).Order(StringComparer.Ordinal)],
            [.. packPaths.Keys.Where(identified.ContainsKey).Order(StringComparer.Ordinal)]);
        Assert.Equal(
            [
                // The five screens that carry their own palette joined the closure, so the import bundle
                // that predates them trails five more identities.
                "screen.character-generation", "screen.death", "screen.intro", "screen.pick.02", "screen.pick.03", "screen.title",
                "window.bank.panel", "window.book.reader", "window.guild.member", "window.guild.service",
                "window.merchant.buttons.buy", "window.merchant.buttons.identify", "window.merchant.buttons.repair",
                "window.merchant.buttons.sell", "window.merchant.buttons.sell-gold", "window.merchant.cost",
                "window.rest.hours-past", "window.rest.hours-remaining", "window.rest.panel",
            ],
            identified.Keys.Except(packPaths.Keys).Order(StringComparer.Ordinal));
        Assert.All(
            identified.Keys.Where(packPaths.ContainsKey),
            id => Assert.Equal(Path.GetFileName(packPaths[id]), Path.GetFileName(identified[id].Path)));
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

    [Fact]
    public void Published_ui_slots_name_admitted_media_and_retain_their_source_digest()
    {
        string root = RepositoryRoot();
        JsonElement inventory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/media/classic-media-inventory.json"))).RootElement;

        // A slot is what a consumer binds, so the published inventory has to name one for every UI
        // image the pack carries, together with the media identity and the bytes it was decoded to.
        Dictionary<string, List<(string Path, string Sha256, string MediaId)>> slots = new(StringComparer.Ordinal);
        foreach (JsonElement artifact in inventory.GetProperty("artifacts").EnumerateArray())
        {
            if (!artifact.TryGetProperty("slot", out JsonElement slot)) continue;
            string mediaId = artifact.GetProperty("mediaId").GetString()!;
            Assert.StartsWith("worldrpg/media/ui/", artifact.GetProperty("path").GetString()!, StringComparison.Ordinal);
            Assert.NotEmpty(mediaId);
            if (!slots.TryGetValue(slot.GetString()!, out List<(string Path, string Sha256, string MediaId)>? filled)) slots[slot.GetString()!] = filled = [];
            // One artifact per media identity: a slot may hold several, but the same identity twice
            // would leave a consumer unable to tell which bytes belong to which part.
            Assert.DoesNotContain(filled, entry => entry.MediaId == mediaId);
            filled.Add((artifact.GetProperty("path").GetString()!, artifact.GetProperty("sha256").GetString()!, mediaId));
        }

        Assert.Equal(
            [
                "bank", "book", "characterGeneration", "characterSheet", "death", "guild", "hudChrome", "hudVitalFatigue",
                "hudVitalHealth", "hudVitalMagicka", "intro", "inventory", "merchant", "pick", "rest", "title",
            ],
            slots.Keys.OrderBy(name => name, StringComparer.Ordinal));

        // A slot names a screen. Where the donor composes a screen from several images, the slot holds
        // every part: a rest dialog without its hour counters, a trade window without its button bars
        // or a guild popup without its member art is a fragment, not the screen the slot names.
        Assert.Equal(3, slots["rest"].Count);
        Assert.Equal(6, slots["merchant"].Count);
        Assert.Equal(2, slots["guild"].Count);
        Assert.Single(slots["book"]);
        Assert.Single(slots["bank"]);
        Assert.Equal(
            ["window.rest.hours-past", "window.rest.hours-remaining", "window.rest.panel"],
            slots["rest"].Select(entry => entry.MediaId).Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "window.merchant.buttons.buy", "window.merchant.buttons.identify", "window.merchant.buttons.repair",
                "window.merchant.buttons.sell", "window.merchant.buttons.sell-gold", "window.merchant.cost",
            ],
            slots["merchant"].Select(entry => entry.MediaId).Order(StringComparer.Ordinal));
        Assert.Equal(["window.guild.member", "window.guild.service"], slots["guild"].Select(entry => entry.MediaId).Order(StringComparer.Ordinal));

        Assert.Contains("window.book.reader", Media("book"));
        Assert.Contains("window.inventory.chrome", Media("inventory"));
        Assert.Contains("window.bank.panel", Media("bank"));

        // Every published part resolves to delivered bytes that still hash to the recorded digest.
        foreach ((string slot, List<(string Path, string Sha256, string MediaId)> parts) in slots)
        {
            foreach ((string path, string sha256, _) in parts)
            {
                string file = Path.Combine(root, "content", path);
                Assert.True(File.Exists(file), $"Published UI slot '{slot}' names '{path}', which is not part of the delivered content.");
                Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
            }
        }

        string[] Media(string slot) => [.. slots[slot].Select(entry => entry.MediaId)];
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
