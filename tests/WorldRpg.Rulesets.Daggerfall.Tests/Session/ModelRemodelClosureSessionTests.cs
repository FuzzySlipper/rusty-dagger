using System.Text;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A site closure draws one pre-combined mesh that names no model, so the art pack's remodels cannot reach it: the session
/// says so once per closure rather than drawing them classic silently, and says it without reading the per-block
/// publication, which nothing reads before a location needs it.
/// </summary>
public sealed class ModelRemodelClosureSessionTests
{
    [Fact]
    public void A_new_game_in_a_closure_says_once_that_the_remodels_draw_classic_there()
    {
        string root = TestData.RepositoryRoot;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile hold = ReadInputs(root);
        Assert.True(hold.Geometry.IsClosure);
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes("""
            {"artReplacements": {"images": [], "models": {
              "61027": {"path": "remodel/61027.rstatmsh", "sha256": "0000000000000001000000000000000200000000000000030000000000000004",
                        "slots": [{"slot": 0, "material": "material/texture-67-0"}]},
              "41209": {"path": "remodel/41209.rstatmsh", "sha256": "0000000000000001000000000000000200000000000000030000000000000004",
                        "slots": [{"slot": 0, "material": "material/texture-67-0"}]}}}}
            """), "test.art");

        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, hold);
        EngineContextFake engine = EngineContextFake.Create(contentService, SpatialFake.Create(hold.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = TestSessions.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new(TestPayload.Definitions, hold, DaggerfallTuning.Defaults, identity) { ArtReplacements = art });
        session.Update(Update());
        session.Update(Update());

        Assert.Equal([$"daggerfall.art/models.closure: Site closure '{hold.ProfileKey.LogicalId}' draws one combined mesh that names no model, so the art pack's 2 ARCH3D remodels draw classic there."],
            engine.PublishedDiagnostics.Where(value => value.StartsWith("daggerfall.art/", StringComparison.Ordinal)));
    }

    /// <summary>An ordinary admitted update: one fixed step with no input.</summary>
    private static ProductUpdate Update() => new(new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 1, 1, 60, 3, 0, 1d / 60d), []);
}
