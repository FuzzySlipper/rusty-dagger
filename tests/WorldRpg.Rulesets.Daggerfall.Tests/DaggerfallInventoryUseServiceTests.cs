using System.Reflection;
using Rusty.Engine;
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
        DaggerfallSiteContext sites = new(definitions.Locations, definitions.Locations.Records[0].Id, null, []);
        DaggerfallInventoryUseService use = new(inventory, definitions, instances, new DaggerfallUniqueItemAllocator(1_000), sites, MinimumRandom.Create(), notebook: notebook);
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
    public void Map_discovery_selects_an_undiscovered_location_then_consumes_and_tombstones_the_unique_item()
    {
        DaggerfallDefinitions definitions = ReadDefinitions();
        DaggerfallSiteRecord active = definitions.Locations.Records.First(record => definitions.Locations.Records.Count(candidate => candidate.Id.Region == record.Id.Region && !candidate.Discovered) > 1);
        DaggerfallSiteContext sites = new(definitions.Locations, active.Id, null, []);
        DaggerfallSiteRecord expected = sites.Records.First(record => record.Id.Region == active.Id.Region && !sites.IsDiscovered(record.Id));
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
        DaggerfallInventoryUseService use = new(inventory, definitions, instances, unique, sites, MinimumRandom.Create());

        DaggerfallInventoryUseResult result = use.Use($"unique:{inventory.Read().UniqueItems.Single().Entity.Value}", inventory.Read().StoreRevision);

        Assert.True(result.Applied, result.Message);
        Assert.True(sites.IsDiscovered(expected.Id));
        Assert.Empty(inventory.Read().UniqueItems);
        Assert.Contains(identity.Value, unique.RemovedEntityIds);
        Assert.Contains(unique.CaptureState().Kinds.Single(kind => kind.Kind == DurableIdentityKind.Item).Removed, value => value == identity.Value);
        Assert.False(use.Use("unique:999999", inventory.Read().StoreRevision).Applied);
        Assert.False(use.Use("stack:missing", inventory.Read().StoreRevision).Applied);
    }

    private static DaggerfallDefinitions ReadDefinitions()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
        return DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(directory!.FullName, "content/worldrpg/payloads/daggerfall.base.json")));
    }

    private class MinimumRandom : DispatchProxy
    {
        internal static IRandomService Create() => DispatchProxy.Create<IRandomService, MinimumRandom>();
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }
}
