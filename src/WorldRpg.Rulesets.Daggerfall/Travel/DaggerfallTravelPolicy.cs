using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>A site that the travel map can expose as a destination.</summary>
internal readonly record struct DaggerfallTravelDestination(
    DaggerfallSiteId Id,
    string Name,
    DaggerfallSiteKind Kind,
    DaggerfallTravelMapPixel MapPixel);

/// <summary>
/// Daggerfall's overland route and its minutes over the normalized site and climate grids: the destinations the map
/// offers, the donor's route walk, a pixel's minutes by transport and terrain, and a quest leg's clock.
/// </summary>
/// <remarks>
/// This is the product-owned equivalent of DFU's <c>TravelTimeCalculator</c>.  It retains the
/// donor's integer path walk and fixed-point shifts, while reading the admitted Rusty grids and site
/// identities instead of a Unity content singleton.  The policy does not advance the calendar or
/// mutate transport state; the expedition and the session own those admitted operations.
/// </remarks>
internal sealed class DaggerfallTravelPolicy
{
    // CLIMATE.PAK's value for open sea.
    private const int OceanClimate = 223;

    // DFU's climateIndices starts at CLIMATE.PAK value 223 (Ocean).
    private static readonly int[] ClimateIndices = [0, 0, 0, 1, 2, 3, 4, 5, 5, 5];

    // Indexed by the climate index above. Values are the donor's fixed-point movement modifiers.
    private static readonly int[] TerrainMovementModifiers = [240, 220, 200, 200, 230, 250];

    private readonly DaggerfallSiteContext _sites;
    private readonly DaggerfallWorldGridsSet _grids;
    private readonly DaggerfallTransportTuning _transport;

    internal DaggerfallTravelPolicy(DaggerfallSiteContext sites, DaggerfallWorldGridsSet grids,
        DaggerfallTransportTuning transport)
    {
        _sites = sites ?? throw new ArgumentNullException(nameof(sites));
        _grids = grids ?? throw new ArgumentNullException(nameof(grids));
        _transport = (transport ?? throw new ArgumentNullException(nameof(transport))).Validate();
    }

    /// <summary>Returns all discovered, exterior-backed destinations in stable site identity order.</summary>
    internal IReadOnlyList<DaggerfallTravelDestination> SupportedDestinations()
    {
        List<DaggerfallTravelDestination> destinations = [];
        foreach (DaggerfallSiteRecord site in _sites.Records)
        {
            if (!site.Discovered && !_sites.IsDiscovered(site.Id)) continue;
            if (TryDestination(site, out DaggerfallTravelDestination destination))
                destinations.Add(destination);
        }

        return destinations;
    }

    /// <summary>
    /// Every exterior-backed destination the admitted location set carries, discovered or not, in
    /// stable site identity order. Only a reader that never names a site to the player - such as where
    /// a region sits on the world map - may use the undiscovered ones.
    /// </summary>
    internal IReadOnlyList<DaggerfallTravelDestination> AllDestinations()
    {
        List<DaggerfallTravelDestination> destinations = [];
        foreach (DaggerfallSiteRecord site in _sites.Records)
            if (TryDestination(site, out DaggerfallTravelDestination destination)) destinations.Add(destination);
        return destinations;
    }

    /// <summary>
    /// Calculates the cautious one-way duration used by donor quest clocks.
    /// </summary>
    /// <remarks>
    /// DFU asks the calculator for cautious, camp-out travel with the cart modifier regardless of
    /// the player's current inventory.  A quest leg receives at least one day after the route is
    /// calculated.  A same-pixel leg keeps the donor's zero route result and therefore also receives
    /// that one-day minimum.
    /// </remarks>
    internal int CautiousQuestLegMinutes(DaggerfallTravelMapPixel origin, DaggerfallSiteId destination)
    {
        origin.RequireInBounds(nameof(origin));
        DaggerfallTravelDestination resolved = ResolveDestination(destination, requireDiscovered: false);
        // The donor's clock walks by cart and camps out: each pixel's minutes take the camp-out share.
        int routeMinutes = 0;
        foreach (DaggerfallTravelMapPixel pixel in Route(origin, resolved.MapPixel))
            routeMinutes = checked(routeMinutes + ((300 * PixelMinutes(pixel, DaggerfallTransportMode.Cart, aboard: false)) >> 8));
        return Math.Max(routeMinutes, DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour);
    }

    /// <summary>Applies DFU Clock's return-trip multiplier after summing seconds, preserving half minutes.</summary>
    internal static long ReturnTripSeconds(long oneWaySeconds)
    {
        if (oneWaySeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(oneWaySeconds), oneWaySeconds, "Travel duration cannot be negative.");
        return checked((oneWaySeconds * 5) / 2);
    }

