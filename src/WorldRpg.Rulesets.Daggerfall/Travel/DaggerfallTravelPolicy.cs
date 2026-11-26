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
/// A terrain-aware overland route between two world-map pixels: the ordered pixel waypoints from
/// <paramref name="Origin"/> (excluded) to <paramref name="Destination"/> (included), the equivalent
/// flat-road cost of each step, total equivalent distance, and whether the route crosses open sea.
/// </summary>
internal sealed record DaggerfallOverlandRoute(
    DaggerfallTravelMapPixel Origin,
    DaggerfallTravelMapPixel Destination,
    IReadOnlyList<DaggerfallTravelMapPixel> Waypoints,
    double[] StepCosts,
    double TotalEquivalentPixels,
    bool UsesOcean);

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
    private const double DiagonalStepLength = 1.4142135623730951;
    private const double SeaBoardingTravelPenaltyPixels = 2.0;
    private const double SeaBoardingRoutingPenalty = 12.0;
    private const double OceanRoutingMultiplier = 2.6;
    private const int LocalAStarMaxSpan = 64;
    private const int LocalAStarMaxExpanded = 32_000;
    private const int CoarseScale = 4;
    private const int MaxRouteCacheEntries = 2048;
    private const int MaxCoarseFieldCacheEntries = 4;

    private static readonly (int Dx, int Dy, double Length)[] Neighbours8 =
    [
        (1, 0, 1.0), (-1, 0, 1.0), (0, 1, 1.0), (0, -1, 1.0),
        (1, 1, DiagonalStepLength), (1, -1, DiagonalStepLength), (-1, 1, DiagonalStepLength), (-1, -1, DiagonalStepLength),
    ];

    // DFU's climateIndices starts at CLIMATE.PAK value 223 (Ocean).
    private static readonly int[] ClimateIndices = [0, 0, 0, 1, 2, 3, 4, 5, 5, 5];

    // Indexed by the climate index above. Values are the donor's fixed-point movement modifiers.
    private static readonly int[] TerrainMovementModifiers = [240, 220, 200, 200, 230, 250];

    private readonly DaggerfallSiteContext _sites;
    private readonly DaggerfallWorldGridsSet _grids;
    private readonly DaggerfallTransportTuning _transport;
    private readonly double _baselineFlatLandMinutes;
    private readonly Dictionary<(DaggerfallTravelMapPixel Origin, DaggerfallTravelMapPixel Destination), DaggerfallOverlandRoute> _routeCache = [];
    private CoarseRoutingGrid? _coarseGrid;
    private readonly List<CoarseDistanceField> _coarseFields = [];

    internal DaggerfallTravelPolicy(DaggerfallSiteContext sites, DaggerfallWorldGridsSet grids,
        DaggerfallTransportTuning transport)
    {
        _sites = sites ?? throw new ArgumentNullException(nameof(sites));
        _grids = grids ?? throw new ArgumentNullException(nameof(grids));
        _transport = (transport ?? throw new ArgumentNullException(nameof(transport))).Validate();
        double baseline = (((102 * _transport.HorseTravelModifier) >> 8) * (256 - TerrainMovementModifiers[5] + 256)) >> 8;
        _baselineFlatLandMinutes = baseline > 0d ? baseline : 1d;
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
    /// Computes the terrain-aware overland route from <paramref name="origin"/> to <paramref name="destination"/>.
    /// Prefers coastal and valley land paths around bays, inlets, and rough mountains, crossing open sea only when
    /// no reasonable land route exists (such as islands or wide cross-bay passages).
    /// </summary>
    internal DaggerfallOverlandRoute OverlandRoute(DaggerfallTravelMapPixel origin, DaggerfallTravelMapPixel destination)
    {
        origin.RequireInBounds(nameof(origin));
        destination.RequireInBounds(nameof(destination));
        if (origin == destination)
            return new(origin, destination, [], [], 0d, false);

        (DaggerfallTravelMapPixel, DaggerfallTravelMapPixel) key = (origin, destination);
        if (_routeCache.TryGetValue(key, out DaggerfallOverlandRoute? cached))
            return cached;

        IReadOnlyList<DaggerfallTravelMapPixel> waypoints = ComputeOverlandWaypoints(origin, destination);
        DaggerfallOverlandRoute route = BuildOverlandRoute(origin, destination, waypoints);
        if (_routeCache.Count >= MaxRouteCacheEntries)
            _routeCache.Clear();
        _routeCache[key] = route;
        return route;
    }

    /// <summary>
    /// Computes the overland courier route distance in equivalent flat-road map pixels from
    /// <paramref name="origin"/> to <paramref name="destination"/> using <see cref="OverlandRoute"/>.
    /// </summary>
    internal double CourierRouteDistancePixels(DaggerfallTravelMapPixel origin, DaggerfallTravelMapPixel destination) =>
        OverlandRoute(origin, destination).TotalEquivalentPixels;

    /// <summary>
    /// Equivalent flat-road travel cost multiplier (&gt;= 1.0) for moving across <paramref name="pixel"/> on land
    /// (or aboard ship on sea). Flat plains/woodlands return 1.0; mountains, swamps, and rough terrain return &gt; 1.0.
    /// </summary>
    internal double TerrainFactor(DaggerfallTravelMapPixel pixel)
    {
        int pixelMinutes = PixelMinutes(pixel, DaggerfallTransportMode.Horse, aboard: true);
        return Math.Max(1.0, pixelMinutes / _baselineFlatLandMinutes);
    }

    private DaggerfallOverlandRoute BuildOverlandRoute(
        DaggerfallTravelMapPixel origin,
        DaggerfallTravelMapPixel destination,
        IReadOnlyList<DaggerfallTravelMapPixel> waypoints)
    {
        if (waypoints.Count == 0)
            return new(origin, destination, [], [], 0d, false);

        double[] stepCosts = new double[waypoints.Count];
        DaggerfallTravelMapPixel previous = origin;
        bool previousOcean = IsOcean(origin);
        bool usesOcean = false;
        double totalEquivalentPixels = 0d;

        for (int i = 0; i < waypoints.Count; i++)
        {
            DaggerfallTravelMapPixel step = waypoints[i];
            double dx = step.X - previous.X;
            double dy = step.Y - previous.Y;
            double stepLength = Math.Sqrt(dx * dx + dy * dy);
            bool ocean = IsOcean(step);
            if (ocean) usesOcean = true;

            double cost = stepLength * TerrainFactor(step);
            if (ocean && !previousOcean)
                cost += SeaBoardingTravelPenaltyPixels;
            stepCosts[i] = cost;
            totalEquivalentPixels += cost;

            previous = step;
            previousOcean = ocean;
        }

        double directDistance = Math.Sqrt(
            (destination.X - origin.X) * (double)(destination.X - origin.X)
            + (destination.Y - origin.Y) * (double)(destination.Y - origin.Y));
        return new(origin, destination, waypoints, stepCosts, Math.Max(totalEquivalentPixels, directDistance), usesOcean);
    }

    private IReadOnlyList<DaggerfallTravelMapPixel> ComputeOverlandWaypoints(
        DaggerfallTravelMapPixel origin,
        DaggerfallTravelMapPixel destination)
    {
        IReadOnlyList<DaggerfallTravelMapPixel> straight = ContiguousLine(origin, destination);
        if (straight.Count <= 1)
            return straight;

        // Fast path: if the 8-adjacent straight walk crosses zero ocean (including diagonal ocean corners)
        // and stays entirely on fast flat land, it is already optimal.
        bool straightHasObstacle = false;
        DaggerfallTravelMapPixel prev = origin;
        for (int i = 0; i < straight.Count; i++)
        {
            DaggerfallTravelMapPixel px = straight[i];
            if (IsOcean(px) || TerrainFactor(px) > 1.05 || TouchesOceanCorner(prev, px))
            {
                straightHasObstacle = true;
                break;
            }
            prev = px;
        }
        if (!straightHasObstacle)
            return straight;

        int span = Math.Max(Math.Abs(destination.X - origin.X), Math.Abs(destination.Y - origin.Y));
        if (span <= LocalAStarMaxSpan)
        {
            return FindLocalAStarWaypoints(origin, destination, margin: Math.Max(16, span), maxExpanded: LocalAStarMaxExpanded)
                ?? straight;
        }

        return FindCoarseBackedWaypoints(origin, destination) ?? straight;
    }

    /// <summary>
    /// Standard 8-connected Bresenham walk from <paramref name="origin"/> (excluded) to <paramref name="destination"/>
    /// (included) where every consecutive step satisfies <c>|dx| &lt;= 1 &amp;&amp; |dy| &lt;= 1</c> (unlike DFU's
    /// <see cref="Path"/>, which retains the donor's strict comparison and can emit a non-adjacent final jump).
    /// </summary>
    internal static IReadOnlyList<DaggerfallTravelMapPixel> ContiguousLine(
        DaggerfallTravelMapPixel origin,
        DaggerfallTravelMapPixel destination)
    {
        if (origin == destination)
            return [];

        int x = origin.X;
        int y = origin.Y;
        int dx = Math.Abs(destination.X - origin.X);
        int dy = Math.Abs(destination.Y - origin.Y);
        int sx = destination.X >= origin.X ? 1 : -1;
        int sy = destination.Y >= origin.Y ? 1 : -1;
        int steps = Math.Max(dx, dy);
        DaggerfallTravelMapPixel[] line = new DaggerfallTravelMapPixel[steps];

        if (dx >= dy)
        {
            int err = dx / 2;
            for (int i = 0; i < steps; i++)
            {
                x += sx;
                err -= dy;
                if (err < 0)
                {
                    y += sy;
                    err += dx;
                }
                line[i] = new(x, y);
            }
        }
        else
        {
            int err = dy / 2;
            for (int i = 0; i < steps; i++)
            {
                y += sy;
                err -= dx;
                if (err < 0)
                {
                    x += sx;
                    err += dy;
                }
                line[i] = new(x, y);
            }
        }
        return line;
    }

    private IReadOnlyList<DaggerfallTravelMapPixel>? FindLocalAStarWaypoints(
        DaggerfallTravelMapPixel origin,
        DaggerfallTravelMapPixel destination,
        int margin,
        int maxExpanded)
    {
        int minX = Math.Max(0, Math.Min(origin.X, destination.X) - margin);
        int maxX = Math.Min(DaggerfallTravelMapPixel.Width - 1, Math.Max(origin.X, destination.X) + margin);
        int minY = Math.Max(0, Math.Min(origin.Y, destination.Y) - margin);
        int maxY = Math.Min(DaggerfallTravelMapPixel.Height - 1, Math.Max(origin.Y, destination.Y) + margin);
        int boxW = maxX - minX + 1;
        int boxH = maxY - minY + 1;
        int cellCount = boxW * boxH;

        double[] gScore = new double[cellCount];
        Array.Fill(gScore, double.PositiveInfinity);
        int[] cameFrom = new int[cellCount];
        Array.Fill(cameFrom, -1);
        bool[] closed = new bool[cellCount];

        int startIdx = (origin.Y - minY) * boxW + (origin.X - minX);
        int goalIdx = (destination.Y - minY) * boxW + (destination.X - minX);
        gScore[startIdx] = 0d;

        PriorityQueue<int, double> open = new();
        open.Enqueue(startIdx, OctileDistance(origin.X, origin.Y, destination.X, destination.Y));

        int expanded = 0;
        while (open.Count > 0)
        {
            int current = open.Dequeue();
            if (closed[current]) continue;
            closed[current] = true;

            if (current == goalIdx)
                return ReconstructLocalPath(cameFrom, startIdx, goalIdx, minX, minY, boxW);

            if (++expanded > maxExpanded)
                return null;

            int cx = minX + (current % boxW);
            int cy = minY + (current / boxW);
            bool currentOcean = IsOcean(new(cx, cy));
            double currentG = gScore[current];

            for (int n = 0; n < Neighbours8.Length; n++)
            {
                (int dx, int dy, double stepLen) = Neighbours8[n];
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < minX || nx > maxX || ny < minY || ny > maxY) continue;

                int nextIdx = (ny - minY) * boxW + (nx - minX);
                if (closed[nextIdx]) continue;

                DaggerfallTravelMapPixel nextPixel = new(nx, ny);
                double stepCost = RoutingStepCost(new(cx, cy), currentOcean, nextPixel, stepLen);
                double tentativeG = currentG + stepCost;
                if (tentativeG < gScore[nextIdx])
                {
                    cameFrom[nextIdx] = current;
                    gScore[nextIdx] = tentativeG;
                    open.Enqueue(nextIdx, tentativeG + OctileDistance(nx, ny, destination.X, destination.Y));
                }
            }
        }
        return null;
    }

    private IReadOnlyList<DaggerfallTravelMapPixel>? FindCoarseBackedWaypoints(
        DaggerfallTravelMapPixel origin,
        DaggerfallTravelMapPixel destination)
    {
        CoarseRoutingGrid grid = _coarseGrid ??= BuildCoarseRoutingGrid();
        int startCx = origin.X / CoarseScale;
        int startCy = origin.Y / CoarseScale;
        int goalCx = destination.X / CoarseScale;
        int goalCy = destination.Y / CoarseScale;
        int startCell = startCy * grid.Width + startCx;
        int goalCell = goalCy * grid.Width + goalCx;

        CoarseDistanceField field = GetOrBuildCoarseField(grid, startCell);
        if (field.CameFrom[goalCell] < 0 && goalCell != startCell)
            return null;

        List<int> coarseCells = [];
        int cursor = goalCell;
        while (cursor >= 0 && cursor != startCell)
        {
            coarseCells.Add(cursor);
            cursor = field.CameFrom[cursor];
        }
        if (cursor != startCell)
            return null;
        coarseCells.Reverse();

        // Stitch exact pixel anchors along the coarse corridor so land segments never clip corner ocean pixels.
        List<DaggerfallTravelMapPixel> waypoints = [];
        DaggerfallTravelMapPixel current = origin;
        for (int i = 0; i < coarseCells.Count; i++)
        {
            int cell = coarseCells[i];
            DaggerfallTravelMapPixel target = i == coarseCells.Count - 1
                ? destination
                : grid.AnchorPixel[cell];
            if (target == current) continue;

            IReadOnlyList<DaggerfallTravelMapPixel> segment = ContiguousLine(current, target);
            bool expectsLand = !IsOcean(current) && !IsOcean(target);
            if (expectsLand && SegmentTouchesOcean(current, segment))
            {
                segment = FindLocalAStarWaypoints(current, target, margin: CoarseScale * 2, maxExpanded: 512) ?? segment;
            }
            for (int s = 0; s < segment.Count; s++)
                waypoints.Add(segment[s]);
            current = target;
        }
        return waypoints;
    }

    private CoarseRoutingGrid BuildCoarseRoutingGrid()
    {
        int w = (DaggerfallTravelMapPixel.Width + CoarseScale - 1) / CoarseScale;
        int h = (DaggerfallTravelMapPixel.Height + CoarseScale - 1) / CoarseScale;
        int count = w * h;
        bool[] isOcean = new bool[count];
        double[] factor = new double[count];
        DaggerfallTravelMapPixel[] anchor = new DaggerfallTravelMapPixel[count];

        for (int cy = 0; cy < h; cy++)
        {
            int py0 = cy * CoarseScale;
            int py1 = Math.Min(DaggerfallTravelMapPixel.Height - 1, py0 + CoarseScale - 1);
            for (int cx = 0; cx < w; cx++)
            {
                int px0 = cx * CoarseScale;
                int px1 = Math.Min(DaggerfallTravelMapPixel.Width - 1, px0 + CoarseScale - 1);
                int idx = cy * w + cx;

                double bestLandFactor = double.PositiveInfinity;
                DaggerfallTravelMapPixel bestLand = new(px0, py0);
                bool anyLand = false;

                for (int py = py0; py <= py1; py++)
                {
                    for (int px = px0; px <= px1; px++)
                    {
                        DaggerfallTravelMapPixel p = new(px, py);
                        if (IsOcean(p)) continue;
                        double tf = TerrainFactor(p);
                        if (!anyLand || tf < bestLandFactor)
                        {
                            anyLand = true;
                            bestLandFactor = tf;
                            bestLand = p;
                        }
                    }
                }

                if (anyLand)
                {
                    isOcean[idx] = false;
                    factor[idx] = bestLandFactor;
                    anchor[idx] = bestLand;
                }
                else
                {
                    isOcean[idx] = true;
                    factor[idx] = OceanRoutingMultiplier;
                    anchor[idx] = new((px0 + px1) / 2, (py0 + py1) / 2);
                }
            }
        }
        return new(w, h, isOcean, factor, anchor);
    }

    private CoarseDistanceField GetOrBuildCoarseField(CoarseRoutingGrid grid, int startCell)
    {
        for (int i = 0; i < _coarseFields.Count; i++)
        {
            if (_coarseFields[i].StartCell == startCell)
                return _coarseFields[i];
        }

        int cellCount = grid.Width * grid.Height;
        double[] dist = new double[cellCount];
        Array.Fill(dist, double.PositiveInfinity);
        int[] cameFrom = new int[cellCount];
        Array.Fill(cameFrom, -1);
        bool[] settled = new bool[cellCount];

        dist[startCell] = 0d;
        PriorityQueue<int, double> open = new();
        open.Enqueue(startCell, 0d);

        while (open.Count > 0)
        {
            int current = open.Dequeue();
            if (settled[current]) continue;
            settled[current] = true;

            int cx = current % grid.Width;
            int cy = current / grid.Width;
            bool curOcean = grid.IsOcean[current];
            double curDist = dist[current];

            for (int n = 0; n < Neighbours8.Length; n++)
            {
                (int dx, int dy, double stepLen) = Neighbours8[n];
                int nx = cx + dx;
                int ny = cy + dy;
                if ((uint)nx >= (uint)grid.Width || (uint)ny >= (uint)grid.Height) continue;

                int next = ny * grid.Width + nx;
                if (settled[next]) continue;

                bool nextOcean = grid.IsOcean[next];
                double edge = stepLen * CoarseScale * grid.Factor[next];
                if (nextOcean && !curOcean)
                    edge += SeaBoardingRoutingPenalty;
                double tentative = curDist + edge;
                if (tentative < dist[next])
                {
                    dist[next] = tentative;
                    cameFrom[next] = current;
                    open.Enqueue(next, tentative);
                }
            }
        }

        CoarseDistanceField created = new(startCell, cameFrom);
        if (_coarseFields.Count >= MaxCoarseFieldCacheEntries)
            _coarseFields.RemoveAt(0);
        _coarseFields.Add(created);
        return created;
    }

    private bool TouchesOceanCorner(DaggerfallTravelMapPixel from, DaggerfallTravelMapPixel to)
    {
        if (from.X == to.X || from.Y == to.Y)
            return false;
        return IsOcean(new(to.X, from.Y)) || IsOcean(new(from.X, to.Y));
    }

    private bool SegmentTouchesOcean(DaggerfallTravelMapPixel start, IReadOnlyList<DaggerfallTravelMapPixel> segment)
    {
        DaggerfallTravelMapPixel prev = start;
        for (int i = 0; i < segment.Count; i++)
        {
            DaggerfallTravelMapPixel next = segment[i];
            if (IsOcean(next) || TouchesOceanCorner(prev, next))
                return true;
            prev = next;
        }
        return false;
    }

    private double RoutingStepCost(DaggerfallTravelMapPixel current, bool currentOcean, DaggerfallTravelMapPixel next, double stepLen)
    {
        if (IsOcean(next))
            return stepLen * OceanRoutingMultiplier + (currentOcean ? 0d : SeaBoardingRoutingPenalty);
        double cost = stepLen * TerrainFactor(next);
        if (!currentOcean && TouchesOceanCorner(current, next))
            cost += SeaBoardingRoutingPenalty;
        return cost;
    }

    private static double OctileDistance(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int min = Math.Min(dx, dy);
        int max = Math.Max(dx, dy);
        return min * (DiagonalStepLength - 1.0) + max;
    }

    private static DaggerfallTravelMapPixel[] ReconstructLocalPath(
        int[] cameFrom,
        int startIdx,
        int goalIdx,
        int minX,
        int minY,
        int boxW)
    {
        List<DaggerfallTravelMapPixel> reversed = [];
        int cursor = goalIdx;
        while (cursor >= 0 && cursor != startIdx)
        {
            reversed.Add(new(minX + (cursor % boxW), minY + (cursor / boxW)));
            cursor = cameFrom[cursor];
        }
        reversed.Reverse();
        return [.. reversed];
    }

    private sealed record CoarseRoutingGrid(
        int Width,
        int Height,
        bool[] IsOcean,
        double[] Factor,
        DaggerfallTravelMapPixel[] AnchorPixel);

    private sealed record CoarseDistanceField(int StartCell, int[] CameFrom);

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
