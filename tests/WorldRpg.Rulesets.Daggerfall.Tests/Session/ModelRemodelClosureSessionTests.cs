using System.Text;
using WorldRpg.Host;
using WorldRpg.Kit;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A site closure draws one pre-combined mesh that names no model, so the art pack's remodels of the models it places
/// cannot reach it: the session says so once, naming them, rather than drawing them classic silently.
/// </summary>
public sealed class ModelRemodelClosureSessionTests
{
    [Fact]
    public void A_new_game_in_a_closure_names_the_remodelled_models_it_draws_classic_once()
    {
        string root = TestData.RepositoryRoot;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile hold = ReadInputs(root);
        Assert.True(hold.Geometry.IsClosure);
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes("""
            {"artReplacements": {"images": [], "models": {
              "61027": {"path": "remodel/61027.rstatmsh", "sha256": "0000000000000001000000000000000200000000000000030000000000000004",
                        "triangles": 12, "slots": [{"slot": 0, "material": "material/texture-67-0"}]}}}}
            """), "test.art");
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(TestSessions.Data(content));
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(TestSessions.Data(content), media, TestPayload.Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(TestSessions.Data(content), media));
        DaggerfallLocationAssembly assembly = new(TestPayload.Definitions, new DaggerfallWorldBlocks(TestSessions.Data(content)), () => product.Value, () => meshes.Value, art.Models);
        DaggerfallSiteProfiles profiles = DaggerfallSiteProfiles.Resolve([DaggerfallAuthoredSite.Of(hold)], assembly, null);
        int levers = profiles.Remodelled(hold.ProfileKey)["61027"];

        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, hold);
        EngineContextFake engine = EngineContextFake.Create(contentService, SpatialFake.Create(hold.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = TestSessions.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new(TestPayload.Definitions, hold, DaggerfallTuning.Defaults, identity) { Profiles = profiles, ArtReplacements = art });
        session.Update(Update());
        session.Update(Update());

        Assert.Equal([$"daggerfall.art/models.closure: Site closure '{hold.ProfileKey.LogicalId}' draws one combined mesh that names no model, so the remodelled models it places draw classic there (model: placements): 61027: {levers}."],
            engine.PublishedDiagnostics.Where(value => value.StartsWith("daggerfall.art/", StringComparison.Ordinal)));
    }

    /// <summary>An ordinary admitted update: one fixed step with no input.</summary>
    private static ProductUpdate Update() => new(new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 1, 1, 60, 3, 0, 1d / 60d), []);
}
