using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// Where the player stands on the world map, and what follows from it: the map pixel (a wilderness step away from a
/// location, or the land a ship sailed from), a quest leg's minutes from there, a map item's reveal of the region, and
/// where an arrival lands at a location's exterior.
/// </summary>
internal sealed class DaggerfallMapPosition(
    DaggerfallTravelPolicy policy,
    DaggerfallTransportPolicy transport,
    DaggerfallSiteContext sites,
    IRandomService random,
    DaggerfallExteriorWorldBounds worldBounds,
    Func<DaggerfallWorldProfileKey> activeProfile,
    Func<DaggerfallExteriorCellId> exteriorCell)
{
    /// <summary>The current world-map pixel, including wilderness steps away from an exterior site and a ship at sea.</summary>
    internal DaggerfallTravelMapPixel Position()
    {
        if (transport.OnShip && transport.ShipReturnProfile is { } land && transport.ShipReturnPose is { } pose)
        {
            DaggerfallSiteRecord origin = sites.Records.Single(record => record.Id == land.Site);
            DaggerfallExteriorCellId cell = DaggerfallExteriorSessionOrigin.CellForLocalPosition(pose.Position,
                DaggerfallExteriorWorldOrigin.At(new(origin.MapPixelX, origin.MapPixelY)), worldBounds);
            return new(cell.X, cell.Y);
        }
        if (activeProfile().Kind == DaggerfallWorldProfileKind.Exterior)
        {
            DaggerfallExteriorCellId cell = exteriorCell();
            return new(cell.X, cell.Y);
        }
        DaggerfallSiteRecord site = sites.ActiveSite
            ?? throw new InvalidOperationException("Quest travel duration requires an active geographic site.");
        return new(site.MapPixelX, site.MapPixelY);
    }

    /// <summary>A quest's cautious leg from where the player stands to a site, in minutes.</summary>
    internal int QuestLegMinutes(DaggerfallSiteId site) => policy.CautiousQuestLegMinutes(Position(), site);

    /// <summary>
    /// Where an arrival lands at a location's exterior: the side it arrives from (the direction it came from, or a keyed
    /// draw without one) and, at a city, the start marker nearest that side.
    /// </summary>
    internal DaggerfallSiteAnchor Landing(DaggerfallSiteProfile exterior, DaggerfallTravelMapPixel? departure, string draw)
    {
        DaggerfallSiteRecord site = sites.Require(exterior.Site
            ?? throw new InvalidOperationException($"Exterior '{exterior.ProfileKey.LogicalId}' names no location."));
        DaggerfallArrivalSide side = DaggerfallLocationArrival.Side(departure, site, count => checked((int)random.DrawKeyed(
            new KeyedRngRequest(0, "daggerfall.location-arrival", draw, 0, count - 1)).Value));
        return DaggerfallLocationArrival.Landing(exterior, site, side);
    }

    /// <summary>
    /// A map's reveal: one location of the player's region that play has not yet revealed, drawn with the map's key, is
    /// revealed. Null with the refusal when the player is in no region or every location there is already known.
    /// </summary>
    internal DaggerfallMapReveal RevealFromMap(string useKey)
    {
        if (sites.Region is not int region)
            return new(null, "A map can only reveal a location while you are in a region.");
        DaggerfallSiteRecord[] eligible = sites.Records.Where(site => site.Id.Region == region && !sites.IsDiscovered(site.Id)).ToArray();
        if (eligible.Length == 0)
            return new(null, "You have already discovered every location in this region.");
        int selected = checked((int)random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.inventory.map-use.v1",
            $"region:{region}:{useKey}", 0, eligible.Length - 1)).Value);
        DaggerfallSiteRecord revealed = eligible[selected];
        sites.Discover(revealed.Id);
        return new(revealed.Name, null);
    }
}
