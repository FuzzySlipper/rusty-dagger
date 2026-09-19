using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class PursuitAndCorpseLootTests
{
    [Fact]
    public void Pursuit_memory_keeps_the_previous_state_for_a_ruleset_transition_fact()
    {
        PursuitMemoryComponent memory = new();

        Assert.Equal(PursuitState.Idle, memory.TransitionTo(PursuitState.Chase));
        Assert.Equal(PursuitState.Chase, memory.TransitionTo(PursuitState.Attack));
        Assert.Equal(PursuitState.Attack, memory.State);
    }

    [Fact]
    public void Corpse_transfer_keeps_one_actor_owned_container_until_its_contents_are_exhausted()
    {
        EntityDirectory entities = new();
        InventoryStore inventory = new();
        MechanicsInventoryContainerCoordinator containers = new(inventory, entities,
            new Dictionary<InventoryItemId, ItemDefinition>
            {
                [new InventoryItemId("gold")] = new(ItemDefinitionId.Parse("gold"), ItemKind.Fungible, 10),
            });
        EntityId recipient = entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("player"));
        containers.RegisterOwner(recipient);
        CorpseLootCoordinator loot = new(entities, containers);
        CorpseLootComponent corpse = loot.Create(
            new DurableIdentityReference(DurableIdentityKind.Container, 2000),
            new EntityTypeId("corpse"),
            originatingSequence: 7,
            [new InventoryContainerSeed(new InventoryItemId("gold"), 2)]);

        CorpseLootTransferResult first = loot.Transfer(corpse, recipient,
            new InventoryContainerSelection(new InventoryItemId("gold"), 1), inventory.Revision);

        Assert.False(first.IsEmpty);
        Assert.True(corpse.IsInteractable);
        Assert.Equal(1UL, Assert.Single(containers.Read(recipient).Stacks).Quantity);
        Assert.Equal(1UL, Assert.Single(loot.Read(corpse)!.Stacks).Quantity);

        CorpseLootTransferResult last = loot.TransferAll(corpse, recipient);

        Assert.True(last.IsEmpty);
        Assert.False(corpse.IsInteractable);
        Assert.Equal(2UL, Assert.Single(containers.Read(recipient).Stacks).Quantity);
        Assert.Empty(loot.Read(corpse)!.Stacks);
        Assert.Throws<InvalidOperationException>(() => loot.TransferAll(corpse, recipient));
    }
}
