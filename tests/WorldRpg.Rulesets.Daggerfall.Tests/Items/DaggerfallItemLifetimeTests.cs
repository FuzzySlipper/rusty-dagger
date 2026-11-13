using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The end of an item's life, by the lifetime owner built alone over the Engine's inventories.</summary>
public sealed class DaggerfallItemLifetimeTests
{
    [Fact]
    public void A_retired_unique_item_leaves_equipment_the_inventory_its_meaning_and_its_identity()
    {
        using ItemHoldersFixture f = new();
        var (item, id) = f.Weapon();
        EquipmentMoveResult worn = new DaggerfallEquipmentMoves(f.Inventory, f.Equipment, TestPayload.Definitions, itemInstances: f.Instances)
            .MoveToSlot(item, new("right-hand"));
        Assert.Equal(EquipmentMoveOutcome.Applied, worn.Outcome);

        f.Lifetime.DestroyUnique(id);

        Assert.Empty(f.Equipment.Read().Assignments);
        Assert.Empty(f.Inventory.Read().UniqueItems);
        Assert.False(f.Instances.ContainsUnique(id));
        Assert.False(f.Containers.Entities.TryResolve(new DurableIdentityReference(DurableIdentityKind.Item, id), out _));
        Assert.Contains(id, f.UniqueItems.RemovedEntityIds);
    }

    [Fact]
    public void Conjured_items_are_retired_when_their_minute_comes_and_others_stay()
    {
        using ItemHoldersFixture f = new();
        var (_, conjured) = f.Weapon();
        var (_, ordinary) = f.Weapon();
        f.Instances.ReplaceUnique(conjured, f.Instances.RequireUnique(conjured) with { Conjuration = new("cast.4", 10) });

        f.Lifetime.ExpireConjured(9);
        Assert.True(f.Instances.ContainsUnique(conjured));

        f.Lifetime.ExpireConjured(10);
        Assert.False(f.Instances.ContainsUnique(conjured));
        Assert.True(f.Instances.ContainsUnique(ordinary));
        Assert.Single(f.Inventory.Read().UniqueItems);
    }

    [Fact]
    public void A_consumed_stack_is_spent_whole_from_its_owner_and_forgotten()
    {
        using ItemHoldersFixture f = new();
        EntityId wagon = f.Container(DaggerfallItemContainers.Wagon, 3);
        InventoryStackId stack = f.Gold(DaggerfallItemOwner.Wagon(3), wagon, "wagon.gold", 40);

        f.Lifetime.ConsumeStack(DaggerfallItemOwner.Wagon(3), stack);

        Assert.Empty(f.Containers.Read(wagon).Stacks);
        Assert.False(f.Instances.ContainsStack(DaggerfallItemOwner.Wagon(3), stack));
    }

    [Fact]
    public void A_quest_end_takes_back_only_that_quest_s_items_the_player_carries()
    {
        using ItemHoldersFixture f = new();
        EntityId wagon = f.Container(DaggerfallItemContainers.Wagon, 3);
        var (_, carried) = f.Weapon("quest-1");
        var (_, otherQuest) = f.Weapon("quest-2");
        var (stowedItem, stowed) = f.Weapon("quest-1");
        f.Containers.Transfer(f.Player, wagon, new(stowedItem.Definition, 1, UniqueEntityId: stowedItem.EntityId));
        f.Instances.ReplaceUnique(stowed, f.Instances.RequireUnique(stowed) with { Owner = DaggerfallItemOwner.Wagon(3) });
        InventoryStackId questGold = f.Gold(DaggerfallItemOwner.Player, f.Player, "quest.gold", 5, "quest-1");
        InventoryStackId ownGold = f.Gold(DaggerfallItemOwner.Player, f.Player, "own.gold", 7);

        f.Lifetime.RemoveCarriedQuestItems("quest-1");

        Assert.False(f.Instances.ContainsUnique(carried));
        Assert.True(f.Instances.ContainsUnique(otherQuest));
        Assert.True(f.Instances.ContainsUnique(stowed));
        Assert.False(f.Instances.ContainsStack(DaggerfallItemOwner.Player, questGold));
        Assert.Equal(7UL, Assert.Single(f.Inventory.Read().Stacks, value => value.Id == ownGold).Quantity);
    }
}
