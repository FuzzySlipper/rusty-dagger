using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class HudSnapshotContractTests
{
    /// <summary>The snapshot the UI suite renders, so both languages read one published document.</summary>
    internal const string HudSnapshotFixture = "tests/WorldRpg.Ui.Tests/fixtures/hud-snapshot.json";

    /// <summary>
    /// The C# projection and the TypeScript reader are checked against one document: this test publishes a
    /// real session snapshot with an open conversation, and the UI suite renders the same file. A key the
    /// projection never emits (the dialogue once was) or one the reader never expects fails one side.
    /// Set <c>DAGGER_WRITE_UI_FIXTURE=1</c> to rewrite the file after an intended projection change.
    /// </summary>
    [Fact]
    public void Published_hud_snapshot_matches_the_fixture_the_ui_suite_renders()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults, identity));
        session.ApplyProductMode(ProductMode.Playing);

        DaggerfallSiteRecord site = session.Site.ActiveSite ?? throw new InvalidOperationException("The fixture session has no admitted site.");
        (int archive, int record) = session.Sites.Projection.Inputs.BillboardSprites.Keys
            .OrderBy(key => key.Item1).ThenBy(key => key.Item2).First();
        long guardId = session.State.Npcs.RegisterCivilian(
            new DaggerfallNpcSite(site.Id.Region, site.Name, string.Empty),
            new DaggerfallNpcAppearance("Breton", "Female", archive, record, 0, 0), "guard", ["talk"]);
        session.MaterializeNpcActor(guardId, session.State.Actors.Get(2000).Pose);
        session.State.Npcs.Place(guardId, session.Sites.ActiveProfile, session.State.Actors.Get(guardId).Position);
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets());
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        // The art images are content bytes rather than projection shape and would make the shared file
        // hundreds of kilobytes; the revision that names them stays.
        Dictionary<string, object?> snapshot = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        Dictionary<string, object?> fatigue = Assert.IsType<object[]>(snapshot["resources"]).Cast<Dictionary<string, object?>>()
            .Single(row => (string)row["id"]! == "stamina");
        Assert.Equal(session.State.Actors.Player.Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("stamina")).ValueInt64 / 64,
            Convert.ToInt64(fatigue["current"]));
        Assert.True(snapshot.Remove("uiArt"));
        // The art revision is minted per session, so the shared file names a fixed one.
        Assert.False(string.IsNullOrEmpty(Assert.IsType<string>(snapshot["uiArtRevision"])));
        snapshot["uiArtRevision"] = "session-art-revision";
        string published = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        Assert.Contains("\"dialogue\": {", published, StringComparison.Ordinal);
        string path = Path.Combine(root, HudSnapshotFixture);
        if (Environment.GetEnvironmentVariable("DAGGER_WRITE_UI_FIXTURE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, published);
        }

        Assert.True(File.Exists(path), $"{HudSnapshotFixture} is missing; run this test with DAGGER_WRITE_UI_FIXTURE=1 to write it.");
        Assert.Equal(File.ReadAllText(path), published);
    }
}