    /// <summary>Converts a nonnegative quest-clock duration to the calendar's seconds.</summary>
    internal static long ToQuestSeconds(long minutes)
    {
        if (minutes < 0)
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Travel duration cannot be negative.");
        return checked(minutes * DaggerfallCalendar.SecondsPerMinute);
    }

    private DaggerfallTravelDestination ResolveDestination(DaggerfallSiteId id, bool requireDiscovered)
    {
        if (!_sites.TryFind(id, out DaggerfallSiteRecord? site))
            throw new InvalidOperationException($"Travel destination {id} is not present in the admitted location set.");
        if (requireDiscovered && !_sites.IsDiscovered(id))
            throw new InvalidOperationException($"Travel destination {id} has not been discovered.");
        if (!TryDestination(site, out DaggerfallTravelDestination destination))
            throw new InvalidOperationException($"Travel destination {id} has no valid exterior map position.");
        return destination;
    }

    private static bool TryDestination(DaggerfallSiteRecord site, out DaggerfallTravelDestination destination)
    {
        destination = default;
        // DFU's travel-map color lookup intentionally leaves the player's ship location unselectable.
        if (site.Kind == DaggerfallSiteKind.HomeYourShips)
            return false;
        if (site.Exterior is not { } exterior)
            return false;
        DaggerfallTravelMapPixel mapPixel = new(site.MapPixelX, site.MapPixelY);
        if (!mapPixel.IsInBounds || exterior.MapPixelX != mapPixel.X || exterior.MapPixelY != mapPixel.Y)
            return false;
        destination = new(site.Id, site.Name, site.Kind, mapPixel);
        return true;
    }

    /// <summary>
    /// The pixels the donor's route walk steps through from <paramref name="origin"/> to <paramref name="destination"/>,
    /// the origin excluded: its integer line, one pixel a move, diagonally where the shorter axis has gathered a whole step.
    /// </summary>
    internal static IReadOnlyList<DaggerfallTravelMapPixel> Route(DaggerfallTravelMapPixel origin, DaggerfallTravelMapPixel destination)
    {
        int distanceX = destination.X - origin.X;
        int distanceY = destination.Y - origin.Y;
        int distanceXAbs = Math.Abs(distanceX);
        int distanceYAbs = Math.Abs(distanceY);
        int furthest = Math.Max(distanceXAbs, distanceYAbs);
        int xDirection = distanceX >= 0 ? 1 : -1;
        int yDirection = distanceY >= 0 ? 1 : -1;
        int x = origin.X;
        int y = origin.Y;
        int shorterIncrementer = 0;
        DaggerfallTravelMapPixel[] route = new DaggerfallTravelMapPixel[furthest];
        for (int movement = 0; movement < furthest; movement++)
        {
            if (furthest == distanceXAbs)
            {
                x += xDirection;
                shorterIncrementer += distanceYAbs;
                // Keep DFU's strict comparison.  It is observable for shallow diagonal routes.
                if (shorterIncrementer > distanceXAbs)
                {
                    shorterIncrementer -= distanceXAbs;
                    y += yDirection;
                }
            }
            else
            {
                y += yDirection;
                shorterIncrementer += distanceXAbs;
                if (shorterIncrementer > distanceYAbs)
                {
                    shorterIncrementer -= distanceYAbs;
                    x += xDirection;
                }
            }
            route[movement] = new(x, y);
        }
        return route;
    }

    /// <summary>
    /// The pixels a party walks from <paramref name="origin"/> to <paramref name="destination"/>: the donor's route walk,
    /// whose strict comparison can leave its last pixel one short across the shorter axis, ending on the destination.
    /// </summary>
    internal static IReadOnlyList<DaggerfallTravelMapPixel> Path(DaggerfallTravelMapPixel origin, DaggerfallTravelMapPixel destination)
    {
        DaggerfallTravelMapPixel[] path = [.. Route(origin, destination)];
        if (path.Length > 0) path[^1] = destination;
        return path;
    }

