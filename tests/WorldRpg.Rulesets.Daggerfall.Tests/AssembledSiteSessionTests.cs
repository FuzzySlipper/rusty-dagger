using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The site catalog resolves profiles when first needed: composition reads only the start site's closure,
/// and a session enters, saves and restores inside locations assembled from their blocks, keyed by their
/// generated profile ids.
/// </summary>
public sealed class AssembledSiteSessionTests
{
    private static readonly DaggerfallSiteId Charing = new(17, 4);

    /// <summary>
    /// Composing the shipped bundle reads only the new-game start site's closure: no other authored site is
    /// parsed and the per-block publication is never opened until a location needs it.
    /// </summary>
    [Fact]
    public void Composition_reads_only_the_start_site_and_never_opens_the_block_publication()
    {
        string root = TestData.RepositoryRoot;
        ProductContent staged = StagedContent(root, out BundleContentFake bundles);
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(staged, new GameBundleId("daggerfall.classic")).RequireComposition();
        DaggerfallSiteProfile start = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, start);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(start.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));

        using IGameSession created = new DaggerfallRuleset(videosEnabled: false).CreateSession(new GameSessionContext(engine.Context, composition));

        DaggerfallSession session = Assert.IsType<DaggerfallSession>(created);
        DaggerfallSiteProfiles profiles = session.Sites.RequireProfiles();
        Assert.Equal(16, profiles.AuthoredKeys.Count);
        Assert.Equal([start.ProfileKey], profiles.AuthoredKeys.Where(profiles.IsResolved));
        Assert.DoesNotContain(DaggerfallWorldBlocks.BundleId, bundles.OpenedBundles);
        Assert.False(profiles.IsResolved(DaggerfallWorldProfileIds.Exterior(Charing)));

        // Asking for a profile no pack overrides assembles it then, from the blocks it places alone.
        DaggerfallWorldProfileKey interior = UnpublishedCharingInterior(profiles);
        DaggerfallSiteProfile assembled = profiles.Require(interior);
        Assert.Equal(interior, assembled.ProfileKey);
        Assert.Contains(DaggerfallWorldBlocks.BundleId, bundles.OpenedBundles);
        Assert.Equal([DaggerfallWorldBlocks.IndexPath, $"rmb/{assembled.InteriorBuilding!.Building.SourceKey.ToLowerInvariant().Replace('.', '-')}/interior-{interior.LogicalId.Split('-')[^1]}.json"],
            bundles.ReadFiles.Where(file => file.Bundle == DaggerfallWorldBlocks.BundleId).Select(file => file.Path));
        // An authored pack still overrides its explicit id.
        Assert.StartsWith("worldrpg/imports/charing/interior-3-4-0/", profiles.Require(DaggerfallWorldProfileIds.Interior(Charing, new(3, 4, 0))).SpatialArtifact.Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// A session enters an assembled building interior and an assembled dungeon, and a save taken inside
    /// each restores there: the active profile, the inactive sites' detached state, the action graphs and
    /// the dungeon discovery all key on the generated profile ids.
    /// </summary>
    [Fact]
    public void A_session_saves_and_restores_inside_an_assembled_interior_and_an_assembled_dungeon()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        ProductContent full = FullContent(root);
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(full);
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(full, media, definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(full, media));
        DaggerfallLocationAssembly assembly = new(definitions, new DaggerfallWorldBlocks(full), () => product.Value, () => meshes.Value);
        DaggerfallSiteProfiles profiles = DaggerfallSiteProfiles.Resolve([DaggerfallAuthoredSite.Of(source)], assembly, null);
        DaggerfallWorldProfileKey interior = UnpublishedCharingInterior(profiles);
        DaggerfallWorldProfileKey dungeon = definitions.Locations.Records
            .Where(record => record.Region == 17 && record.DungeonBlocks.Count > 0 && record.Climate is not null && record.Id != source.Site)
            .Select(record => DaggerfallWorldProfileIds.Dungeon(record.Id)).First(assembly.Places);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, profiles.Require(interior));
        PopulateContent(content, profiles.Require(dungeon));
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, source, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        Assert.True(session.TryTransitionTo(interior));
        Assert.Equal(interior, session.Sites.ActiveProfile);
        // Every block part of the interior was placed; the source dungeon's closure was removed.
        Assert.Equal(profiles.Require(interior).Geometry.Spatial.Count, spatial.ContentResidencyRequests.Last().Admitted.Length);
        RulesetSavePayload insideInterior = session.CaptureSave();
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(insideInterior);
        Assert.Equal(interior, saved.Site.ActiveProfile!.Require());
        Assert.Equal("17/4/" + interior.LogicalId.Split('/')[2], saved.Site.ActiveProfile.LogicalId);
        Assert.Equal(source.ProfileKey, saved.Site.ReturnProfile!.Require());
        Assert.Contains(saved.SiteDeltas, delta => delta.Profile.Require() == source.ProfileKey);

        using (DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, insideInterior))
        {
            Assert.Equal(interior, restored.Sites.ActiveProfile);
            Assert.Equal(source.ProfileKey, restored.Sites.ReturnProfile);
            Assert.True(restored.TryTransitionTo(dungeon));
            Assert.Equal(dungeon, restored.Sites.ActiveProfile);
            Assert.True(restored.State.DungeonActions.ContainsKey(dungeon));
            RulesetSavePayload insideDungeon = restored.CaptureSave();
            DaggerfallSavePayload dungeonSave = DaggerfallSavePayload.Read(insideDungeon);
            Assert.Equal(dungeon, dungeonSave.Site.ActiveProfile!.Require());
            Assert.Contains(dungeonSave.SiteDeltas, delta => delta.Profile.Require() == interior);
            Assert.Contains(dungeonSave.DungeonActions, snapshot => snapshot.ProfileId == dungeon.LogicalId);
            Assert.Contains(dungeonSave.DungeonDiscovery, snapshot => snapshot.Profile == dungeon);

            using DaggerfallSession again = DaggerfallSession.Restore(engine.Context, composition, insideDungeon);
            Assert.Equal(dungeon, again.Sites.ActiveProfile);
            Assert.True(again.State.DungeonActions.ContainsKey(dungeon));
            Assert.True(again.State.DungeonDiscoveries.ContainsKey(dungeon));
            Assert.Equal(dungeonSave.Doors.Select(door => door.Id), DaggerfallSavePayload.Read(again.CaptureSave()).Doors.Select(door => door.Id));
        }
    }

    /// <summary>The first Charing building with an interior that no authored pack publishes.</summary>
    private static DaggerfallWorldProfileKey UnpublishedCharingInterior(DaggerfallSiteProfiles profiles)
    {
        DaggerfallSiteExterior exterior = TestPayload.Definitions.Locations.Records.Single(record => record.Id == Charing).Exterior!;
        return exterior.Buildings.Keys.OrderBy(building => building.BlockX).ThenBy(building => building.BlockY).ThenBy(building => building.Index)
            .Select(building => DaggerfallWorldProfileIds.Interior(Charing, building))
            .First(key => !profiles.AuthoredKeys.Contains(key) && profiles.TryGet(key, out _));
    }
}
