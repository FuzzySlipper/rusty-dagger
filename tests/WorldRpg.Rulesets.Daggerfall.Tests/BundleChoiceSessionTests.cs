using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Variants;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Choosing the game at the Daggerfall title screen, driven through the Host product over the committed bundles.</summary>
public sealed class BundleChoiceSessionTests
{
    [Fact]
    public void The_title_screen_switches_to_the_contact_game_and_a_quick_started_game_runs_under_it()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new(variants: DaggerfallVariants.All);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.classic"));
        product.Start();
        DaggerfallSession classic = ruleset.RequireSession();

        // The entry screen offers both committed games by their authored titles.
        Assert.Equal("daggerfall.classic", engine.PublishedNested("bundles", "current"));
        Assert.Equal("Classic Daggerfall", Option(engine, 0));
        Assert.Equal("Contact + mitigation", Option(engine, 1));

        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"bundle-select","key":"daggerfall.contact"}""")]));

        DaggerfallSession contact = ruleset.RequireSession();
        Assert.NotSame(classic, contact);
        Assert.Equal(ProductMode.Title, product.Mode);
        Assert.Equal("daggerfall.contact", engine.PublishedNested("bundles", "current"));

        product.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"character-quick-start","name":"","race":"breton","gender":"male","faceIndex":0,"reflexes":2,"career":"class16"}""")]));
        DaggerfallSession game = ruleset.RequireSession();
        Assert.NotEqual(ProductMode.Title, product.Mode);
        // A game begun from the title no longer offers a choice of game.
        Assert.Null(engine.PublishedNested("bundles", "current"));
        // The contact variant's owners are live: a held block raises the guard classic does not have.
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
        using (WorldRpgProduct product = new(new ProductCreateContext(first.Context, FullContent(root), input), new CapturingDaggerfallRuleset(variants: DaggerfallVariants.All), new GameBundleId("daggerfall.classic")))
        {
            product.Start();
            product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"bundle-select","key":"daggerfall.contact"}""")]));
            Assert.Equal("daggerfall.contact", first.PublishedNested("bundles", "current"));
        }

        // A build that no longer compiles the contact variant cannot start the remembered game.
        EngineContextFake second = Engine();
        using WorldRpgProduct restarted = new(new ProductCreateContext(second.Context, FullContent(root), input), new CapturingDaggerfallRuleset(), new GameBundleId("daggerfall.classic"));
        restarted.Start();
        Assert.Equal(ProductMode.Title, restarted.Mode);
        Assert.Equal("daggerfall.classic", second.PublishedNested("bundles", "current"));
        const string notice = "The remembered game 'daggerfall.contact' is unavailable; the default is active.";
        Assert.Contains(notice, second.PublishedField("lastOutcome"), StringComparison.Ordinal);
        restarted.Attach();
        Assert.Contains(notice, second.PublishedField("lastOutcome"), StringComparison.Ordinal);
    }

    /// <summary>One offered game's title, read from the decoded snapshot.</summary>
    private static string? Option(EngineContextFake engine, int index) =>
        engine.Published() is IReadOnlyDictionary<string, object?> snapshot && snapshot["bundles"] is IReadOnlyDictionary<string, object?> bundles
            && bundles["options"] is IReadOnlyList<object?> options && options[index] is IReadOnlyDictionary<string, object?> option
            ? option["title"] as string : null;
}
