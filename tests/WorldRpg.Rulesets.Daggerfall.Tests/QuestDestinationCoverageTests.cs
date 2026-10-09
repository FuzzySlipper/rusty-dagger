using System.Reflection;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using Xunit.Abstractions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Destination coverage of the shipped quest corpora. Each corpus's Place declarations are expanded, world-wide,
/// into every profile the source Place rules can select for them (<see cref="QuestDestinationExpansion"/>). Every one must resolve to a catalog
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
        QuestDestinationExpansion expansion = new(definitions, sites, allocator);

        HashSet<DaggerfallWorldProfileKey> everything = [];
        foreach (ContentPack pack in world.ContentPacks.Where(pack => pack.Role == DaggerfallRuleset.QuestCorpusRole
            || pack.Role == DaggerfallRuleset.NamedQuestCorpusRole || pack.Role == DaggerfallRuleset.FightersGuildQuestCorpusRole).OrderBy(pack => pack.Id.Value))
        {
            HashSet<string> files = [.. JsonNode.Parse(pack.Payload.Span)!["quests"]!.AsArray().Select(quest => quest!["sourceFile"]!.GetValue<string>())];
            DaggerfallQuestResourceDefinition[] places = [.. definitions.QuestSources.Resources.Where(value => value.Kind == "place" && files.Contains(value.SourceFile))];
            HashSet<DaggerfallWorldProfileKey> destinations = [];
            foreach (DaggerfallQuestResourceDefinition place in places) destinations.UnionWith(expansion.Selectable(place));
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

    [Fact]
    public void A_remote_house_reached_through_the_retry_fallback_is_a_covered_destination()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
        DaggerfallSiteRecord farmstead = definitions.Locations.Records.Single(site => site.Region == 17 && site.Name == "The Ashwing Farmstead");
        DaggerfallSiteRecord manor = definitions.Locations.Records.Single(site => site.Region == 17 && site.Name == "Hearthfield Manor");
        DaggerfallSiteContext sites = new(definitions.Locations, farmstead.Id, null, []);
        sites.AdmitBuildingNames(RandomMinimum.Create(), definitions, blocks);
        // $CUREWER's `Place _childhouse_ remote house2`: a remote house of type 18 whose P3=0 wildcard is AllValid.
        DaggerfallQuestResourceDefinition childhouse = definitions.QuestSources.Resources.Single(value => value.Kind == "place"
            && value.SourceFile == "$CUREWER.txt" && value.TargetSourceSpelling == "house2");
        DaggerfallQuestPlace row = definitions.QuestSources.Tables.Places.Resolve("house2");
        Assert.Equal((0, 18, 0), (row.P1, row.P2, row.P3));
        DaggerfallQuestPlaceAllocator inspect = new(definitions, sites, RandomMinimum.Create(), (_, _) => false, _ => null, (_, _) => "unused");
        // The region has exact house2 matches, so only the attempts from the fallback on can request the tavern (type 15).
        Assert.Contains(sites.BuildingsAt(farmstead.Id), building => inspect.IsQuestBuilding(farmstead, building, row.P2, row.P3));
        DaggerfallSiteBuildingSource[] wildcard = [.. sites.BuildingsAt(manor.Id).OrderBy(value => value.Id.BlockY).ThenBy(value => value.Id.BlockX)
            .ThenBy(value => value.Id.Index).Where(building => inspect.IsQuestBuilding(manor, building, -1, 0))];
        DaggerfallSiteBuildingSource tavern = Assert.Single(wildcard,
            building => DaggerfallWorldProfileIds.Interior(manor.Id, building.Id).LogicalId == "17/3/interior-0-0-7");
        Assert.Equal(15, tavern.Source.BuildingType);
        Assert.False(inspect.IsQuestBuilding(manor, tavern, row.P2, row.P3));

        // The first 249 town draws land on the current location; the 250th, a wildcard attempt, on the manor and its tavern.
        DaggerfallSiteRecord[] region = [.. sites.Records.Where(site => site.Region == farmstead.Region)];
        string identity = "instance/" + childhouse.CanonicalId;
        long Draw(KeyedRngRequest request)
        {
            string key = request.ToString();
            if (key.Contains(identity + "/town/", StringComparison.Ordinal))
                return Array.FindIndex(region, site => site.Id == (key.Contains(identity + "/town/250", StringComparison.Ordinal) ? manor.Id : farmstead.Id));
            if (key.Contains(identity + "/building/250", StringComparison.Ordinal)) return Array.IndexOf(wildcard, tavern);
            throw new InvalidOperationException($"Unexpected Place draw {key}.");
        }
        DaggerfallQuestPlaceAllocator allocator = new(definitions, sites, ScriptedKeyedRandom.Create(Draw), (_, _) => false, _ => null, (_, _) => "residence");
        DaggerfallWorldProfileKey reached = DaggerfallQuestPlacements.ProfileKey(allocator.Allocate("instance", childhouse, [], []).Binding)!.Value;
        Assert.Equal(DaggerfallWorldProfileIds.Interior(manor.Id, tavern.Id), reached);

        Assert.Contains(reached, new QuestDestinationExpansion(definitions, sites, inspect).Selectable(childhouse));
    }
}

