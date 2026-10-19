using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Character creation's quick start, driven through the Host product's entry screen.</summary>
public sealed class CharacterQuickStartTests
{
    private const string QuickStart = """{"action":"character-quick-start","name":"","race":"breton","gender":"female","faceIndex":0,"reflexes":2,"career":"class16"}""";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Quick_start_commits_a_complete_character_from_the_draft_and_begins_the_new_game(bool draftOpen)
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
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.classic"));
        product.Start();
        DaggerfallSession title = ruleset.RequireSession();
        if (draftOpen)
        {
            product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"character-begin"}""")]));
            Assert.NotNull(title.State.Character.Pending);
        }

        product.Update(new ProductUpdate(OuterUpdate(2), [Ui(QuickStart)]));

        DaggerfallSession game = ruleset.RequireSession();
        Assert.NotSame(title, game);
        Assert.NotEqual(ProductMode.Title, product.Mode);
        Assert.Null(game.State.Character.Pending);
        Assert.False(game.RequiresCharacterInitialization);
        var identity = game.State.Character.Identity;
        Assert.Equal(("Adventurer", "breton", "class16"), (identity.Name, identity.RaceId, identity.CareerId));
        Assert.NotNull(game.State.Character.Background);
    }
}
