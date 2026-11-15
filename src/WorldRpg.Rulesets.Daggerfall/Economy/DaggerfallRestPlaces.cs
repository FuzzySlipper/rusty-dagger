using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Property;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Where the player may rest inside a building, and the room a tavern has let them: a paid room there, their own
/// house, or the Fighters Guild hall's privilege (Society's rule).
/// </summary>
internal sealed class DaggerfallRestPlaces(DaggerfallLodgingState lodging, DaggerfallPropertyState property,
    DaggerfallGuildMembershipPolicy guilds)
{
    /// <summary>Whether the building the player is inside lets them rest: a room paid for here, their house, or the hall.</summary>
    internal bool InteriorPrivilege(DaggerfallSiteId site, DaggerfallInteriorBuilding? building, long now, int day)
    {
        if (building is not null && lodging.RemainingSeconds(site,
            new(building.BlockX, building.BlockY, building.Building.Index), now) > 0)
            return true;
        if (building is not null && property.HouseAccess(new(site, building.Building, building.BlockX, building.BlockY)).Allowed)
            return true;
        return DaggerfallConcreteGuildPolicy.FightersHallRestAllowed(building, guilds, site.Region, day);
    }

    /// <summary>Inside a tavern, the seconds left on the room paid for there; null anywhere else.</summary>
    internal long? TavernRoomSeconds(DaggerfallSiteId site, DaggerfallInteriorBuilding? building, long now) =>
        building is { BuildingType: DaggerfallLodgingState.TavernBuildingType } tavern
            ? lodging.RemainingSeconds(site, new(tavern.BlockX, tavern.BlockY, tavern.Building.Index), now)
            : null;
}
