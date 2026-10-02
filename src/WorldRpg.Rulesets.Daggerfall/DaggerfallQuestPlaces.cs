using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Source-selected geography, distinct from whether its projection is currently admitted.</summary>
internal sealed record DaggerfallQuestPlaceSelection(DaggerfallWorldProfileKind Kind, int MapId,
    int? BuildingKey = null, int MagicNumberIndex = 0);

/// <summary>The donor Place policy over the existing immutable site/building/marker catalogs.</summary>
internal sealed class DaggerfallQuestPlaceAllocator(
    DaggerfallDefinitions definitions, DaggerfallSiteContext sites, IRandomService random,
    Func<DaggerfallSiteId, DaggerfallSiteBuildingSource, bool> isOwnedHouse,
    Func<int, string?> regionName,
    Func<string, int, string> residenceName)
{
    private static readonly int[] AllValid = [0, 2, 3, 5, 6, 8, 9, 11, 12, 13, 14, 15, 17, 18, 19, 20];
    private static readonly int[] AnyHouse = [17, 18, 19, 20];
    private static readonly int[] AnyShop = [0, 2, 5, 6, 7, 8, 9, 12, 13];

    internal DaggerfallQuestResourceState Allocate(string instanceId, DaggerfallQuestResourceDefinition resource,
        IEnumerable<DaggerfallQuestResourceState> parentResources, IEnumerable<DaggerfallQuestResourceState> activeResources)
    {
        if (resource.Kind != "place") throw new ArgumentException("Place allocation requires a Place declaration.");
        string identity = $"{instanceId}/{resource.CanonicalId}";
        string name = resource.PlaceKind == "randomPermanent"
            ? Choose(resource.Sites ?? [], identity + "/permanent")
            : resource.TargetSourceSpelling ?? throw new ArgumentException($"Place '{resource.CanonicalId}' has no source table name.");
        DaggerfallQuestPlace row = definitions.QuestSources.Tables.Places.Resolve(name);
        DaggerfallQuestResourceState[] parent = [.. parentResources];
        DaggerfallQuestResourceState[] active = [.. activeResources];
        DaggerfallSiteRecord? current = sites.ActiveSite;
        DaggerfallSiteRecord site;
        DaggerfallSiteBuildingSource? building = null;
        DaggerfallWorldProfileKind kind;
        int magic = 0;
        switch (resource.PlaceKind)
        {
            case "permanent":
            case "randomPermanent":
                if (row.P1 <= 0x300) throw new NotSupportedException($"Permanent Place '{name}' has invalid fixed id {row.P1}.");
                site = sites.Records.FirstOrDefault(value => value.Exterior?.SourceLocationId == row.P1)!;
                if (site is not null) kind = row.P1 == 50000 ? DaggerfallWorldProfileKind.Dungeon : DaggerfallWorldProfileKind.Exterior;
                else
                {
                    site = sites.Records.FirstOrDefault(value => value.Exterior?.SourceLocationId == row.P1 - 1)
                        ?? throw new NotSupportedException($"Permanent Place '{name}' has no source location {row.P1} or {row.P1 - 1}.");
                    kind = site.DungeonBlocks.Count > 0 ? DaggerfallWorldProfileKind.Dungeon : DaggerfallWorldProfileKind.Interior;
                }
                if (kind == DaggerfallWorldProfileKind.Interior)
                {
                    if (!sites.TryResolveQuestBuilding(site.Id, row.P1, out building, out string? unavailable))
                        throw new NotSupportedException(unavailable);
                }
                if (kind == DaggerfallWorldProfileKind.Dungeon)
                {
                    if (!Markers(site, null).Any(marker => row.P1 != 50000 || marker.Kind == DaggerfallSiteMarkerKind.QuestSpawn))
                        throw new NotSupportedException($"Permanent dungeon '{name}' carries no required quest markers.");
                    if (row.P2 >> 8 == 0xfa) magic = row.P2 & 255;
                }
                break;
            case "local" when row.P1 != 1:
                site = current ?? throw new NotSupportedException($"Local Place '{resource.CanonicalId}' requires an actual current site.");
                kind = DaggerfallWorldProfileKind.Interior;
                DaggerfallSiteBuildingSource[] local = Buildings(site, row.P2, row.P3, parent, active);
                if (local.Length == 0 && row.P2 is >= 17 and <= 22) local = Buildings(site, -1, 1, parent, active);
                building = Choose(local, identity + "/local");
                break;
            case "local": // The donor routes local dungeon declarations through the remote dungeon selector.
            case "remote":
                if (current is null) throw new NotSupportedException($"Remote Place '{resource.CanonicalId}' requires the current region.");
                DaggerfallSiteRecord[] region = [.. sites.Records.Where(value => value.Region == current.Region)];
                if (row.P1 == 0)
                {
                    kind = DaggerfallWorldProfileKind.Interior;
                    site = current;
                    // Preserve Place.cs's retries and its p2=-1 fallback. With p3=0 that code collects AllValid, despite setting requiredBuildingType to AnyHouse.
                    for (int attempt = 1; attempt < 500; attempt++)
                    {
                        DaggerfallSiteRecord candidate = Choose(region, identity + $"/town/{attempt}");
                        if (candidate.Id == current.Id || IsDungeon(candidate.Kind)) continue;
                        int type = attempt >= 250 && row.P2 is >= 17 and <= 22 ? -1 : row.P2;
                        int wildcard = row.P3;
                        if (!(type == -1 && wildcard is 0 or 1)
                            && candidate.Exterior?.BuildingReferences.Any(reference => reference.BuildingType == type) != true) continue;
                        DaggerfallSiteBuildingSource[] found = Buildings(candidate, type, wildcard, parent, active);
                        if (found.Length == 0) continue;
                        site = candidate;
                        building = Choose(found, identity + $"/building/{attempt}");
                        break;
                    }
                    if (building is null) throw new NotSupportedException($"Remote Place '{name}' found no eligible building in region {current.Region}.");
                }
                else if (row.P1 == 1)
                {
                    kind = DaggerfallWorldProfileKind.Dungeon;
                    DaggerfallSiteRecord[] DungeonCandidates(int type) => [.. region.Where(value => IsDungeon(value.Kind)
                        && (type == -1 ? value.DungeonType is >= 0 and <= 16 : value.DungeonType == type)
                        && !parent.Concat(active).SelectMany(LocationBindings).Any(binding => binding.PlaceSelection?.Kind == DaggerfallWorldProfileKind.Dungeon
                            && binding.Places[0].Require() == value.Id))];
                    DaggerfallSiteRecord? SelectDungeon(int type)
                    {
                        var candidates = DungeonCandidates(type);
                        if (candidates.Length == 0) return null;
                        var selected = Choose(candidates, identity + $"/dungeon/{type}");
                        return Markers(selected, null).Count > 0 ? selected : null;
                    }
                    site = SelectDungeon(row.P2) ?? SelectDungeon(-1)
                        ?? throw new NotSupportedException($"Remote dungeon Place '{name}' found no marker-bearing eligible dungeon.");
                }
                else if (row.P1 == 2)
                {
                    kind = DaggerfallWorldProfileKind.Exterior;
                    site = Choose(region.Where(value => row.P2 == -1 || (int)value.Kind == row.P2).ToArray(), identity + "/exterior");
                }
                else throw new NotSupportedException($"Remote Place '{name}' has unknown source P1 {row.P1}.");
                break;
            default: throw new ArgumentException($"Place '{resource.CanonicalId}' has unknown scope '{resource.PlaceKind}'.");
        }

        return Selected(resource.CanonicalId, identity, site, building, kind, magic);
    }

    internal DaggerfallQuestResourceState? AllocatePersonHome(string instanceId, DaggerfallQuestResourceDefinition person,
        bool individual, bool questor, DaggerfallWorldProfileKey currentProfile, DaggerfallInteriorBuilding? currentInterior,
        IEnumerable<DaggerfallQuestResourceState> parentResources, IEnumerable<DaggerfallQuestResourceState> activeResources)
    {
        var options = person.Person ?? throw new ArgumentException("Person home selection requires its source options.");
        string symbol = person.CanonicalId + ".home", identity = instanceId + "/" + symbol;
        if (questor || individual && options.AtHome)
        {
            var site = sites.ActiveSite ?? throw new NotSupportedException("A quest giver home requires the actual current site.");
            if (currentProfile.Site != site.Id) throw new ArgumentException("The current Person home profile does not project the current site.");
            if (currentProfile.Kind == DaggerfallWorldProfileKind.Interior && currentInterior is null)
                throw new ArgumentException("The current interior Person home requires its actual building identity.");
            DaggerfallSiteBuildingSource? building = currentInterior is { } interior
                ? sites.RequireBuildingSource(site.Id, new(interior.BlockX, interior.BlockY, interior.Building.Index)) : null;
            return Selected(symbol, identity, site, building, currentProfile.Kind, 0,
                building is null ? null : sites.RequireBuilding(site.Id, building.Id).Name);
        }
        if (individual) return null; // Explicit place-at supplies this individual's dialog place later.
        var current = sites.ActiveSite ?? throw new NotSupportedException("Person home selection requires the current region.");
        string scope = options.Scope ?? (current.Exterior?.Buildings.Count > 0
            && random.DrawKeyed(new(0, "daggerfall.quest.person-home", identity + "/scope", 0, 1)).Value == 0 ? "local" : "remote");
        string preferred = "house";
        string? factionTableKey = options.Group ?? options.FactionType ?? options.Faction;
        if (factionTableKey is not null)
        {
            var hint = definitions.QuestSources.Tables.ActorItemTables.Factions.Resolve(factionTableKey);
            if (hint.P1 == 0 && hint.P2 is >= 0 and <= 20 && hint.P3 == 0)
                preferred = definitions.QuestSources.Tables.Places.Rows.FirstOrDefault(row => row.P2 == hint.P2)?.Name ?? "house";
        }
        DaggerfallQuestResourceDefinition Declaration(string type) => person with
        {
            Kind = "place", CanonicalId = symbol, PlaceKind = scope, Person = null,
            TargetSourceSpelling = type, TargetCanonicalId = type,
        };
        try { return Allocate(instanceId, Declaration(preferred), parentResources, activeResources); }
        catch (NotSupportedException) when (preferred != "house")
        { return Allocate(instanceId, Declaration("house"), parentResources, activeResources); }
    }

    private DaggerfallQuestResourceState Selected(string symbol, string identity, DaggerfallSiteRecord site,
        DaggerfallSiteBuildingSource? building, DaggerfallWorldProfileKind kind, int magic, string? currentName = null)
    {
        int? key = building is null ? null : BuildingKey(building.Id);
        DaggerfallQuestResourceBinding binding = building is null ? DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index))
            : DaggerfallQuestResourceBinding.PlaceBuilding(sites, site.MapId, key!.Value);
        binding = binding with { PlaceSelection = new(kind, site.MapId, key, magic) };
        string? display = currentName ?? (building is null ? null : building.Source.BuildingType is >= 17 and <= 20
            ? residenceName(identity, site.Region) : sites.RequireBuilding(site.Id, building.Id).Name);
        return new(symbol, binding)
        { Text = new(Name: display, NameTwo: site.Name, NameThree: site.Name, NameFour: regionName(site.Region)) };
    }

    internal IReadOnlyList<DaggerfallSiteMarker> Markers(DaggerfallSiteRecord site, DaggerfallSiteBuildingSource? building)
    {
        DaggerfallBlocksSnapshot blocks = sites.Blocks ?? throw new NotSupportedException("Quest allocation requires the admitted block marker catalog.");
        if (building is not null)
            return blocks.QuestMarkers.TryGetValue(new(building.Source.Id.SourceKey, building.Source.Id.Index), out var markers) ? markers : [];
        List<DaggerfallSiteMarker> result = [];
        foreach (DaggerfallSiteDungeonBlock block in site.DungeonBlocks)
            if (blocks.QuestMarkers.TryGetValue(new(block.SourceKey, null), out var markers))
                result.AddRange(markers.Select(marker => marker with { BlockX = block.X, BlockZ = block.Z }));
        return result;
    }

    private DaggerfallSiteBuildingSource[] Buildings(DaggerfallSiteRecord site, int type, int faction,
        DaggerfallQuestResourceState[] parent, DaggerfallQuestResourceState[] active) =>
        [.. sites.BuildingsAt(site.Id).OrderBy(value => value.Id.BlockY).ThenBy(value => value.Id.BlockX).ThenBy(value => value.Id.Index)
            .Where(building => (type == -1 ? (faction == 1 ? AnyHouse : faction == 2 ? AnyShop : AllValid).Contains(building.Source.BuildingType)
                    : building.Source.BuildingType == type)
                && (building.Source.BuildingType != 11 || faction == 0 || building.Source.FactionId == faction)
                && building.Source.FactionId != DaggerfallConcreteGuildCatalog.ThievesFactionId
                && building.Source.FactionId != DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId
                && !isOwnedHouse(site.Id, building)
                && !active.Any(resource => Claims(resource, site.Id, building))
                && (building.Source.BuildingType == 11 || !parent.Any(resource => Claims(resource, site.Id, building)))
                && Markers(site, building).Count > 0)];

    private T Choose<T>(IReadOnlyList<T> values, string key) => values.Count > 0
        ? values[checked((int)random.DrawKeyed(new(0, "daggerfall.quest.place", key, 0, values.Count - 1)).Value)]
        : throw new NotSupportedException($"Quest Place selection '{key}' has no source-backed eligible candidates.");

    private static bool IsDungeon(DaggerfallSiteKind kind) => kind is DaggerfallSiteKind.DungeonKeep
        or DaggerfallSiteKind.DungeonLabyrinth or DaggerfallSiteKind.DungeonRuin or DaggerfallSiteKind.Graveyard;
    internal static IEnumerable<DaggerfallQuestResourceBinding> LocationBindings(DaggerfallQuestResourceState resource)
    {
        if (resource.Binding.Kind == DaggerfallQuestResourceBindingKind.Place) yield return resource.Binding;
        if (resource.SelectedPerson?.Home is { } home) yield return home.Binding;
    }

    internal static bool Claims(DaggerfallQuestResourceState resource, DaggerfallSiteId site, DaggerfallSiteBuildingSource building) =>
        LocationBindings(resource).Any(binding => binding.Building is { } claim && binding.Places[0].Require() == site
            && claim == new DaggerfallQuestBuildingClaim(building.Source.Id.SourceKey, building.Source.Id.Index, building.Id.BlockX, building.Id.BlockY));
    internal static int BuildingKey(DaggerfallSiteBuildingId id)
    {
        int key = (id.BlockX << 16) | (id.BlockY << 8) | id.Index;
        return key == 0 ? 1 << 24 : key;
    }
}
