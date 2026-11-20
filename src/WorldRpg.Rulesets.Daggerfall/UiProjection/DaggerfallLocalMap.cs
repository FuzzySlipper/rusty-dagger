using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The local map panel: the city or dungeon map it shows, whether it is open, and its building selection and notes.</summary>
/// <param name="profile">The inputs of the site profile the player is in.</param>
/// <param name="active">The active profile's key.</param>
/// <param name="exteriorSitePosition">Where a point of the exterior profile lies on the site's own map.</param>
internal sealed class DaggerfallLocalMap(PlayerControlState player, DaggerfallSiteContext site,
    IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallDungeonDiscovery> discoveries, Func<DaggerfallSiteProfile> profile,
    Func<DaggerfallWorldProfileKey> active, Func<WorldPoint, WorldPoint> exteriorSitePosition, PresentationState presentation)
{
    private bool _open;

    /// <summary>
    /// The map the HUD draws while the panel is open, and when there is none, what the panel says instead: the place
    /// the player is in has no map (the wilderness, a site with no town, a place whose map was not imported).
    /// </summary>
    internal DaggerfallMapPresentation? ReadOpen(out string? absence)
    {
        absence = null;
        return _open ? Read(out absence) : null;
    }

    internal void SetOpen(bool open) => _open = open;

    internal DaggerfallMapPresentation? Read() => Read(out _);

    private DaggerfallMapPresentation? Read(out string? absence)
    {
        absence = null;
        if (player.Position is not WorldPoint position) return null;
        DaggerfallSiteProfile inputs = profile();
        if (inputs.ProfileKind is (DaggerfallWorldProfileKind.Dungeon or DaggerfallWorldProfileKind.Interior) && discoveries.TryGetValue(active(), out var discovery))
            return DaggerfallMapProjection.Dungeon(inputs, discovery, position, player.YawRadians, site.ActiveSite?.Name);
        if (inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior && site.ActiveSite?.Exterior is not null && site.Blocks is not null)
            return DaggerfallMapProjection.City(site, exteriorSitePosition(position), player.YawRadians);
        absence = inputs.ProfileKind switch
        {
            DaggerfallWorldProfileKind.Dungeon => "There is no map of this dungeon.",
            DaggerfallWorldProfileKind.Interior => "There is no map of this building.",
            _ => site.ActiveSite is { } here ? $"There is no map of {here.Name}." : "There is no map of the wilderness.",
        };
        return null;
    }

    internal void SelectBuilding(DaggerfallPlayerUiAction action)
    {
        if (active().Kind != DaggerfallWorldProfileKind.Exterior || action.Region is not int region || action.Destination is not int index
            || action.Item?.Split('/') is not [var x, var y, var ordinal] || !int.TryParse(x, out int blockX)
            || !int.TryParse(y, out int blockY) || !int.TryParse(ordinal, out int buildingIndex))
        { presentation.SetOutcome("That map building selection is unavailable."); return; }
        try
        {
            if (!site.IsBuildingDiscovered(new(region, index), new(blockX, blockY, buildingIndex)))
            { presentation.SetOutcome("That building has not been discovered."); return; }
            DaggerfallSiteBuildingRecord building = site.SelectBuilding(new(region, index), new(blockX, blockY, buildingIndex));
            presentation.SetOutcome($"Selected {building.Name}. Ask someone here for directions.");
        }
        catch (InvalidOperationException) { presentation.SetOutcome("That building cannot be selected on this map."); }
    }

    /// <summary>
    /// Adds a note at the player's position on the current dungeon's map, or edits or removes one.
    /// Notes belong to the dungeon's discovery owner, which saves them with what the player has explored.
    /// </summary>
    internal void ChangeNote(DaggerfallPlayerUiAction action)
    {
        if (profile().ProfileKind is not (DaggerfallWorldProfileKind.Dungeon or DaggerfallWorldProfileKind.Interior)
            || !discoveries.TryGetValue(active(), out DaggerfallDungeonDiscovery? discovery))
        {
            presentation.SetOutcome("Notes can only be added to an interior or dungeon map.");
            return;
        }
        switch (action.Kind)
        {
            case DaggerfallUiActionKind.MapNoteAdd:
                if (player.Position is not WorldPoint position) { presentation.SetOutcome("You cannot place a note without a position."); return; }
                discovery.AddNoteMarker(discovery.NextNoteId(), position, action.Text!.Trim());
                presentation.SetOutcome("Note added to the map.");
                return;
            case DaggerfallUiActionKind.MapNoteEdit:
                if (discovery.NoteMarkers.All(note => note.Id != action.Note)) { presentation.SetOutcome("That map note no longer exists."); return; }
                discovery.EditNoteMarker(action.Note!, action.Text!.Trim());
                presentation.SetOutcome("Map note changed.");
                return;
            default:
                presentation.SetOutcome(discovery.RemoveNoteMarker(action.Note!) ? "Map note removed." : "That map note no longer exists.");
                return;
        }
    }
}
