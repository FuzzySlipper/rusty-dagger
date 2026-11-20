using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Quit from the title screen and the game menu, driven through the Host product over the committed game.</summary>
public sealed class QuitSessionTests
{
    [Fact]
    public void Quit_where_the_window_can_close_says_when_play_since_the_last_save_would_be_lost_and_ends_the_product()
    {
        (WorldRpgProduct product, EngineContextFake engine) = Start(exitAvailable: true);
        using WorldRpgProduct running = product;
        Assert.False(Unsaved(engine), "The title offers Quit with nothing in progress.");

        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"character-quick-start","name":"","race":"breton","gender":"male","faceIndex":0,"reflexes":2,"career":"class16"}""")]));
        FinishLoading(product, 1);
        Assert.NotEqual(ProductMode.Title, product.Mode);
        Assert.False(Unsaved(engine), "A game just begun has nothing to lose.");

        for (ulong step = 2; step < 6; step++) product.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.True(Unsaved(engine), "Play since the game began would be lost.");

        product.Update(new ProductUpdate(OuterUpdate(6), [Ui("""{"action":"save-game"}""")]));
        Assert.Equal("Quick saved.", engine.PublishedField("lastOutcome"));
        Assert.False(Unsaved(engine), "A save keeps the game as it stands.");

        Assert.Equal(0, engine.Host.Exits);
        product.Update(new ProductUpdate(OuterUpdate(7), [Ui("""{"action":"quit-game"}""")]));
        Assert.Equal(1, engine.Host.Exits);
    }

    [Fact]
    public void A_streamed_game_offers_no_quit_and_says_how_to_leave_when_asked_anyway()
    {
        (WorldRpgProduct product, EngineContextFake engine) = Start(exitAvailable: false);
        using WorldRpgProduct running = product;
        Assert.Null(Quit(engine));

        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"quit-game"}""")]));
        Assert.Equal(0, engine.Host.Exits);
        Assert.Equal("Close the window to quit.", engine.PublishedField("lastOutcome"));
    }

    private static (WorldRpgProduct Product, EngineContextFake Engine) Start(bool exitAvailable)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), persistence: new InMemoryPersistenceService());
        engine.Host.ExitAvailable = exitAvailable;
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), new TestDaggerfallRuleset(),
            new GameBundleId("daggerfall.classic"));
        product.Start();
        Assert.Equal(ProductMode.Title, product.Mode);
        return (product, engine);
    }

    /// <summary>The HUD's quit, or null where it offers none.</summary>
    private static Dictionary<string, object?>? Quit(EngineContextFake engine) =>
        Assert.IsType<Dictionary<string, object?>>(engine.Published())["quit"] as Dictionary<string, object?>;

    private static bool Unsaved(EngineContextFake engine) => Assert.IsType<bool>(Assert.IsType<Dictionary<string, object?>>(Quit(engine))["unsaved"]);
}
