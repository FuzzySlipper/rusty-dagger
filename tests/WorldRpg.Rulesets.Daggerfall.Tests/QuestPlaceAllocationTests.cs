using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestPlaceAllocationTests
{
    [Fact]
    public void Publication_retains_all_dungeon_placements_and_exact_region_names()
    {
        var definitions = TestPayload.Definitions;
        Assert.Equal(definitions.Locations.Dungeons, definitions.Locations.Records.Count(value => value.DungeonBlocks.Count > 0));
        Assert.Equal("Daggerfall", definitions.BuildingNames.RegionNames[17]);
        Assert.Equal("Cybiades", definitions.BuildingNames.RegionNames[61]);
        Assert.Contains(definitions.Locations.Records, site => site.DungeonBlocks.GroupBy(block => block.SourceKey).Any(group => group.Count() > 1));
    }
    [Fact]
    public void Ordinary_start_selects_an_actual_house_claim_and_retains_selected_text_across_restore()
    {
        DaggerfallDefinitions definitions = Definitions();
        var blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
        var site = definitions.Locations.Records.First(site => site.Exterior?.Buildings.Values.Count(building =>
            building.Source.BuildingType is >= 17 and <= 20 && HasMarkers(building)) >= 2);
        var houses = site.Exterior!.Buildings.Values.Where(building => building.Source.BuildingType is >= 17 and <= 20
            && HasMarkers(building)).OrderBy(building => building.Id.BlockY).ThenBy(building => building.Id.BlockX).ThenBy(building => building.Id.Index).ToArray();
        DaggerfallSiteContext sites = new(definitions.Locations, site.Id, null, []);
        sites.AdmitBuildingNames(RandomMinimum.Create(), definitions, blocks);
        DaggerfallNames names = new(definitions, RandomMinimum.Create());
        DaggerfallQuestInstances quests = new(definitions, RandomMinimum.Create());
        quests.BindPlaceAllocator(new(definitions, sites, RandomMinimum.Create(), (_, _) => false, _ => "Test region", names.Residence));
        var first = quests.Start(new("first", "allocation.txt", "allocation", DaggerfallQuestLifecycle.Active, null, [], []));
        var resource = Assert.Single(first.Resources);
        Assert.Equal(houses[0].Source.Id.SourceKey, resource.Binding.Building!.SourceKey);
        Assert.Equal(houses[0].Id.Index, resource.Binding.Building.Index);
        Assert.Equal(site.MapId, resource.Binding.PlaceSelection!.MapId);
        Assert.Equal(DaggerfallQuestPlaceAllocator.BuildingKey(houses[0].Id), resource.Binding.PlaceSelection.BuildingKey);
        Assert.True(quests.ClaimsBuilding(site.Id, houses[0]));
        Assert.Contains("Residence", resource.Text!.Name);
        Assert.Equal(site.Name, resource.Text.NameTwo);
        Assert.Equal(site.Name, resource.Text.NameThree);
        Assert.Equal("Test region", resource.Text.NameFour);
        var second = quests.Start(new("second", "allocation.txt", "allocation", DaggerfallQuestLifecycle.Active, null, [], []));
        Assert.Equal(houses[1].Id.Index, second.Resources[0].Binding.Building!.Index);
        Assert.NotEqual(resource.Binding.Building, second.Resources[0].Binding.Building);
        DaggerfallQuestInstances restored = new(definitions, RandomMaximum.Create());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(quests.Capture(), DaggerfallSaveJsonContext.Default.DaggerfallQuestInstancesSave);
        restored.Restore(JsonSerializer.Deserialize(bytes, DaggerfallSaveJsonContext.Default.DaggerfallQuestInstancesSave)!);
        Assert.True(restored.TryGet("first", out var saved));
        Assert.Equal(resource.Binding.Building, saved!.Resources[0].Binding.Building);
        Assert.Equal(resource.Binding.PlaceSelection, saved.Resources[0].Binding.PlaceSelection);
        Assert.Equal(resource.Text, saved.Resources[0].Text);
        Assert.True(restored.ClaimsBuilding(site.Id, houses[0]));
        restored.Complete("first", "finished");
        Assert.False(restored.ClaimsBuilding(site.Id, houses[0]));

        bool HasMarkers(DaggerfallSiteBuildingSource building) => blocks.QuestMarkers.TryGetValue(
            new(building.Source.Id.SourceKey, building.Source.Id.Index), out var markers) && markers.Count > 0;
    }

    [Fact]
    public void Caller_supplied_symbol_spelling_does_not_allocate_a_second_place()
    {
        DaggerfallDefinitions definitions = Definitions();
        var site = definitions.Locations.Records[0];
        DaggerfallSiteContext sites = new(definitions.Locations, site.Id, null, []);
        DaggerfallQuestInstances quests = new(definitions, RandomMinimum.Create());
        // No marker catalog: this only succeeds when the explicit binding is honored.
        quests.BindPlaceAllocator(new(definitions, sites, RandomMinimum.Create(), (_, _) => false, _ => null, (_, _) => throw new Exception()));
        var started = quests.Start(new("provided", "allocation.txt", "allocation", DaggerfallQuestLifecycle.Active, null,
            [new("_house_", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)))], []));
        Assert.Single(started.Resources);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Owned_active_and_marker_free_buildings_are_ineligible_without_a_substitute(bool markersAvailable, bool owned, bool claimed)
    {
        DaggerfallDefinitions definitions = Definitions();
        var blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
        bool HasMarkers(DaggerfallSiteBuildingSource value) => value.Source.BuildingType == 17 && value.Source.FactionId is not (42 or 108)
            && blocks.QuestMarkers.TryGetValue(new(value.Source.Id.SourceKey, value.Source.Id.Index), out var markers) && markers.Count > 0;
        var source = definitions.Locations.Records.First(site => site.Exterior?.Buildings.Values.Any(HasMarkers) == true);
        var building = source.Exterior!.Buildings.Values.First(HasMarkers);
        var site = source with { Exterior = source.Exterior with { Buildings = new Dictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource> { [building.Id] = building } } };
        DaggerfallSiteContext sites = new(definitions.Locations with { Records = [site] }, site.Id, null, []);
        sites.AdmitBuildingNames(RandomMinimum.Create(), definitions, markersAvailable ? blocks : new(new Dictionary<DaggerfallRmbBuildingId, DaggerfallRmbBuildingSource>()));
        DaggerfallQuestPlaceAllocator allocator = new(definitions, sites, RandomMinimum.Create(), (_, _) => owned, _ => null, (_, _) => "unused");
        var declaration = definitions.QuestSources.Resources.Single(value => value.SourceFile == "allocation.txt");
        Assert.Contains("no source-backed", Assert.Throws<NotSupportedException>(() => allocator.Allocate("unavailable-house", declaration, [], claimed ? [new DaggerfallQuestResourceState("house", DaggerfallQuestResourceBinding.PlaceBuilding(sites, site.MapId, DaggerfallQuestPlaceAllocator.BuildingKey(building.Id)))] : [])).Message);
        Assert.False(sites.TryResolveQuestBuilding(site.Id, 0, out _, out _));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Shared_name_composition_preserves_the_existing_biography_selections(int bank)
    {
        string[] races = ["breton", "redguard", "nord", "dark-elf", "high-elf", "wood-elf", "khajiit", "argonian"];
        var definitions = TestPayload.Definitions;
        var people = DaggerfallBiographyPeople.Roll(definitions, races[bank], RandomMinimum.Create(), 0);
        var names = new DaggerfallNames(definitions, RandomMinimum.Create());
        Assert.Equal(people.Name, names.FullName(bank, false, "name"));
        Assert.Equal(people.FemaleName, names.FullName(bank, true, "female"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Remote_exact_type_preserves_the_donor_maps_header_filter(bool inHeader)
    {
        var definitions = TestPayload.Definitions;
        var blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
        bool Eligible(DaggerfallSiteBuildingSource building) => building.Source.BuildingType == 15
            && building.Source.FactionId is not (42 or 108) && blocks.QuestMarkers.TryGetValue(new(building.Source.Id.SourceKey, building.Source.Id.Index), out var markers) && markers.Count > 0;
        var original = definitions.Locations.Records.First(site => site.Index > 0 && site.Kind == DaggerfallSiteKind.TownCity
            && site.Exterior?.Buildings.Values.Any(Eligible) == true);
        var building = original.Exterior!.Buildings.Values.First(Eligible);
        var destination = original with { Exterior = original.Exterior with {
            Buildings = new Dictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource> { [building.Id] = building },
            BuildingReferences = inHeader ? [new(building.SourceLocationId ?? 0, 0, 15)] : [] } };
        var origin = definitions.Locations.Records.Single(site => site.Region == destination.Region && site.Index == 0);
        DaggerfallSiteContext sites = new(definitions.Locations with { Records = [origin, destination] }, origin.Id, null, []);
        sites.AdmitBuildingNames(LodgingRandom.Create(), definitions, blocks);
        DaggerfallQuestPlaceAllocator allocator = new(definitions, sites, LodgingRandom.Create(), (_, _) => false, _ => null, (_, _) => "unused");
        var declaration = definitions.QuestSources.Resources.First(value => value.Kind == "place" && value.PlaceKind == "remote" && value.TargetSourceSpelling == "tavern");
        if (inHeader)
        {
            var selected = allocator.Allocate("remote", declaration, [], []);
            Assert.Equal(destination.Id, selected.Binding.Places[0].Require());
            Assert.Equal(building.Source.Id.SourceKey, selected.Binding.Building!.SourceKey);
        }
        else Assert.Contains("no eligible building", Assert.Throws<NotSupportedException>(() => allocator.Allocate("remote", declaration, [], [])).Message);
    }

    internal static DaggerfallDefinitions Definitions()
    {
        JsonObject root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"allocation","displayName":"","sourceFile":"allocation.txt","disposition":"compiled","messages":[],"blocks":[],"diagnostics":[]}
            """));
        root["questSources"]!["resources"]!["declarations"]!.AsArray().Add(JsonNode.Parse("""
            {"quest":"allocation","sourceFile":"allocation.txt","sourceLine":1,"kind":"place","symbol":{"sourceSpelling":"_house_","canonicalId":"house"},"sourceText":"Place _house_ local house","targetSourceSpelling":"house","targetCanonicalId":"house","placeKind":"local","parameters":[],"foe":null,"item":null,"person":null,"place":{"sites":[{"sourceSpelling":"house","canonicalId":"house"}]}}
            """));
        return DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
}
