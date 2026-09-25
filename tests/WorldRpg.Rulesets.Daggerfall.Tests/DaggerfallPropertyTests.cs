using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Property;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallPropertyTests
{
    [Fact]
    public void House_offers_keep_source_identity_and_donor_eligibility_and_price()
    {
        DaggerfallSiteId town = new(17, 4);
        DaggerfallHouseCandidate houseForSale = Candidate(town, "TOWN00.RMB", 1, 2.5f, blockX: 0, blockY: 0);
        DaggerfallHouseCandidate houseOne = Candidate(town, "TOWN00.RMB", 2, 1.25f, buildingType: 17, blockX: 0, blockY: 0);
        DaggerfallHouseCandidate houseFive = Candidate(town, "TOWN00.RMB", 3, 1.25f, buildingType: 21, blockX: 0, blockY: 0);
        DaggerfallHouseCandidate questHouse = Candidate(town, "TOWN00.RMB", 4, 1.25f, isQuestBuilding: true, blockX: 0, blockY: 0);
        DaggerfallHouseCandidate dungeonHouse = Candidate(new DaggerfallSiteId(17, 179), "DUNGEON.RMB", 0, 4f,
            siteKind: DaggerfallSiteKind.DungeonLabyrinth, blockX: 0, blockY: 0);

        IReadOnlyList<DaggerfallHouseOffer> offers = DaggerfallPropertyPolicy.HousesForSale(
            [houseFive, dungeonHouse, questHouse, houseOne, houseForSale]);

        Assert.Equal(2, offers.Count);
        DaggerfallHouseOffer first = Assert.Single(offers, offer => offer.Identity.Building.Index == 1);
        Assert.Equal(3_200UL, first.Price);
        Assert.Equal(2_720UL, first.SalePrice);
        Assert.Equal("house/17/4/TOWN00.RMB/0/0/1", first.StorageKey.Value);
        Assert.Equal(houseOne.Identity, Assert.Single(offers, offer => offer.Identity.Building.Index == 2).Candidate.Identity);
    }

    [Fact]
    public void Ship_offers_require_a_port_and_retain_donor_prices_and_arrival_anchors()
    {
        Assert.Empty(DaggerfallPropertyPolicy.ShipsForSale(atPortTown: false, region: 17));

        IReadOnlyList<DaggerfallShipOffer> offers = DaggerfallPropertyPolicy.ShipsForSale(atPortTown: true, region: 17);

        Assert.Equal(2, offers.Count);
        DaggerfallShipOffer small = Assert.Single(offers, offer => offer.Type == DaggerfallShipType.Small);
        DaggerfallShipOffer large = Assert.Single(offers, offer => offer.Type == DaggerfallShipType.Large);
        Assert.Equal(100_000UL, small.Price);
        Assert.Equal(85_000UL, small.SalePrice);
        Assert.Equal(new DaggerfallShipArrivalAnchor(2, 2), small.Arrival);
        Assert.Equal(200_000UL, large.Price);
        Assert.Equal(170_000UL, large.SalePrice);
        Assert.Equal(new DaggerfallShipArrivalAnchor(5, 5), large.Arrival);
    }

    [Fact]
    public void Purchase_uses_the_bank_settlement_and_refusal_does_not_materialize_ownership()
    {
        DaggerfallHouseOffer offer = Assert.Single(DaggerfallPropertyPolicy.HousesForSale(
            [Candidate(new DaggerfallSiteId(17, 4), "TOWN00.RMB", 1, 2f, blockX: 0, blockY: 0)]));
        DaggerfallPropertyState state = new();
        Settlement settlement = new(accept: false);

        DaggerfallPropertyTransactionResult refused = state.PurchaseHouse(offer, settlement);

        Assert.False(refused.Applied);
        Assert.Equal(DaggerfallPropertyTransactionDenial.InsufficientFunds, refused.Denial);
        Assert.False(state.OwnsHouse(offer.Identity));
        Assert.Equal(offer.Price, Assert.Single(settlement.Requests).Amount);

        settlement.Accept = true;
        DaggerfallPropertyTransactionResult purchased = state.PurchaseHouse(offer, settlement);

        Assert.True(purchased.Applied);
        Assert.True(state.OwnsHouse(offer.Identity));
        Assert.Equal(DaggerfallPropertyTransactionDenial.AlreadyOwned,
            state.PurchaseHouse(offer, settlement).Denial);
    }

    [Fact]
    public void Property_save_restores_identity_and_sale_retains_the_bound_kit_container()
    {
        DaggerfallHouseOffer offer = Assert.Single(DaggerfallPropertyPolicy.HousesForSale(
            [Candidate(new DaggerfallSiteId(17, 4), "TOWN00.RMB", 1, 2f, blockX: 0, blockY: 0)]));
        DaggerfallPropertyState state = new();
        Settlement settlement = new(accept: true);
        Assert.True(state.PurchaseHouse(offer, settlement).Applied);

        state.BindStorage(offer.StorageKey, 701);
        DaggerfallPropertyState restored = new(state.Capture());
        Assert.True(restored.OwnsHouse(offer.Identity));
        Assert.True(restored.TryGetStorageContainer(offer.StorageKey, out long restoredContainer));
        Assert.Equal(701, restoredContainer);

        DaggerfallPropertyTransactionResult sold = restored.SellHouse(offer, settlement);

        Assert.True(sold.Applied);
        Assert.False(restored.OwnsHouse(offer.Identity));
        Assert.True(restored.TryGetStorageContainer(offer.StorageKey, out long retainedContainer));
        Assert.Equal(701, retainedContainer);
        Assert.True(restored.StorageAfter(DaggerfallPropertyStorageEvent.Unload).PreserveContents);
        Assert.True(restored.StorageAfter(DaggerfallPropertyStorageEvent.Sale).PreserveContents);
        Assert.Equal(DaggerfallPropertyBankOperation.Sale, settlement.Requests[^1].Operation);
        Assert.Equal(offer.SalePrice, settlement.Requests[^1].Amount);
    }

    [Fact]
    public void Access_requires_the_exact_house_and_admitted_ship_context()
    {
        DaggerfallSiteId town = new(17, 4);
        DaggerfallHouseOffer house = Assert.Single(DaggerfallPropertyPolicy.HousesForSale(
            [Candidate(town, "TOWN00.RMB", 1, 2f, blockX: 0, blockY: 0)]));
        DaggerfallPropertyState state = new();
        Settlement settlement = new(accept: true);
        Assert.True(state.PurchaseHouse(house, settlement).Applied);

        Assert.True(state.HouseAccess(new DaggerfallHouseEntryContext(town, house.Identity.Building, 0, 0)).Allowed);
        Assert.Equal(DaggerfallPropertyAccessDenial.WrongBuilding,
            state.HouseAccess(new DaggerfallHouseEntryContext(town, new DaggerfallRmbBuildingId("TOWN00.RMB", 2), 0, 0)).Denial);
        Assert.Equal(DaggerfallPropertyAccessDenial.WrongSite,
            DaggerfallPropertyPolicy.HouseAccess(house.Identity,
                new DaggerfallHouseEntryContext(new DaggerfallSiteId(17, 5), house.Identity.Building, 0, 0)).Denial);

        DaggerfallShipOffer ship = Assert.Single(DaggerfallPropertyPolicy.ShipsForSale(true, 17),
            offer => offer.Type == DaggerfallShipType.Small);
        Assert.True(state.PurchaseShip(ship, settlement).Applied);
        Assert.Equal(DaggerfallPropertyAccessDenial.ShipUnavailableAtSite,
            state.ShipBoarding(new DaggerfallShipAccessContext(false, false, false)).Denial);
        Assert.Equal(DaggerfallPropertyAccessDenial.Indoor,
            state.ShipBoarding(new DaggerfallShipAccessContext(true, false, true)).Denial);
        Assert.True(state.ShipBoarding(new DaggerfallShipAccessContext(false, false, true)).Allowed);
        Assert.True(state.ShipStorage(onShip: true).Allowed);
        Assert.Equal(DaggerfallPropertyAccessDenial.NotOnShip, state.ShipStorage(onShip: false).Denial);
    }

    [Fact]
    public void House_identity_keeps_same_rmb_slot_distinct_across_block_placements()
    {
        DaggerfallSiteId town = new(17, 4);
        DaggerfallHouseCandidate west = Candidate(town, "TOWN00.RMB", 1, 2f, blockX: 3, blockY: 4);
        DaggerfallHouseCandidate east = Candidate(town, "TOWN00.RMB", 1, 2f, blockX: 4, blockY: 4);

        Assert.NotEqual(west.Identity, east.Identity);
        Assert.NotEqual(DaggerfallPropertyStorageKey.ForHouse(west.Identity),
            DaggerfallPropertyStorageKey.ForHouse(east.Identity));
    }

    [Fact]
    public void Property_storage_round_trips_stack_and_unique_contents_through_kit()
    {
        using StorageFixture fixture = new();
        DaggerfallSiteId site = new(17, 4);
        DaggerfallHouseOffer offer = Assert.Single(DaggerfallPropertyPolicy.HousesForSale(
            [Candidate(site, "TOWN00.RMB", 1, 2f, blockX: 0, blockY: 0)]));
        DaggerfallPropertyState state = new();
        Assert.True(state.PurchaseHouse(offer, new Settlement(accept: true)).Applied);
        DaggerfallPropertyStorage storage = fixture.CreateStorage(state);
        EntityId propertyOwner = storage.Ensure(offer.StorageKey);
        long propertyId = state.TryGetStorageContainer(offer.StorageKey, out long bound) ? bound
            : throw new InvalidOperationException("Property storage did not bind a durable container.");
        DaggerfallItemOwner itemOwner = PropertyOwner(propertyId);

        DaggerfallItemDefinition gold = fixture.Definitions.RequireItem(new DaggerfallItemId("gold-piece"));
        InventoryStackId goldStack = InventoryStackId.Parse("property.gold");
        fixture.Containers.Seed(propertyOwner,
            [new InventoryContainerSeed(new InventoryItemId(gold.Id.Value), 17, Stack: goldStack)]);
        fixture.Instances.RegisterDefaultStack(itemOwner,
            new InventoryStack(goldStack, ItemDefinitionId.Parse(gold.Id.Value), 17), gold);

        DaggerfallItemDefinition unique = fixture.Definitions.Items.Values
            .Concat(fixture.Definitions.TemplateItems.Values)
            .First(definition => DaggerActorFactory.ToManagedItem(definition).Kind == ItemKind.Unique);
        const ulong uniqueId = 9001;
        fixture.Containers.Seed(propertyOwner,
            [new InventoryContainerSeed(new InventoryItemId(unique.Id.Value),
                UniqueItem: new DurableIdentityReference(DurableIdentityKind.Item, uniqueId))]);
        fixture.Instances.RegisterDefaultUnique(uniqueId, unique, itemOwner);

        DaggerfallPropertySave saved = state.Capture(storage.Capture);
        DaggerfallPropertyStorageSave storageSave = Assert.Single(saved.Storage);
        Assert.Equal(17UL, Assert.Single(storageSave.Inventory.Stacks).Quantity);
        Assert.Equal(uniqueId, Assert.Single(storageSave.Inventory.UniqueItems).EntityId);

        using StorageFixture restoredFixture = new(fixture.Identities.CaptureState());
        DaggerfallPropertyState restoredState = new(saved);
        DaggerfallPropertyStorage restoredStorage = restoredFixture.CreateStorage(restoredState);
        restoredStorage.Restore(saved);
        InventoryView restored = restoredStorage.Read(offer.StorageKey);
        Assert.Equal(17UL, Assert.Single(restored.Stacks).Quantity);
        Assert.Equal(uniqueId, restoredFixture.Entities.IdentityOf(Assert.Single(restored.UniqueItems).Entity).Value);
        Assert.Equal(storageSave.Inventory.Stacks[0].Metadata,
            restoredStorage.Capture(offer.StorageKey).Stacks[0].Metadata);
        Assert.Equal(storageSave.Inventory.UniqueItems[0].Metadata,
            restoredStorage.Capture(offer.StorageKey).UniqueItems[0].Metadata);
    }

    [Fact]
    public void Property_stack_transfer_uses_the_property_identity_when_player_has_no_matching_stack()
    {
        using StorageFixture fixture = new();
        DaggerfallSiteId site = new(17, 4);
        DaggerfallHouseOffer offer = Assert.Single(DaggerfallPropertyPolicy.HousesForSale(
            [Candidate(site, "TOWN00.RMB", 1, 2f, blockX: 0, blockY: 0)]));
        DaggerfallPropertyState state = new();
        Assert.True(state.PurchaseHouse(offer, new Settlement(accept: true)).Applied);
        DaggerfallPropertyStorage storage = fixture.CreateStorage(state);
        _ = storage.Ensure(offer.StorageKey);
        long propertyId = state.TryGetStorageContainer(offer.StorageKey, out long bound)
            ? bound
            : throw new InvalidOperationException("Property storage did not bind a durable container.");

        DaggerfallItemDefinition gold = fixture.Definitions.RequireItem(new DaggerfallItemId("gold-piece"));
        InventoryStackId playerStack = InventoryStackId.Parse("player.gold");
        fixture.Containers.Seed(fixture.Player,
            [new InventoryContainerSeed(new InventoryItemId(gold.Id.Value), 17, Stack: playerStack)]);
        fixture.Instances.RegisterDefaultStack(DaggerfallItemOwner.Player,
            new InventoryStack(playerStack, ItemDefinitionId.Parse(gold.Id.Value), 17), gold);

        InventoryContainerTransferReceipt toProperty = storage.TransferTo(offer.StorageKey,
            new(new InventoryItemId(gold.Id.Value), 17, Stack: playerStack), fixture.Store.Revision);
        InventoryStackId propertyStack = Assert.Single(storage.Read(offer.StorageKey).Stacks).Id;
        Assert.Equal(DaggerfallItemOwner.Property(propertyId),
            fixture.Instances.RequireStack(DaggerfallItemOwner.Property(propertyId), propertyStack).Owner);
        Assert.Equal(playerStack, toProperty.Stacks.Single().SourceStack);

        InventoryContainerTransferReceipt toPlayer = storage.TransferFrom(offer.StorageKey,
            new(new InventoryItemId(gold.Id.Value), 17, Stack: propertyStack), fixture.Store.Revision);
        Assert.Empty(storage.Read(offer.StorageKey).Stacks);
        Assert.Equal(17UL, Assert.Single(fixture.Containers.Read(fixture.Player).Stacks).Quantity);
        Assert.Equal(DaggerfallItemOwner.Player,
            fixture.Instances.RequireStack(DaggerfallItemOwner.Player,
                toPlayer.Stacks.Single().DestinationStack).Owner);
        Assert.Equal(17UL, fixture.Containers.Read(fixture.Player).Stacks.Single().Quantity);
    }

    private static DaggerfallItemOwner PropertyOwner(long id) => DaggerfallItemOwner.Property(id);

    private static DaggerfallHouseCandidate Candidate(
        DaggerfallSiteId site,
        string sourceKey,
        int index,
        float modelRadius,
        int buildingType = 1,
        DaggerfallSiteKind siteKind = DaggerfallSiteKind.TownCity,
        bool isQuestBuilding = false,
        int blockX = 0,
        int blockY = 0) => new(
            site,
            new DaggerfallRmbBuildingId(sourceKey, index),
            buildingType,
            siteKind,
            modelRadius,
            isQuestBuilding,
            blockX,
            blockY);

    private sealed class Settlement(bool accept) : IDaggerfallPropertyBankSettlement
    {
        internal bool Accept { get; set; } = accept;
        internal List<DaggerfallPropertyBankRequest> Requests { get; } = [];

        public DaggerfallPropertyPaymentResult Apply(DaggerfallPropertyBankRequest request)
        {
            Requests.Add(request);
            return Accept
                ? DaggerfallPropertyPaymentResult.Accepted()
                : DaggerfallPropertyPaymentResult.Refused(DaggerfallPropertyPaymentDenial.InsufficientFunds);
        }
    }

    private sealed class StorageFixture : IDisposable
    {
        internal DaggerfallDefinitions Definitions { get; }
        internal EntityDirectory Entities { get; } = new();
        internal InventoryStore Store { get; } = new();
        internal EntityId Player { get; }
        internal MechanicsInventoryContainerCoordinator Containers { get; }
        internal DaggerfallItemInstances Instances { get; } = new();
        internal DurableIdentityAllocator Identities { get; }
        private readonly IReadOnlyDictionary<InventoryItemId, ItemDefinition> _items;

        internal StorageFixture(DurableIdentityState? restoredIdentities = null)
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
                "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
            Definitions = TestPayload.Definitions;
            _items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            Player = Entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1),
                new EntityTypeId("test.player"));
            Store.RegisterInventory(new InventoryState(Player));
            Entities.Store.Add(Player, new InventoryComponent(Store, Player));
            Containers = new MechanicsInventoryContainerCoordinator(Store, Entities, _items);
            Identities = restoredIdentities is null
                ? new DurableIdentityAllocator(DurableIdentityKind.Container, 100)
                : DurableIdentityAllocator.Restore(restoredIdentities);
        }

        internal DaggerfallPropertyStorage CreateStorage(DaggerfallPropertyState state) =>
            new(Containers, Instances, Definitions, Identities, state, Player);

        public void Dispose() => Entities.Dispose();
    }
}
