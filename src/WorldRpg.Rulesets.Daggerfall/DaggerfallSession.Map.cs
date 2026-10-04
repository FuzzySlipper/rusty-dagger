using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private bool _mapOpen;
    internal DaggerfallMapPresentation? ReadMapPresentation()
    {
        if (State.PlayerControl.Position is not WorldPoint player) return null;
        DaggerfallSiteProfile profile = _sites.Projection.Inputs;
        if (profile.ProfileKind == DaggerfallWorldProfileKind.Dungeon && State.DungeonDiscoveries.TryGetValue(_activeProfileKey, out var discovery))
            return DaggerfallMapProjection.Dungeon(profile, discovery, player, State.PlayerControl.YawRadians, Site.ActiveSite?.Name);
        if (profile.ProfileKind == DaggerfallWorldProfileKind.Exterior && Site.ActiveSite?.Exterior is not null && Site.Blocks is not null)
            return DaggerfallMapProjection.City(Site, _sites.ExteriorSitePosition(player), State.PlayerControl.YawRadians);
        return null;
    }

    private void SelectMapBuilding(DaggerfallPlayerUiAction action)
    {
        if (_activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior || action.Region is not int region || action.Destination is not int index
            || action.Item?.Split('/') is not [var x, var y, var ordinal] || !int.TryParse(x, out int blockX)
            || !int.TryParse(y, out int blockY) || !int.TryParse(ordinal, out int buildingIndex))
        { Presentation.SetOutcome("That map building selection is unavailable."); return; }
        try
        {
            DaggerfallSiteBuildingRecord building = Site.SelectBuilding(new(region, index), new(blockX, blockY, buildingIndex));
            Presentation.SetOutcome($"Selected {building.Name}. Ask someone here for directions.");
        }
        catch (InvalidOperationException exception) { Presentation.SetOutcome(exception.Message); }
    }

    private (string Name, string Hint)? MapDirections()
    {
        if (Site.SelectedBuilding is not { } selected) return null;
        DaggerfallDialogueDestination? destination = ResolveDialogueDirection(
            $"direction:building:{selected.Building.BlockX}:{selected.Building.BlockY}:{selected.Building.Index}");
        return destination is { } value ? (value.Name, value.Hint) : null;
    }

    /// <summary>Builds the talk directory from the site/map/NPC owners already used by map and activation.</summary>
    private IReadOnlyList<DaggerfallDialogueTopicOption> DialogueDirectory()
    {
        DaggerfallSiteRecord? site = Site.ActiveSite;
        if (site is null) return [];
        List<DaggerfallDialogueTopicOption> entries = [];
        foreach (DaggerfallSiteBuildingSource source in Site.BuildingsAt(site.Id)
            .OrderBy(value => value.Id.BlockY).ThenBy(value => value.Id.BlockX).ThenBy(value => value.Id.Index))
        {
            DaggerfallSiteBuildingRecord building;
            try { building = Site.RequireBuilding(site.Id, source.Id); }
            catch (InvalidOperationException) { continue; }
            if (string.IsNullOrWhiteSpace(building.Name)) continue;
            string id = $"direction:building:{source.Id.BlockX}:{source.Id.BlockY}:{source.Id.Index}";
            entries.Add(new(id, $"Where is {building.Name}?"));
        }

        foreach (DaggerfallNpc npc in State.Npcs.All
            .Where(npc => _dialogue?.IsLiveTalkTarget(npc) == true)
            .OrderBy(npc => npc.DurableId))
        {
            string display = npc.DisplayName ?? npc.Role;
            entries.Add(new($"direction:npc:{npc.DurableId}", $"Where is {display}?"));
        }

        foreach (DaggerfallSiteRecord place in Site.Records
            .Where(place => place.Id.Region == site.Id.Region && place.Id != site.Id)
            .OrderBy(place => place.Name, StringComparer.Ordinal).ThenBy(place => place.Id.Index))
            entries.Add(new($"direction:site:{place.Id.Region}:{place.Id.Index}", $"Where is {place.Name}?"));
        return entries;
    }

    /// <summary>
    /// Resolves a directory key against the same live site/NPC/map records that own world state. Site
    /// answers reveal the destination through the existing durable discovery owner; building answers
    /// select the existing transient map marker so the next map projection shows the disclosed target.
    /// </summary>
    private DaggerfallDialogueDestination? ResolveDialogueDirection(string? target)
    {
        DaggerfallSiteRecord? active = Site.ActiveSite;
        if (active is null || string.IsNullOrWhiteSpace(target)) return null;
        if (target.StartsWith("direction:building:", StringComparison.Ordinal))
        {
            string[] parts = target[19..].Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int blockX)
                || !int.TryParse(parts[1], out int blockY) || !int.TryParse(parts[2], out int index)) return null;
            DaggerfallSiteBuildingId id = new(blockX, blockY, index);
            if (_activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior) return null;
            try
            {
                DaggerfallSiteBuildingRecord building = Site.SelectBuilding(active.Id, id);
                bool sameBuilding = _dialogue?.CurrentNpc() is { } speaker
                    && string.Equals(speaker.Site.Building, $"{blockX}/{blockY}/{index}", StringComparison.Ordinal);
                return new(target, building.Name, BuildingHint(active.Id, id), Known: true)
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
            if (npc is null || _dialogue?.IsLiveTalkTarget(npc) != true) return null;
            string hint = _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior
                && npc.X is float x && npc.Z is float z && State.PlayerControl.Position is WorldPoint player
                ? CardinalHint(new(x, player.Y, z), _sites.ExteriorSitePosition(player)) : "here";
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
        if (State.PlayerControl.Position is not WorldPoint player) return "here";
        WorldPoint target = DaggerfallMapProjection.BuildingPosition(Site, site, building);
        return CardinalHint(target, _sites.ExteriorSitePosition(player));
    }

    private static string CardinalHint(WorldPoint target, WorldPoint start)
    {
        string[] directions = ["north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"];
        float angle = MathF.Atan2(target.X - start.X, start.Z - target.Z);
        int octant = ((int)MathF.Round(angle / (MathF.PI / 4)) + 8) % 8;
        return MathF.Abs(target.X - start.X) < 1 && MathF.Abs(target.Z - start.Z) < 1 ? "here" : directions[octant];
    }
}
