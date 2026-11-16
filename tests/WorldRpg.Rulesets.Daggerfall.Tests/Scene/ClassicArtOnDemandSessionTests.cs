using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Kit;
using Xunit;
using Xunit.Abstractions;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// With refreshed art drawn, a site and the UI art open no classic file of an entry the art pack replaces: a session
/// composes and plays with those files absent. The developer's <c>world.classic-art on</c> builds them at the switch, and
/// refuses by name when one is absent.
/// </summary>
public sealed class ClassicArtOnDemandSessionTests(ITestOutputHelper output)
{
    private const string Payload = "worldrpg/packs/daggerfall.art-replacements/payload.json";

    private static readonly Lazy<DaggerfallArtReplacements> Art = new(() =>
        DaggerfallArtReplacements.Read(TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content", Payload)), "daggerfall.art-replacements"));

    /// <summary>The shipped locations assembled with the art pack's remodels, as the product assembles them.</summary>
    private static readonly Lazy<DaggerfallLocationAssembly> Remodelled = new(() =>
    {
        DaggerfallData data = Data(FullContent(TestData.RepositoryRoot));
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(data);
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(data, media, TestPayload.Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(data, media));
        return new DaggerfallLocationAssembly(TestPayload.Definitions, new DaggerfallWorldBlocks(data), () => product.Value, () => meshes.Value, Art.Value.Models);
    });

    public static TheoryData<string> Sites => [DaggerfallWorldProfileIds.Dungeon(PrivateersHold).LogicalId, DaggerfallWorldProfileIds.Exterior(Charing).LogicalId];

    [Theory]
    [MemberData(nameof(Sites))]
    public void A_site_and_the_ui_art_compose_and_play_without_the_classic_files_the_art_pack_replaces(string logicalId)
    {
        DaggerfallArtReplacements art = Art.Value;
        DaggerfallSiteProfile site = Site(logicalId);
        ContentFake content = Content(site, art);
        HashSet<string> replaced = [.. ReplacedSiteFiles(site, art), .. ReplacedNatureFiles(site, art), Candle(site, art)];
        foreach (string path in replaced.Concat(ReplacedUiImages(art).Values)) content.Remove(path);
        using DaggerfallSession session = Start(content, site, art, out _, out _);
        // The Light spell's candle burns in its refreshed flat.
        session.Sites.Projection.Appearance.UpdateMagicCandle(Vector3.Zero);
        for (ulong step = 1; step <= 3; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.Empty(content.Opened.Intersect(replaced));

        // The developer's switch builds the classic visuals at once, and refuses by name when a file is absent.
        DebugCommandResult refused = session.Commands<IDeveloperSceneCommands>().ClassicArt("on");
        Assert.False(refused.Succeeded);
        Assert.StartsWith("Classic art cannot be drawn: ", refused.Message, StringComparison.Ordinal);
        Assert.Contains(replaced, path => refused.Message.Contains(path, StringComparison.Ordinal));
        // The run stays in refreshed art and goes on drawing it.
        int tried = content.Opened.Count;
        session.Update(new ProductUpdate(OuterUpdate(4), []));
        Assert.Empty(content.Opened.Skip(tried).Intersect(replaced));
    }

    [Theory]
    [MemberData(nameof(Sites))]
    public void Classic_art_opens_the_replaced_classic_files_at_the_switch_and_draws_them(string logicalId)
    {
        DaggerfallArtReplacements art = Art.Value;
        DaggerfallSiteProfile site = Site(logicalId);
        ContentFake content = Content(site, art);
        HashSet<string> replaced = [.. ReplacedSiteFiles(site, art), Candle(site, art)];
        HashSet<string> nature = [.. ReplacedNatureFiles(site, art)];
        using DaggerfallSession session = Start(content, site, art, out EngineContextFake engine, out string[] delivered);
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        // A Light spell's candle burns as the switch is made (the session puts it out again on its next update, as no spell burns).
        session.Sites.Projection.Appearance.UpdateMagicCandle(Vector3.Zero);
        Report(logicalId, content, [.. replaced, .. nature], delivered, art);
        Assert.Empty(content.Opened.Intersect(replaced.Concat(nature)));
        Assert.Empty(delivered.Intersect(ReplacedUiImages(art).Keys));

        // The switch itself opens every replaced file the site draws, the burning candle's among them, before any update draws.
        Assert.True(session.Commands<IDeveloperSceneCommands>().ClassicArt("on").Succeeded);
        string[] unopened = [.. replaced.Except(content.Opened).Select(path => $"{path} {Holder(site, path)}")];
        foreach (string path in unopened) output.WriteLine($"unopened: {path}");
        Assert.Empty(unopened);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        // The UI has the classic images it draws without naming them.
        string[] demanded = [.. Assert.IsType<object?[]>(Assert.IsType<Dictionary<string, object?>>(
            Assert.IsType<Dictionary<string, object?>>(engine.Published())["uiArtDemanded"])["images"])
            .Cast<Dictionary<string, object?>>().Select(image => Assert.IsType<string>(image["id"]))];
        Assert.Superset(new HashSet<string>(["screen.title", "screen.death", "spell.icons", "spell.selectors", "effect.glint.magic", "effect.glint.coins"]),
            new HashSet<string>(demanded));
        // Outdoors, the nature the resident cells place too, each sprite at most once for the terrain and once for the site
        // where it also places that record as a decoration: what the switch built is what classic art draws.
        Assert.Equal(nature.Count > 0, content.Opened.Any(nature.Contains));
        HashSet<string> placed = [.. site.Decorations.SelectMany(flat => new[] { flat.Summer.TexturePath, flat.Winter.TexturePath })];
        Assert.All(content.Opened.Where(nature.Contains).GroupBy(path => path), opens => Assert.InRange(opens.Count(), 1, placed.Contains(opens.Key) ? 2 : 1));
        Assert.True(session.Commands<IDeveloperSceneCommands>().ClassicArt("off").Succeeded);
        session.Update(new ProductUpdate(OuterUpdate(3), []));
    }

    private static DaggerfallSiteProfile Site(string logicalId) =>
        DaggerfallWorldProfileIds.TryParse(logicalId, out DaggerfallWorldProfileKey key) ? Remodelled.Value.Assemble(key) : throw new ArgumentException(logicalId);

    private static DaggerfallSession Start(ContentFake content, DaggerfallSiteProfile site, DaggerfallArtReplacements art, out EngineContextFake engine,
        out string[] delivered)
    {
        List<string> releases = [];
        engine = EngineContextFake.Create(content, SpatialFake.Create(site.Geometry.Spatial[0].Sha256, releases).Service, new AppearanceFake(releases));
        DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, site, DaggerfallTuning.Defaults)
        {
            Data = DataOf(content), ArtReplacements = art, Profiles = new DaggerfallSiteProfiles([site]),
        });
        session.PublishInitial();
        // The images the first snapshot delivers up front, which the UI art read at composition.
        delivered = [.. Assert.IsType<object?[]>(Assert.IsType<Dictionary<string, object?>>(
            Assert.IsType<Dictionary<string, object?>>(engine.Published())["uiArt"])["images"])
            .Cast<Dictionary<string, object?>>().Select(image => Assert.IsType<string>(image["id"]))];
        return session;
    }

    /// <summary>A session's content at <paramref name="site"/> with the art pack.</summary>
    private static ContentFake Content(DaggerfallSiteProfile site, DaggerfallArtReplacements art)
    {
        ContentFake content = new([]);
        PopulateContent(content, site);
        foreach (DaggerfallArtImage image in art.Images) content.Add(image.Path, TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content", image.Path)));
        return content;
    }

    /// <summary>The classic files of a site's replaced entries: what a site appearance drawing refreshed art never opens.</summary>
    private static IEnumerable<string> ReplacedSiteFiles(DaggerfallSiteProfile site, DaggerfallArtReplacements art)
    {
        foreach (NormalizedMaterial material in site.Materials)
        {
            if (art.Surface(material.TexturePath) is not null)
            {
                yield return material.TexturePath;
                // A classic texture of several frames draws them only as classic art.
                foreach (ContentArtifact frame in material.Frames?.Textures.Skip(1) ?? []) yield return frame.Path;
            }
            foreach (ContentArtifact? variant in new[] { material.WinterTexture, material.RainTexture })
                if (variant is not null && art.Surface(variant.Path) is not null) yield return variant.Path;
        }
        IReadOnlyDictionary<string, DaggerfallSiteRemodel> remodels = site.Geometry.Remodels;
        foreach (DaggerfallSiteMesh mesh in site.Geometry.Meshes) if (mesh.ModelId is { } id && remodels.ContainsKey(id)) yield return mesh.Path;
        foreach (DaggerfallCityGateDefinition gate in site.CityGates)
            foreach (DaggerfallCityGateState state in new[] { gate.Open, gate.Closed })
                if (state.ModelId is { } id && remodels.ContainsKey(id)) yield return state.Visual.Path;
        foreach (DaggerfallDungeonActionModelDefinition model in site.DungeonActionModels)
            if (model.ModelId is { } id && remodels.ContainsKey(id)) yield return model.Visual.Path;
        foreach (DaggerfallSiteDecoration flat in site.Decorations)
        {
            if (Replaced(flat.Source)) yield return flat.Summer.TexturePath;
            if (Replaced(flat.WinterSource)) yield return flat.Winter.TexturePath;
        }
        foreach (NormalizedActorSprite sprite in site.ActorSprites.Values.SelectMany(sprite => sprite.Female is { } female ? [sprite, female] : new[] { sprite }))
        {
            if (sprite.SourceTexture is { } person && art.People.ContainsKey(person)) yield return sprite.TexturePath;
            if (sprite.Corpse?.SourceTexture is { } corpse && art.Meshes.ContainsKey(corpse)) yield return sprite.Corpse.TexturePath;
        }
        foreach (NormalizedClassicEffect effect in site.ClassicPresentation.Effects)
            if (art.Effects.ContainsKey(effect.MediaId)) yield return effect.TexturePath;

        // A decoration is replaced by a refreshed flat, or by a mesh unless it hangs: the test's animation reads no joints,
        // so a hanging mesh's rig is refused and its sprite drawn in its place.
        bool Replaced((int Archive, int Record) source) => art.Flats.ContainsKey(source)
            || art.Meshes.TryGetValue(source, out DaggerfallArtMesh? mesh) && mesh.Hangs is null;
    }

    /// <summary>The magic candle's classic sprite, which the art set draws anew.</summary>
    private static string Candle(DaggerfallSiteProfile site, DaggerfallArtReplacements art)
    {
        Assert.True(art.Flats.ContainsKey((210, 3)));
        return site.BillboardSprites[(210, 3)].TexturePath;
    }

    /// <summary>
    /// The classic sprites of the nature records an exterior publishes, which its terrain cells place and which refreshed
    /// art draws in the flora field.
    /// </summary>
    private static IEnumerable<string> ReplacedNatureFiles(DaggerfallSiteProfile site, DaggerfallArtReplacements art) =>
        site.ProfileKind != DaggerfallWorldProfileKind.Exterior ? []
            : site.BillboardSprites.Where(pair => DaggerfallNatureArchives.Contains(pair.Key.Archive) && art.Flats.ContainsKey(pair.Key))
                .Select(pair => pair.Value.TexturePath);

    /// <summary>
    /// The classic images of the UI art the art set draws anew, by identity, with their files: every published image it
    /// replaces, the glints it redraws, and the spell atlases, every cell of which it has an image for.
    /// </summary>
    private static Dictionary<string, string> ReplacedUiImages(DaggerfallArtReplacements art)
    {
        Dictionary<string, string> replaced = new(StringComparer.Ordinal);
        foreach (string inventory in new[] { DaggerfallUiArt.InventoryPath, DaggerfallUiArt.CharacterInventoryPath, DaggerfallUiArt.PaperDollInventoryPath })
        {
            using JsonDocument document = JsonDocument.Parse(TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content", inventory)));
            foreach (JsonElement artifact in document.RootElement.GetProperty("artifacts").EnumerateArray())
                if (artifact.TryGetProperty("mediaId", out JsonElement element) && element.GetString() is { } id
                    && (art.For(id) is not null || art.Effects.ContainsKey(id) || id is DaggerfallSpellConstruction.IconAtlasMedia or DaggerfallSpellConstruction.SelectorAtlasMedia))
                    replaced[id] = artifact.GetProperty("path").GetString()!;
        }
        Assert.All(DaggerfallSpellConstruction.RefreshableMedia(), cell => Assert.NotNull(art.For(cell)));
        return replaced;
    }

    /// <summary>What a site draws from a file, for a failure's message.</summary>
    private static string Holder(DaggerfallSiteProfile site, string path) =>
        string.Join(",", site.Decorations.Where(flat => flat.Summer.TexturePath == path || flat.Winter.TexturePath == path).Select(flat => $"decoration {flat.Source}/{flat.WinterSource}")
            .Concat(site.ActorSprites.Where(pair => pair.Value.TexturePath == path).Select(pair => $"actor {pair.Key}")));

    private void Report(string site, ContentFake content, HashSet<string> replaced, string[] delivered, DaggerfallArtReplacements art)
    {
        string[] classic = [.. delivered.Where(ReplacedUiImages(art).ContainsKey)];
        output.WriteLine($"{site}: the UI art delivers {delivered.Length} images up front, {classic.Length} of them classic images the art set draws anew");
        string[] opened = [.. content.Opened.Distinct(StringComparer.Ordinal)];
        output.WriteLine($"{site}: opened {opened.Length} distinct files ({content.Opened.Count} opens); replaced classic files known {replaced.Count}, opened {opened.Count(replaced.Contains)}");
        long Bytes(IEnumerable<string> paths) => paths.Sum(path => (long)(content.Body(path)?.Length
            ?? TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content", path)).Length));
        output.WriteLine($"  bytes {Bytes(opened)}, of which replaced {Bytes(opened.Where(replaced.Contains))}");
        foreach (IGrouping<string, string> kind in opened.GroupBy(Kind).OrderBy(group => group.Key, StringComparer.Ordinal))
            output.WriteLine($"  {kind.Key}: {kind.Count()} (replaced {kind.Count(replaced.Contains)}, {Bytes(kind.Where(replaced.Contains))} bytes)");
    }

    /// <summary>A file's kind for the report: its folder, with the effects and combat atlases each one kind.</summary>
    private static string Kind(string path) => path[..path.LastIndexOf('/')];
}
