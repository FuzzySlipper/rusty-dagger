using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Choosing the game at the Daggerfall title screen, driven through the Host product over the committed bundles.</summary>
public sealed class BundleChoiceSessionTests
{
    [Fact]
    public void Stepping_the_game_choice_only_records_it_and_creating_a_character_switches_to_it_and_a_quick_started_game_runs_under_it()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        TestDaggerfallRuleset ruleset = new();
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, WithSecondGame(root), input), ruleset, new GameBundleId("daggerfall.classic"));
        product.Start();
        DaggerfallSession classic = RunningSession(product);

        // The entry screen offers every committed game by its authored title.
        Assert.Equal("daggerfall.classic", engine.PublishedNested("bundles", "current"));
        Assert.Equal(["Classic Daggerfall", "Developer: combat start", "Developer: Charing market closed", "Developer: Charing town start",
            "Developer: wilderness start", "Second game"], Enumerable.Range(0, 6).Select(index => Option(engine, index)));

        // Stepping the choice is cheap: the title keeps its session, loads nothing, and shows the chosen game.
        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"bundle-select","key":"test.second"}""")]));
        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"bundle-select","key":"daggerfall.town-start"}""")]));
        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"bundle-select","key":"test.second"}""")]));
        Assert.Null(product.Loading);
        Assert.Same(classic, RunningSession(product));
        Assert.Equal("test.second", engine.PublishedNested("bundles", "current"));

        // Creating a character starts the chosen game, and creation opens there.
        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"character-begin"}""")]));
        Assert.Null(classic.State.Character.Pending);
        FinishLoading(product, 1);

        DaggerfallSession second = RunningSession(product);
        Assert.NotSame(classic, second);
        Assert.Equal(ProductMode.Title, product.Mode);
        Assert.Equal("test.second", engine.PublishedNested("bundles", "current"));
        Assert.NotNull(second.State.Character.Pending);

        product.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"character-quick-start","name":"","race":"breton","gender":"male","faceIndex":0,"reflexes":2,"career":"class16"}""")]));
        FinishLoading(product, 2);
        DaggerfallSession game = RunningSession(product);
        Assert.NotEqual(ProductMode.Title, product.Mode);
        // A game begun from the title no longer offers a choice of game.
        Assert.Null(engine.PublishedNested("bundles", "current"));
        // The game is live: a held block raises the guard.
        game.State.WeaponDrawn = true;
        product.Update(new ProductUpdate(OuterUpdate(3), [Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "block")]));
        Assert.True(game.PlayerGuardRaised);
    }

    [Fact]
    public void Begin_under_the_chosen_combat_start_switches_to_it_and_starts_play_at_once()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), new TestDaggerfallRuleset(videosEnabled: true),
            new GameBundleId("daggerfall.classic"));
        product.Start();
        DaggerfallSession classic = RunningSession(product);

        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"bundle-select","key":"daggerfall.combat-start"}""")]));
        Assert.Same(classic, RunningSession(product));
        product.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"begin"}""")]));
        // The classic title neither begins nor opens creation: the combat start does its own Begin.
        Assert.Null(classic.State.Character.Pending);
        // The update after the chosen game opens begins it, its new game built on the same loading screen's next work.
        FinishLoading(product, 2);

        Assert.Equal(ProductMode.Playing, product.Mode);
        DaggerfallSession game = RunningSession(product);
        Assert.Equal(DaggerfallWorldProfileIds.Dungeon(PrivateersHold), game.Sites.ActiveProfile);
        Assert.Equal("class16", game.State.Character.Capture().CareerId);
    }

    /// <summary>
    /// Under held time the title's choice of game and its Begin reach the session in one update, which is how Begin
    /// started the combat start again after a death and Quit to title with Charing chosen (#10982).
    /// </summary>
    [Theory]
    [InlineData("fresh title")]
    [InlineData("quit to title")]
    [InlineData("death, then quit to title")]
    public void A_game_chosen_in_the_same_update_as_begin_is_the_one_that_starts(string path)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        DaggerfallSiteProfile charing = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        PopulateContent(content, charing);
        foreach (DaggerfallSiteProfile near in new AssembledWorld().NearbyExteriors(charing.Site!.Value)) AssembledWorldRun.Admit(content, near);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), new TestDaggerfallRuleset(videosEnabled: true),
            new GameBundleId("daggerfall.classic"));
        product.Start();
        if (path != "fresh title")
        {
            product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"bundle-select","key":"daggerfall.combat-start"}"""), Ui("""{"action":"begin"}""")]));
            FinishLoading(product, 1);
            Assert.Equal(DaggerfallWorldProfileIds.Dungeon(PrivateersHold), RunningSession(product).Sites.ActiveProfile);
            if (path == "quit to title") product.QuitToTitle();
            else
            {
                product.MarkDead();
                // The death screen's quit goes up on the loading screen at once and reaches the title on the updates after.
                product.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"death-quit"}""")]));
                FinishLoading(product, 2);
            }
            Assert.Equal(ProductMode.Title, product.Mode);
            Assert.Equal("daggerfall.combat-start", engine.PublishedNested("bundles", "current"));
        }

        product.Update(new ProductUpdate(OuterUpdate(3), [Ui("""{"action":"bundle-select","key":"daggerfall.town-start"}"""), Ui("""{"action":"begin"}""")]));
        FinishLoading(product, 3);

        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Equal(charing.ProfileKey, RunningSession(product).Sites.ActiveProfile);
    }

    [Fact]
    public void A_remembered_game_that_no_longer_composes_is_explained_on_the_title_screen_after_start_and_attach()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        InMemoryPersistenceService persistence = new();
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            return EngineContextFake.Create(content, SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases).Service, new AppearanceFake(releases), persistence: persistence);
        }

        EngineContextFake first = Engine();
        using (WorldRpgProduct product = new(new ProductCreateContext(first.Context, WithSecondGame(root), input), new TestDaggerfallRuleset(), new GameBundleId("daggerfall.classic")))
        {
            product.Start();
            product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"bundle-select","key":"test.second"}""")]));
            product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"character-begin"}""")]));
            FinishLoading(product, 1);
            Assert.Equal("test.second", first.PublishedNested("bundles", "current"));
        }

        // A build whose bundle now names a pack nothing carries cannot start the remembered game.
        ProductContent withdrawn = WithSecondGame(root, missingPack: "test.withdrawn");
        EngineContextFake second = Engine();
        using WorldRpgProduct restarted = new(new ProductCreateContext(second.Context, withdrawn, input), new TestDaggerfallRuleset(), new GameBundleId("daggerfall.classic"));
        restarted.Start();
        Assert.Equal(ProductMode.Title, restarted.Mode);
        Assert.Equal("daggerfall.classic", second.PublishedNested("bundles", "current"));
        const string notice = "The remembered game 'test.second' is unavailable; the default is active.";
        Assert.Contains(notice, second.PublishedField("lastOutcome"), StringComparison.Ordinal);
        restarted.Attach();
        Assert.Contains(notice, second.PublishedField("lastOutcome"), StringComparison.Ordinal);
    }

    /// <summary>The committed content with a second game over the classic bundle's packs, and a pack no source carries when one is named.</summary>
    private static ProductContent WithSecondGame(string root, string? missingPack = null)
    {
        ProductContent full = StagedContent(root, out BundleContentFake bundles);
        JsonObject bundle = JsonNode.Parse(full.ReadBytes("worldrpg/bundles/daggerfall.classic.bundle.json").Span)!.AsObject();
        bundle["id"] = "test.second";
        bundle["title"] = "Second game";
        if (missingPack is not null) bundle["contentPacks"]!.AsArray().Add(new JsonObject { ["id"] = missingPack });
        ProductContentFile[] files = [.. full.Files.ToArray(),
            new(Encoding.UTF8.GetBytes("worldrpg/bundles/test.second.bundle.json"), Encoding.UTF8.GetBytes(bundle.ToJsonString()))];
        return new ProductContent(files, bundles);
    }

    /// <summary>One offered game's title, read from the decoded snapshot.</summary>
    private static string? Option(EngineContextFake engine, int index) =>
        engine.Published() is IReadOnlyDictionary<string, object?> snapshot && snapshot["bundles"] is IReadOnlyDictionary<string, object?> bundles
            && bundles["options"] is IReadOnlyList<object?> options && options[index] is IReadOnlyDictionary<string, object?> option
            ? option["title"] as string : null;
}
