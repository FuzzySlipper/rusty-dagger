using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallTravelPolicy _travelPolicy;
    private DaggerfallTravelDestination[] _travelSearchResults = [];
    private bool _travelSearchInitialized;
    private DaggerfallSiteId? _travelSelectedDestination;
    private (bool Cautious, bool Inn, bool Ship) _travelSelectedOptions = (true, false, false);
    private string? _travelMessage;
    private DaggerfallTravelMap? _travelMap;
    private bool _travelMapOpen;
    private DaggerfallServiceProvider? _guildTeleportProvider;

    private DaggerfallGuildProviderResult OpenGuildTeleport(DaggerfallServiceProvider provider)
    {
        _guildTeleportProvider = provider;
        _travelSelectedDestination = null;
        _travelMapOpen = true;
        _travelMapRegion = null;
        _travelMessage = "Select a destination for free guild teleportation.";
        RequestPanel(DaggerfallPanel.Travel);
        return new(true, DaggerfallGuildProviderDenial.None, _travelMessage);
    }

    private bool GuildTeleportAvailable() => _guildTeleportProvider is { } provider
        && State.ConcreteGuildServices.Evaluate(DaggerfallConcreteGuildCatalog.MagesFactionId,
            DaggerfallConcreteGuildService.Teleport, checked((int)_time.Calendar.DayNumber), new(provider)).CanUse;
    private int? _travelMapRegion;
    private int _travelMapPage;

    /// <summary>Opens, closes or turns the travel map. Which view is open is presentation state and is not saved.</summary>
    private void ChangeTravelMap(DaggerfallPlayerUiAction action)
    {
        if (!action.Open)
        {
            CloseTravelMap();
            return;
        }
        if (action.Region is int region && action.Page is int page && page >= DaggerfallTravelMap.PageCount(region))
        {
            _travelMessage = "The travel map has no such view of that region.";
            return;
        }
        _travelMapOpen = true;
        _travelMapRegion = action.Region;
        _travelMapPage = action.Region is null ? 0 : action.Page ?? 0;
    }

    /// <summary>
    /// Closes the travel map, so its sheet stops riding every snapshot once the player has left the
    /// travel workflow: the DOM hid the panel, a journey began, or the product left play for the entry
    /// screen or death.
    /// </summary>
    private void CloseTravelMap()
    {
        _guildTeleportProvider = null;
        _travelMapOpen = false; _travelMapRegion = null; _travelMapPage = 0;
    }

    private DaggerfallTravelMapView ReadTravelMap()
    {
        _travelMap ??= new DaggerfallTravelMap(_travelPolicy.AllDestinations());
        DaggerfallTravelMapPixel? player;
        try { player = QuestTravelOrigin(); }
        catch (InvalidOperationException) { player = null; }
        return _travelMap.Read(_travelPolicy.SupportedDestinations(), _definitions.BuildingNames.RegionNames, player, _travelMapRegion, _travelMapPage);
    }

    private void ChangeTravel(DaggerfallPlayerUiAction action)
    {
        if (action.Kind == DaggerfallUiActionKind.TravelSearch)
        {
            string term = action.Text?.Trim() ?? string.Empty;
            // A place named by the term comes before the places of a region it names, so a capital that
            // shares its region's name ("Daggerfall") is not crowded out of the list by its region.
            _travelSearchResults = [.. _travelPolicy.SupportedDestinations()
                .Select(destination => (Destination: destination, Rank: SearchRank(destination, term)))
                .Where(match => match.Rank is not null)
                .OrderBy(match => match.Rank)
                .Select(match => match.Destination)
                .Take(40)];
            _travelSearchInitialized = true;
            _travelMessage = _travelSearchResults.Length == 0 ? "No discovered destination matches that place or region." : null;
            return;
        }
        if (action.Region is not int region || action.Destination is not int index)
            throw new ArgumentException("Travel preview requires a selected destination.", nameof(action));
        _travelSelectedDestination = new DaggerfallSiteId(region, index);
        _travelSelectedOptions = (action.Cautious, action.Inn, action.Ship);
        _travelMessage = null;
        DaggerfallTravelQuote? quote = CurrentTravelQuote();
        if (quote is { CanAfford: false }) _travelMessage = "You cannot afford the selected route and lodging options.";
    }

    /// <summary>
    /// How well a destination answers a search: its exact name, then a name starting with the term, then a name
    /// containing it, then a region that names it; null when nothing does. The order within a rank is the catalog's.
    /// </summary>
    private int? SearchRank(DaggerfallTravelDestination destination, string term)
    {
        if (destination.Name.Equals(term, StringComparison.OrdinalIgnoreCase)) return 0;
        if (destination.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 1;
        if (destination.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) return 2;
        return DaggerfallRegionNames.Name(_definitions.BuildingNames.RegionNames, destination.Id.Region)
            .Contains(term, StringComparison.OrdinalIgnoreCase) ? 3 : null;
    }

    private DaggerfallTravelQuote? CurrentTravelQuote()
    {
        if (_travelSelectedDestination is not DaggerfallSiteId destination) return null;
        try
        {
            DaggerfallCurrencyTotals funds = State.Currency.Read();
            DaggerfallTravelOptions options = new(
                _travelSelectedOptions.Cautious,
                _travelSelectedOptions.Inn,
                _travelSelectedOptions.Ship,
                State.Transport.HasHorse(State.Inventory.Read()),
                State.Transport.HasCart(State.Inventory.Read()),
                State.Property?.OwnsShip ?? false,
                AvailableGold: checked(funds.Gold + funds.LettersOfCredit),
                AvailableGoldPieces: funds.Gold,
                FreeShipPassage: HasFreeGuildShipTravel());
            if (_guildTeleportProvider is not null)
            {
                var target = _travelPolicy.SupportedDestinations().FirstOrDefault(value => value.Id == destination);
                if (target == default) return null;
                return new(QuestTravelOrigin(), target, options, 0, 0, 0, 0, 0, 0,
                    $"guild-teleport:{_guildTeleportProvider.NpcId}:{_activeProfileKey}:{destination}");
            }
            DaggerfallTravelQuote quote = _travelPolicy.Quote(QuestTravelOrigin(), destination, options);
            return quote;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            // The route owner refuses a destination it cannot quote; its detail names map data, not a reason
            // the player can act on.
            _travelMessage = "No travel route to that destination is available right now.";
            return null;
        }
    }

    internal DaggerfallTravelPresentation ReadTravelPresentation()
    {
        if (!_travelSearchInitialized)
        {
            _travelSearchResults = [.. _travelPolicy.SupportedDestinations().Take(40)];
            _travelSearchInitialized = true;
        }
        DaggerfallTravelQuote? quote = CurrentTravelQuote();
        string? unavailable = quote is null ? null : TravelRefusal(quote, out _);
        return new(_travelSearchResults, quote, _travelMessage ?? unavailable, unavailable is null && quote is { CanAfford: true }, State.Travel.LastResult)
        { GuildTeleport = _guildTeleportProvider is not null, RegionNames = _definitions.BuildingNames.RegionNames, Map = _travelMapOpen ? ReadTravelMap() : null };
    }

    /// <summary>Accepts the live quote once; all elapsed consequences use the session's single calendar.</summary>
    internal DaggerfallTravelResult? ExecuteTravel(string expectedQuote, ulong expectedCost)
    {
        DaggerfallTravelQuote? quote = CurrentTravelQuote();
        DaggerfallSiteProfile? destination = null;
        string? refusal = quote is null ? "Preview a current travel route before starting." : TravelRefusal(quote, out destination);
        if (quote is not null && (expectedQuote != quote.Identity || expectedCost != (ulong)quote.TotalCost))
            refusal = "The route or carried funds changed. Preview the journey again.";
        if (refusal is not null || quote is null)
        {
            _travelMessage = refusal; Presentation.SetOutcome(refusal!); return null;
        }
        if (_guildTeleportProvider is not null)
        {
            bool arrived = _sites.TryRelocatePlayer(destination!.ProfileKey,
                ArrivalLanding(destination, null, quote.Identity));
            if (arrived)
            {
                _travelSelectedDestination = null;
                CloseTravelMap();
                _dialogue?.Close();
                _input.Neutralize(); _locomotion.Neutralize();
                RequestPanel(DaggerfallPanel.Resume);
            }
            _travelMessage = arrived ? $"The guild teleports you to {quote.Destination.Name}." : "The destination could not be reached.";
            Presentation.SetOutcome(_travelMessage);
            return null;
        }
        if (quote.TotalCost > 0 && !State.Currency.TrySpendCarried((ulong)quote.TotalCost, (ulong)quote.InnCost))
        {
            _travelMessage = "The carried payment is no longer available."; Presentation.SetOutcome(_travelMessage); return null;
        }
        // Retiring the selection prevents a second action from repeating the same accepted payment.
        _travelSelectedDestination = null; _travelMessage = null;
        // As the donor's travel windows close on departure, the journey ends the map view.
        CloseTravelMap();
        _input.Neutralize(); _locomotion.Neutralize();
        DaggerfallWorldProfileKey origin = _activeProfileKey;
        long started = _time.Calendar.ToAbsoluteSeconds();
        State.Travel.Begin(State.Transport.ShipReturnProfile?.Site ?? origin.Site, quote, started);
        DaggerfallTravelOutcome outcome = DaggerfallTravelOutcome.Stopped;
        try
        {
            if (quote.Options.SpeedCautious)
            {
                (_, bool noRegen) = RestCharacterTraits();
                DaggerfallRestRecoveryModule.RecoverForCautiousTravel(State.Actors.Player.Stats, noRegen);
            }
            DaggerfallTravelMapPixel departure = QuestTravelOrigin();
            outcome = AdvanceTravelTime(quote.TravelSeconds, origin);
            if (outcome == DaggerfallTravelOutcome.Arrived && !_sites.TryRelocatePlayer(destination!.ProfileKey,
                    ArrivalLanding(destination, departure, $"travel:{quote.Identity}:{started}")))
                outcome = DaggerfallTravelOutcome.Unavailable;
            if (outcome == DaggerfallTravelOutcome.Arrived)
            {
                if (State.Transport.OnShip)
                {
                    // Source fast travel disembarks at the destination; stored ship contents
                    // remain on the inactive ship, while its detached land return is retired.
                    _ = State.Transport.LeaveShip();
                    _sites.ClearReturnDestination();
                }
                long delay = TravelArrivalDelay(_time.Calendar,
                    State.RacialOverrides.Current?.IsVampire == true || PlayerSpecials.SunDamage,
                    quote.Options.SpeedCautious);
                // Effects and deadlines of the arrival adjustment run at the actual destination
                // through the shared calendar.
                if (delay > 0) outcome = AdvanceTravelTime(delay, _activeProfileKey);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException)
        {
            outcome = DaggerfallTravelOutcome.Unavailable;
            _travelMessage = null;
        }
        long ended = _time.Calendar.ToAbsoluteSeconds();
        DaggerfallTravelMapPixel actualPixel = QuestTravelOrigin();
        string status = outcome == DaggerfallTravelOutcome.Arrived ? $"Arrived at {quote.Destination.Name}."
            : TravelInterruptionText(outcome);
        string message = $"{status} Paid {quote.TotalCost} gold; {DaggerfallCalendar.DescribeDuration(ended - started)} passed."
            + (_travelMessage is null ? "" : " " + _travelMessage);
        DaggerfallTravelResult result = State.Travel.Complete(ended, _activeProfileKey.Site, actualPixel, outcome, message);
        // The completed journey is published once, as the travel result; the panel's own line is left for
        // the next preview.
        _travelMessage = null; Presentation.SetOutcome(message);
        if (ended > started && !State.Actors.Player.IsDefeated)
        {
            State.SkillUses.RaiseSkills(ended); State.LevelUps.BeginIfEligible();
        }
        // Like rest, a journey is one synthetic time increase: held items reroll once, on arrival.
        if (ended > started) _itemCastTriggers.CompleteTimeIncrease();
        // The donor's travel windows are gone once the journey ends, arrived or interrupted, so the
        // player is back in control where it ended rather than under a menu.
        RequestPanel(DaggerfallPanel.Resume);
        return result;
    }

    /// <summary>Player wording for a journey that ended before its destination.</summary>
    internal static string TravelInterruptionText(DaggerfallTravelOutcome outcome) => outcome switch
    {
        DaggerfallTravelOutcome.Defeated => "You were defeated on the road.",
        DaggerfallTravelOutcome.Unavailable => "Your destination could not be reached; the journey ended early.",
        DaggerfallTravelOutcome.SaveBoundary => "Your journey stopped where it was saved.",
        _ => "Your journey was interrupted.",
    };

    /// <summary>
    /// Where a journey or a release lands at a location's exterior: the side it arrives from (the journey's
    /// direction, or a keyed draw without one) and, at a city, the start marker nearest that side.
    /// </summary>
    private DaggerfallSiteAnchor ArrivalLanding(DaggerfallSiteProfile exterior, DaggerfallTravelMapPixel? departure, string draw)
    {
        DaggerfallSiteRecord site = _site.Require(exterior.Site
            ?? throw new InvalidOperationException($"Exterior '{exterior.ProfileKey.LogicalId}' names no location."));
        DaggerfallArrivalSide side = DaggerfallLocationArrival.Side(departure, site, count => checked((int)_random.DrawKeyed(
            new KeyedRngRequest(0, "daggerfall.location-arrival", draw, 0, count - 1)).Value));
        return DaggerfallLocationArrival.Landing(exterior, site, side);
    }

    private string? TravelRefusal(DaggerfallTravelQuote quote, out DaggerfallSiteProfile? destination)
    {
        destination = null;
        if (_guildTeleportProvider is not null && !GuildTeleportAvailable()) return "The guild teleportation service is no longer available.";
        bool teleport = _guildTeleportProvider is not null;
        if (!teleport && State.RacialOverrides.Current?.IsVampire == true && _time.Calendar.IsDay)
            return "Vampires cannot start fast travel during daylight.";
        if (State.Actors.Player.IsDefeated) return "You cannot travel while defeated.";
        if (!teleport && _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior) return "Leave the building or dungeon before travelling.";
        if (_encounters.HasPending(_activeProfileKey.LogicalId))
            return "Something approaches. Move or turn to let the encounter arrive before travelling.";
        if (HasNearbyRestEnemy()) return "Nearby enemies prevent travel.";
        if (!quote.CanAfford) return "You cannot afford the route and its coin-only inn cost.";
        // The destination's exterior is resolved before anything is paid: a location the catalog cannot place
        // cannot be travelled to, and the player reads that rather than the content gap behind it.
        DaggerfallSiteProfile exterior;
        try
        {
            if (_sites.Profiles is not { } profiles || !profiles.TryGet(DaggerfallWorldProfileIds.Exterior(quote.Destination.Id), out exterior))
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
    /// Raises the clock for a journey. As the donor's fast travel (DaggerfallTravelPopUp with PreventEnemySpawns),
    /// no random encounter is selected on the road: only defeat, a relocation or a refused advance ends it early.
    /// </summary>
    private DaggerfallTravelOutcome AdvanceTravelTime(long requestedSeconds, DaggerfallWorldProfileKey origin)
    {
        long started = _time.Calendar.ToAbsoluteSeconds();
        while (_time.Calendar.ToAbsoluteSeconds() - started < requestedSeconds)
        {
            if (State.Actors.Player.IsDefeated) return DaggerfallTravelOutcome.Defeated;
            if (_activeProfileKey != origin) return DaggerfallTravelOutcome.Relocated;
            long now = _time.Calendar.ToAbsoluteSeconds();
            long slice = Math.Min(requestedSeconds - (now - started), DaggerfallCalendar.SecondsPerMinute - now % DaggerfallCalendar.SecondsPerMinute);
            // The donor's travel raises the clock without a per-minute fatigue loss; only cautious
            // travel touches fatigue, by refilling it before the journey.
            DaggerfallCalendarAdvance advance = AdvanceElapsedTime(slice, deferSkillAdvancement: true, idleFatigue: false,
                completeTimeIncrease: false);
            if (State.Actors.Player.IsDefeated) return DaggerfallTravelOutcome.Defeated;
            if (_activeProfileKey != origin) return DaggerfallTravelOutcome.Relocated;
            if (advance.AppliedSeconds != slice) return DaggerfallTravelOutcome.Stopped;
        }
        return DaggerfallTravelOutcome.Arrived;
    }

    /// <summary>DFU cautious arrival is 07:10 by day; sunlight-vulnerable careers arrive at dusk instead.</summary>
    internal static long TravelArrivalDelay(DaggerfallCalendar calendar, bool sunlightVulnerable, bool cautious = true)
    {
        long timeOfDay = calendar.SecondOfDay;
        if (sunlightVulnerable) return calendar.IsDay ? DaggerfallCalendar.DuskHour * (DaggerfallCalendar.MinutesPerHour * DaggerfallCalendar.SecondsPerMinute) - timeOfDay : 0;
        if (!cautious) return 0;
        const long morning = 7 * (DaggerfallCalendar.MinutesPerHour * DaggerfallCalendar.SecondsPerMinute) + 10 * DaggerfallCalendar.SecondsPerMinute;
        if (timeOfDay < morning) return morning - timeOfDay;
        return calendar.Hour >= DaggerfallCalendar.DuskHour ? DaggerfallCalendar.SecondsPerDay - timeOfDay + morning : 0;
    }

    /// <summary>The current world-map pixel, including wilderness steps away from an exterior site.</summary>
    private DaggerfallTravelMapPixel QuestTravelOrigin()
    {
        if (State.Transport.OnShip && State.Transport.ShipReturnProfile is { } land && State.Transport.ShipReturnPose is { } pose)
        {
            DaggerfallSiteRecord origin = _site.Records.Single(record => record.Id == land.Site);
            DaggerfallExteriorCellId cell = DaggerfallExteriorSessionOrigin.CellForLocalPosition(pose.Position,
                DaggerfallExteriorWorldOrigin.At(new(origin.MapPixelX, origin.MapPixelY)),
                new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height));
            return new(cell.X, cell.Y);
        }
        if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior)
        {
            DaggerfallExteriorCellId cell = _sites.CurrentExteriorCell();
            return new(cell.X, cell.Y);
        }
        DaggerfallSiteRecord site = _site.ActiveSite
            ?? throw new InvalidOperationException("Quest travel duration requires an active geographic site.");
        return new(site.MapPixelX, site.MapPixelY);
    }
}

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
