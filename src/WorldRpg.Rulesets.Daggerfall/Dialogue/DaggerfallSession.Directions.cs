using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The "Where is" directory: the places, people and quest places a speaker can point to, and what an answer discloses.</summary>
internal sealed partial class DaggerfallSession
{
    private (string Name, string Hint)? MapDirections()
    {
        if (Site.SelectedBuilding is not { } selected) return null;
        DaggerfallDialogueDestination? destination = ResolveDialogueDirection(
            $"direction:building:{selected.Building.BlockX}:{selected.Building.BlockY}:{selected.Building.Index}");
        return destination is { } value ? (value.Name, value.Hint) : null;
    }

    /// <summary>Builds the talk directory from the site/map/NPC owners already used by map and activation.</summary>
    internal IReadOnlyList<DaggerfallDialogueTopicOption> DialogueDirectory()
    {
        DaggerfallSiteRecord? site = Site.ActiveSite;
        if (site is null) return [];
        List<DaggerfallDialogueTopicOption> entries = [];
        List<(int Order, DaggerfallDialogueTopicOption Topic)> buildings = [];
        foreach (DaggerfallSiteBuildingSource source in Site.BuildingsAt(site.Id)
            .OrderBy(value => value.Id.BlockY).ThenBy(value => value.Id.BlockX).ThenBy(value => value.Id.Index))
        {
            DaggerfallSiteBuildingRecord building;
            try { building = Site.RequireBuilding(site.Id, source.Id); }
            catch (InvalidOperationException) { continue; }
            // Only the kinds of building the directory lists: no houses, walls or other unnamed fabric.
            if (string.IsNullOrWhiteSpace(building.Name)
                || !DaggerfallDialogueTopicGroups.BuildingCategories.TryGetValue(building.BuildingType, out var category)) continue;
            string id = $"direction:building:{source.Id.BlockX}:{source.Id.BlockY}:{source.Id.Index}";
            buildings.Add((category.Order, new(id, $"Where is {building.Name}?", Category: category.Label)));
        }
        entries.AddRange(buildings.OrderBy(entry => entry.Order).ThenBy(entry => entry.Topic.Label, StringComparer.Ordinal).Select(entry => entry.Topic));

        foreach (DaggerfallNpc npc in State.Npcs.All
            .Where(npc => _dialogue?.IsLiveTalkTarget(npc) == true)
            .OrderBy(npc => npc.DurableId))
        {
            string display = npc.DisplayName ?? npc.Role;
            entries.Add(new($"direction:npc:{npc.DurableId}", $"Where is {display}?", Category: DaggerfallDialogueTopicGroups.People));
        }

        foreach (DaggerfallSiteRecord place in Site.Records
            .Where(place => place.Id.Region == site.Id.Region && place.Id != site.Id)
            .OrderBy(place => place.Name, StringComparer.Ordinal).ThenBy(place => place.Id.Index))
            entries.Add(new($"direction:site:{place.Id.Region}:{place.Id.Index}", $"Where is {place.Name}?", Category: DaggerfallDialogueTopicGroups.Elsewhere));
        foreach (var (quest, resource) in State.Quests.DialogueResources())
        {
            string key = $"direction:quest:{Uri.EscapeDataString(quest.InstanceId)}:{Uri.EscapeDataString(resource.Symbol)}";
            if (ResolveDialogueDirection(key) is { } destination) entries.Add(new(key, $"Where is {destination.Name}?", Category: DaggerfallDialogueTopicGroups.QuestPlaces));
        }
        return entries;
    }

