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
            .Where(npc => npc.Presence == DaggerfallNpcPresence.Active
                && npc.Site.Region == site.Id.Region
                && string.Equals(npc.Site.Location, site.Name, StringComparison.Ordinal))
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
                return new(target, building.Name, BuildingHint(active.Id, id), Known: true);
            }
            catch (InvalidOperationException) { return null; }
        }

        if (target.StartsWith("direction:npc:", StringComparison.Ordinal)
            && long.TryParse(target[14..], out long npcId))
        {
            DaggerfallNpc? npc = State.Npcs.All.SingleOrDefault(value => value.DurableId == npcId);
            if (npc is null || npc.Presence != DaggerfallNpcPresence.Active
                || npc.Site.Region != active.Id.Region
                || !string.Equals(npc.Site.Location, active.Name, StringComparison.Ordinal)) return null;
            string hint = npc.X is float x && npc.Z is float z && State.PlayerControl.Position is WorldPoint player
                ? CardinalHint(new(x, player.Y, z), _sites.ExteriorSitePosition(player)) : "here";
            return new(target, npc.DisplayName ?? npc.Role, hint, Known: true);
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
            Site.Discover(id);
            return new(target, place.Name, "on the map", known);
        }
        return null;
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
