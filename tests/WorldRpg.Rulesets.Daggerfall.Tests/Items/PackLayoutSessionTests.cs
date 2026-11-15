using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The pack's cells across the session's own save and load, which is where they were lost (#10825).</summary>
public sealed class PackLayoutSessionTests
{
    [Fact]
    public void A_loaded_pack_keeps_every_cell_the_player_left_its_items_in()
    {
        using ConditionSessionFixture f = new();
        DaggerfallSession s = f.Session;
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        // Uniques before stacks, so the grant order differs from the stacks-first order a fresh read hands out.
        foreach (string material in new[] { "iron", "steel", "silver" })
            factory.Materialize(factory.Create(new("Weapons", $"pack-layout.sword.{material}", DaggerfallItemOwner.Player, TemplateIndex: 113, Material: material)),
                s.State.Inventory, s.State.ItemInstances, unique: s.UniqueItemAllocator.AllocateReference());
        factory.Materialize(factory.Create(new("Jewellery", "pack-layout.ring", DaggerfallItemOwner.Player, TemplateIndex: 135)),
            s.State.Inventory, s.State.ItemInstances, unique: s.UniqueItemAllocator.AllocateReference());
        factory.Materialize(factory.Create(new("Gems", "pack-layout.rubies", DaggerfallItemOwner.Player, Quantity: 3, TemplateIndex: 0)),
            s.State.Inventory, s.State.ItemInstances, InventoryStackId.Parse("pack-layout.rubies"));
        factory.Materialize(factory.Create(new("Currency", "pack-layout.gold", DaggerfallItemOwner.Player, Quantity: 500, TemplateIndex: 276)),
            s.State.Inventory, s.State.ItemInstances, InventoryStackId.Parse("pack-layout.gold"));
        s.EquipmentMoves.ReconcileLayout();
        string[] placed = [.. Cells(s).Select(cell => cell.Key)];
        Assert.True(placed.Length >= 8, $"The pack holds {placed.Length} items.");

        // The player reverses the pack and then drags its first item to a far cell, leaving a gap at the front.
        s.EquipmentMoves.ArrangeGrid(placed.Reverse());
        Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveStackToGrid(InventoryStackId.Parse("pack-layout.gold"), 0).Outcome);
        Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveStackToGrid(InventoryStackId.Parse("pack-layout.gold"), 40).Outcome);
        (int Cell, string Item)[] before = Durable(s);

        using DaggerfallSession restored = f.Restore(s.CaptureSave());

        Assert.Equal(before, Durable(restored));
        using DaggerfallSession again = f.Restore(restored.CaptureSave());
        Assert.Equal(before, Durable(again));
    }

    /// <summary>Each placed cell and the layout key in it, ordered by cell.</summary>
    private static (int Cell, string Key)[] Cells(DaggerfallSession s)
    {
        s.EquipmentMoves.ReconcileLayout();
        InventoryView read = s.EquipmentMoves.ReadInventory();
        return [.. read.Stacks.Select(stack => DaggerfallEquipmentMoves.LayoutKey(stack.Id))
            .Concat(read.UniqueItems.Select(item => DaggerfallEquipmentMoves.LayoutKey(item.Entity.Value)))
            .Where(key => s.EquipmentMoves.GridPosition(key) is not null)
            .Select(key => (s.EquipmentMoves.GridPosition(key)!.Value, key)).OrderBy(cell => cell.Item1)];
    }

    /// <summary>Each placed cell and the item in it, named by what lasts across a load: a stack's id, a unique's durable identity.</summary>
    private static (int Cell, string Item)[] Durable(DaggerfallSession s)
    {
        s.EquipmentMoves.ReconcileLayout();
        InventoryView read = s.EquipmentMoves.ReadInventory();
        return [.. read.Stacks.Select(stack => (Key: DaggerfallEquipmentMoves.LayoutKey(stack.Id), Item: $"stack:{stack.Id.Value}"))
            .Concat(read.UniqueItems.Select(item => (Key: DaggerfallEquipmentMoves.LayoutKey(item.Entity.Value),
                Item: $"item:{s.State.Actors.Entities.IdentityOf(item.Entity).Value}")))
            .Where(entry => s.EquipmentMoves.GridPosition(entry.Key) is not null)
            .Select(entry => (s.EquipmentMoves.GridPosition(entry.Key)!.Value, entry.Item)).OrderBy(cell => cell.Item1)];
    }
}
