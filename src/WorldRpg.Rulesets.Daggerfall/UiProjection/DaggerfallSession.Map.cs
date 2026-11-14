using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The local map panel: the city or dungeon map it shows, whether it is open, and its building selection and notes.</summary>
internal sealed partial class DaggerfallSession
{
    private bool _mapOpen;
    internal DaggerfallMapPresentation? ReadMapPresentation()
    {
        if (State.PlayerControl.Position is not WorldPoint player) return null;
        DaggerfallSiteProfile profile = _sites.Projection.Inputs;
        if (profile.ProfileKind is (DaggerfallWorldProfileKind.Dungeon or DaggerfallWorldProfileKind.Interior) && State.DungeonDiscoveries.TryGetValue(_activeProfileKey, out var discovery))
            return DaggerfallMapProjection.Dungeon(profile, discovery, player, State.PlayerControl.YawRadians, Site.ActiveSite?.Name);
        if (profile.ProfileKind == DaggerfallWorldProfileKind.Exterior && Site.ActiveSite?.Exterior is not null && Site.Blocks is not null)
            return DaggerfallMapProjection.City(Site, _exterior.ExteriorSitePosition(player), State.PlayerControl.YawRadians);
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
            if (!Site.IsBuildingDiscovered(new(region, index), new(blockX, blockY, buildingIndex)))
            { Presentation.SetOutcome("That building has not been discovered."); return; }
            DaggerfallSiteBuildingRecord building = Site.SelectBuilding(new(region, index), new(blockX, blockY, buildingIndex));
            Presentation.SetOutcome($"Selected {building.Name}. Ask someone here for directions.");
        }
        catch (InvalidOperationException) { Presentation.SetOutcome("That building cannot be selected on this map."); }
    }

    /// <summary>
    /// Adds a note at the player's position on the current dungeon's map, or edits or removes one.
    /// Notes belong to the dungeon's discovery owner, which saves them with what the player has explored.
    /// </summary>
    private void ChangeMapNote(DaggerfallPlayerUiAction action)
    {
        if (_sites.Projection.Inputs.ProfileKind is not (DaggerfallWorldProfileKind.Dungeon or DaggerfallWorldProfileKind.Interior)
            || !State.DungeonDiscoveries.TryGetValue(_activeProfileKey, out DaggerfallDungeonDiscovery? discovery))
        {
            Presentation.SetOutcome("Notes can only be added to an interior or dungeon map.");
            return;
        }
        switch (action.Kind)
        {
            case DaggerfallUiActionKind.MapNoteAdd:
                if (State.PlayerControl.Position is not WorldPoint position) { Presentation.SetOutcome("You cannot place a note without a position."); return; }
                discovery.AddNoteMarker(discovery.NextNoteId(), position, action.Text!.Trim());
                Presentation.SetOutcome("Note added to the map.");
                return;
            case DaggerfallUiActionKind.MapNoteEdit:
                if (discovery.NoteMarkers.All(note => note.Id != action.Note)) { Presentation.SetOutcome("That map note no longer exists."); return; }
                discovery.EditNoteMarker(action.Note!, action.Text!.Trim());
                Presentation.SetOutcome("Map note changed.");
                return;
            default:
                Presentation.SetOutcome(discovery.RemoveNoteMarker(action.Note!) ? "Map note removed." : "That map note no longer exists.");
                return;
        }
    }
}
