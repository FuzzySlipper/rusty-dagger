using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Who holds an item, read both ways by the ownership owner built alone over the Engine's inventories.</summary>
public sealed class DaggerfallItemOwnershipTests
{
    [Fact]
    public void Each_owner_scope_names_the_live_inventory_that_holds_its_items()
    {
        using ItemHoldersFixture f = new();
        EntityId wagon = f.Container(DaggerfallItemContainers.Wagon, 7);
        EntityId ground = f.Container(DaggerfallItemContainers.Ground, 8);

        Assert.Equal(f.Player, f.Ownership.Entity(DaggerfallItemOwner.Player));
        Assert.Equal(wagon, f.Ownership.Entity(DaggerfallItemOwner.Wagon(7)));
        Assert.Equal(ground, f.Ownership.Entity(DaggerfallItemOwner.Ground(8)));
        Assert.Equal(f.Corpse, f.Ownership.Entity(DaggerfallItemOwner.Corpse(42)));
        Assert.Equal([42L], f.CorpseRequests);
        Assert.Throws<InvalidOperationException>(() => f.Ownership.Entity(DaggerfallItemOwner.Merchant(9)));
    }

    [Theory]
    [InlineData("wagon")]
    [InlineData("ground")]
    [InlineData("property")]
    [InlineData("merchant")]
    [InlineData("repair-custody")]
    [InlineData("quest")]
    public void A_resident_item_belongs_to_the_owner_its_container_stands_for(string scope)
    {
        using ItemHoldersFixture f = new();
        (EntityTypeId type, DaggerfallItemOwner owner) = scope switch
        {
            "wagon" => (DaggerfallItemContainers.Wagon, DaggerfallItemOwner.Wagon(11)),
            "ground" => (DaggerfallItemContainers.Ground, DaggerfallItemOwner.Ground(11)),
            "property" => (DaggerfallItemContainers.Property, DaggerfallItemOwner.Property(11)),
            "merchant" => (DaggerfallItemContainers.Merchant, DaggerfallItemOwner.Merchant(11)),
            "repair-custody" => (DaggerfallItemContainers.RepairCustody, DaggerfallItemOwner.RepairCustody(11)),
            _ => (DaggerfallItemContainers.QuestCustody, DaggerfallItemOwner.Quest(11)),
        };
        EntityId container = f.Container(type, 11);
        var (item, id) = f.Weapon();
        Assert.Equal(DaggerfallItemOwner.Player, f.Ownership.ResidentOwner(id));

        f.Containers.Transfer(f.Player, container, new(item.Definition, 1, UniqueEntityId: item.EntityId));

        Assert.Equal(owner, f.Ownership.ResidentOwner(id));
    }

    [Fact]
    public void An_item_no_live_container_holds_answers_nothing()
    {
        using ItemHoldersFixture f = new();
        Assert.Null(f.Ownership.ResidentOwner(999_999));
        EntityId unknown = f.Container(new EntityTypeId("test.unknown-container"), 12);
        var (item, id) = f.Weapon();
        f.Containers.Transfer(f.Player, unknown, new(item.Definition, 1, UniqueEntityId: item.EntityId));
        Assert.Null(f.Ownership.ResidentOwner(id));
    }
}

/// <summary>
/// The Engine's inventories and the item owners over them, without the session: a player with a pack and
/// equipment, containers made on request, item instances and the unique allocator.
/// </summary>
internal sealed class ItemHoldersFixture : IDisposable
{
    private readonly ActorsState _actors = new();
    private readonly DaggerfallDefinitions _definitions = TestPayload.Definitions;

    internal EntityId Player { get; }
    internal EntityId Corpse { get; }
    internal List<long> CorpseRequests { get; } = [];
    internal InventoryStore Store { get; } = new();
    internal MechanicsInventoryCoordinator Inventory { get; }
    internal MechanicsEquipmentCoordinator Equipment { get; }
    internal MechanicsInventoryContainerCoordinator Containers { get; }
    internal DaggerfallItemInstances Instances { get; } = new();
    internal DaggerfallUniqueItemAllocator UniqueItems { get; } = new(5_000);
    internal DaggerfallItemOwnership Ownership { get; }
    internal DaggerfallItemLifetime Lifetime { get; }

    internal ItemHoldersFixture()
    {
        var items = _definitions.Items.Values.Concat(_definitions.TemplateItems.Values)
            .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
        var slots = _definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
        DaggerfallActorDefinition actor = _definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(actor,
            DaggerfallPlayerVitals.Initial(actor.Stats, _definitions.Catalogs.RequireCareer("class00")));
        Player = _actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health").Actor.Entity;
        Store.RegisterInventory(new InventoryState(Player));
        Store.RegisterEquipment(new EquipmentState(Player));
        InventoryComponent pack = new(Store, Player);
        EquipmentComponent worn = new(Store, Player);
        _actors.Store.Add(Player, pack);
        _actors.Store.Add(Player, worn);
        Inventory = new(pack, _actors.Entities, items);
        Equipment = new(pack, worn, _actors.Entities, items, slots);
        Containers = new(Store, _actors.Entities, items);
        Corpse = Container(new EntityTypeId("test.corpse"), 900);
        Ownership = new(_actors, Store, actorId => { CorpseRequests.Add(actorId); return Corpse; });
        Lifetime = new(_actors.Entities, Store, Instances, UniqueItems, Ownership);
    }

    internal EntityId Container(EntityTypeId type, ulong id)
    {
        EntityId owner = _actors.Entities.Create(new DurableIdentityReference(DurableIdentityKind.Container, id), type);
        Containers.RegisterOwner(owner);
        return owner;
    }

    internal (UniqueItem Item, ulong Id) Weapon(string? questId = null)
    {
        DaggerfallItemDefinition definition = _definitions.RequireItem(new("template-121-daedric"));
        DurableIdentityReference id = UniqueItems.AllocateReference();
        UniqueItem item = Equipment.Materialize(id, new(definition.Id.Value));
        Instances.RegisterDefaultUnique(id.Value, definition, DaggerfallItemOwner.Player);
        if (questId is not null) Instances.ReplaceUnique(id.Value, Instances.RequireUnique(id.Value) with { QuestId = questId, QuestItemSymbol = "_item_" });
        return (item, id.Value);
    }

    /// <summary>A stack of gold pieces held by <paramref name="owner"/> in <paramref name="container"/>.</summary>
    internal InventoryStackId Gold(DaggerfallItemOwner owner, EntityId container, string key, ulong quantity, string? questId = null)
    {
        DaggerfallItemFactory factory = new(_definitions, RandomMinimum.Create());
        DaggerfallCreatedItem created = factory.Create(new("Currency", key, owner, Quantity: quantity, TemplateIndex: 276));
        InventoryStackId stack = InventoryStackId.Parse(key);
        Containers.Seed(container, [new(created.Item, quantity, Stack: stack)]);
        Instances.RegisterStack(owner, stack, questId is null ? created.Metadata : created.Metadata with { QuestId = questId, QuestItemSymbol = "_item_" });
        return stack;
    }

    public void Dispose() => _actors.Dispose();
}
