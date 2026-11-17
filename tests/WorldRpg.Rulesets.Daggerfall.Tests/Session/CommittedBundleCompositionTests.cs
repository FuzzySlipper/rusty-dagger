using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class CommittedBundleCompositionTests
{
    private const string NewGameStartRole = "daggerfall.new-game-start";

    /// <summary>Every game bundle committed under the content root, by id.</summary>
    public static TheoryData<string> CommittedBundles()
    {
        TheoryData<string> bundles = [];
        foreach (string path in Directory.GetFiles(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/bundles"), "*.bundle.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(TestContentFiles.ReadCopy(path));
            bundles.Add(document.RootElement.GetProperty("id").GetString()!);
        }
        return bundles;
    }

    /// <summary>
    /// A committed bundle is only real if the ruleset can start a game from it: this resolves each one,
    /// admits every pack it selects through the real readers, creates the session through the compiled
    /// ruleset and takes one admitted update. The session starts at the site the authored new-game definition, or the
    /// bundle's new-game start pack, names.
    /// </summary>
    [Theory]
    [MemberData(nameof(CommittedBundles))]
    public void Every_committed_bundle_creates_a_session_through_the_real_ruleset(string bundleId)
    {
        string root = TestData.RepositoryRoot;
        ResolvedGameComposition composition = TestSessions.Resolve(FullContent(root), new GameBundleId(bundleId)).RequireComposition();
        // A bundle's new-game start pack, when it selects one, names its start over the base definition.
        DaggerfallNewGameStart startDefinition = composition.ContentPacks.SingleOrDefault(pack => pack.Role.Value == NewGameStartRole) is { } startOverride
            ? DaggerfallBaseContent.ReadNewGameStart(startOverride.Payload, TestPayload.Definitions.NewGame, startOverride.Id.Value).Start
            : TestPayload.Definitions.NewGame.Start;
        Assert.True(DaggerfallWorldProfileIds.TryParse(startDefinition.Profile, out DaggerfallWorldProfileKey startKey));
        DaggerfallSiteProfile start = Assembled(startKey);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, start);
        // A new game builds its start with the variant its opening quests select, so each variant the quests pack carries
        // for the start's place is served too.
        foreach (string variant in QuestPackVariants(startKey.Site))
            if (Assembly.Value.Varies(startKey, variant)) PopulateContent(content, Assembly.Value.Assemble(startKey, variant));
        // An exterior start streams the assembled exteriors around it.
        if (start.Site is { } site && start.ProfileKey == DaggerfallWorldProfileIds.Exterior(site))
            foreach (DaggerfallSiteProfile near in new AssembledWorld().NearbyExteriors(site)) AssembledWorldRun.Admit(content, near);
        SpatialFake spatial = SpatialFake.Create(start.Geometry.Spatial[0].Sha256, releases);
        // The player stays put: one step must not stream in neighbouring exterior cells the fake does not carry.
        spatial.KeepPosition = true;
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        TestDaggerfallRuleset ruleset = new(videosEnabled: false);

        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId(bundleId)))
        {
            product.Start();
            NewGameSessionTests.Commit(RunningSession(product));
            product.Begin();
            Assert.Equal(ProductMode.Playing, product.Mode);
            ProductUpdateFacts facts = new(ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d);
            Assert.Equal(ProductUpdateResult.None, product.Update(new ProductUpdate(facts, ReadOnlySpan<ProductInputEvent>.Empty)));
            Assert.Equal(1, spatial.StepCalls);
            DaggerfallSession session = RunningSession(product);
            Assert.Equal(start.Site, session.Site.Active);
            product.Shutdown();
        }
    }

    /// <summary>The location variants the shipped quests pack states for a place, by name.</summary>
    private static IEnumerable<string> QuestPackVariants(DaggerfallSiteId site)
    {
        using JsonDocument payload = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/packs/daggerfall.quests/payload.json")));
        return payload.RootElement.TryGetProperty("worldVariants", out JsonElement variants)
            ? [.. variants.GetProperty("locations").EnumerateArray()
                .Where(value => value.GetProperty("region").GetInt32() == site.Region && value.GetProperty("location").GetInt32() == site.Index)
                .Select(value => value.GetProperty("variant").GetString()!)]
            : [];
    }

    /// <summary>
    /// The data container carries every root the default composition opens as a whole (the sky, the cinematics,
    /// the world audio and the per-block world), and every clip an assembled location names is a file below the world
    /// media audio root in it.
    /// </summary>
    [Fact]
    public void The_data_container_carries_every_root_the_default_composition_opens()
    {
        string root = TestData.RepositoryRoot;
        IReadOnlyCollection<string> carried = [.. TestDataContainers.Entries(TestDataContainers.Data).Keys];
        // The cinematics' root is opened only for a converted cinematic, which an import makes only with --cinematics.
        string[] opened = [DaggerfallSkyMedia.LogicalRoot, DaggerfallWorldMedia.AudioRoot, DaggerfallWorldBlocks.Root,
            .. TestCinematics.Carried ? [DaggerfallCinematicPresentation.Root] : Array.Empty<string>()];
        Assert.All(opened, prefix => Assert.Contains(carried, path => path.StartsWith(prefix + "/", StringComparison.Ordinal)));
        ProductContent content = FullContent(root);
        ResolvedGameComposition composition = TestSessions.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition();
        HashSet<string> entries = [.. carried];
        List<string> missingClips = [];
        DaggerfallSiteProfile site = ReadInputs(root);
        string clips = DaggerfallWorldMedia.AudioRoot;
        Assert.NotEmpty(site.Audio);
        missingClips.AddRange(site.Audio
            .Where(clip => !clip.Path.StartsWith(clips + "/", StringComparison.Ordinal) || !entries.Contains(clip.Path))
            .Select(clip => $"{clip.Id} -> {clip.Path}"));
        Assert.Empty(missingClips);
    }

    /// <summary>
    /// Every committed pack is selected by a committed bundle, so no payload sits beside the product
    /// admitted by nothing, and the authored new-game definition names content the default bundle admits:
    /// its start is a location the catalog places, and a new game started from it begins every quest it names.
    /// </summary>
    [Fact]
    public void Every_committed_pack_is_selected_and_the_new_game_starts_what_it_names()
    {
        string root = TestData.RepositoryRoot;
        ProductContent content = FullContent(root);
        HashSet<string> selected = [];
        foreach (string bundleId in CommittedBundles().Cast<object[]>().Select(row => (string)row[0]))
        {
            ResolvedGameComposition resolved = TestSessions.Resolve(content, new GameBundleId(bundleId)).RequireComposition();
            foreach (ContentPack pack in resolved.ContentPacks) selected.Add(pack.Id.Value);
        }
        // Every pack a source describes: the tracked descriptors and the derived packs the data container carries.
        string[] committed = [.. Directory.GetFiles(Path.Combine(root, "content/worldrpg/packs"), "pack.json", SearchOption.AllDirectories)
            .Select(path => JsonDocument.Parse(TestContentFiles.ReadCopy(path)).RootElement.GetProperty("id").GetString()!)
            .Concat(TestSessions.Installed(content).SelectMany(installed => installed.Description?.ContentPacks ?? [])
                .Select(pack => pack.GetProperty("id").GetString()!))
            .Order(StringComparer.Ordinal)];
        Assert.Equal<IEnumerable<string>>(committed, selected.Order(StringComparer.Ordinal));

        DaggerfallNewGameDefinition newGame = TestPayload.Definitions.NewGame;
        ResolvedGameComposition classic = TestSessions.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition();
        Assert.True(DaggerfallWorldProfileIds.TryParse(newGame.Start.Profile, out DaggerfallWorldProfileKey startKey));
        Assert.Equal(startKey, Assembled(startKey).ProfileKey);
        Assert.NotEmpty(newGame.Quests);

        using IGameSession title = CreateWithBundle(_ => { });
        DaggerfallSession titleSession = Assert.IsType<DaggerfallSession>(title);
        NewGameSessionTests.Commit(titleSession);
        using DaggerfallSession game = Assert.IsType<DaggerfallSession>(titleSession.CreateNewGame());
        Assert.All(newGame.Quests, file => Assert.Contains(game.State.Quests.All, quest => quest.SourceFile == file));
    }

    [Fact]
    public void A_bundle_that_carries_no_record_for_a_new_game_quest_is_refused_by_name()
    {
        // The introduction is a record of the quests pack; a bundle without it would otherwise
        // start a game that silently lacks it.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CreateWithBundle(bundle =>
        {
            JsonArray packs = bundle["contentPacks"]!.AsArray();
            packs.Remove(packs.Single(pack => pack!["id"]!.GetValue<string>() == "daggerfall.quests"));
        }));
        Assert.Contains("carries no quest record for the new-game quests", error.Message, StringComparison.Ordinal);
        Assert.Contains("'_BRISIEN.txt'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Admission_refuses_a_pack_role_the_ruleset_does_not_interpret()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CreateWithPackRole("daggerfall.art-replacements", "daggerfall.unknown"));

        Assert.Contains("declares role 'daggerfall.unknown'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Admission_requires_exactly_one_base_pack()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CreateWithPackRole("daggerfall.base", "daggerfall.blocks"));

        Assert.Contains("selects 0 'daggerfall.base' content packs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_game_starts_at_the_authored_start_site_whatever_the_bundle_order()
    {
        // Pack order carries no meaning: the authored new-game definition names where play begins.
        using IGameSession session = CreateWithBundle(bundle =>
        {
            JsonArray packs = bundle["contentPacks"]!.AsArray();
            JsonNode[] reversed = [.. packs.Select(pack => pack!.DeepClone()).Reverse()];
            packs.Clear();
            foreach (JsonNode pack in reversed) packs.Add(pack);
        });
        DaggerfallSession daggerfall = Assert.IsType<DaggerfallSession>(session);
        Assert.Equal(DaggerfallWorldProfileIds.Dungeon(PrivateersHold).LogicalId, TestPayload.Definitions.NewGame.Start.Profile);
        Assert.Equal(ReadInputs(TestData.RepositoryRoot).ProfileKey, daggerfall.Sites.ActiveProfile);
    }

    [Fact]
    public void A_new_game_start_no_location_places_is_refused_by_name()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CreateWithFile("worldrpg/packs/daggerfall.base/payload.json",
            payload => payload["newGame"]!["start"]!["profile"] = "17/4/dungeon"));
        Assert.Contains("names profile '17/4/dungeon', which no location places", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Resolves the default bundle after editing its descriptor, then creates a session.</summary>
    private static IGameSession CreateWithBundle(Action<JsonObject> edit) => CreateWithFile("worldrpg/bundles/daggerfall.classic.bundle.json", edit);

    /// <summary>Resolves the default bundle with one pack's declared role replaced, then creates a session.</summary>
    private static IGameSession CreateWithPackRole(string packId, string role) =>
        CreateWithFile($"worldrpg/packs/{packId}/pack.json", manifest => manifest["role"] = role);

    /// <summary>Resolves the default bundle after editing one staged JSON file, then creates a session.</summary>
    private static IGameSession CreateWithFile(string path, Action<JsonObject> edit)
    {
        string root = TestData.RepositoryRoot;
        // The edited file replaces one eager file; the bundles the Host stages travel with it.
        ProductContent full = StagedContent(root, out BundleContentFake bundles);
        JsonObject edited = JsonNode.Parse(full.ReadBytes(path).Span)!.AsObject();
        edit(edited);
        ProductContentFile[] files = [.. full.Files.ToArray().Select(file => Encoding.UTF8.GetString(file.Path.Span) == path
            ? new ProductContentFile(file.Path.ToArray(), Encoding.UTF8.GetBytes(edited.ToJsonString()))
            : file)];
        ResolvedGameComposition composition = TestSessions.Resolve(new ProductContent(files, bundles), new GameBundleId("daggerfall.classic")).RequireComposition();
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases).Service, new AppearanceFake(releases));
        return new DaggerfallRuleset(videosEnabled: false).CreateSession(new GameSessionContext(engine.Context, composition));
    }
}
