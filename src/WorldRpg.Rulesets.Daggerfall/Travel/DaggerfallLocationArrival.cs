using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>The side of a location a traveller arrives at, in the donor's numbering.</summary>
internal enum DaggerfallArrivalSide { North = 0, South = 1, East = 2, West = 3 }

/// <summary>
/// Where the player arrives at a location brought in from outside it: fast travel, a court's release, a
/// ship returning to land. This is the donor's <c>StreamingWorld.PositionPlayerToLocation</c>: one side of the
/// location is chosen, from the direction of the journey when there is one, and the player stands a tenth of
/// a block outside that side's midpoint, facing in. A city (and the player's ship) instead receives them at
/// whichever of its source start markers lies nearest that point.
/// </summary>
internal static class DaggerfallLocationArrival
{
    // RMBLayout.RMBSide: one RMB block, a map pixel's eighth.
    private const float BlockSide = DaggerfallTerrainSurfaceBuilder.HorizontalSize / 8F;

    // Buildings can stand at a block's very edge; the donor lands a tenth of a block outside the location.
    private const float Clearance = BlockSide * 0.1F;

    /// <summary>
    /// The side a journey arrives at. Without a journey, or when it does not move, any side is equally likely.
    /// Otherwise the donor weighs the two axes by how far the journey runs along each, and the traveller arrives
    /// at the side they came from.
    /// </summary>
    /// <param name="draw">Draws a value in <c>[0, exclusiveMaximum)</c> from the caller's keyed gameplay RNG.</param>
    internal static DaggerfallArrivalSide Side(DaggerfallTravelMapPixel? origin, DaggerfallSiteRecord destination, Func<int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(draw);
        if (origin is not { } from) return (DaggerfallArrivalSide)draw(4);
        // World X runs east with the map pixel; world Z runs north, against it.
        int east = destination.MapPixelX - from.X;
        int north = from.Y - destination.MapPixelY;
        if (east == 0 && north == 0) return (DaggerfallArrivalSide)draw(4);
        int alongX = Math.Abs(east), alongZ = Math.Abs(north);
        int roll = draw(alongX + alongZ);
        bool eastWest = alongX > alongZ ? roll < alongX : roll >= alongZ;
        return eastWest
            ? east > 0 ? DaggerfallArrivalSide.West : DaggerfallArrivalSide.East
            : north > 0 ? DaggerfallArrivalSide.South : DaggerfallArrivalSide.North;
    }

    /// <summary>The landing pose in the exterior profile's frame.</summary>
    internal static DaggerfallSiteAnchor Landing(DaggerfallSiteProfile exterior, DaggerfallSiteRecord site, DaggerfallArrivalSide side)
    {
        ArgumentNullException.ThrowIfNull(exterior);
        ArgumentNullException.ThrowIfNull(site);
        DaggerfallSiteExterior footprint = site.Exterior
            ?? throw new InvalidOperationException($"Location {site.Id} ('{site.Name}') has no exterior to arrive at.");
        // The profile frame puts the location's near corner at its origin, its blocks running east along +X and
        // north along -Z.
        float halfWidth = footprint.Width * 0.5F * BlockSide, halfHeight = footprint.Height * 0.5F * BlockSide;
        Vector3 centre = new(halfWidth, 0F, -halfHeight);
        (Vector3 offset, Vector3 facing) = side switch
        {
            DaggerfallArrivalSide.North => (new Vector3(0F, 0F, -(halfHeight + Clearance)), Vector3.UnitZ),
            DaggerfallArrivalSide.South => (new Vector3(0F, 0F, halfHeight + Clearance), -Vector3.UnitZ),
            DaggerfallArrivalSide.East => (new Vector3(halfWidth + Clearance, 0F, 0F), -Vector3.UnitX),
            _ => (new Vector3(-(halfWidth + Clearance), 0F, 0F), Vector3.UnitX),
        };
        Vector3 edge = centre + offset;
        float yaw = ActorHeading.Yaw(facing);
        if (site.Kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.HomeYourShips
            && StartMarkers(exterior).OrderBy(marker => Vector3.Distance(edge, marker.Position.ToVector())).FirstOrDefault() is { } nearest)
            return new DaggerfallSiteAnchor("arrival", nearest.Position, yaw, 0F).Validate();
        return new DaggerfallSiteAnchor("arrival", new WorldPoint(edge.X, edge.Y, edge.Z), yaw, 0F).Validate();
    }

    /// <summary>
    /// A location's source start markers: an assembled exterior states each of them; an authored closure
    /// publishes its own start marker as its start.
    /// </summary>
    private static IEnumerable<DaggerfallSiteAnchor> StartMarkers(DaggerfallSiteProfile exterior)
    {
        DaggerfallSiteAnchor[] markers = [.. exterior.Anchors.Values
            .Where(anchor => anchor.Id.StartsWith(DaggerfallLocationAssembly.StartMarkerAnchorPrefix, StringComparison.Ordinal))];
        return markers.Length != 0 || !exterior.Anchors.TryGetValue(DaggerfallLocationAssembly.StartAnchor, out DaggerfallSiteAnchor? start)
            ? markers
            : [start];
    }
}
