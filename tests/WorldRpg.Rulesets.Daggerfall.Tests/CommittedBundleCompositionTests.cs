using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class CommittedBundleCompositionTests
{
    private const string SiteRole = "daggerfall.site";

    /// <summary>Every game bundle committed under the content root, by id.</summary>
    public static TheoryData<string> CommittedBundles()
    {
        TheoryData<string> bundles = [];
        foreach (string path in Directory.GetFiles(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/bundles"), "*.bundle.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            bundles.Add(document.RootElement.GetProperty("id").GetString()!);
        }
        return bundles;
    }

    /// <summary>
    /// A committed bundle is only real if the ruleset can start a game from it: this resolves each one,
    /// admits every pack it selects through the real readers, creates the session through the compiled
    /// ruleset and takes one admitted update. The session starts at the site the authored new-game definition names.
    /// </summary>
    [Theory]
    [MemberData(nameof(CommittedBundles))]
    public void Every_committed_bundle_creates_a_session_through_the_real_ruleset(string bundleId)
    {
        string root = TestData.RepositoryRoot;
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId(bundleId)).RequireComposition();
        ContentPack firstSite = composition.ContentPacks.First(pack => pack.Role.Value == SiteRole);
        DaggerfallSiteProfile start = DaggerfallSiteContent.Read(FullContent(root), firstSite.Payload, TestPayload.Definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, start);
        SpatialFake spatial = SpatialFake.Create(start.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new(videosEnabled: false);

        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId(bundleId)))
        {
            product.Start();
            NewGameSessionTests.Commit(ruleset.RequireSession());
            product.Begin();
            Assert.Equal(ProductMode.Playing, product.Mode);
            ProductUpdateFacts facts = new(ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d);
            Assert.Equal(ProductUpdateResult.None, product.Update(new ProductUpdate(facts, ReadOnlySpan<ProductInputEvent>.Empty)));
            Assert.Equal(1, spatial.StepCalls);
            DaggerfallSession session = ruleset.RequireSession();
            Assert.Equal(start.Site, session.Site.Active);
            product.Shutdown();
        }
    }

    /// <summary>
    /// A site's audio bodies are opened through the bundle its payload names, and the SDK stages only the
    /// bundles the Host declares, so every site payload's declaration must match one Host item exactly.
    /// </summary>
    [Fact]
    public void Every_site_payload_names_an_audio_bundle_the_host_stages_at_its_publication_root()
    {
        string root = TestData.RepositoryRoot;
        Dictionary<string, string> declared = XDocument.Load(Path.Combine(root, "src/WorldRpg.Host/WorldRpg.Host.csproj"))
            .Descendants("RustyEngineContentBundle")
            .ToDictionary(item => item.Attribute("Include")!.Value, item => item.Attribute("Root")!.Value, StringComparer.Ordinal);
        (string Root, string Bundle)[] sites = [.. SiteAudioBundles(root)];

        Assert.NotEmpty(sites);
        Assert.All(sites, site =>
        {
            Assert.True(declared.TryGetValue(site.Bundle, out string? stagedRoot), $"The Host does not declare site audio bundle '{site.Bundle}'.");
            Assert.Equal(site.Root, stagedRoot);
        });
    }

    /// <summary>
    /// The Host stages exactly the bundles the default composition opens. Each named bundle the
    /// ruleset opens by id, and each audio bundle a selected site payload declares, is a Host item at
    /// the root its reader strips; every clip a selected site publishes is a file under that root; and
    /// every Host item is one of those or a named staging-only declaration nothing opens.
    /// </summary>
    [Fact]
    public void The_host_stages_exactly_the_bundles_the_default_composition_opens()
    {
        string root = TestData.RepositoryRoot;
        Dictionary<string, string> declared = XDocument.Load(Path.Combine(root, "src/WorldRpg.Host/WorldRpg.Host.csproj"))
            .Descendants("RustyEngineContentBundle")
            .ToDictionary(item => item.Attribute("Include")!.Value, item => item.Attribute("Root")!.Value, StringComparer.Ordinal);
        Dictionary<string, string> opened = new(StringComparer.Ordinal)
        {
            [DaggerfallMusicBundle.BundleId] = DaggerfallMusicBundle.LogicalRoot,
            [DaggerfallSkyMedia.BundleId] = DaggerfallSkyMedia.LogicalRoot,
            [DaggerfallCinematicPresentation.BundleId] = DaggerfallCinematicPresentation.Root.TrimEnd('/'),
        };
        ProductContent content = FullContent(root);
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition();
        List<string> missingClips = [];
        foreach (ContentPack pack in composition.ContentPacks.Where(pack => pack.Role.Value == SiteRole))
        {
            DaggerfallSiteProfile site = DaggerfallSiteContent.Read(content, pack.Payload, TestPayload.Definitions);
            string bundle = site.AudioBundle ?? throw new InvalidOperationException($"Site pack '{pack.Id.Value}' declares no audio bundle.");
            string clips = $"{site.ProfileKey.LogicalId}/media/audio/clips";
            Assert.True(opened.TryAdd(bundle, clips), $"Audio bundle '{bundle}' is opened by two owners.");
            missingClips.AddRange(site.Audio
                .Where(clip => !clip.Path.StartsWith(clips + "/", StringComparison.Ordinal) || !File.Exists(Path.Combine(root, "content", clip.Path)))
                .Select(clip => $"{pack.Id.Value}: {clip.Id} -> {clip.Path}"));
        }
        Assert.Empty(missingClips);

        // Declared and staged but opened by no owner. The classic catalog's WAV bodies are a lazy bundle
        // only so they stay out of the eager startup snapshot; each site stages and opens its own clips.
        Dictionary<string, string> stagingOnly = new(StringComparer.Ordinal) { ["daggerfall.classic-audio"] = "worldrpg/media/audio/clips" };
        string[] rulesetSources = [.. Directory.GetFiles(Path.Combine(root, "src/WorldRpg.Rulesets.Daggerfall"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)];
        Assert.All(stagingOnly.Keys, id => Assert.DoesNotContain(rulesetSources, source => source.Contains($"\"{id}\"", StringComparison.Ordinal)));

        Assert.Equal(
            opened.Concat(stagingOnly).OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray(),
            declared.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray());
        Assert.All(declared.Values, staged => Assert.True(Directory.Exists(Path.Combine(root, "content", staged)), $"Staged bundle root '{staged}' was not generated."));
    }

    /// <summary>
    /// Every committed pack is selected by a committed bundle, so no payload sits beside the product
    /// admitted by nothing, and the authored new-game definition names content the default bundle admits:
    /// its start site is a selected site pack, and a new game started from it begins every quest it names.
    /// </summary>
    [Fact]
    public void Every_committed_pack_is_selected_and_the_new_game_starts_what_it_names()
    {
        string root = TestData.RepositoryRoot;
        ProductContent content = FullContent(root);
        HashSet<string> selected = [];
        foreach (string bundleId in CommittedBundles().Cast<object[]>().Select(row => (string)row[0]))
        {
            ResolvedGameComposition resolved = GameCompositionResolver.Resolve(content, new GameBundleId(bundleId)).RequireComposition();
            foreach (ContentPack pack in resolved.ContentPacks) selected.Add(pack.Id.Value);
        }
        string[] committed = [.. Directory.GetFiles(Path.Combine(root, "content/worldrpg/content-packs"), "*.pack.json")
            .Select(path => JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.GetProperty("id").GetString()!)
            .Order(StringComparer.Ordinal)];
        Assert.Equal<IEnumerable<string>>(committed, selected.Order(StringComparer.Ordinal));

        DaggerfallNewGameDefinition newGame = TestPayload.Definitions.NewGame;
        ResolvedGameComposition classic = GameCompositionResolver.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition();
        Assert.Contains(classic.ContentPacks, pack => pack.Id.Value == newGame.StartSitePack && pack.Role.Value == SiteRole);
        Assert.NotEmpty(newGame.Quests);

        using IGameSession title = CreateWithBundle(_ => { });
        DaggerfallSession titleSession = Assert.IsType<DaggerfallSession>(title);
        NewGameSessionTests.Commit(titleSession);
        using DaggerfallSession game = Assert.IsType<DaggerfallSession>(titleSession.CreateNewGame());
        Assert.All(newGame.Quests, file => Assert.Contains(game.State.Quests.All, quest => quest.SourceFile == file));
    }

    [Fact]
    public void A_bundle_that_admits_no_source_for_a_new_game_quest_is_refused_by_name()
    {
        // The tutorial and the introduction are published by the late story corpus; a bundle without it
        // would otherwise start a game that silently lacks them.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CreateWithBundle(bundle =>
        {
            JsonArray packs = bundle["contentPacks"]!.AsArray();
            packs.Remove(packs.Single(pack => pack!["id"]!.GetValue<string>() == "daggerfall.quests.story-late"));
        }));
        Assert.Contains("admits no quest source for the new-game quests '_TUTOR__.txt', '_BRISIEN.txt'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Admission_refuses_a_pack_role_the_ruleset_does_not_interpret()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CreateWithPackRole("daggerfall.quests.mages", "daggerfall.unknown"));

        Assert.Contains("declares role 'daggerfall.unknown'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Admission_requires_exactly_one_base_pack()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CreateWithPackRole("daggerfall.base", "daggerfall.blocks"));

        Assert.Contains("selects 0 'daggerfall.base' content packs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_corpus_id_is_admitted_from_what_its_payload_states()
    {
        string root = TestData.RepositoryRoot;
        JsonObject payload = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.mages.json")))!.AsObject();
        payload["id"] = "a-corpus-no-code-names";

        IReadOnlyList<DaggerfallClassicQuestCorpusReceipt> receipts = DaggerfallClassicQuestCorpusContent.Read(
            new ProductContent(Array.Empty<ProductContentFile>()), Encoding.UTF8.GetBytes(payload.ToJsonString()), TestPayload.Definitions);

        Assert.NotEmpty(receipts);
        Assert.All(receipts, receipt => Assert.True(receipt.IsOrdinaryOffer));
    }

    [Fact]
    public void A_new_game_starts_at_the_authored_start_site_whatever_the_bundle_order()
    {
        // Site packs listed in reverse put a ship, then dungeons, ahead of Privateer's Hold; the authored
        // new-game definition still names where play begins.
        using IGameSession session = CreateWithBundle(bundle =>
        {
            JsonArray packs = bundle["contentPacks"]!.AsArray();
            JsonNode[] reversed = [.. packs.Select(pack => pack!.DeepClone()).Reverse()];
            packs.Clear();
            foreach (JsonNode pack in reversed) packs.Add(pack);
        });
        DaggerfallSession daggerfall = Assert.IsType<DaggerfallSession>(session);
        Assert.Equal("daggerfall.privateers-hold", TestPayload.Definitions.NewGame.StartSitePack);
        Assert.Equal(ReadInputs(TestData.RepositoryRoot).ProfileKey, daggerfall.Sites.ActiveProfile);
    }

    [Fact]
    public void A_bundle_without_the_authored_start_site_is_refused_by_name()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CreateWithBundle(bundle =>
        {
            JsonArray packs = bundle["contentPacks"]!.AsArray();
            packs.Remove(packs.Single(pack => pack!["id"]!.GetValue<string>() == "daggerfall.privateers-hold"));
        }));
        Assert.Contains("does not select the new-game start site 'daggerfall.privateers-hold'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Resolves the default bundle after editing its descriptor, then creates a session.</summary>
    private static IGameSession CreateWithBundle(Action<JsonObject> edit)
    {
        string root = TestData.RepositoryRoot;
        // The edited descriptor replaces one eager file; the bundles the Host stages travel with it.
        ProductContent full = StagedContent(root, out BundleContentFake bundles);
        const string bundlePath = "worldrpg/bundles/daggerfall.classic.bundle.json";
        JsonObject bundle = JsonNode.Parse(full.ReadBytes(bundlePath).Span)!.AsObject();
        edit(bundle);
        ProductContentFile[] files = [.. full.Files.ToArray().Select(file => Encoding.UTF8.GetString(file.Path.Span) == bundlePath
            ? new ProductContentFile(file.Path.ToArray(), Encoding.UTF8.GetBytes(bundle.ToJsonString()))
            : file)];
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(new ProductContent(files, bundles), new GameBundleId("daggerfall.classic")).RequireComposition();
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        return new DaggerfallRuleset(videosEnabled: false).CreateSession(new GameSessionContext(engine.Context, composition));
    }

    /// <summary>Resolves the default bundle with one pack's declared role replaced, then creates a session.</summary>
    private static IGameSession CreateWithPackRole(string packId, string role)
    {
        string root = TestData.RepositoryRoot;
        // The edited descriptor replaces one eager file; the bundles the Host stages travel with it.
        ProductContent full = StagedContent(root, out BundleContentFake bundles);
        string manifestPath = $"worldrpg/content-packs/{packId}.pack.json";
        JsonObject manifest = JsonNode.Parse(full.ReadBytes(manifestPath).Span)!.AsObject();
        manifest["role"] = role;
        ProductContentFile[] files = [.. full.Files.ToArray().Select(file => Encoding.UTF8.GetString(file.Path.Span) == manifestPath
            ? new ProductContentFile(file.Path.ToArray(), Encoding.UTF8.GetBytes(manifest.ToJsonString()))
            : file)];
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(new ProductContent(files, bundles), new GameBundleId("daggerfall.classic")).RequireComposition();
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        return new DaggerfallRuleset(videosEnabled: false).CreateSession(new GameSessionContext(engine.Context, composition));
    }
}
