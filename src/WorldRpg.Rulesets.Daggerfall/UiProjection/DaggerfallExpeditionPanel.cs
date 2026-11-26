using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One of the player's vitals and its pool, as the HUD counts them.</summary>
internal readonly record struct DaggerfallExpeditionVital(double Current, double Maximum);

/// <summary>An encounter the party must enter to meet, as its centred panel names it: who, and what Enter does.</summary>
internal sealed record DaggerfallExpeditionEncounterView(string Title, string Line);

/// <summary>
/// What the expedition's travel map shows about the world the Engine draws: the names over it and the way its view
/// faces and whether it follows the party; where the party heads and the pin; the time, weather and land; how
/// the party goes, at what speed and how long the leg has left; the ship, the lodging, the player's vitals; what Enter or
/// Leave does now; a waiting encounter, a guild teleport's choice; the map's line and the last Find; and the bands
/// within the party's sight.
/// </summary>
internal sealed record DaggerfallExpeditionView(
    IReadOnlyList<DaggerfallOverlandLabel> Labels, double ViewHeading, bool Following,
    DaggerfallExpeditionMark? Destination, string? DestinationName,
    DaggerfallExpeditionMark? Pin, string? PinName,
    DaggerfallExpeditionMode Mode, int Speed, int Speeds,
    DaggerfallExpeditionShip Ship, bool Swimming, bool CanBoard, int ShipFare,
    string Date, string Time, DaggerfallWeatherKind Weather, string Land, string? Here, int? Lodging,
    long? RemainingSeconds, DaggerfallTransportMode Transport,
    DaggerfallExpeditionVital Health, DaggerfallExpeditionVital Fatigue, DaggerfallExpeditionVital Magicka,
    bool CanLeave, bool CanEnter, string EnterLabel,
    DaggerfallExpeditionEncounterView? Encounter, bool Teleporting, string? TeleportPlace,
    string? Message, IReadOnlyList<DaggerfallTravelDestination> Found,
    IReadOnlyList<DaggerfallExpeditionBandView> Bands);

/// <summary>
/// The world map: the atlas to browse, with where the party or the player stands, where the party heads, the pin and the
/// last Find; whether the party is out on the travel map, and whether Travel there can set it going.
/// </summary>
internal sealed record DaggerfallWorldMapView(
    DaggerfallTravelMapView Map, double PartyX, double PartyY,
    DaggerfallExpeditionMark? Destination, string? DestinationName,
    DaggerfallExpeditionMark? Pin, string? PinName,
    IReadOnlyList<DaggerfallTravelDestination> Found, bool Travelling, bool CanTravel);

