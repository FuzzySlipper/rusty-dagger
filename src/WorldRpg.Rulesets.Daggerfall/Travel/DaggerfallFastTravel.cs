using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>A journey the live quote admits: the quote and the destination's exterior it was resolved against.</summary>
internal sealed record DaggerfallTravelAcceptance(DaggerfallTravelQuote Quote, DaggerfallSiteProfile Destination);

/// <summary>A paid journey on the road: its days have passed (or been cut short) and its arrival is still to come.</summary>
internal sealed record DaggerfallJourney(DaggerfallTravelQuote Quote, DaggerfallSiteProfile Destination, long Started,
    DaggerfallTravelMapPixel Departure, DaggerfallTravelOutcome Outcome);

/// <summary>
/// Fast travel: the travel panel's search, its selected destination and options, its message, the travel map and the
/// guild teleport offer, and the rules a journey follows from its quote to its result. What a journey does in other
/// domains (payment, relocation, controls, skills, the end of the time increase) is the session's Depart and Arrive lists.
/// </summary>
/// <param name="passTime">Passes one slice of the road through the session's one calendar, without completing the time increase.</param>
/// <param name="options">The travel options for the selected flags (cautious, inn, ship), with the funds and transport they observe.</param>
/// <param name="encounterPending">Whether an encounter is on its way to the profile with this logical id.</param>
internal sealed class DaggerfallFastTravel(
    DaggerfallTravelPolicy policy,
    DaggerfallTravelState journeys,
    DaggerfallTransportPolicy transport,
    DaggerfallSiteContext sites,
    IRandomService random,
    IReadOnlyList<string> regionNames,
    DaggerfallWorldNames worldNames,
    DaggerfallExteriorWorldBounds worldBounds,
    DaggerfallAtlasTiles? atlasTiles,
    Func<DaggerfallCalendar> calendar,
    Func<long, DaggerfallCalendarAdvance> passTime,
    Func<DaggerfallWorldProfileKey> activeProfile,
    Func<DaggerfallExteriorCellId> exteriorCell,
    Func<bool> playerDefeated,
    Func<bool> isVampire,
    Func<string, bool> encounterPending,
    Func<bool> enemyNearby,
    Func<DaggerfallSiteProfiles?> profiles,
    Func<(bool Cautious, bool Inn, bool Ship), DaggerfallTravelOptions> options,
    Func<bool> classicArt,
    Func<float> yaw)
{
    private DaggerfallTravelDestination[] _searchResults = [];
    private bool _searchInitialized;
    private DaggerfallSiteId? _selectedDestination;
    private (bool Cautious, bool Inn, bool Ship) _selectedOptions = (true, false, false);
    private string? _message;
    private DaggerfallTravelMap? _map;
    private bool _mapOpen;
    private (long NpcId, Func<bool> Available)? _guildTeleport;

    /// <summary>Whether the panel is offering a guild's free teleport rather than a journey.</summary>
    internal bool GuildTeleport => _guildTeleport is not null;

    /// <summary>The panel's line: a refusal, a search miss or a journey's outcome, until the next preview.</summary>
    internal string? Message => _message;

    /// <summary>The policy that routes and prices journeys.</summary>
    internal DaggerfallTravelPolicy Policy => policy;

    /// <summary>
    /// Offers a guild's free teleport from the provider with this NPC: the map opens with no destination chosen.
    /// <paramref name="available"/> answers whether the guild still offers it when the player accepts.
    /// </summary>
    internal string OfferGuildTeleport(long npcId, Func<bool> available)
    {
        _guildTeleport = (npcId, available);
        _selectedDestination = null;
        _mapOpen = true;
        _message = "Select a destination for free guild teleportation.";
        return _message;
    }

    /// <summary>Opens the travel map. Whether it is open is presentation state and is not saved.</summary>
    internal void OpenMap() => _mapOpen = true;

    /// <summary>
    /// Closes the travel map, so its sheet stops riding every snapshot once the player has left the travel workflow: the
    /// DOM hid the panel, a journey began, or the product left play for the entry screen or death.
    /// </summary>
    internal void CloseMap()
    {
        _guildTeleport = null;
        _mapOpen = false;
    }

    /// <summary>Grants the atlas tiles the page shows, which the map then names with the URLs it draws them from.</summary>
    internal void GrantAtlasTiles(IReadOnlyList<DaggerfallAtlasTileRequest> tiles) => atlasTiles?.Grant(tiles);

    /// <summary>
    /// Searches the discovered destinations. A place named by the term comes before the places of a region it names, so a
    /// capital that shares its region's name ("Daggerfall") is not crowded out of the list by its region.
    /// </summary>
    internal void Search(string? text)
    {
        string term = text?.Trim() ?? string.Empty;
        _searchResults = [.. policy.SupportedDestinations()
            .Select(destination => (Destination: destination, Rank: PlaceRank(destination.Name, destination.Id.Region, term)))
            .Where(match => match.Rank is not null)
            .OrderBy(match => match.Rank)
            .Select(match => match.Destination)
            .Take(40)];
        _searchInitialized = true;
        _message = _searchResults.Length == 0 ? "No discovered destination matches that place or region." : null;
    }

    /// <summary>Selects a destination and the route and lodging options to quote it with.</summary>
    internal void Select(int? region, int? destination, bool cautious, bool inn, bool ship)
    {
        if (region is not int regionIndex || destination is not int index)
            throw new ArgumentException("Travel preview requires a selected destination.", nameof(destination));
        _selectedDestination = new DaggerfallSiteId(regionIndex, index);
        _selectedOptions = (cautious, inn, ship);
        _message = null;
        DaggerfallTravelQuote? quote = Quote();
        if (quote is { CanAfford: false }) _message = "You cannot afford the selected route and lodging options.";
    }

    /// <summary>
    /// How well a place answers a search: its exact name, then a name starting with the term, then a name containing it,
    /// then a region that names it; null when nothing does. The order within a rank is the catalog's.
    /// </summary>
    internal int? PlaceRank(string name, int region, string term)
    {
        if (name.Equals(term, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains(term, StringComparison.OrdinalIgnoreCase)) return 2;
        return DaggerfallRegionNames.Name(regionNames, region)
            .Contains(term, StringComparison.OrdinalIgnoreCase) ? 3 : null;
    }

    /// <summary>The live quote for the selected destination and options, or null with no selection or no route.</summary>
    internal DaggerfallTravelQuote? Quote()
    {
        if (_selectedDestination is not DaggerfallSiteId destination) return null;
        try
        {
            DaggerfallTravelOptions selected = options(_selectedOptions);
            if (_guildTeleport is { } teleport)
            {
                var target = policy.SupportedDestinations().FirstOrDefault(value => value.Id == destination);
                if (target == default) return null;
                return new(Position(), target, selected, 0, 0, 0, 0, 0, 0,
                    $"guild-teleport:{teleport.NpcId}:{activeProfile()}:{destination}");
            }
            return policy.Quote(Position(), destination, selected);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            // The route owner refuses a destination it cannot quote; its detail names map data, not a reason
            // the player can act on.
            _message = "No travel route to that destination is available right now.";
            return null;
        }
    }

    /// <summary>What the travel panel shows: the search, the live quote, the panel's line and the open map.</summary>
    internal DaggerfallTravelPresentation Read()
    {
        if (!_searchInitialized)
        {
            _searchResults = [.. policy.SupportedDestinations().Take(40)];
            _searchInitialized = true;
        }
        DaggerfallTravelQuote? quote = Quote();
        string? unavailable = quote is null ? null : Refusal(quote, out _);
        return new(_searchResults, quote, _message ?? unavailable, unavailable is null && quote is { CanAfford: true }, journeys.LastResult)
        { GuildTeleport = _guildTeleport is not null, RegionNames = regionNames, Map = _mapOpen ? ReadMap() : null };
    }

    private DaggerfallTravelMapView ReadMap()
    {
        _map ??= new DaggerfallTravelMap(policy.AllDestinations());
        DaggerfallTravelMapPixel? player;
        try { player = Position(); }
        catch (InvalidOperationException) { player = null; }
        // North is up and yaw turns to the right, so the heading reads as a compass bearing.
        double? heading = player is null ? null : ((yaw() * 180 / Math.PI) % 360 + 360) % 360;
        // The atlas is the land's own map; the classic art set draws the classic map instead.
        return _map.Read(policy.SupportedDestinations(), regionNames, player, heading, classicArt() ? null : atlasTiles, worldNames);
    }

    /// <summary>
    /// Why the quoted journey cannot start now, or null with the destination's exterior resolved. The exterior is resolved
    /// before anything is paid: a location the catalog cannot place cannot be travelled to.
    /// </summary>
    internal string? Refusal(DaggerfallTravelQuote quote, out DaggerfallSiteProfile? destination)
    {
        destination = null;
        if (_guildTeleport is { } offered && !offered.Available()) return "The guild teleportation service is no longer available.";
        bool teleport = _guildTeleport is not null;
        if (!teleport && isVampire() && calendar().IsDay)
            return "Vampires cannot start fast travel during daylight.";
        if (playerDefeated()) return "You cannot travel while defeated.";
        if (!teleport && activeProfile().Kind != DaggerfallWorldProfileKind.Exterior) return "Leave the building or dungeon before travelling.";
        if (encounterPending(activeProfile().LogicalId))
            return "Something approaches. Move or turn to let the encounter arrive before travelling.";
        if (enemyNearby()) return "Nearby enemies prevent travel.";
        if (!quote.CanAfford) return "You cannot afford the route and its coin-only inn cost.";
        DaggerfallSiteProfile exterior;
        try
        {
            if (profiles() is not { } admitted || !admitted.TryGet(DaggerfallWorldProfileIds.Exterior(quote.Destination.Id), out exterior))
                return $"You cannot travel to {quote.Destination.Name} from here.";
        }
        catch (InvalidOperationException failure)
        {
            // A location whose blocks do not assemble is named with what is missing rather than half-entered.
            return $"You cannot travel to {quote.Destination.Name}: {DaggerfallRejectionText.For(failure)}";
        }
        destination = exterior; return null;
    }

    /// <summary>
    /// Accepts the live quote the player confirmed: null, with the panel's line naming why, when there is none, it is
    /// refused, or the route or carried funds changed since the player read it.
    /// </summary>
    internal DaggerfallTravelAcceptance? Accept(string expectedQuote, ulong expectedCost)
    {
        DaggerfallTravelQuote? quote = Quote();
        DaggerfallSiteProfile? destination = null;
        string? refusal = quote is null ? "Preview a current travel route before starting." : Refusal(quote, out destination);
        if (quote is not null && (expectedQuote != quote.Identity || expectedCost != (ulong)quote.TotalCost))
            refusal = "The route or carried funds changed. Preview the journey again.";
        if (refusal is not null || quote is null)
        {
            _message = refusal;
            return null;
        }
        return new(quote, destination!);
    }

    /// <summary>
    /// The player has left the panel for the journey or the guild's teleport: the selection retires, so a second action
    /// cannot repeat the same accepted payment, and as the donor's travel windows close on departure, the map closes.
    /// </summary>
    internal void Depart()
    {
        _selectedDestination = null;
        _message = null;
        CloseMap();
    }

    /// <summary>The line a guild teleport ends with.</summary>
    internal string Teleported(bool arrived, DaggerfallTravelQuote quote)
    {
        _message = arrived ? $"The guild teleports you to {quote.Destination.Name}." : "The destination could not be reached.";
        return _message;
    }

    /// <summary>The line a journey ends with when its payment is no longer carried.</summary>
    internal string Unpaid()
    {
        _message = "The carried payment is no longer available.";
        return _message;
    }

    /// <summary>A journey's road or arrival failed; the reason is the outcome's, not the panel's line.</summary>
    internal void Fail() => _message = null;

    /// <summary>Starts a paid journey from the site the player is leaving at this absolute second.</summary>
    internal void Begin(DaggerfallSiteId origin, DaggerfallTravelQuote quote, long started) => journeys.Begin(origin, quote, started);

    /// <summary>
    /// Raises the clock for a journey, a minute's slice at a time. As the donor's fast travel (DaggerfallTravelPopUp with
    /// PreventEnemySpawns), no random encounter is selected on the road: only defeat, a relocation or a refused advance
    /// ends it early.
    /// </summary>
    internal DaggerfallTravelOutcome Road(long requestedSeconds, DaggerfallWorldProfileKey origin)
    {
        long started = calendar().ToAbsoluteSeconds();
        while (calendar().ToAbsoluteSeconds() - started < requestedSeconds)
        {
            if (playerDefeated()) return DaggerfallTravelOutcome.Defeated;
            if (activeProfile() != origin) return DaggerfallTravelOutcome.Relocated;
            long now = calendar().ToAbsoluteSeconds();
            long slice = Math.Min(requestedSeconds - (now - started), DaggerfallCalendar.SecondsPerMinute - now % DaggerfallCalendar.SecondsPerMinute);
            // The donor's travel raises the clock without a per-minute fatigue loss; only cautious
            // travel touches fatigue, by refilling it before the journey.
            DaggerfallCalendarAdvance advance = passTime(slice);
            if (playerDefeated()) return DaggerfallTravelOutcome.Defeated;
            if (activeProfile() != origin) return DaggerfallTravelOutcome.Relocated;
            if (advance.AppliedSeconds != slice) return DaggerfallTravelOutcome.Stopped;
        }
        return DaggerfallTravelOutcome.Arrived;
    }

    /// <summary>DFU cautious arrival is 07:10 by day; sunlight-vulnerable careers arrive at dusk instead.</summary>
    internal static long ArrivalDelay(DaggerfallCalendar calendar, bool sunlightVulnerable, bool cautious = true)
    {
        long timeOfDay = calendar.SecondOfDay;
        if (sunlightVulnerable) return calendar.IsDay ? DaggerfallCalendar.DuskHour * (DaggerfallCalendar.MinutesPerHour * DaggerfallCalendar.SecondsPerMinute) - timeOfDay : 0;
        if (!cautious) return 0;
        const long morning = 7 * (DaggerfallCalendar.MinutesPerHour * DaggerfallCalendar.SecondsPerMinute) + 10 * DaggerfallCalendar.SecondsPerMinute;
        if (timeOfDay < morning) return morning - timeOfDay;
        return calendar.Hour >= DaggerfallCalendar.DuskHour ? DaggerfallCalendar.SecondsPerDay - timeOfDay + morning : 0;
    }

    /// <summary>
    /// Ends a journey where it stands: its result, recorded once with what it paid and how long it took, and the line the
    /// player reads; the panel's own line is left for the next preview.
    /// </summary>
    internal DaggerfallTravelResult Complete(DaggerfallJourney journey, DaggerfallTravelOutcome outcome)
    {
        long ended = calendar().ToAbsoluteSeconds();
        DaggerfallTravelMapPixel actualPixel = Position();
        string status = outcome == DaggerfallTravelOutcome.Arrived ? $"Arrived at {journey.Quote.Destination.Name}."
            : InterruptionText(outcome);
        string message = $"{status} Paid {journey.Quote.TotalCost} gold; {DaggerfallCalendar.DescribeDuration(ended - journey.Started)} passed."
            + (_message is null ? "" : " " + _message);
        DaggerfallTravelResult result = journeys.Complete(ended, activeProfile().Site, actualPixel, outcome, message);
        _message = null;
        return result;
    }

    /// <summary>Player wording for a journey that ended before its destination.</summary>
    internal static string InterruptionText(DaggerfallTravelOutcome outcome) => outcome switch
    {
        DaggerfallTravelOutcome.Defeated => "You were defeated on the road.",
        DaggerfallTravelOutcome.Unavailable => "Your destination could not be reached; the journey ended early.",
        DaggerfallTravelOutcome.SaveBoundary => "Your journey stopped where it was saved.",
        _ => "Your journey was interrupted.",
    };

    /// <summary>
    /// Where a journey or a release lands at a location's exterior: the side it arrives from (the journey's direction, or
    /// a keyed draw without one) and, at a city, the start marker nearest that side.
    /// </summary>
    internal DaggerfallSiteAnchor Landing(DaggerfallSiteProfile exterior, DaggerfallTravelMapPixel? departure, string draw)
    {
        DaggerfallSiteRecord site = sites.Require(exterior.Site
            ?? throw new InvalidOperationException($"Exterior '{exterior.ProfileKey.LogicalId}' names no location."));
        DaggerfallArrivalSide side = DaggerfallLocationArrival.Side(departure, site, count => checked((int)random.DrawKeyed(
            new KeyedRngRequest(0, "daggerfall.location-arrival", draw, 0, count - 1)).Value));
        return DaggerfallLocationArrival.Landing(exterior, site, side);
    }

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

/// <summary>What the travel panel shows: the search, the live quote, its line, whether the journey can start and the last result.</summary>
internal sealed record DaggerfallTravelPresentation(
    IReadOnlyList<DaggerfallTravelDestination> Destinations,
    DaggerfallTravelQuote? Quote,
    string? Message,
    bool ExecutionAvailable,
    DaggerfallTravelResult? LastResult)
{
    /// <summary>The published region names a destination is labelled with.</summary>
    internal bool GuildTeleport { get; init; }
    internal IReadOnlyList<string> RegionNames { get; init; } = [];

    /// <summary>The open travel map, or null while the player has not opened it.</summary>
    internal DaggerfallTravelMapView? Map { get; init; }

    internal string RegionName(int region) => DaggerfallRegionNames.Name(RegionNames, region);
}
