using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Every permanent Place of the source Places table (Quests-Places.txt) selects its fixed location (SetupFixedLocation:
/// the exterior its id names, else the dungeon or building of the id before it) and that selection names a profile
/// the catalog resolves, with the dungeon's quest markers. The quest sources name 21 of those locations, the
/// main-quest cities, castles and covens among them.
/// </summary>
public sealed class QuestPermanentPlaceTests
{
    [Fact]
    public void Every_permanent_place_resolves_to_its_catalog_profile_and_the_sources_name_twenty_one_locations()
    {
        AssembledWorld world = new();
        DaggerfallDefinitions definitions = world.Definitions;
        DaggerfallSiteContext sites = new(definitions.Locations, AssembledWorld.PrivateersHold, null, []);
        sites.AdmitBuildingNames(RandomMinimum.Create(), definitions, world.Blocks);
        DaggerfallQuestPlaceAllocator allocator = new(definitions, sites, RandomMinimum.Create(), (_, _) => false, _ => null, (_, _) => "unused");
        DaggerfallQuestResourceDefinition template = definitions.QuestSources.Resources.First(value => value.Kind == "place" && value.PlaceKind == "permanent");

        Dictionary<string, DaggerfallQuestResourceBinding> selected = new(StringComparer.Ordinal);
        foreach (DaggerfallQuestPlace row in definitions.QuestSources.Tables.Places.Rows.Where(row => row.P1 > 0x300))
        {
            DaggerfallQuestResourceBinding binding = allocator.Allocate("permanent", template with
                { CanonicalId = "place", TargetSourceSpelling = row.Name, TargetCanonicalId = row.Name }, [], []).Binding;
            DaggerfallWorldProfileKey key = DaggerfallQuestPlacements.ProfileKey(binding) ?? throw new InvalidOperationException(row.Name);
            Assert.True(world.Profiles.TryGet(key, out DaggerfallSiteProfile profile), $"{row.Name}: {key.LogicalId}");
            Assert.True(DaggerfallQuestPlacements.Matches(binding, profile), row.Name);
            DaggerfallSiteRecord site = definitions.Locations.Records.Single(record => record.Id == key.Site);
            if (key.Kind == DaggerfallWorldProfileKind.Dungeon)
            {
                // The profile carries the markers the selection required, every one in the location's own block layout.
                Assert.Equal(allocator.Markers(site, null).Select(marker => (marker.SourceKey, marker.BlockX, marker.BlockZ, marker.SourceOrdinal)).Order(),
                    profile.QuestMarkers.Select(marker => (marker.SourceKey, marker.BlockX, marker.BlockZ, marker.SourceOrdinal)).Order());
                Assert.NotEmpty(DaggerfallQuestPlacements.SourceOrder(profile, site, DaggerfallSiteMarkerKind.QuestSpawn));
            }
            selected.Add(row.Name, binding);
        }

        // The permanent Places the quest sources declare, and the list the one randomPermanent Place draws from.
        string[] named = [.. definitions.QuestSources.Resources.Where(value => value.Kind == "place" && value.PlaceKind == "permanent")
                .Select(value => value.TargetSourceSpelling!)
            .Concat(definitions.QuestSources.Resources.Where(value => value.Kind == "place" && value.PlaceKind == "randomPermanent")
                .SelectMany(value => value.Sites!))
            .Distinct(StringComparer.Ordinal)];
        string[] locations = [.. named.Select(name => definitions.QuestSources.Tables.Places.Resolve(name).Name)
            .Select(name => selected[name].Places[0].Require())
            .Distinct()
            .Select(id => definitions.Locations.Records.Single(record => record.Id == id).Name)
            .Order(StringComparer.Ordinal)];
        Assert.Equal([
            "Castle Faallem", "Castle Llugwych", "Castle Necromoghan", "Coven of the Tide", "Coven on the Bluff", "Daggerfall",
            "Direnni Tower", "Glenmoril Coven", "Kykos Coven", "Lysandus' Tomb", "Mantellan Crux", "Orsinium", "Privateer's Hold",
            "Scourg Barrow", "Sentinel", "Shedungent", "Skeffington Coven", "The Fortress of Fhojum", "Tristore Laboratory",
            "Wayrest", "Woodborne Hall"], locations);
    }
}
