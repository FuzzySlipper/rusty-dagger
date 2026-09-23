using System.Numerics;
using System.Globalization;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallTransportAccessContext TransportAccess()
    {
        bool dungeon = _activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon;
        float? exitDistance = null;
        if (dungeon && State.PlayerControl.Position is WorldPoint position
            && _siteProjection.Inputs.DungeonMap is { } map)
        {
            Vector3 here = position.ToVector();
            foreach (DaggerfallDungeonMapMarker marker in map.Markers)
            {
                if (marker.Kind != DaggerfallDungeonMapMarkerKind.Entrance) continue;
                float distance = Vector3.Distance(here, marker.Position.ToVector());
                exitDistance = exitDistance is float current ? Math.Min(current, distance) : distance;
            }
        }
        return new DaggerfallTransportAccessContext(
            IsIndoor: _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior,
            IsDungeon: dungeon,
            DungeonExitDistance: exitDistance,
            ShipAccessAllowed: false);
    }

    private void ChangeTransport(DaggerfallPlayerUiAction action)
    {
        DaggerfallTransportActionResult result = action.Action switch
        {
            "transport-toggle" => State.Transport.ToggleMount(State.Inventory.Read(), TransportAccess()),
            "transport-select" => State.Transport.SelectMount(action.Mode switch
            {
                "foot" => DaggerfallTransportMode.Foot,
                "horse" => DaggerfallTransportMode.Horse,
                "cart" => DaggerfallTransportMode.Cart,
                _ => throw new InvalidOperationException("Parsed transport mode is not supported."),
            }, State.Inventory.Read(), TransportAccess()),
            "transport-leave-ship" => State.Transport.LeaveShip(),
            _ => throw new InvalidOperationException("Parsed transport action is not supported."),
        };
        if (result.Applied)
        {
            _climbing.Detach();
            if (result.Relocation is { } returnPose)
                ApplyRelocation(returnPose.Position, returnPose.YawRadians, returnPose.PitchRadians);
        }
        Presentation.SetOutcome(result.Message);
    }

    private void ChangeWagon(DaggerfallPlayerUiAction action)
    {
        if (action.Revision != _inventoryUi.Read().Revision)
        {
            Presentation.SetOutcome("Inventory changed. Choose the item again.");
            return;
        }
        bool put = action.Action == "wagon-put";
        InventoryView? source = put ? State.Inventory.Read() : State.Wagon.Read();
        if (source is null || action.Item is null || !TryWagonSelection(source, action.Item, action.Amount, out InventoryContainerSelection? selection))
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
            Presentation.SetOutcome(error.Message);
        }
    }

    private static bool TryWagonSelection(InventoryView source, string key, ulong? amount,
        out InventoryContainerSelection? selection)
    {
        selection = null;
        if (key.StartsWith("unique:", StringComparison.Ordinal)
            && ulong.TryParse(key.AsSpan("unique:".Length), NumberStyles.None, CultureInfo.InvariantCulture, out ulong entity))
        {
            if (amount is not null and not 1UL) return false;
            Rusty.Engine.Mechanics.UniqueInventoryItem? item = source.UniqueItems.Where(value => value.Entity.Value == entity)
                .Select(value => (Rusty.Engine.Mechanics.UniqueInventoryItem?)value).SingleOrDefault();
            if (item is null) return false;
            selection = new(new InventoryItemId(item.Value.Definition.Value), 1, UniqueEntityId: entity);
            return true;
        }
        if (key.StartsWith("stack:", StringComparison.Ordinal))
        {
            string id = key["stack:".Length..];
            InventoryStack? stack = source.Stacks.Where(value => StringComparer.Ordinal.Equals(value.Id.Value, id))
                .Select(value => (InventoryStack?)value).SingleOrDefault();
            if (stack is null) return false;
            ulong quantity = amount ?? stack.Value.Quantity;
            if (quantity == 0 || quantity > stack.Value.Quantity) return false;
            selection = new(new InventoryItemId(stack.Value.Definition.Value), quantity, Stack: stack.Value.Id);
            return true;
        }
        return false;
    }
}
