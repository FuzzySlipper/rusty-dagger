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

/// <summary>
/// What the expedition map shows: the atlas with the party, where it heads and the pin; the time, weather and land; how
/// the party goes, at what speed and how long the leg has left; the ship, the lodging, the player's vitals; what Enter or
/// Leave does now; a waiting encounter, a guild teleport's choice; the map's line and the last Find.
/// </summary>
internal sealed record DaggerfallExpeditionView(
    DaggerfallTravelMapView Map,
    double PartyX, double PartyY, double? Heading,
    DaggerfallExpeditionMark? Destination, string? DestinationName,
    DaggerfallExpeditionMark? Pin, string? PinName,
    DaggerfallExpeditionMode Mode, int Speed, int Speeds,
    DaggerfallExpeditionShip Ship, bool Swimming, bool CanBoard, int ShipFare,
    string Date, string Time, DaggerfallWeatherKind Weather, string Land, string? Here, int? Lodging,
    long? RemainingSeconds, DaggerfallTransportMode Transport,
    DaggerfallExpeditionVital Health, DaggerfallExpeditionVital Fatigue, DaggerfallExpeditionVital Magicka,
    bool CanLeave, bool CanEnter, string EnterLabel,
    bool EncounterWaiting, bool Teleporting, string? TeleportPlace,
    string? Message, IReadOnlyList<DaggerfallTravelDestination> Found);

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
internal sealed class DaggerfallExpeditionPanel(DaggerfallExpedition expedition, DaggerfallTravelPolicy policy,
    IDaggerfallExpeditionSurroundings world, ActorsState actors, IReadOnlyList<string> regionNames, DaggerfallWorldNames worldNames,
    DaggerfallAtlasTiles? atlasTiles, Func<bool> classicArt, DaggerfallTransportPolicy transport,
    DaggerfallWagonStorage wagon, MechanicsInventoryCoordinator inventory, DaggerfallInventoryPresentation inventoryUi,
    Action requestTravelPlaces, Func<DaggerfallTransportAccessContext> transportAccess, Action openExpedition, Action enterExpedition,
    Func<DaggerfallTransportActionResult> boardShip, Func<DaggerfallTransportActionResult> leaveShip,
    Action<DaggerfallTransportActionResult> applyTransportChange, PresentationState presentation)
{
    private DaggerfallTravelMap? _map;

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
            default: throw new InvalidOperationException("Parsed expedition action is not supported.");
        }
    }

    /// <summary>What the expedition map shows now, or null while it is closed.</summary>
    internal DaggerfallExpeditionView? Read()
    {
        if (!expedition.Open) return null;
        IReadOnlyList<DaggerfallTravelDestination> known = policy.SupportedDestinations();
        _map ??= new DaggerfallTravelMap(policy.AllDestinations());
        DaggerfallTravelMapPixel party = expedition.Party;
        DaggerfallTravelMapView map = _map.Read(known, regionNames, party, expedition.Heading, classicArt() ? null : atlasTiles, worldNames);
        (double x, double y) = expedition.Position;
        DaggerfallCalendar calendar = world.Calendar;
        DaggerfallSiteRecord? here = expedition.LocationHere;
        DaggerfallTravelDestination? teleport = expedition.TeleportPlace is { } chosen ? known.FirstOrDefault(place => place.Id == chosen) : null;
        bool atSea = policy.IsOcean(party);
        StatsComponent stats = actors.Player.Stats;
        return new(map, x, y, expedition.Heading,
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
            expedition.EncounterWaiting, expedition.Teleporting, teleport?.Name,
            expedition.Message, expedition.Found);
    }

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
