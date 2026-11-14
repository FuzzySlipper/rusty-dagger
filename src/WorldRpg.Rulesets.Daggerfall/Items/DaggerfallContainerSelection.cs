using System.Globalization;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Reads an inventory panel's item key (<c>unique:ENTITY</c> or <c>stack:ID</c>) and amount as a selection of what
/// the source container holds, for a transfer between containers such as the wagon or a property's storage.
/// </summary>
internal static class DaggerfallContainerSelection
{
    internal static bool TryParse(InventoryView source, string key, ulong? amount,
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