    /// <summary>
    /// Resolves a directory key against the same live site/NPC/map records that own world state. Site
    /// answers reveal the destination through the existing durable discovery owner; building answers
    /// select the existing transient map marker so the next map projection shows the disclosed target.
    /// </summary>
    internal DaggerfallDialogueDestination? ResolveDialogueDirection(string? target)
    {
        DaggerfallSiteRecord? active = Site.ActiveSite;
        if (active is null || string.IsNullOrWhiteSpace(target)) return null;
        if (target.StartsWith("direction:quest:", StringComparison.Ordinal))
        {
            if (QuestDirectionTarget(target) is not { } translated) return null;
            var destination = ResolveDialogueDirection(translated.Target);
            return destination is null ? null : destination with { Name = translated.Name };
        }
        if (target.StartsWith("direction:building:", StringComparison.Ordinal))
        {
            string[] parts = target[19..].Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int blockX)
                || !int.TryParse(parts[1], out int blockY) || !int.TryParse(parts[2], out int index)) return null;
            DaggerfallSiteBuildingId id = new(blockX, blockY, index);
            // Asked outdoors or inside one of the town's buildings; a dungeon has no streets to point along.
            if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon) return null;
            try
            {
                DaggerfallSiteBuildingRecord building = Site.RequireBuilding(active.Id, id);
                bool sameBuilding = _dialogue?.CurrentNpc() is { } speaker
                    && string.Equals(speaker.Site.Building, $"{blockX}/{blockY}/{index}", StringComparison.Ordinal);
                return new(target, building.Name, BuildingHint(active.Id, id), Known: Site.IsBuildingDiscovered(active.Id, id))
                {
                    SameBuilding = sameBuilding,
                    QuestLocality = State.Quests.ClaimsBuilding(active.Id, building.Source),
                };
            }
            catch (InvalidOperationException) { return null; }
        }

        if (target.StartsWith("direction:npc:", StringComparison.Ordinal)
            && long.TryParse(target[14..], out long npcId))
        {
            DaggerfallNpc? npc = State.Npcs.All.SingleOrDefault(value => value.DurableId == npcId);
            if (npc is null || _dialogue?.TryReadLiveTalkPosition(npc, out WorldPoint npcPosition) != true) return null;
            string hint = _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior
                && State.PlayerControl.Position is WorldPoint player
                ? CardinalHint(_exterior.ExteriorSitePosition(npcPosition), _exterior.ExteriorSitePosition(player)) : "here";
            DaggerfallNpc? speaker = _dialogue?.CurrentNpc();
            bool sameBuilding = speaker is not null && !string.IsNullOrEmpty(speaker.Site.Building)
                && string.Equals(speaker.Site.Building, npc.Site.Building, StringComparison.Ordinal);
            bool sameOrganization = speaker is not null && FactionsShareOrganization(
                speaker.Appearance.FactionId, npc.Appearance.FactionId);
            bool questLocality = speaker is not null
                && speaker.Site.Region == npc.Site.Region
                && string.Equals(speaker.Site.Location, npc.Site.Location, StringComparison.Ordinal)
                && State.Quests.QuestContacts(npc.DurableId).Count != 0;
            return new(target, npc.DisplayName ?? npc.Role, hint, Known: true)
            {
                SameBuilding = sameBuilding,
                SameOrganization = sameOrganization,
                QuestLocality = questLocality,
            };
        }

        if (target.StartsWith("direction:site:", StringComparison.Ordinal))
        {
            string[] parts = target[15..].Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int region) || !int.TryParse(parts[1], out int index)) return null;
            DaggerfallSiteId id = new(region, index);
            DaggerfallSiteRecord place;
            try { place = Site.Require(id); }
            catch (InvalidOperationException) { return null; }
            if (place.Id.Region != active.Id.Region) return null;
            bool known = Site.IsDiscovered(id);
            return new(target, place.Name, "on the map", known);
        }
        return null;
    }

    private (string Target, string Name)? QuestDirectionTarget(string key)
    {
        string[] parts = key[16..].Split(':');
        if (parts.Length != 2) return null;
        string instance = Uri.UnescapeDataString(parts[0]), symbol = Uri.UnescapeDataString(parts[1]);
        var selected = State.Quests.DialogueResources().SingleOrDefault(value => value.Instance.InstanceId == instance && value.Resource.Symbol == symbol);
        if (selected.Resource is not { } resource) return null;
        string name = resource.Text?.Name ?? resource.SelectedPerson?.DisplayName ?? resource.Symbol;
        if (resource.SelectedPerson is not null)
        {
            foreach (long id in resource.Binding.ActorIds)
                if (ResolveDialogueDirection($"direction:npc:{id}") is not null) return ($"direction:npc:{id}", name);
        }
        var binding = resource.SelectedPerson?.Home?.Binding ?? resource.Binding;
        if (binding.Kind != DaggerfallQuestResourceBindingKind.Place || binding.Places is not [var place] || place.Region is not int region || place.Index is not int index) return null;
        if (Site.ActiveSite?.Id == new DaggerfallSiteId(region, index) && binding.Building is { } building)
            return ($"direction:building:{building.BlockX}:{building.BlockY}:{building.Index}", name);
        return ($"direction:site:{place.Region}:{place.Index}", name);
    }

    /// <summary>
    /// Retains the donor's organization relation: a parent, child, shared hierarchy, or an
    /// authored ally/enemy relation is a known organization subject. The check walks the catalog's
    /// current links with cycle protection instead of equating only exact faction identities.
    /// </summary>
    private bool FactionsShareOrganization(int firstId, int secondId)
    {
        if (firstId == 0 || secondId == 0) return false;
        if (!_definitions.Factions.Factions.ContainsKey(firstId) || !_definitions.Factions.Factions.ContainsKey(secondId)) return false;

        HashSet<int> firstHierarchy = FactionHierarchy(firstId);
        HashSet<int> secondHierarchy = FactionHierarchy(secondId);
        if (firstHierarchy.Overlaps(secondHierarchy)) return true;

        foreach (int ancestorId in firstHierarchy)
        {
            if (!_definitions.Factions.Factions.TryGetValue(ancestorId, out DaggerfallFactionDefinition? ancestor)) continue;
            foreach (int relatedId in secondHierarchy)
                if (ancestor.Allies.Contains(relatedId) || ancestor.Enemies.Contains(relatedId)) return true;
        }
        return false;
    }

    private HashSet<int> FactionHierarchy(int factionId)
    {
        HashSet<int> hierarchy = [];
        int current = factionId;
        while (hierarchy.Add(current)
            && _definitions.Factions.Factions.TryGetValue(current, out DaggerfallFactionDefinition? faction)
            && faction.Parent != 0)
            current = faction.Parent;
        return hierarchy;
    }

    /// <summary>Applies a site disclosure only after the dialogue owner has admitted the answer.</summary>
    private bool DiscloseDialogueDirection(string target)
    {
        if (target.StartsWith("direction:quest:", StringComparison.Ordinal))
            return QuestDirectionTarget(target) is { } quest && DiscloseDialogueDirection(quest.Target);
        if (target.StartsWith("direction:building:", StringComparison.Ordinal))
        {
            if (Site.Active is not { } site || target[19..].Split(':') is not [var x, var y, var ordinal]
                || !int.TryParse(x, out int blockX) || !int.TryParse(y, out int blockY) || !int.TryParse(ordinal, out int buildingIndex)) return false;
            try
            {
                DaggerfallSiteBuildingId building = new(blockX, blockY, buildingIndex);
                Site.DiscoverBuilding(site, building); Site.SelectBuilding(site, building); return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        if (!target.StartsWith("direction:site:", StringComparison.Ordinal)) return false;
        string[] parts = target[15..].Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int region) || !int.TryParse(parts[1], out int index)) return false;
        DaggerfallSiteId id = new(region, index);
        DaggerfallSiteRecord? active = Site.ActiveSite;
        if (active is null || active.Id.Region != region) return false;
        try { _ = Site.Require(id); }
        catch (InvalidOperationException) { return false; }
        if (!Site.IsDiscovered(id)) Site.Discover(id);
        return true;
    }

    private string BuildingHint(DaggerfallSiteId site, DaggerfallSiteBuildingId building)
    {
        // Inside a building the directions start at its door on the map, as the donor's do from the
        // player's map position, since the interior's own coordinates are not the town's.
        WorldPoint? start = _activeProfileKey.Kind == DaggerfallWorldProfileKind.Interior
            && DaggerfallWorldProfileIds.TryParse(_activeProfileKey.LogicalId, out _, out DaggerfallSiteBuildingId? inside) && inside is { } here
                ? Site.BuildingMapPosition(site, here)
            : State.PlayerControl.Position is WorldPoint player ? _exterior.ExteriorSitePosition(player) : null;
        if (start is not { } origin) return "here";
        return CardinalHint(Site.BuildingMapPosition(site, building), origin);
    }

    private static string CardinalHint(WorldPoint target, WorldPoint start)
    {
        return MathF.Abs(target.X - start.X) < 1 && MathF.Abs(target.Z - start.Z) < 1
            ? "here" : DaggerfallCompass.Word(MathF.Atan2(target.X - start.X, start.Z - target.Z));
    }
}
