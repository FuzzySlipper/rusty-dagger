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
            _travelSearchResults = [.. _travelPolicy.SupportedDestinations()
                .Where(destination => destination.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(40)];
            _travelSearchInitialized = true;
            _travelMessage = _travelSearchResults.Length == 0 ? "No discovered destination matches that name." : null;
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
                AvailableGoldPieces: funds.Gold);
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
        { RegionNames = _definitions.BuildingNames.RegionNames, Map = _travelMapOpen ? ReadTravelMap() : null };
    }

    /// <summary>Accepts the live quote once; all elapsed consequences use the session's single calendar.</summary>
    internal DaggerfallTravelResult? ExecuteTravel(string expectedQuote, ulong expectedCost)
    {
        DaggerfallTravelQuote? quote = CurrentTravelQuote();
        DaggerfallRelocationDestination? destination = null;
        string? refusal = quote is null ? "Preview a current travel route before starting." : TravelRefusal(quote, out destination);
        if (quote is not null && (expectedQuote != quote.Identity || expectedCost != (ulong)quote.TotalCost))
            refusal = "The route or carried funds changed. Preview the journey again.";
        if (refusal is not null || quote is null)
        {
            _travelMessage = refusal; Presentation.SetOutcome(refusal!); return null;
        }
        if (quote.TotalCost > 0 && !State.Currency.TrySpendCarried((ulong)quote.TotalCost, (ulong)quote.InnCost))
        {
            _travelMessage = "The carried payment is no longer available."; Presentation.SetOutcome(_travelMessage); return null;
        }
        // Retiring the selection prevents a second action from repeating the same accepted payment.
        _travelSelectedDestination = null;
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
            outcome = AdvanceTravelTime(quote.TravelSeconds, origin, quote.Options);
            if (outcome == DaggerfallTravelOutcome.Arrived && !_sites.TryRelocate(destination!))
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
                    State.RacialOverrides.Current?.IsVampire == true || State.Character.CustomCareer?.Disadvantages.Any(trait => trait.Id == "damage" && trait.Target == "sunlight") == true,
                    quote.Options.SpeedCautious);
                // The donor suppresses new random spawns during its arrival adjustment. Effects
                // and deadlines still run at the actual destination through the shared calendar.
                if (delay > 0) outcome = AdvanceTravelTime(delay, _activeProfileKey, quote.Options, selectEncounters: false);
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
        _travelMessage = message; Presentation.SetOutcome(message);
        if (ended > started && !State.Actors.Player.IsDefeated)
        {
            State.SkillUses.RaiseSkills(ended); State.LevelUps.BeginIfEligible();
        }
        // Like rest, a journey is one synthetic time increase: held items reroll once, on arrival.
        if (ended > started) _itemCastTriggers.CompleteTimeIncrease();
        return result;
    }

    /// <summary>Player wording for a journey that ended before its destination.</summary>
    internal static string TravelInterruptionText(DaggerfallTravelOutcome outcome) => outcome switch
    {
        DaggerfallTravelOutcome.Encounter => "Your journey was interrupted by an encounter.",
        DaggerfallTravelOutcome.Defeated => "You were defeated on the road.",
        DaggerfallTravelOutcome.Unavailable => "Your destination could not be reached; the journey ended early.",
        DaggerfallTravelOutcome.SaveBoundary => "Your journey stopped where it was saved.",
        _ => "Your journey was interrupted.",
    };

    private string? TravelRefusal(DaggerfallTravelQuote quote, out DaggerfallRelocationDestination? destination)
    {
        destination = null;
        if (State.RacialOverrides.Current?.IsVampire == true && _time.Calendar.IsDay)
            return "Vampires cannot start fast travel during daylight.";
        if (State.Actors.Player.IsDefeated) return "You cannot travel while defeated.";
        if (_activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior) return "Leave the building or dungeon before travelling.";
        if (HasNearbyRestEnemy()) return "Nearby enemies prevent travel.";
        if (!quote.CanAfford) return "You cannot afford the route and its coin-only inn cost.";
        DaggerfallWorldProfileKey[] profiles = [.. (_sites.Profiles?.Keys ?? [])
            .Where(profile => profile.Site == quote.Destination.Id && profile.Kind == DaggerfallWorldProfileKind.Exterior)];
        // A destination this bundle carries no single arrival place for cannot be travelled to; the
        // player reads that rather than the content gap behind it.
        if (profiles.Length != 1 || !_sites.Profiles!.Require(profiles[0]).Anchors.ContainsKey("start"))
            return $"You cannot travel to {quote.Destination.Name} from here.";
        destination = new(profiles[0], "start"); return null;
    }

    private DaggerfallTravelOutcome AdvanceTravelTime(long requestedSeconds, DaggerfallWorldProfileKey origin, DaggerfallTravelOptions options, bool selectEncounters = true)
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
            if (selectEncounters && !options.SleepModeInn && !options.TravelShip
                && _time.Calendar.ToAbsoluteSeconds() % DaggerfallCalendar.SecondsPerMinute == 0)
            {
                long minute = _time.Calendar.ToAbsoluteSeconds() / DaggerfallCalendar.SecondsPerMinute;
                DaggerfallEncounterRequest? encounter = RestEncounterRequest(minute);
                if (encounter is not null && QueueEncounter(encounter).Choice.MobileId is not null)
                    return DaggerfallTravelOutcome.Encounter;
            }
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
    internal IReadOnlyList<string> RegionNames { get; init; } = [];

    /// <summary>The open travel map, or null while the player has not opened it.</summary>
    internal DaggerfallTravelMapView? Map { get; init; }

    internal string RegionName(int region) => DaggerfallRegionNames.Name(RegionNames, region);
}
