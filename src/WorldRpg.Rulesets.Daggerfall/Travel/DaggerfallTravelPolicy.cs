using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>A Daggerfall world-map pixel used as a travel route endpoint.</summary>
internal readonly record struct DaggerfallTravelMapPixel(int X, int Y)
{
    internal const int Width = 1000;
    internal const int Height = 500;

    internal bool IsInBounds => X >= 0 && X < Width && Y >= 0 && Y < Height;

    internal void RequireInBounds(string parameterName)
    {
        if (!IsInBounds)
            throw new ArgumentOutOfRangeException(parameterName, this,
                $"Travel map pixels must be inside the {Width} by {Height} world map.");
    }
}

/// <summary>The options selected by the travel window and the transport facts it observed.</summary>
/// <remarks>
/// The two possession fields are facts used by the donor formula.  They are deliberately separate
/// from <see cref="TravelShip"/>: the donor permits a player without an owned ship to charter one,
/// charging an ocean fare for that trip.  Horse takes precedence over cart when both are present,
/// exactly as <c>TravelTimeCalculator.CalculateTravelTime</c> does.
/// </remarks>
internal readonly record struct DaggerfallTravelOptions(
    bool SpeedCautious,
    bool SleepModeInn,
    bool TravelShip,
    bool HasHorse,
    bool HasCart,
    bool HasShip,
    ulong AvailableGold = ulong.MaxValue,
    ulong AvailableGoldPieces = ulong.MaxValue)
{
    /// <summary>The cautious camp-out/cart facts used by quest travel clocks.</summary>
    internal static DaggerfallTravelOptions QuestClock { get; } =
        new(SpeedCautious: true, SleepModeInn: false, TravelShip: false,
            HasHorse: false, HasCart: true, HasShip: false);
}

/// <summary>A site that the travel map can expose as a destination.</summary>
internal readonly record struct DaggerfallTravelDestination(
    DaggerfallSiteId Id,
    string Name,
    DaggerfallSiteKind Kind,
    DaggerfallTravelMapPixel MapPixel);

/// <summary>The deterministic output of one admitted travel route calculation.</summary>
internal sealed record DaggerfallTravelQuote(
    DaggerfallTravelMapPixel Origin,
    DaggerfallTravelDestination Destination,
    DaggerfallTravelOptions Options,
    int DistanceMapPixels,
    int OceanPixels,
    int TravelMinutes,
    int InnCost,
    int ShipCost,
    int TotalCost,
    string Identity)
{
    internal long TravelSeconds => checked((long)TravelMinutes * DaggerfallCalendar.SecondsPerMinute);

    /// <summary>Whether the currency facts captured in the quote can pay both fare requirements.</summary>
    internal bool CanAfford => (ulong)TotalCost <= Options.AvailableGold
        && (ulong)InnCost <= Options.AvailableGoldPieces;
}

/// <summary>
/// Daggerfall's overland route, duration and fare policy over the normalized site and climate grids.
/// </summary>
/// <remarks>
/// This is the product-owned equivalent of DFU's <c>TravelTimeCalculator</c>.  It retains the
/// donor's integer path walk and fixed-point shifts, while reading the admitted Rusty grids and site
/// identities instead of a Unity content singleton.  The policy does not advance the calendar or
/// mutate transport state; the session owns those admitted operations.
/// </remarks>
internal sealed class DaggerfallTravelPolicy
{
    // DFU's climateIndices starts at CLIMATE.PAK value 223 (Ocean).
    private static readonly int[] ClimateIndices = [0, 0, 0, 1, 2, 3, 4, 5, 5, 5];

    // Indexed by the climate index above. Values are the donor's fixed-point movement modifiers.
    private static readonly int[] TerrainMovementModifiers = [240, 220, 200, 200, 230, 250];

    private readonly DaggerfallSiteContext _sites;
    private readonly DaggerfallWorldGridsSet _grids;
    private readonly DaggerfallTransportTuning _transport;