/// <summary>
/// Every profile the allocator can select for a Place declaration, world-wide, over the allocator's own predicates:
/// building type and faction, the MAPS header filter, every building type the retried remote town draw requests (a
/// house type becomes a wildcard from the fallback attempt on, whether or not exact matches exist), the local house
/// fallback, the dungeon-type fallback, required quest markers and the excluded guild halls. The run-time exclusions
/// of claimed and owned buildings and dungeons are not applied; because they can empty the exact candidates, the local
/// house and dungeon-type fallbacks count alongside the exact matches. A remote town draw never selects the current
/// location, so a region with a single location offers no remote building.
/// </summary>
internal sealed class QuestDestinationExpansion(DaggerfallDefinitions definitions, DaggerfallSiteContext sites, DaggerfallQuestPlaceAllocator allocator)
{
    private readonly ILookup<int, DaggerfallSiteRecord> regions = sites.Records.ToLookup(record => record.Region);
    // One selection set per distinct Place rule, shared by every declaration and corpus naming it.
    private readonly Dictionary<string, DaggerfallWorldProfileKey[]> selections = new(StringComparer.Ordinal);

    internal DaggerfallWorldProfileKey[] Selectable(DaggerfallQuestResourceDefinition place)
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

    private IEnumerable<DaggerfallWorldProfileKey> Buildings(DaggerfallSiteRecord site, int type, int faction) =>
        sites.BuildingsAt(site.Id).Where(building => allocator.IsQuestBuilding(site, building, type, faction))
            .Select(building => DaggerfallWorldProfileIds.Interior(site.Id, building.Id));

    // A remote town building: each type the retried draw requests, over the region's towns under the MAPS header filter.
    private IEnumerable<DaggerfallWorldProfileKey> RemoteBuildings(DaggerfallQuestPlace row)
    {
        int[] types = [.. Enumerable.Range(1, DaggerfallQuestPlaceAllocator.RemoteTownAttempts)
            .Select(attempt => DaggerfallQuestPlaceAllocator.RemoteBuildingType(row, attempt)).Distinct()];
        return regions.Where(region => region.Skip(1).Any()).SelectMany(region => types.SelectMany(type => region
            .Where(site => !DaggerfallQuestPlaceAllocator.IsDungeon(site.Kind) && DaggerfallQuestPlaceAllocator.HeaderAdmits(site, type, row.P3))
            .SelectMany(site => Buildings(site, type, row.P3))));
    }

    // A local building at whichever location the quest starts in, with the house fallback.
    private IEnumerable<DaggerfallWorldProfileKey> LocalBuildings(DaggerfallQuestPlace row) => sites.Records.SelectMany(site =>
        DaggerfallQuestPlaceAllocator.HasHouseFallback(row.P2)
            ? Buildings(site, row.P2, row.P3).Concat(Buildings(site, -1, 1)) : Buildings(site, row.P2, row.P3));

    // A remote dungeon of the type, or of any ordinary type, that carries quest markers.
    private IEnumerable<DaggerfallWorldProfileKey> RemoteDungeons(DaggerfallQuestPlace row) => sites.Records
        .Where(site => (DaggerfallQuestPlaceAllocator.DungeonMatches(site, row.P2) || DaggerfallQuestPlaceAllocator.DungeonMatches(site, -1))
            && allocator.Markers(site, null).Count > 0)
        .Select(site => DaggerfallWorldProfileIds.Dungeon(site.Id));

    private DaggerfallWorldProfileKey Permanent(string name) => DaggerfallQuestPlacements.ProfileKey(allocator.Allocate("coverage",
        definitions.QuestSources.Resources.First(value => value.Kind == "place" && value.PlaceKind == "permanent") with
        { CanonicalId = "place", TargetSourceSpelling = name, TargetCanonicalId = name }, [], []).Binding)!.Value;
}

/// <summary>Answers each keyed draw from a script over its request.</summary>
internal class ScriptedKeyedRandom : DispatchProxy
{
    private Func<KeyedRngRequest, long> script = null!;

    internal static IRandomService Create(Func<KeyedRngRequest, long> script)
    {
        IRandomService service = DispatchProxy.Create<IRandomService, ScriptedKeyedRandom>();
        ((ScriptedKeyedRandom)(object)service).script = script;
        return service;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
        ? new KeyedRngReceipt(script((KeyedRngRequest)arguments![0]!))
        : throw new NotSupportedException(method?.Name);
}
