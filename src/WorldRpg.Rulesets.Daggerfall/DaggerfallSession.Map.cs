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
        if (_activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior || Site.SelectedBuilding is not { } selected
            || selected.Site != Site.ActiveSite?.Id || State.PlayerControl.Position is not WorldPoint player) return null;
        WorldPoint target = DaggerfallMapProjection.BuildingPosition(Site, selected.Site, selected.Building);
        WorldPoint start = _sites.ExteriorSitePosition(player);
        // Normalized north is -Z; compass order follows the donor's eight direction hints.
        string[] directions = ["north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"];
        float angle = MathF.Atan2(target.X - start.X, start.Z - target.Z);
        int octant = ((int)MathF.Round(angle / (MathF.PI / 4)) + 8) % 8;
        string hint = MathF.Abs(target.X - start.X) < 1 && MathF.Abs(target.Z - start.Z) < 1 ? "here" : directions[octant];
        return (Site.RequireBuilding(selected.Site, selected.Building).Name, hint);
    }
}