    /// <summary>
    /// Computes the overland courier route distance in equivalent flat-road map pixels from
    /// <paramref name="origin"/> to <paramref name="destination"/> by walking <see cref="Path"/> over the
    /// admitted climate/terrain grid and weighting each step by <see cref="PixelMinutes"/> (normalized
    /// against fast flat land) plus a harbor/ferry boarding cost when entering open sea. Two sites equally
    /// far in straight-line map coordinates therefore differ whenever one crosses mountains, swamps, deserts,
    /// or open water.
    /// </summary>
    internal double CourierRouteDistancePixels(DaggerfallTravelMapPixel origin, DaggerfallTravelMapPixel destination)
    {
        origin.RequireInBounds(nameof(origin));
        destination.RequireInBounds(nameof(destination));
        if (origin == destination)
            return 0d;

        double baselineFlatLandMinutes = (((102 * _transport.HorseTravelModifier) >> 8) * (256 - TerrainMovementModifiers[5] + 256)) >> 8;
        if (baselineFlatLandMinutes <= 0d)
            baselineFlatLandMinutes = 1d;

        const double SeaBoardingPenaltyPixels = 2.0;
        DaggerfallTravelMapPixel previous = origin;
        bool previousOcean = IsOcean(origin);
        double totalEquivalentPixels = 0d;

        foreach (DaggerfallTravelMapPixel step in Path(origin, destination))
        {
            double dx = step.X - previous.X;
            double dy = step.Y - previous.Y;
            double stepLength = Math.Sqrt(dx * dx + dy * dy);
            if (stepLength <= 0d)
                continue;

            bool ocean = IsOcean(step);
            int pixelMinutes = PixelMinutes(step, DaggerfallTransportMode.Horse, aboard: true);
            double terrainFactor = Math.Max(1.0, pixelMinutes / baselineFlatLandMinutes);
            totalEquivalentPixels += stepLength * terrainFactor;
            if (ocean && !previousOcean)
                totalEquivalentPixels += SeaBoardingPenaltyPixels;

            previous = step;
            previousOcean = ocean;
        }

        double directDistance = Math.Sqrt(
            (destination.X - origin.X) * (double)(destination.X - origin.X)
            + (destination.Y - origin.Y) * (double)(destination.Y - origin.Y));
        return Math.Max(totalEquivalentPixels, directDistance);
    }

    /// <summary>Whether a map pixel is open sea, which only a ship crosses at speed.</summary>
    internal bool IsOcean(DaggerfallTravelMapPixel pixel) => ClimateAt(pixel.X, pixel.Y).Value == OceanClimate;

    /// <summary>The source climate of a map pixel, which its weather and encounters read.</summary>
    internal int Climate(DaggerfallTravelMapPixel pixel) => ClimateAt(pixel.X, pixel.Y).Value;

    /// <summary>The name of a map pixel's climate, as the land is described.</summary>
    internal string ClimateName(DaggerfallTravelMapPixel pixel) => ClimateAt(pixel.X, pixel.Y).Name;

    /// <summary>
    /// The donor's minutes to move into one map pixel, lodging at inns rather than camping out: the sea at the ship's or
    /// the swimmer's pace, the land at the transport's pace over the pixel's terrain.
    /// </summary>
    /// <param name="aboard">Whether a ship carries the traveller over the sea.</param>
    internal int PixelMinutes(DaggerfallTravelMapPixel pixel, DaggerfallTransportMode transport, bool aboard)
    {
        DaggerfallClimateCell climate = ClimateAt(pixel.X, pixel.Y);
        if (climate.Value == OceanClimate) return aboard ? _transport.ShipOceanMinutes : _transport.FootOceanMinutes;
        int climateOffset = climate.Value - OceanClimate;
        if ((uint)climateOffset >= (uint)ClimateIndices.Length)
            throw new InvalidOperationException($"Travel route climate value {climate.Value} at {pixel.X}/{pixel.Y} is not a Daggerfall climate.");
        int terrainIndex = ClimateIndices[climateOffset];
        int transportModifier = transport switch
        {
            DaggerfallTransportMode.Horse => _transport.HorseTravelModifier,
            DaggerfallTransportMode.Cart => _transport.CartTravelModifier,
            _ => _transport.FootTravelModifier,
        };
        return (((102 * transportModifier) >> 8) * (256 - TerrainMovementModifiers[terrainIndex] + 256)) >> 8;
    }

    private DaggerfallClimateCell ClimateAt(int mapPixelX, int mapPixelY)
    {
        // MapsFile.GetClimateIndex adds one to world-pixel X to align CLIMATE.PAK with the height
        // map.  The normalized grid retains that sentinel column, so the +1 is part of this route
        // policy rather than a general property of DaggerfallClimateGridDefinition.GetCell.
        DaggerfallClimateCell climate = _grids.ClimateAtWorldPixel(mapPixelX, mapPixelY);
        if (climate.Disposition != DaggerfallClimateCoordinateDisposition.Found)
            throw new InvalidOperationException($"Travel route climate at map pixel {mapPixelX}/{mapPixelY} is outside the admitted climate grid.");
        if (climate.Value < 223 || climate.Value > 232)
            throw new InvalidOperationException($"Travel route climate at map pixel {mapPixelX}/{mapPixelY} has unresolved value {climate.Value}.");
        if (climate.Name.Length == 0)
            throw new InvalidOperationException($"Travel route climate at map pixel {mapPixelX}/{mapPixelY} has no admitted name for value {climate.Value}.");
        return climate;
    }
}
