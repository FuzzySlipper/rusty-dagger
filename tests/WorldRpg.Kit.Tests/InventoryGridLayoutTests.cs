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

    [Fact]
    public void Arranging_lays_the_named_keys_out_first_and_leaves_what_does_not_fit_as_overflow()
    {
        InventoryGridLayout layout = new(3);
        layout.Reconcile(["a", "b", "c", "d"]);
        Assert.Null(layout.Position("d"));
        ulong revision = layout.Revision;
        layout.Arrange(["d", "b", "a"]);
        Assert.Equal((0, 1, 2), (layout.Position("d"), layout.Position("b"), layout.Position("a")));
        Assert.Null(layout.Position("c"));
        Assert.True(layout.Revision > revision);
        layout.Arrange(["a"]);
        Assert.Equal((0, 1, 2), (layout.Position("a"), layout.Position("d"), layout.Position("b")));
    }
}