    internal DaggerfallTravelPolicy(DaggerfallSiteContext sites, DaggerfallWorldGridsSet grids,
        DaggerfallTransportTuning? transport = null)
    {
        _sites = sites ?? throw new ArgumentNullException(nameof(sites));
        _grids = grids ?? throw new ArgumentNullException(nameof(grids));
        _transport = (transport ?? DaggerfallTransportTuning.Donor).Validate();
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
    /// Calculates a route from an explicit current map pixel to a site destination.
    /// </summary>
    /// <param name="origin">The current map pixel, including an ocean pixel while on a ship.</param>
    /// <param name="destination">The durable region/index identity selected by the travel map.</param>
    /// <param name="options">The speed, lodging, transport, and ownership facts for this quote.</param>
    /// <param name="requireDiscovered">Whether the destination must be revealed to ordinary travel-map play.</param>
    internal DaggerfallTravelQuote Quote(DaggerfallTravelMapPixel origin, DaggerfallSiteId destination,
        DaggerfallTravelOptions options, bool requireDiscovered = true)
    {
        origin.RequireInBounds(nameof(origin));
        DaggerfallTravelDestination resolved = ResolveDestination(destination, requireDiscovered);
        if (origin == resolved.MapPixel)
            throw new InvalidOperationException($"Travel route to {destination} has no movement and is not a valid route.");

        RouteResult route = CalculateRoute(origin, resolved.MapPixel, options);
        (int innCost, int shipCost, int totalCost) = CalculateCosts(route.TravelMinutes, route.OceanPixels, options);
        return new DaggerfallTravelQuote(origin, resolved, options, route.DistanceMapPixels,
            route.OceanPixels, route.TravelMinutes, innCost, shipCost, totalCost,
            Identity(origin, destination, options));
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
        int routeMinutes = origin == resolved.MapPixel
            ? 0
            : CalculateRoute(origin, resolved.MapPixel, DaggerfallTravelOptions.QuestClock).TravelMinutes;
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

    private RouteResult CalculateRoute(DaggerfallTravelMapPixel origin, DaggerfallTravelMapPixel destination,
        DaggerfallTravelOptions options)
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
        int oceanPixels = 0;
        int minutesTotal = 0;

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

            DaggerfallClimateCell climate = ClimateAt(x, y);
            int minutesThisMove;
            if (climate.Value == 223)
            {
                oceanPixels = checked(oceanPixels + 1);
                minutesThisMove = options.TravelShip ? _transport.ShipOceanMinutes : _transport.FootOceanMinutes;
            }
            else
            {
                int climateOffset = climate.Value - 223;
                if ((uint)climateOffset >= (uint)ClimateIndices.Length)
                    throw new InvalidOperationException($"Travel route climate value {climate.Value} at {x}/{y} is not a Daggerfall climate.");
                int terrainIndex = ClimateIndices[climateOffset];
                int transportModifier = options.HasHorse
                    ? _transport.HorseTravelModifier
                    : options.HasCart ? _transport.CartTravelModifier : _transport.FootTravelModifier;
                minutesThisMove = (((102 * transportModifier) >> 8)
                    * (256 - TerrainMovementModifiers[terrainIndex] + 256)) >> 8;
            }

            if (!options.SleepModeInn)
                minutesThisMove = (300 * minutesThisMove) >> 8;
            minutesTotal = checked(minutesTotal + minutesThisMove);
        }

        if (!options.SpeedCautious)
            minutesTotal >>= 1;
        return new RouteResult(furthest, oceanPixels, minutesTotal);
    }

    private DaggerfallClimateCell ClimateAt(int mapPixelX, int mapPixelY)
    {
        // MapsFile.GetClimateIndex adds one to world-pixel X to align CLIMATE.PAK with the height
        // map.  The normalized grid retains that sentinel column, so the +1 is part of this route
        // policy rather than a general property of DaggerfallClimateGridDefinition.GetCell.
        DaggerfallClimateCell climate = _grids.Climate.GetCell(mapPixelX + 1, mapPixelY);
        if (climate.Disposition != DaggerfallClimateCoordinateDisposition.Found)
            throw new InvalidOperationException($"Travel route climate at map pixel {mapPixelX}/{mapPixelY} is outside the admitted climate grid.");
        if (climate.Value < 223 || climate.Value > 232)
            throw new InvalidOperationException($"Travel route climate at map pixel {mapPixelX}/{mapPixelY} has unresolved value {climate.Value}.");
        if (climate.Name.Length == 0)
            throw new InvalidOperationException($"Travel route climate at map pixel {mapPixelX}/{mapPixelY} has no admitted name for value {climate.Value}.");
        return climate;
    }

    private static (int InnCost, int ShipCost, int TotalCost) CalculateCosts(int travelMinutes, int oceanPixels,
        DaggerfallTravelOptions options)
    {
        int travelHours = checked((travelMinutes + (DaggerfallCalendar.MinutesPerHour - 1))
            / DaggerfallCalendar.MinutesPerHour);
        long innCost = 0;
        if (options.SleepModeInn)
        {
            long chargeableHours = (long)travelHours - oceanPixels;
            innCost = 5L * (chargeableHours / DaggerfallCalendar.HoursPerDay);
            if (innCost < 0) innCost = 0;
            innCost += 5;
        }

        long shipCost = oceanPixels > 0 && !options.HasShip && options.TravelShip
            ? 25L * ((oceanPixels / DaggerfallCalendar.HoursPerDay) + 1)
            : 0;
        long totalCost = checked(innCost + shipCost);
        if (innCost < 0 || shipCost < 0 || totalCost < 0)
            throw new InvalidOperationException("Travel cost calculation produced a negative fare.");
        return (checked((int)innCost), checked((int)shipCost), checked((int)totalCost));
    }

    private static string Identity(DaggerfallTravelMapPixel origin, DaggerfallSiteId destination,
        DaggerfallTravelOptions options) => FormattableString.Invariant(
            $"daggerfall.travel.v1/{origin.X},{origin.Y}/{destination.Region},{destination.Index}/cautious={(options.SpeedCautious ? 1 : 0)};inn={(options.SleepModeInn ? 1 : 0)};ship={(options.TravelShip ? 1 : 0)};horse={(options.HasHorse ? 1 : 0)};cart={(options.HasCart ? 1 : 0)};owned-ship={(options.HasShip ? 1 : 0)};gold={options.AvailableGold};pieces={options.AvailableGoldPieces}");

    private readonly record struct RouteResult(int DistanceMapPixels, int OceanPixels, int TravelMinutes);
}
