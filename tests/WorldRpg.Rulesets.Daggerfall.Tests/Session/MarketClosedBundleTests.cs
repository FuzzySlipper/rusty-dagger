using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The developer scenario bundle "Charing market closed" begins a quick-started game in Charing's west market lane,
/// facing the square, with its one opening quest, Market Closed, whose barred variant of the market the town is built with
/// from the first.
/// </summary>
public sealed class MarketClosedBundleTests
{
    private const string Bundle = "daggerfall.market-closed";

    [Fact]
    public void Begin_starts_in_charings_market_lane_facing_the_square_already_barred_by_market_closed()
    {
        ProductContent full = FullContent(TestData.RepositoryRoot);
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile closed = Assembly.Value.Assemble(exterior.ProfileKey, "closed");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, closed);
        foreach (DaggerfallSiteProfile near in new AssembledWorld().NearbyExteriors(exterior.Site!.Value)) AssembledWorldRun.Admit(content, near);
        SpatialFake spatial = SpatialFake.Create(exterior.Geometry.Spatial[0].Sha256, releases);
        spatial.KeepPosition = true;
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, full, input), new TestDaggerfallRuleset(videosEnabled: true), new GameBundleId(Bundle));
        product.Start();

        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"begin"}""")]));
        FinishLoading(product, 1);

        Assert.Equal(ProductMode.Playing, product.Mode);
        DaggerfallSession game = RunningSession(product);
        Assert.Equal(exterior.ProfileKey, game.Sites.ActiveProfile);
        Assert.InRange(game.State.PlayerControl.YawRadians, 1.57F, 1.58F);
        Assert.Equal("MKTCLOSE.txt", Assert.Single(game.State.Quests.All).SourceFile);
        // The town is built barred from the first: the opening quest's world update is known before the start site is.
        DaggerfallSiteProfile built = game.Sites.Projection.Inputs;
        Assert.NotSame(Assembled(exterior.ProfileKey), built);
        Assert.Equal(closed.Geometry.Spatial.Single(part => part.Id == "block/3/3"), built.Geometry.Spatial.Single(part => part.Id == "block/3/3"));
        Assert.Contains(built.Geometry.Meshes, mesh => mesh.ModelId == "41239");
        QuestDiseaseTests.Advance(game);
        Assert.Same(built, game.Sites.Projection.Inputs);
        Assert.Equal(new DaggerfallWorldVariantSave(Charing.Region, Charing.Index, "closed"),
            Assert.Single(DaggerfallSavePayload.Read(game.CaptureSave()).WorldVariants));
    }
}
