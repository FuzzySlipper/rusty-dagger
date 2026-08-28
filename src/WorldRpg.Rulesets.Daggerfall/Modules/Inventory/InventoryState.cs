namespace WorldRpg.Rulesets.Daggerfall.Modules.Inventory;

internal sealed class InventoryState(IEnumerable<ItemStack> initialItems)
{
    private readonly List<ItemStack> _items = [.. initialItems];
    internal IReadOnlyList<ItemStack> Items => _items;
    internal void Add(ItemStack item) => _items.Add(item);
}

internal sealed record ItemStack(string ItemId, int Quantity);
