using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The travel panel's transport and wagon actions: a change of mount or ship, and moving items to and from the wagon.</summary>
internal sealed partial class DaggerfallSession
{
    private void ChangeTransport(DaggerfallPlayerUiAction action)
    {
        DaggerfallTransportActionResult result = action.Kind switch
        {
            DaggerfallUiActionKind.TransportSelect => State.Transport.SelectMount(action.Mode switch
            {
                "foot" => DaggerfallTransportMode.Foot,
                "horse" => DaggerfallTransportMode.Horse,
                "cart" => DaggerfallTransportMode.Cart,
                _ => throw new InvalidOperationException("Parsed transport mode is not supported."),
            }, State.Inventory.Read(), TransportAccess()),
            DaggerfallUiActionKind.TransportBoardShip => BoardPropertyShip(),
            DaggerfallUiActionKind.TransportLeaveShip => LeavePropertyShip(),
            _ => throw new InvalidOperationException("Parsed transport action is not supported."),
        };
        ApplyTransportChange(result);
        Presentation.SetOutcome(result.Message);
    }

    private void ChangeWagon(DaggerfallPlayerUiAction action)
    {
        if (action.Revision != _inventoryUi.Read().Revision)
        {
            Presentation.SetOutcome("Inventory changed. Choose the item again.");
            return;
        }
        bool put = action.Kind == DaggerfallUiActionKind.WagonPut;
        InventoryView? source = put ? State.Inventory.Read() : State.Wagon.Read();
        if (source is null || action.Item is null || !DaggerfallContainerSelection.TryParse(source, action.Item, action.Amount, out InventoryContainerSelection? selection))
        {
            Presentation.SetOutcome("That item is no longer available for wagon transfer.");
            return;
        }
        try
        {
            if (put)
                _ = State.Wagon.TransferToWagon(selection!, TransportAccess(), State.Inventory.Read().StoreRevision);
            else
                _ = State.Wagon.TransferFromWagon(selection!, TransportAccess(), State.Inventory.Read().StoreRevision);
            Presentation.SetOutcome(put ? "Item stored in wagon." : "Item taken from wagon.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            Presentation.SetOutcome(put ? "That item cannot be stored in the wagon." : "That item cannot be taken from the wagon.");
        }
    }
}
