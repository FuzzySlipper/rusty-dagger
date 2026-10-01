using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallBuildingDirectoryTests
{
    [Fact]
    public void Repeated_names_and_repeated_rmb_slots_remain_distinct_placed_buildings()
    {
        using DaggerfallSession session = Session(out AppearanceFake appearance);
        int appearances = appearance.CreatedAppearances;
        int atlases = appearance.CreatedAtlases;
        DaggerfallSiteContext sites = session.Site;
        DaggerfallSiteRecord charing = Assert.Single(sites.Records, site => site.Name == "Charing");
        DaggerfallSiteBuildingSource[] walls = [.. sites.BuildingsAt(charing.Id).Where(building => building.Source.BuildingType == 23)];
        Assert.True(walls.Length > 2);
        DaggerfallSiteBuildingRecord first = sites.RequireBuilding(charing.Id, walls[0].Id);
        DaggerfallSiteBuildingRecord second = sites.RequireBuilding(charing.Id, walls[1].Id);
        Assert.Equal("City Wall", first.Name);
        Assert.Equal(first.Name, second.Name);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(23, first.BuildingType);
        Assert.Null(first.Faction);
        // Reusing an RMB source block twice in this town does not collapse its placements.
        var repeated = walls.GroupBy(building => building.Source.Id).First(group => group.Count() > 1).ToArray();
        Assert.NotEqual(repeated[0].Id, repeated[1].Id);
        Assert.Equal(sites.RequireBuilding(charing.Id, repeated[0].Id).Name,
            sites.RequireBuilding(charing.Id, repeated[1].Id).Name);
        DaggerfallSiteRecord other = sites.Records.First(site => site.Id != charing.Id
            && site.Exterior!.Buildings.Values.Any(building => building.Source.BuildingType == 23));
        DaggerfallSiteBuildingSource otherWall = sites.BuildingsAt(other.Id).First(building => building.Source.BuildingType == 23);
        Assert.Equal(first.Name, sites.RequireBuilding(other.Id, otherWall.Id).Name);
        Assert.NotEqual(charing.Id, other.Id);
        Assert.NotEqual(charing.Id, sites.Active);
        Assert.Equal(appearances, appearance.CreatedAppearances);
        Assert.Equal(atlases, appearance.CreatedAtlases);
    }

    [Fact]
    public void Location_specific_faction_and_name_resolve_through_the_existing_catalogs()
    {
        using DaggerfallSession session = Session(out _);
        var pair = session.Site.Records.SelectMany(site => session.Site.BuildingsAt(site.Id).Select(building => (Site: site, Building: building)))
            .First(pair => pair.Building.Source.BuildingType == 11 && pair.Building.Source.FactionId != 0
                && TestPayload.Definitions.Factions.Factions.ContainsKey(pair.Building.Source.FactionId));
        DaggerfallSiteBuildingRecord resolved = session.Site.RequireBuilding(pair.Site.Id, pair.Building.Id);
        Assert.Equal(11, resolved.BuildingType);
        Assert.Equal(TestPayload.Definitions.Factions.Factions[resolved.FactionId], resolved.Faction);
        Assert.Equal(resolved.Faction!.Name, resolved.Name);
        Assert.Equal(pair.Building.Quality, resolved.Quality);
        Assert.Same(pair.Building, resolved.Source);
    }

    [Fact]
    public void Content_admission_rejects_unpublished_source_slots_before_they_can_grant_building_meaning()
    {
        DaggerfallSiteRecord site = TestPayload.Definitions.Locations.Records.First(site => site.Exterior!.Buildings.Count > 0);
        DaggerfallSiteBuildingSource building = site.Exterior!.Buildings.Values.First();
        DaggerfallSiteBuildingSource invalid = building with { Source = building.Source with { Id = new("ABSENT.RMB", 255) } };
        DaggerfallSiteRecord broken = site with { Exterior = site.Exterior with
        {
            Buildings = new Dictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource> { [invalid.Id] = invalid },
        } };
        DaggerfallLocationSet locations = TestPayload.Definitions.Locations with { Records = [broken] };
        DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
        string error = Assert.Throws<InvalidOperationException>(() => blocks.AdmitLocations(locations)).Message;
        Assert.Contains(site.Id.ToString(), error);
        Assert.Contains(invalid.Id.ToString(), error);
        Assert.Contains("ABSENT.RMB:255", error);
        // Specialization intentionally changes faction, quality and seed; only the source identity joins.
        blocks.AdmitLocations(TestPayload.Definitions.Locations);
    }

    [Fact]
    public void Missing_building_and_site_reads_name_the_requested_identity()
    {
        using DaggerfallSession session = Session(out _);
        var site = TestPayload.Definitions.Locations.Records[0].Id;
        DaggerfallSiteBuildingId missing = new(99, 98, 97);
        string error = Assert.Throws<InvalidOperationException>(() => session.Site.RequireBuilding(site, missing)).Message;
        Assert.Contains(site.ToString(), error);
        Assert.Contains(missing.ToString(), error);
        DaggerfallSiteId absent = new(99, 99999);
        Assert.Contains(absent.ToString(), Assert.Throws<InvalidOperationException>(() => session.Site.RequireBuilding(absent, missing)).Message);
    }

    [Fact]
    public void The_session_site_owner_resolves_unloaded_buildings_before_any_appearance_is_engaged()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, appearance);
        DaggerfallSiteContext sites = new(TestPayload.Definitions.Locations);
        sites.AdmitBuildingNames(engine.Context.Random, TestPayload.Definitions,
            DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))));
        var site = sites.Records.First(site => site.Exterior!.Buildings.Values.Any(building => building.Source.BuildingType == 23));
        var wall = sites.BuildingsAt(site.Id).First(building => building.Source.BuildingType == 23);
        Assert.Equal("City Wall", sites.RequireBuilding(site.Id, wall.Id).Name);
        Assert.Null(sites.Active);
        Assert.Equal(0, appearance.CreatedAppearances);
        Assert.Equal(0, appearance.CreatedAtlases);
    }

    [Fact]
    public void Directory_reads_add_no_durable_state_or_second_save_section()
    {
        using DaggerfallSession session = Session(out _);
        DaggerfallSiteSave before = session.Site.Capture();
        var site = session.Site.Records.First(site => site.Exterior!.Buildings.Values.Any(building => building.Source.BuildingType == 23));
        var building = session.Site.BuildingsAt(site.Id).First(building => building.Source.BuildingType == 23);
        _ = session.Site.RequireBuilding(site.Id, building.Id);
        DaggerfallSiteSave saved = DaggerfallSavePayload.Read(session.CaptureSave()).Site;
        Assert.Equal(before.Active, saved.Active);
        Assert.Equal(before.ReturnAnchor, saved.ReturnAnchor);
        Assert.Equal(before.Discovered, saved.Discovered);
    }

    private static DaggerfallSession Session(out AppearanceFake appearance)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, appearance);
        return DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults)
        {
            Blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.blocks.json"))),
        });
    }
}
