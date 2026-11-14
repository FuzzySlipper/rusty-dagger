using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallInventoryUseServiceTests
{
    [Fact]
    public void Readable_book_use_retains_normalized_pages_without_consuming_its_stack()
    {
        DaggerfallDefinitions definitions = ReadDefinitions();
        using EntityDirectory entities = new();
        InventoryStore store = new();
        EntityId player = entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("player"));
        store.RegisterInventory(new InventoryState(player));
        InventoryComponent component = new(store, player);
        entities.Store.Add(player, component);
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId("template-277"));
        MechanicsInventoryCoordinator inventory = new(component, entities, new Dictionary<InventoryItemId, ItemDefinition>
        {
            [new(definition.Id.Value)] = DaggerActorFactory.ToManagedItem(definition),
        });
        InventoryStackId stack = InventoryStackId.Parse("retained-book");
        inventory.Grant(new(new InventoryItemId(definition.Id.Value), stack, 1));
        DaggerfallItemInstances instances = new();
        instances.RegisterStack(DaggerfallItemOwner.Player, stack,
            DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player) with { BookId = 59 });
        DaggerfallBookNotebook notebook = new(definitions, new DaggerfallTextResolver(definitions.Text));
        DaggerfallInventoryUseService use = new(inventory, definitions, instances, new DaggerfallUniqueItemAllocator(1_000),
            _ => throw new InvalidOperationException("A book reveals no location."), notebook: notebook);
        ulong revision = inventory.Read().StoreRevision;

        DaggerfallInventoryUseResult result = use.Use($"stack:{stack.Value}", revision);

        Assert.True(result.Applied, result.Message);
        Assert.NotNull(result.OpenedBook);
        Assert.Equal(59, result.OpenedBook!.BookId);
        Assert.Equal(1UL, inventory.Read().Stacks.Single(item => item.Id == stack).Quantity);
        Assert.Equal(result.OpenedBook.Pages[0], notebook.Read().Book!.Text);
        Assert.False(use.Use($"stack:{stack.Value}", revision + 1).Applied);
        Assert.Equal(1UL, inventory.Read().Stacks.Single(item => item.Id == stack).Quantity);
    }

    [Fact]
    public void A_map_that_reveals_a_location_is_consumed_and_tombstoned_and_one_that_reveals_nothing_is_kept()
    {
        DaggerfallDefinitions definitions = ReadDefinitions();
        using EntityDirectory entities = new();
        InventoryStore store = new();
        EntityId player = entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("player"));
        store.RegisterInventory(new InventoryState(player));
        store.RegisterEquipment(new EquipmentState(player));
        InventoryComponent component = new(store, player);
        EquipmentComponent equipmentComponent = new(store, player);
        entities.Store.Add(player, component);
        entities.Store.Add(player, equipmentComponent);
        DaggerfallItemDefinition map = definitions.RequireItem(new DaggerfallItemId("template-287"));
        ItemDefinition sourceMap = DaggerActorFactory.ToManagedItem(map);
        ItemDefinition managedMap = new(sourceMap.Id, ItemKind.Unique, 1);
        Dictionary<InventoryItemId, ItemDefinition> items = new()
        {
            [new InventoryItemId(map.Id.Value)] = managedMap,
        };
        MechanicsInventoryCoordinator inventory = new(component, entities, items);
        MechanicsEquipmentCoordinator equipment = new(component, equipmentComponent, entities, items,
            new Dictionary<WorldRpg.Kit.Inventory.EquipmentSlotId, EquipmentSlotDefinition>());
        DaggerfallUniqueItemAllocator unique = new(1_000);
        DurableIdentityReference identity = unique.AllocateReference();
        _ = equipment.Materialize(identity, new InventoryItemId(map.Id.Value));
        DaggerfallItemInstances instances = new();
        instances.RegisterDefaultUnique(identity.Value, map, DaggerfallItemOwner.Player);
        DaggerfallMapReveal reveal = new(null, "You have already discovered every location in this region.");
        List<string> asked = [];
        DaggerfallInventoryUseService use = new(inventory, definitions, instances, unique, key => { asked.Add(key); return reveal; });
        string key = $"unique:{inventory.Read().UniqueItems.Single().Entity.Value}";

        DaggerfallInventoryUseResult refused = use.Use(key, inventory.Read().StoreRevision);
        Assert.False(refused.Applied);
        Assert.Equal(reveal.Refusal, refused.Message);
        Assert.Single(inventory.Read().UniqueItems);

        reveal = new("Lost Keep", null);
        DaggerfallInventoryUseResult result = use.Use(key, inventory.Read().StoreRevision);

        Assert.True(result.Applied, result.Message);
        Assert.Equal("Map reveals Lost Keep.", result.Message);
        // The reveal is drawn by the map's durable identity, not its runtime entity.
        Assert.Equal([$"unique:{identity.Value}", $"unique:{identity.Value}"], asked);
        Assert.Empty(inventory.Read().UniqueItems);
        Assert.Contains(identity.Value, unique.RemovedEntityIds);
        Assert.Contains(unique.CaptureState().Kinds.Single(kind => kind.Kind == DurableIdentityKind.Item).Removed, value => value == identity.Value);
        Assert.False(use.Use("unique:999999", inventory.Read().StoreRevision).Applied);
        Assert.False(use.Use("stack:missing", inventory.Read().StoreRevision).Applied);
    }

    private static DaggerfallDefinitions ReadDefinitions()
    {
        return TestPayload.Definitions;
    }
}
