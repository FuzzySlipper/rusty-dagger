using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Choosing the game at the Daggerfall title screen, driven through the Host product over the committed bundles.</summary>
public sealed class BundleChoiceSessionTests
{
    [Fact]
    public void The_title_screen_switches_to_another_game_and_a_quick_started_game_runs_under_it()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new();
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, WithSecondGame(root), input), ruleset, new GameBundleId("daggerfall.classic"));
        product.Start();
        DaggerfallSession classic = ruleset.RequireSession();

        // The entry screen offers every committed game by its authored title.
        Assert.Equal("daggerfall.classic", engine.PublishedNested("bundles", "current"));
        Assert.Equal("Classic Daggerfall", Option(engine, 0));
        Assert.Equal("Developer: Charing town start", Option(engine, 1));
        Assert.Equal("Developer: wilderness start", Option(engine, 2));
        Assert.Equal("Second game", Option(engine, 3));

        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"bundle-select","key":"test.second"}""")]));
        FinishLoading(product, 1);

        DaggerfallSession second = ruleset.RequireSession();
        Assert.NotSame(classic, second);
        Assert.Equal(ProductMode.Title, product.Mode);
        Assert.Equal("test.second", engine.PublishedNested("bundles", "current"));

        product.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"character-quick-start","name":"","race":"breton","gender":"male","faceIndex":0,"reflexes":2,"career":"class16"}""")]));
        FinishLoading(product, 2);
        DaggerfallSession game = ruleset.RequireSession();
        Assert.NotEqual(ProductMode.Title, product.Mode);
        // A game begun from the title no longer offers a choice of game.
        Assert.Null(engine.PublishedNested("bundles", "current"));
        // The game is live: a held block raises the guard.
        game.State.WeaponDrawn = true;
        product.Update(new ProductUpdate(OuterUpdate(3), [Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "block")]));
        Assert.True(game.PlayerGuardRaised);
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
            return EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases), persistence: persistence);
        }

        EngineContextFake first = Engine();
        using (WorldRpgProduct product = new(new ProductCreateContext(first.Context, WithSecondGame(root), input), new CapturingDaggerfallRuleset(), new GameBundleId("daggerfall.classic")))
        {
            product.Start();
            product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"bundle-select","key":"test.second"}""")]));
            FinishLoading(product, 1);
            Assert.Equal("test.second", first.PublishedNested("bundles", "current"));
        }

        // A build whose bundle now names a pack nothing carries cannot start the remembered game.
        ProductContent withdrawn = WithSecondGame(root, missingPack: "test.withdrawn");
        EngineContextFake second = Engine();
        using WorldRpgProduct restarted = new(new ProductCreateContext(second.Context, withdrawn, input), new CapturingDaggerfallRuleset(), new GameBundleId("daggerfall.classic"));
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
