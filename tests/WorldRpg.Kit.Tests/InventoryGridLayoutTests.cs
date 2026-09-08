using WorldRpg.Kit.Inventory;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class InventoryGridLayoutTests
{
    [Fact]
    public void Reconciliation_preserves_moves_removes_missing_keys_and_uses_free_slots()
    {
        InventoryGridLayout layout = new(3);
        layout.Reconcile(["a", "b"]);
        layout.Move("a", 2);
        layout.Reconcile(["a", "b"]);
        Assert.Equal(2, layout.Position("a"));
        layout.Reconcile(["a", "c"]);
        Assert.Null(layout.Position("b"));
        Assert.Equal(0, layout.Position("c"));
        layout.Move("a", 0);
        Assert.Equal(2, layout.Position("c"));
    }

    [Fact]
    public void Overflow_can_exchange_a_position_without_becoming_inventory_authority()
    {
        InventoryGridLayout layout = new(2);
        layout.Reconcile(["a", "b", "c"]);
        Assert.Null(layout.Position("c"));
        layout.Place("c", 1);
        layout.Reconcile(["a", "b", "c"]);
        Assert.Equal(1, layout.Position("c"));
        Assert.Null(layout.Position("b"));
        Assert.Equal(2, layout.Positions.Count);
    }
}
