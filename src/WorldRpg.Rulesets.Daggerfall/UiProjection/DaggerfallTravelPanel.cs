using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The travel panel's actions: fast travel's search, preview, map and atlas, a journey accepted, a change of mount or
/// ship, and moving items to and from the wagon.
/// </summary>
/// <param name="requestTravelPlaces">The HUD sends the travel map's places again.</param>
/// <param name="transportAccess">Where the player's transport can be reached from here.</param>
/// <param name="acceptTravel">Session: the journey's list for the quote the player accepted.</param>
/// <param name="boardShip">Session: boarding the owned ship's list.</param>
/// <param name="leaveShip">Session: leaving the ship's list.</param>
/// <param name="applyTransportChange">Session: what a change of transport does across domains.</param>
internal sealed class DaggerfallTravelPanel(DaggerfallFastTravel fastTravel, DaggerfallTransportPolicy transport,
    DaggerfallWagonStorage wagon, MechanicsInventoryCoordinator inventory, DaggerfallInventoryPresentation inventoryUi,
    Action requestTravelPlaces, Func<DaggerfallTransportAccessContext> transportAccess, Action<string, ulong> acceptTravel,
    Func<DaggerfallTransportActionResult> boardShip, Func<DaggerfallTransportActionResult> leaveShip,
    Action<DaggerfallTransportActionResult> applyTransportChange, PresentationState presentation)
{
    internal void Search(DaggerfallPlayerUiAction action) => fastTravel.Search(action.Text);

    internal void Preview(DaggerfallPlayerUiAction action) =>
        fastTravel.Select(action.Region, action.Destination, action.Cautious, action.Inn, action.Ship);

    internal void ChangeMap(DaggerfallPlayerUiAction action)
    {
        if (action.Open) OpenMap();
        else fastTravel.CloseMap();
    }

    /// <summary>Opens the travel map; an open map's places travel again, since the page opened it afresh or lost them.</summary>
    internal void OpenMap()
    {
        fastTravel.OpenMap();
        requestTravelPlaces();
    }

    internal void GrantAtlasTiles(DaggerfallPlayerUiAction action) => fastTravel.GrantAtlasTiles(action.Tiles!);

    internal void Accept(DaggerfallPlayerUiAction action) => acceptTravel(action.Key!, action.Amount!.Value);

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
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            presentation.SetOutcome(put ? "That item cannot be stored in the wagon." : "That item cannot be taken from the wagon.");
        }
    }
}
