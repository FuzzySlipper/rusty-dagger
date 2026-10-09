using System.Text.Json.Nodes;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using Xunit.Abstractions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Destination coverage of the shipped quest corpora. Each corpus's Place declarations are expanded, world-wide,
/// into every profile the source Place rules can select for them (the allocator's eligibility: building type and
/// faction, the MAPS header filter, the house fallback, the dungeon-type fallback, quest markers, the excluded guild
/// halls; the dynamic exclusions of claimed and owned buildings are left out). Every one must resolve to a catalog
/// profile, and the block each draws its quest markers from must publish the markers the selection relied on. The
/// counts, with the share the bundle's site packs publish, are written to the test output for the coverage record.
/// </summary>
public sealed class QuestDestinationCoverageTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_destination_the_quest_corpora_can_select_resolves_with_the_markers_it_was_selected_for()
    {
        AssembledWorld world = new();
        DaggerfallDefinitions definitions = world.Definitions;
        DaggerfallSiteContext sites = new(definitions.Locations, AssembledWorld.PrivateersHold, null, []);
        sites.AdmitBuildingNames(RandomMinimum.Create(), definitions, world.Blocks);
        DaggerfallQuestPlaceAllocator allocator = new(definitions, sites, RandomMinimum.Create(), (_, _) => false, _ => null, (_, _) => "unused");
        ILookup<int, DaggerfallSiteRecord> regions = sites.Records.ToLookup(record => record.Region);

        // Each location's marker-bearing buildings outside the excluded guild halls, read once.
        Dictionary<DaggerfallSiteId, DaggerfallSiteBuildingSource[]> eligible = [];
        IEnumerable<DaggerfallWorldProfileKey> Buildings(DaggerfallSiteRecord site, int type, int faction)
        {
            if (!eligible.TryGetValue(site.Id, out DaggerfallSiteBuildingSource[]? buildings))
                eligible[site.Id] = buildings = [.. sites.BuildingsAt(site.Id).Where(building => building.Source.FactionId != DaggerfallConcreteGuildCatalog.ThievesFactionId
                    && building.Source.FactionId != DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId && allocator.Markers(site, building).Count > 0)];
            return buildings.Where(building => DaggerfallQuestPlaceAllocator.BuildingMatches(building.Source.BuildingType, building.Source.FactionId, type, faction))
                .Select(building => DaggerfallWorldProfileIds.Interior(site.Id, building.Id));
        }
        // A remote town building: the retried draw over the region's other towns, under the MAPS header filter,
        // falling back to any building for a house type when the region has none of it.
        IEnumerable<DaggerfallWorldProfileKey> RemoteBuildings(DaggerfallQuestPlace row)
        {
            foreach (IGrouping<int, DaggerfallSiteRecord> region in regions)
            {
                IEnumerable<DaggerfallWorldProfileKey> Of(int type) => region.Where(site => !IsDungeon(site.Kind)
                        && (type == -1 && row.P3 is 0 or 1 || site.Exterior?.BuildingReferences.Any(reference => reference.BuildingType == type) == true))
                    .SelectMany(site => Buildings(site, type, row.P3));
                DaggerfallWorldProfileKey[] exact = [.. Of(row.P2)];
                foreach (DaggerfallWorldProfileKey key in exact.Length == 0 && row.P2 is >= 17 and <= 22 ? Of(-1) : exact) yield return key;
            }
        }
        // A local building at whichever location the quest starts in, with the house fallback.
        IEnumerable<DaggerfallWorldProfileKey> LocalBuildings(DaggerfallQuestPlace row) => sites.Records.SelectMany(site =>
        {
            DaggerfallWorldProfileKey[] exact = [.. Buildings(site, row.P2, row.P3)];
            return exact.Length == 0 && row.P2 is >= 17 and <= 22 ? Buildings(site, -1, 1) : exact;
        });
        // A remote dungeon of the type, else of any ordinary type, that carries quest markers.
        IEnumerable<DaggerfallWorldProfileKey> RemoteDungeons(DaggerfallQuestPlace row) => regions.SelectMany(region =>
        {
            DaggerfallSiteRecord[] Of(int type) => [.. region.Where(site => IsDungeon(site.Kind)
                && (type == -1 ? site.DungeonType is >= 0 and <= 16 : site.DungeonType == type) && allocator.Markers(site, null).Count > 0)];
            DaggerfallSiteRecord[] exact = Of(row.P2);
            return (exact.Length == 0 ? Of(-1) : exact).Select(site => DaggerfallWorldProfileIds.Dungeon(site.Id));
        });
        DaggerfallWorldProfileKey Permanent(string name) => DaggerfallQuestPlacements.ProfileKey(allocator.Allocate("coverage",
            definitions.QuestSources.Resources.First(value => value.Kind == "place" && value.PlaceKind == "permanent") with
            { CanonicalId = "place", TargetSourceSpelling = name, TargetCanonicalId = name }, [], []).Binding)!.Value;

        // One selection set per distinct Place rule, shared by every declaration and corpus naming it.
        Dictionary<string, DaggerfallWorldProfileKey[]> selections = new(StringComparer.Ordinal);
        DaggerfallWorldProfileKey[] Selectable(DaggerfallQuestResourceDefinition place)
        {
            // A randomPermanent Place lists its sites; every other Place names one Places table row.
            DaggerfallQuestPlace row = definitions.QuestSources.Tables.Places.Resolve(
                place.PlaceKind == "randomPermanent" ? place.Sites![0] : place.TargetSourceSpelling!);
            string rule = place.PlaceKind is "permanent" or "randomPermanent"
                ? place.PlaceKind + ":" + string.Join(",", place.PlaceKind == "randomPermanent" ? place.Sites! : [place.TargetSourceSpelling!])
                : $"{(row.P1 == 1 ? "remote" : place.PlaceKind)}/{row.P1}/{row.P2}/{row.P3}";
            if (selections.TryGetValue(rule, out DaggerfallWorldProfileKey[]? cached)) return cached;
            DaggerfallWorldProfileKey[] keys = [.. (place.PlaceKind switch
            {
                "permanent" => [Permanent(place.TargetSourceSpelling!)],
                "randomPermanent" => place.Sites!.Select(Permanent),
                "local" or "remote" when row.P1 == 1 => RemoteDungeons(row),
                "local" => LocalBuildings(row),
                "remote" when row.P1 == 0 => RemoteBuildings(row),
                "remote" when row.P1 == 2 => sites.Records.Where(site => row.P2 == -1 || (int)site.Kind == row.P2)
                    .Select(site => DaggerfallWorldProfileIds.Exterior(site.Id)),
                _ => throw new InvalidOperationException($"{place.SourceFile} '{place.SourceText}' has no Place rule."),
            }).Distinct()];
            Assert.True(keys.Length > 0, $"{place.SourceFile} '{place.SourceText}' selects nothing.");
            return selections[rule] = keys;
        }

        HashSet<DaggerfallWorldProfileKey> everything = [];
        foreach (ContentPack pack in world.ContentPacks.Where(pack => pack.Role == DaggerfallRuleset.QuestCorpusRole
            || pack.Role == DaggerfallRuleset.NamedQuestCorpusRole || pack.Role == DaggerfallRuleset.FightersGuildQuestCorpusRole).OrderBy(pack => pack.Id.Value))
        {
            HashSet<string> files = [.. JsonNode.Parse(pack.Payload.Span)!["quests"]!.AsArray().Select(quest => quest!["sourceFile"]!.GetValue<string>())];
            DaggerfallQuestResourceDefinition[] places = [.. definitions.QuestSources.Resources.Where(value => value.Kind == "place" && files.Contains(value.SourceFile))];
            HashSet<DaggerfallWorldProfileKey> destinations = [];
            foreach (DaggerfallQuestResourceDefinition place in places) destinations.UnionWith(Selectable(place));
            DaggerfallWorldProfileKey[] unresolved = [.. destinations.Where(key => !world.Profiles.Contains(key))];
            Assert.True(unresolved.Length == 0, $"{pack.Id.Value}: {unresolved.Length} destinations have no catalog profile, e.g. {unresolved.FirstOrDefault().LogicalId}.");
            output.WriteLine(string.Join(" | ", pack.Id.Value, $"quests {files.Count}", $"places {places.Length}",
                string.Join(" ", places.GroupBy(place => place.PlaceKind).OrderBy(group => group.Key).Select(group => $"{group.Key} {group.Count()}")),
                $"locations {destinations.Select(key => key.Site).Distinct().Count()}",
                $"profiles {destinations.Count} (interiors {destinations.Count(key => key.Kind == DaggerfallWorldProfileKind.Interior)}, "
                    + $"dungeons {destinations.Count(key => key.Kind == DaggerfallWorldProfileKind.Dungeon)}, exteriors {destinations.Count(key => key.Kind == DaggerfallWorldProfileKind.Exterior)})",
                $"published before {destinations.Count(world.PublishedKeys.Contains)}",
                $"reachable now {destinations.Count - unresolved.Length}"));
            everything.UnionWith(destinations);
        }
        output.WriteLine($"all corpora | locations {everything.Select(key => key.Site).Distinct().Count()} | profiles {everything.Count} | published before {everything.Count(world.PublishedKeys.Contains)}");

        // Each interior and dungeon block publishes the quest markers the selection read from the block catalog.
        Dictionary<DaggerfallWorldBlockKey, IReadOnlyList<DaggerfallSiteMarker>> expected = [];
        foreach (DaggerfallWorldProfileKey key in everything)
        {
            Assert.True(sites.TryFind(key.Site, out DaggerfallSiteRecord site));
            if (key.Kind == DaggerfallWorldProfileKind.Interior)
            {
                Assert.True(DaggerfallWorldProfileIds.TryParse(key.LogicalId, out _, out DaggerfallSiteBuildingId? placed));
                DaggerfallSiteBuildingSource building = sites.RequireBuildingSource(site.Id, placed!.Value);
                expected.TryAdd(new(DaggerfallWorldBlockKind.RmbInterior, building.Source.Id.SourceKey, building.Source.Id.Index), allocator.Markers(site, building));
            }
            else if (key.Kind == DaggerfallWorldProfileKind.Dungeon)
                foreach (DaggerfallSiteDungeonBlock block in site.DungeonBlocks)
                    expected.TryAdd(new(DaggerfallWorldBlockKind.Rdb, block.SourceKey), world.Blocks.QuestMarkers.GetValueOrDefault(new(block.SourceKey, null)) ?? []);
        }
        foreach (DaggerfallWorldBlockKey[] batch in expected.Keys.Chunk(256))
            foreach ((DaggerfallWorldBlockKey key, DaggerfallWorldBlockDocument document) in world.WorldBlocks.Read(batch))
                Assert.True(expected[key].Select(marker => (marker.Kind, marker.SourceOrdinal)).Order().SequenceEqual(document.Section("questMarkers")
                    .Select(marker => (marker!["kind"]!.GetValue<string>() == "spawn" ? DaggerfallSiteMarkerKind.QuestSpawn : DaggerfallSiteMarkerKind.QuestItem,
                        (int?)marker["sourceOrdinal"]!.GetValue<int>())).Order()), $"{key} publishes other quest markers than the block catalog.");
        output.WriteLine($"blocks checked for quest markers | {expected.Count}");
    }

    private static bool IsDungeon(DaggerfallSiteKind kind) => kind is DaggerfallSiteKind.DungeonKeep
        or DaggerfallSiteKind.DungeonLabyrinth or DaggerfallSiteKind.DungeonRuin or DaggerfallSiteKind.Graveyard;
}