/// <summary>
/// The travel panel's and the expedition map's actions: the map opened, run at a speed, a mode, a destination, a stop, a
/// pin, a Find, a ship, leaving or entering, and its atlas tiles; and, in the travel panel, a change of mount and moving
/// items to and from the wagon.
/// </summary>
/// <param name="requestTravelPlaces">The HUD sends the travel map's places again.</param>
/// <param name="transportAccess">Where the player's transport can be reached from here.</param>
/// <param name="openExpedition">Session: the map's opening list.</param>
/// <param name="enterExpedition">Session: entering the world from the map (or the guild's teleport).</param>
/// <param name="boardShip">Session: boarding the owned ship's list.</param>
/// <param name="leaveShip">Session: leaving the ship's list.</param>
/// <param name="applyTransportChange">Session: what a change of transport does across domains.</param>
/// <param name="overland">The travel map the Engine draws, whose view the map's actions move and point into.</param>
/// <param name="playerPixel">The world-map pixel the player stands on, which the world map shows while the party is not out.</param>
internal sealed class DaggerfallExpeditionPanel(DaggerfallExpedition expedition, DaggerfallTravelPolicy policy,
    IDaggerfallExpeditionSurroundings world, ActorsState actors, IReadOnlyList<string> regionNames, DaggerfallWorldNames worldNames,
    DaggerfallAtlasTiles? atlasTiles, Func<bool> classicArt, DaggerfallTransportPolicy transport,
    DaggerfallWagonStorage wagon, MechanicsInventoryCoordinator inventory, DaggerfallInventoryPresentation inventoryUi,
    Action requestTravelPlaces, Func<DaggerfallTransportAccessContext> transportAccess, Action openExpedition, Action enterExpedition,
    Func<DaggerfallTransportActionResult> boardShip, Func<DaggerfallTransportActionResult> leaveShip,
    Action<DaggerfallTransportActionResult> applyTransportChange, PresentationState presentation,
    DaggerfallOverlandMap overland, Func<DaggerfallTravelMapPixel> playerPixel,
    Func<string, (string? Faction, string? Detail)>? describePersistentBand = null)
{
    private DaggerfallTravelMap? _map;
    private bool _worldMapOpen;

    /// <summary>Whether the page shows the world map, as it reports: it holds the world, and the travel map's time with it.</summary>
    internal bool WorldMapOpen => _worldMapOpen;

    internal void Open() => openExpedition();

    /// <summary>The page lost the map's places or opened it afresh, so they travel again.</summary>
    internal void RequestPlaces() => requestTravelPlaces();

    internal void GrantAtlasTiles(DaggerfallPlayerUiAction action) => atlasTiles?.Grant(action.Tiles!);

    internal void Change(DaggerfallPlayerUiAction action)
    {
        if (!expedition.Open) { presentation.SetOutcome("The travel map is not open."); return; }
        switch (action.Kind)
        {
            case DaggerfallUiActionKind.ExpeditionSpeed:
                if (action.Speed is int speed && speed <= expedition.Tuning.Speeds) expedition.SetSpeed(speed);
                break;
            case DaggerfallUiActionKind.ExpeditionMode:
                if (Mode(action.Mode) is { } mode) expedition.SetMode(mode);
                break;
            case DaggerfallUiActionKind.ExpeditionGo:
                expedition.SetDestination(new(action.X!.Value, action.Y!.Value), Place(action));
                break;
            case DaggerfallUiActionKind.ExpeditionStop: expedition.Stop(); break;
            case DaggerfallUiActionKind.ExpeditionPin:
                if (action.X is int x && action.Y is int y) expedition.SetPin(new(x, y), Place(action));
                else expedition.ClearPin();
                break;
            case DaggerfallUiActionKind.ExpeditionFind: expedition.Find(action.Text, regionNames); break;
            case DaggerfallUiActionKind.ExpeditionShip: _ = expedition.ToggleShip(); break;
            case DaggerfallUiActionKind.ExpeditionLeave: _ = expedition.Leave(); break;
            case DaggerfallUiActionKind.ExpeditionEnter: enterExpedition(); break;
            case DaggerfallUiActionKind.ExpeditionView:
                IReadOnlyList<double> move = action.View!;
                overland.View.Move(move[0], move[1], move[2], move[3], move[4]);
                break;
            case DaggerfallUiActionKind.ExpeditionFollow: overland.View.Follow(); break;
            case DaggerfallUiActionKind.ExpeditionLook:
                (double lookX, double lookZ) = DaggerfallOverlandView.Metres(action.X!.Value + .5, action.Y!.Value + .5);
                overland.View.Look(lookX, lookZ);
                break;
            case DaggerfallUiActionKind.ExpeditionGoAt:
            case DaggerfallUiActionKind.ExpeditionPinAt:
                if (overland.Pick(action.Point![0], action.Point[1]) is not { } pixel) { presentation.SetOutcome("Choose a point on the land."); break; }
                DaggerfallSiteId? there = expedition.LocationAt(pixel) is { } site && expedition.Known(site.Id) ? site.Id : null;
                if (action.Kind == DaggerfallUiActionKind.ExpeditionGoAt) expedition.SetDestination(pixel, there);
                else expedition.SetPin(pixel, there);
                break;
            default: throw new InvalidOperationException("Parsed expedition action is not supported.");
        }
    }

    /// <summary>
    /// The world map's actions: the page shows or hides it; a Find; a pin set or cleared; and Travel there, which sets the
    /// party going for the point, opening the travel map first when it is closed.
    /// </summary>
    internal void ChangeWorldMap(DaggerfallPlayerUiAction action)
    {
        switch (action.Kind)
        {
            case DaggerfallUiActionKind.WorldMap:
                if (action.Open && !_worldMapOpen) requestTravelPlaces();
                _worldMapOpen = action.Open;
                break;
            case DaggerfallUiActionKind.WorldMapFind: expedition.Find(action.Text, regionNames); break;
            case DaggerfallUiActionKind.WorldMapPin:
                if (action.X is int x && action.Y is int y) expedition.SetPin(new(x, y), Place(action));
                else expedition.ClearPin();
                break;
            case DaggerfallUiActionKind.WorldMapTravel:
                if (!expedition.Open) openExpedition();
                if (!expedition.Open) break;
                if (expedition.Held is { } held) { presentation.SetOutcome(held); break; }
                expedition.SetDestination(new(action.X!.Value, action.Y!.Value), Place(action));
                break;
            default: throw new InvalidOperationException("Parsed world map action is not supported.");
        }
    }

    /// <summary>What the world map shows now, or null while the page does not show it.</summary>
    internal DaggerfallWorldMapView? ReadWorldMap()
    {
        if (!_worldMapOpen) return null;
        (double x, double y) = expedition.Open ? expedition.Position : (playerPixel().X + .5, playerPixel().Y + .5);
        DaggerfallTravelMapPixel party = new((int)Math.Floor(x), (int)Math.Floor(y));
        _map ??= new DaggerfallTravelMap(policy.AllDestinations());
        DaggerfallTravelMapView map = _map.Read(policy.SupportedDestinations(), regionNames, party, expedition.Open ? expedition.Heading : null,
            classicArt() ? null : atlasTiles, worldNames);
        return new(map, x, y, expedition.Destination, Name(expedition.Destination), expedition.Pin, Name(expedition.Pin), expedition.Found,
            expedition.Open, !expedition.Open || (expedition.Held is null && !expedition.EncounterWaiting));
    }

    /// <summary>What the expedition map shows now, or null while it is closed.</summary>
    internal DaggerfallExpeditionView? Read()
    {
        if (!expedition.Open) return null;
        DaggerfallTravelMapPixel party = expedition.Party;
        DaggerfallCalendar calendar = world.Calendar;
        DaggerfallSiteRecord? here = expedition.LocationHere;
        DaggerfallTravelDestination? teleport = expedition.TeleportPlace is { } chosen ? policy.SupportedDestinations().FirstOrDefault(place => place.Id == chosen) : null;
        bool atSea = policy.IsOcean(party);
        StatsComponent stats = actors.Player.Stats;
        IReadOnlyList<DaggerfallExpeditionBandView> bands = [.. expedition.SeenBands.Select(EnrichBand)];
        return new(overland.Labels, overland.View.Heading, overland.View.Following,
            expedition.Destination, Name(expedition.Destination), expedition.Pin, Name(expedition.Pin),
            expedition.Mode, expedition.Speed, expedition.Tuning.Speeds,
            expedition.Ship, expedition.Swimming, expedition.Ship == DaggerfallExpeditionShip.None && !atSea && expedition.NearPort(),
            world.OwnsShip || world.FreeShipPassage ? 0 : expedition.Tuning.ShipHire,
            calendar.DescribeDate(), calendar.DescribeTime(), world.Weather(policy.Climate(party)), policy.ClimateName(party),
            here is not null && expedition.Known(here.Id) ? here.Name : null, expedition.LodgingHere,
            expedition.RemainingSeconds(), world.Transport,
            Vital(stats, DaggerfallMechanicsIds.Health), Worn(stats), Vital(stats, DaggerfallMechanicsIds.Magicka),
            expedition.CanLeave, expedition.Teleporting ? teleport is not null : !atSea && expedition.Held is null,
            expedition.Teleporting ? "Teleport" : here is not null && expedition.Known(here.Id) ? $"Enter {here.Name}" : "Enter",
            Encounter(), expedition.Teleporting, teleport?.Name,
            expedition.Message, expedition.Found, bands);
    }

    private DaggerfallExpeditionBandView EnrichBand(DaggerfallExpeditionBandView band)
    {
        if (describePersistentBand is null || string.IsNullOrWhiteSpace(band.PersistentBandId))
            return band;
        (string? faction, string? detail) = describePersistentBand(band.PersistentBandId);
        if (faction is null && detail is null)
            return band;
        return band with
        {
            Faction = faction ?? band.Faction,
            Detail = detail ?? band.Detail,
        };
    }

    /// <summary>The waiting encounter's panel: the band that caught the party, or an unseen one; null when none waits.</summary>
    private DaggerfallExpeditionEncounterView? Encounter() => !expedition.EncounterWaiting ? null
        : expedition.EncounterFoe is { } foe ? new(foe, "There is no outrunning them now. Enter to fight.")
        : new("Something approaches", "It is upon you before you can see what it is. Enter to meet it.");

    /// <summary>A mount chosen, or a ship boarded or left, in the travel panel.</summary>
    internal void ChangeTransport(DaggerfallPlayerUiAction action)
    {
        DaggerfallTransportActionResult result = action.Kind switch
        {
            DaggerfallUiActionKind.TransportSelect => transport.SelectMount(action.Mode switch
            {
                "foot" => DaggerfallTransportMode.Foot,
                "horse" => DaggerfallTransportMode.Horse,
                "cart" => DaggerfallTransportMode.Cart,
                _ => throw new InvalidOperationException("Parsed transport mode is not supported."),
            }, inventory.Read(), transportAccess()),
            DaggerfallUiActionKind.TransportBoardShip => boardShip(),
            DaggerfallUiActionKind.TransportLeaveShip => leaveShip(),
            _ => throw new InvalidOperationException("Parsed transport action is not supported."),
        };
        applyTransportChange(result);
        presentation.SetOutcome(result.Message);
    }

    internal void ChangeWagon(DaggerfallPlayerUiAction action)
    {
        if (action.Revision != inventoryUi.Read().Revision)
        {
            presentation.SetOutcome("Inventory changed. Choose the item again.");
            return;
        }
        bool put = action.Kind == DaggerfallUiActionKind.WagonPut;
        InventoryView? source = put ? inventory.Read() : wagon.Read();
        if (source is null || action.Item is null || !DaggerfallContainerSelection.TryParse(source, action.Item, action.Amount, out InventoryContainerSelection? selection))
        {
            presentation.SetOutcome("That item is no longer available for wagon transfer.");
            return;
        }
        try
        {
            if (put)
                _ = wagon.TransferToWagon(selection!, transportAccess(), inventory.Read().StoreRevision);
            else
                _ = wagon.TransferFromWagon(selection!, transportAccess(), inventory.Read().StoreRevision);
            presentation.SetOutcome(put ? "Item stored in wagon." : "Item taken from wagon.");
        }
        catch (DaggerfallItemExitRefusedException refused) { presentation.SetOutcome(refused.Message); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            presentation.SetOutcome(put ? "That item cannot be stored in the wagon." : "That item cannot be taken from the wagon.");
        }
    }

    private string? Name(DaggerfallExpeditionMark? mark) =>
        mark?.Place is { } place && expedition.Known(place) && expedition.LocationAt(mark.Pixel) is { } site ? site.Name : null;

    private static DaggerfallSiteId? Place(DaggerfallPlayerUiAction action) =>
        action.Region is int region && action.Index is int index ? new(region, index) : null;

    private static DaggerfallExpeditionMode? Mode(string? mode) => mode switch
    {
        "cautious" => DaggerfallExpeditionMode.Cautious,
        "reckless" => DaggerfallExpeditionMode.Reckless,
        "halt" => DaggerfallExpeditionMode.Halt,
        "camp" => DaggerfallExpeditionMode.Camp,
        _ => null,
    };

    /// <summary>Fatigue as the HUD shows it: how worn out the player is, in its display units, filling as the party tires.</summary>
    private static DaggerfallExpeditionVital Worn(StatsComponent stats)
    {
        Track track = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value));
        long maximum = DaggerfallFormulaPolicy.DisplayFatigue(track.Maximum.ValueInt64);
        return new(maximum - DaggerfallFormulaPolicy.DisplayFatigue(track.ValueInt64), maximum);
    }

    private static DaggerfallExpeditionVital Vital(StatsComponent stats, DaggerfallTrackId id)
    {
        Track track = stats.GetTrack(TrackId.Parse(id.Value));
        return new(track.Current, track.MaximumValue);
    }
}
