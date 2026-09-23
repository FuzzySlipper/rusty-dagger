using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTransportTests
{
    [Fact]
    public void Toggle_mount_prefers_horse_and_interior_transition_removes_mount()
    {
        using Fixture fixture = new();
        fixture.AddCart(20);
        fixture.AddHorse(21);
        DaggerfallTransportPolicy policy = new();

        DaggerfallTransportActionResult mounted = policy.ToggleMount(fixture.ReadPlayer(), new());

        Assert.True(mounted.Applied);
        Assert.Equal(DaggerfallTransportMode.Horse, policy.Mode);
        Assert.False(policy.CanRun);
        Assert.Equal(128, policy.TravelModifier());

        policy.ForceFootOnInteriorTransition();

        Assert.Equal(DaggerfallTransportMode.Foot, policy.Mode);
        Assert.True(policy.IsOnFoot);
        Assert.True(policy.CanRun);
    }

    [Fact]
    public void Mounts_are_rejected_indoors_and_missing_owned_items_are_reported()
    {
        using Fixture fixture = new();
        DaggerfallTransportPolicy policy = new();

        DaggerfallTransportActionResult missing = policy.ToggleMount(fixture.ReadPlayer(), new());
        Assert.Equal(DaggerfallTransportRejection.MissingHorse, missing.Rejection);

        fixture.AddHorse(22);
        DaggerfallTransportActionResult indoor = policy.SelectMount(DaggerfallTransportMode.Horse, fixture.ReadPlayer(), new(IsIndoor: true));
        Assert.Equal(DaggerfallTransportRejection.Indoor, indoor.Rejection);
        Assert.Equal(DaggerfallTransportMode.Foot, policy.Mode);

        policy.SelectMount(DaggerfallTransportMode.Horse, fixture.ReadPlayer(), new());
        fixture.DestroyUnique(22);
        policy.Reconcile(fixture.ReadPlayer(), new());
        Assert.Equal(DaggerfallTransportMode.Foot, policy.Mode);
    }

    [Fact]
    public void Ship_boarding_retains_return_pose_in_current_state_and_leaving_returns_it()
    {
        DaggerfallTransportPose pose = new(new WorldPoint(12, 3, -8), 1.2f, -0.3f);
        DaggerfallTransportPolicy policy = new();

        DaggerfallTransportActionResult boarded = policy.BoardShip(true, new(), pose);

        Assert.True(boarded.Applied);
        Assert.True(policy.OnShip);
        Assert.Equal(DaggerfallTransportMode.Foot, policy.Mode);
        Assert.Equal(51, policy.OceanMinutesPerMapPixel());
        DaggerfallTransportSave saved = policy.Capture();

        DaggerfallTransportPolicy restored = new();
        restored.Restore(saved);
        DaggerfallTransportActionResult left = restored.LeaveShip();

        Assert.True(left.Applied);
        Assert.Equal(pose, left.Relocation);
        Assert.Equal(DaggerfallTransportMode.Foot, restored.Mode);
        Assert.False(restored.OnShip);
        Assert.Equal(DaggerfallTransportSave.Foot, restored.Capture());
    }

    [Fact]
    public void Donor_speed_and_travel_constants_are_exposed_as_one_policy()
    {
        using Fixture fixture = new();
        fixture.AddHorse(23);
        DaggerfallTransportPolicy policy = new();

        Assert.Equal(256, policy.TravelModifier());
        Assert.Equal(255, policy.OceanMinutesPerMapPixel());
        Assert.Equal(150d / 39.5d, policy.MovementSpeed(0, 39.5d), 8);
        Assert.True(policy.SelectMount(DaggerfallTransportMode.Horse, fixture.ReadPlayer(), new()).Applied);
        Assert.Equal(375d / 39.5d, policy.MovementSpeed(0, 39.5d), 8);
    }

    [Fact]
    public void Wagon_transfer_uses_engine_container_and_preserves_daggerfall_owner_metadata()
    {
        using Fixture fixture = new();
        fixture.AddCart(30);
        InventoryStackId gold = fixture.AddGold(40, 10);
        DaggerfallWagonStorage wagon = fixture.CreateWagonStorage();

        InventoryContainerTransferReceipt toWagon = wagon.TransferToWagon(
            new(new InventoryItemId("gold-piece"), 4, Stack: gold), new(), fixture.Store.Revision);

        Assert.Equal(6UL, fixture.ReadPlayer().Stacks.Single(stack => stack.Id == gold).Quantity);
        Assert.Equal(4UL, Assert.Single(wagon.Read()!.Stacks).Quantity);
        Assert.Equal(DaggerfallItemOwner.Wagon(100), fixture.Instances.RequireStack(DaggerfallItemOwner.Wagon(100),
            toWagon.Stacks.Single().DestinationStack).Owner);
        Assert.Equal(100, wagon.Id);

        InventoryStackId wagonStack = Assert.Single(wagon.Read()!.Stacks).Id;
        _ = wagon.TransferFromWagon(new(new InventoryItemId("gold-piece"), 4, Stack: wagonStack), new(), fixture.Store.Revision);

        Assert.Equal(10UL, fixture.ReadPlayer().Stacks.Single(stack => stack.Id == gold).Quantity);
        Assert.Empty(wagon.Read()!.Stacks);
        Assert.Throws<InvalidOperationException>(() => fixture.Instances.RequireStack(DaggerfallItemOwner.Wagon(100), wagonStack));
    }

    [Fact]
    public void Wagon_requires_cart_and_dungeon_exit_proximity_and_blocks_transport_items()
    {
        using Fixture fixture = new();
        DaggerfallWagonStorage wagon = fixture.CreateWagonStorage();
        Assert.False(wagon.CanAccess(fixture.ReadPlayer(), new()));

        fixture.AddCart(31);
        Assert.False(wagon.CanAccess(fixture.ReadPlayer(), new(IsDungeon: true, DungeonExitDistance: 6f)));
        Assert.True(wagon.CanAccess(fixture.ReadPlayer(), new(IsDungeon: true, DungeonExitDistance: 4.9f)));

        Assert.Throws<InvalidOperationException>(() => wagon.TransferToWagon(
            new(new InventoryItemId(DaggerfallTransportPolicy.CartItemId), 1, UniqueEntityId: 31), new(), fixture.Store.Revision));
        Assert.False(wagon.Exists);
    }

    [Fact]
    public void Wagon_capacity_and_save_restore_preserve_contents()
    {
        using Fixture fixture = new();
        fixture.AddCart(32);
        InventoryStackId gold = fixture.AddGold(41, 300_001);
        DaggerfallWagonStorage wagon = fixture.CreateWagonStorage();

        Assert.Throws<InvalidOperationException>(() => wagon.TransferToWagon(
            new(new InventoryItemId("gold-piece"), 300_001, Stack: gold), new(), fixture.Store.Revision));
        Assert.False(wagon.Exists);

        fixture.DestroyStack(gold);
        InventoryStackId retained = fixture.AddGold(42, 40);
        _ = wagon.TransferToWagon(new(new InventoryItemId("gold-piece"), 40, Stack: retained), new(), fixture.Store.Revision);
        DaggerfallWagonSave saved = wagon.Capture()!;
        DurableIdentityState identities = fixture.Identities.CaptureState();

        using Fixture restoredFixture = new(identities);
        DaggerfallWagonStorage restored = restoredFixture.CreateWagonStorage();
        restored.Restore(saved);

        Assert.Equal(100, restored.Id);
        Assert.Equal(40UL, Assert.Single(restored.Read()!.Stacks).Quantity);
        Assert.Equal(DaggerfallItemOwner.Wagon(100), restoredFixture.Instances.RequireStack(
            DaggerfallItemOwner.Wagon(100), Assert.Single(restored.Read()!.Stacks).Id).Owner);
    }

    [Fact]
    public void Wagon_unique_transfer_and_save_use_durable_item_identity_when_runtime_id_differs()
    {
        using Fixture fixture = new();
        fixture.AddCart(50);
        fixture.AddUnique(51, "iron-dagger");
        DaggerfallWagonStorage wagon = fixture.CreateWagonStorage();
        Rusty.Engine.Mechanics.UniqueInventoryItem playerItem = fixture.ReadPlayer().UniqueItems.Single(item => item.Definition.Value == "iron-dagger");

        Assert.NotEqual(51UL, playerItem.Entity.Value);
        _ = wagon.TransferToWagon(new(new InventoryItemId("iron-dagger"), 1, UniqueEntityId: playerItem.Entity.Value),
            new(), fixture.Store.Revision);

        Assert.Equal(DaggerfallItemOwner.Wagon(100), fixture.Instances.RequireUnique(51).Owner);
        DaggerfallUniqueSave saved = Assert.Single(wagon.Capture()!.Inventory.UniqueItems);
        Assert.Equal(51UL, saved.EntityId);
        DaggerfallWagonSave wagonSave = wagon.Capture()!;
        DurableIdentityState identitySave = fixture.Identities.CaptureState();
        using Fixture reloadedFixture = new(identitySave);
        DaggerfallWagonStorage reloadedWagon = reloadedFixture.CreateWagonStorage();
        reloadedWagon.Restore(wagonSave);
        Rusty.Engine.Mechanics.UniqueInventoryItem restoredItem = Assert.Single(reloadedWagon.Read()!.UniqueItems);
        Assert.Equal(51UL, reloadedFixture.Entities.IdentityOf(restoredItem.Entity).Value);
        Assert.Equal(DaggerfallItemOwner.Wagon(100), reloadedFixture.Instances.RequireUnique(51).Owner);

        Rusty.Engine.Mechanics.UniqueInventoryItem wagonItem = Assert.Single(wagon.Read()!.UniqueItems);
        _ = wagon.TransferFromWagon(new(new InventoryItemId("iron-dagger"), 1, UniqueEntityId: wagonItem.Entity.Value),
            new(), fixture.Store.Revision);
        Assert.Equal(DaggerfallItemOwner.Player, fixture.Instances.RequireUnique(51).Owner);
    }

    [Fact]
    public void Transport_projection_is_semantic_and_contains_wagon_access_state()
    {
        using Fixture fixture = new();
        fixture.AddCart(43);
        DaggerfallWagonStorage wagon = fixture.CreateWagonStorage();

        DaggerfallTransportPresentation projection = DaggerfallTransportProjection.Read(new(), fixture.ReadPlayer(), new(), false, wagon);

        Assert.Equal(DaggerfallTransportMode.Foot, projection.Mode);
        Assert.Equal("horse", projection.Options.Single(option => option.Mode == DaggerfallTransportMode.Horse).Id);
        Assert.True(projection.Options.Single(option => option.Mode == DaggerfallTransportMode.Cart).Available);
        Assert.True(projection.Wagon.Accessible);
        Assert.Equal(300_000, projection.Wagon.CapacityClassicUnits);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly DaggerfallDefinitions Definitions;
        internal readonly EntityDirectory Entities = new();
        internal readonly InventoryStore Store = new();
        internal readonly EntityId Player;
        internal readonly MechanicsInventoryContainerCoordinator Containers;
        internal readonly MechanicsInventoryCoordinator Inventory;
        internal readonly DaggerfallItemInstances Instances = new();
        internal readonly DurableIdentityAllocator Identities;
        private readonly IReadOnlyDictionary<InventoryItemId, ItemDefinition> _items;

        internal Fixture(DurableIdentityState? restoredIdentities = null)
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
            Definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(directory!.FullName, "content/worldrpg/payloads/daggerfall.base.json")));
            _items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            Player = Entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("test.player"));
            Store.RegisterInventory(new InventoryState(Player));
            InventoryComponent component = new(Store, Player);
            Entities.Store.Add(Player, component);
            Inventory = new(component, Entities, _items);
            Containers = new(Store, Entities, _items);
            Identities = restoredIdentities is null
                ? new DurableIdentityAllocator(DurableIdentityKind.Container, 100)
                : DurableIdentityAllocator.Restore(restoredIdentities);
        }

        internal InventoryView ReadPlayer() => Containers.Read(Player);

        internal void AddHorse(ulong id) => AddUnique(id, DaggerfallTransportPolicy.HorseItemId);
        internal void AddCart(ulong id) => AddUnique(id, DaggerfallTransportPolicy.CartItemId);
        internal void AddUnique(ulong id, string definition) => AddUniqueItem(id, definition);

        internal InventoryStackId AddGold(ulong ordinal, ulong quantity)
        {
            InventoryStackId stack = InventoryStackId.Parse($"fixture.gold.{ordinal}");
            Inventory.Grant(new(new InventoryItemId("gold-piece"), stack, quantity));
            Instances.RegisterDefaultStack(DaggerfallItemOwner.Player, new InventoryStack(stack, ItemDefinitionId.Parse("gold-piece"), quantity),
                Definitions.RequireItem(new DaggerfallItemId("gold-piece")));
            return stack;
        }

        internal void DestroyUnique(ulong id) => Store.DestroyUnique(Entities.Resolve(new DurableIdentityReference(DurableIdentityKind.Item, id)));

        internal void DestroyStack(InventoryStackId stack) => Store.Consume(Player, stack, Store.Read(Player).Stacks.Single(value => value.Id == stack).Quantity);

        internal DaggerfallWagonStorage CreateWagonStorage() => new(Containers, Instances, Definitions, Player, Identities);

        private void AddUniqueItem(ulong id, string definition)
        {
            Containers.Seed(Player, [new InventoryContainerSeed(new InventoryItemId(definition), UniqueItem: new DurableIdentityReference(DurableIdentityKind.Item, id))]);
            Instances.RegisterDefaultUnique(id, Definitions.RequireItem(new DaggerfallItemId(definition)), DaggerfallItemOwner.Player);
        }

        public void Dispose() => Entities.Dispose();
    }
}
