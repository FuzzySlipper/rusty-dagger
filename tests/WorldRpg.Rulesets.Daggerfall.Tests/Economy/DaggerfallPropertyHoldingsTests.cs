using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The player's houses and ship, and the goods of an interior's property containers, built without the session.</summary>
public sealed class DaggerfallPropertyHoldingsTests
{
    [Fact]
    public void A_house_drawn_for_sale_is_owned_entered_reached_for_storage_and_sold_through_the_bank()
    {
        using Holdings h = new();
        DaggerfallHouseOffer[] offers = h.Owner.HouseOffers();
        DaggerfallHouseOffer offer = Assert.IsType<DaggerfallHouseOffer>(offers.FirstOrDefault());
        Assert.Equal(offers.Select(value => value.StorageKey), h.Owner.HouseOffers().Select(value => value.StorageKey));
        string key = offer.StorageKey.Value;

        Assert.Empty(h.Owner.Offers(bank: false));
        Assert.All(h.Owner.Offers(bank: true), view => Assert.False(view.Owned || view.CanBuy || view.CanSell || view.CanEnter));
        Assert.Null(h.Owner.Trade(buy: true, offer, key));

        Assert.True(h.Property.AwardHouse(offer).Applied);
        DaggerfallHouseOffer owned = Assert.IsType<DaggerfallHouseOffer>(h.Owner.HouseOffer(key));
        DaggerfallPropertyOfferView listed = Assert.Single(h.Owner.Offers(bank: false), view => view.Key == key);
        Assert.True(listed.Owned && listed.Enterable);
        Assert.False(listed.CanSell || listed.CanEnter);
        Assert.True(Assert.Single(h.Owner.Offers(bank: true), view => view.Key == key).CanSell);
        Assert.Null(h.Owner.Entrance(owned));

        DaggerfallInteriorBuilding inside = new(owned.Identity.BlockX, owned.Identity.BlockY, owned.Identity.Building, 17, 0);
        Assert.True(h.Owner.OwnsInterior(inside));
        Assert.False(h.Owner.OwnsInterior(inside with { BlockX = inside.BlockX + 1 }));
        Assert.Null(h.Owner.AccessibleStorage());
        h.Profile = h.Profile with { Kind = DaggerfallWorldProfileKind.Interior };
        h.Standing = inside;
        Assert.Equal(DaggerfallPropertyStorageKey.ForHouse(owned.Identity), h.Owner.AccessibleStorage());

        DaggerfallPropertyTransactionResult sold = Assert.IsType<DaggerfallPropertyTransactionResult>(h.Owner.Trade(buy: false, owned, key));
        Assert.True(sold.Applied);
        Assert.False(h.Property.OwnsHouse(owned.Identity));
        Assert.Null(h.Owner.AccessibleStorage());
    }

    [Fact]
    public void Without_a_ship_there_is_no_deck_and_boarding_is_refused_by_the_property_gate()
    {
        using Holdings h = new();
        Assert.Null(h.Owner.ShipArrival());
        Assert.Null(h.Owner.OwnedShipProfile());
        Assert.False(h.Owner.OwnsInterior(new(0, 0, new("SHIP.RMB", 0), 24, 0)));

        DaggerfallTransportAccessContext context = DaggerfallTransportAccessContext.At(DaggerfallWorldProfileKind.Exterior,
            h.Player.Position, null, shipAccessAllowed: false);
        DaggerfallTransportActionResult refused = Assert.IsType<DaggerfallTransportActionResult>(h.Owner.BoardingRefusal(context, out _));
        Assert.False(refused.Applied);
        Assert.Equal(DaggerfallTransportRejection.MissingShip, refused.Rejection);
        Assert.False(h.Owner.Board(context, null, h.Profile).Applied);
    }

    [Fact]
    public void A_property_container_opens_private_outside_the_players_own_building_and_taking_from_it_is_a_reported_theft()
    {
        using Holdings h = new();
        using SocietyStage society = new();
        DaggerfallSiteBuildingSource shop = h.Site.Exterior!.Buildings.Values.First();
        DaggerfallInteriorBuilding? standing = new(shop.Id.BlockX, shop.Id.BlockY, shop.Source.Id, shop.Source.BuildingType, 0);
        bool owned = false;
        DaggerfallWorldProfileKey interior = DaggerfallWorldProfileIds.Interior(h.Site.Id, shop.Id);
        DaggerfallPropertyContainerPlacement chest = new("chest", new WorldPoint(1, 0, 1), [], [new WorldPoint(2, 0, 1), new WorldPoint(9, 0, 1)]);
        DaggerfallGroundContainers containers = new(h.Fixture.Containers, h.Fixture.Instances, h.Fixture.PlayerEntity,
            new DurableIdentityAllocator(DurableIdentityKind.Container, 500), interior);
        PlayerControlState player = new(new WorldPoint(3, 0, 1), 0f, 0f);
        DaggerfallPropertyLoot loot = new(containers, h.Fixture.Definitions, RandomMinimum.Create(), h.Fixture.UniqueItems,
            society.Crimes, society.Crime, h.Fixture.Instances, society.Actors, new(), society.Character, society.SkillUses, player, h.Sites,
            () => h.Fixture.Calendar, () => interior, () => [chest], () => standing, _ => owned);

        Assert.Null(loot.Open(12345));
        loot.Stock();
        DaggerfallGroundContainer placed = Assert.Single(containers.All.Values);
        Assert.Equal(new WorldPoint(2, 0, 1), loot.InteractionPoint(placed));
        owned = true;
        Assert.Equal(false, loot.Open(placed.Id));
        owned = false;
        Assert.Equal(true, loot.Open(placed.Id));
        standing = null;
        Assert.Equal(true, loot.Open(placed.Id));
        standing = new(shop.Id.BlockX, shop.Id.BlockY, shop.Source.Id, shop.Source.BuildingType, 0);

        InventoryStackId stack = InventoryStackId.Parse("chest.gold");
        h.Fixture.Containers.Seed(placed.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 3, Stack: stack)]);
        DaggerfallItemDefinition gold = h.Fixture.Definitions.RequireItem(new DaggerfallItemId("gold-piece"));
        h.Fixture.Instances.RegisterStack(DaggerfallItemOwner.Ground(placed.Id), stack,
            DaggerfallItemInstanceMetadata.Default(gold, DaggerfallItemOwner.Ground(placed.Id)));
        Assert.True(DaggerfallContainerSelection.TryParse(containers.Read(placed.Id)!, $"stack:{stack.Value}", 3, out InventoryContainerSelection? selection));
        InventoryContainerTransferReceipt taken = containers.Take(placed.Id, selection!, h.Fixture.Containers.Read(h.Fixture.PlayerEntity).StoreRevision);
        long attempts = society.Crime.OperationCount;
        loot.ObserveTake(placed, taken, "gold-piece", 3);
        long recorded = society.Crime.OperationCount;
        Assert.True(recorded > attempts);
        Assert.All(taken.Stacks, value => Assert.True(h.Fixture.Instances.RequireStack(DaggerfallItemOwner.Player, value.DestinationStack).Stolen));

        owned = true;
        loot.ObserveTake(placed, taken, "gold-piece", 3);
        Assert.Equal(recorded, society.Crime.OperationCount);
    }

    /// <summary>The holdings over a real property state, storage, bank and transport at a city of ten or more buildings that is no port.</summary>
    private sealed class Holdings : IDisposable
    {
        internal GuildMakerFixture Fixture { get; } = new();
        internal DaggerfallSiteRecord Site { get; }
        internal DaggerfallSiteContext Sites { get; }
        internal DaggerfallPropertyState Property { get; } = new(DaggerfallTuning.Defaults.Property);
        internal PlayerControlState Player { get; } = new(new WorldPoint(0, 0, 0), 0f, 0f);
        internal DaggerfallWorldProfileKey Profile { get; set; }
        internal DaggerfallInteriorBuilding? Standing { get; set; }
        internal DaggerfallPropertyHoldings Owner { get; }

        internal Holdings()
        {
            Site = Fixture.Definitions.Locations.Records.First(record => record.Kind == DaggerfallSiteKind.TownCity
                && record.Exterior is { PortTownAndUnknown: 0 } exterior
                && exterior.Buildings.Count >= 10
                && exterior.Buildings.Values.Count(building => building.ModelRadius is > 0 && building.Source.BuildingType is >= 17 and <= 20) >= 2);
            Sites = new(Fixture.Definitions.Locations, Site.Id, null, []);
            Sites.AdmitBuildingNames(RandomMinimum.Create(), Fixture.Definitions, DaggerfallBlocksContent.Read(TestContentFiles.ReadCopy(
                Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))));
            Profile = DaggerfallWorldProfileIds.Exterior(Site.Id);
            DaggerfallPropertyStorage storage = new(Fixture.Containers, Fixture.Instances, Fixture.Definitions,
                new DurableIdentityAllocator(DurableIdentityKind.Container, 100), Property, Fixture.PlayerEntity);
            Owner = new(Property, storage, new DaggerfallRegionalBankState(Fixture.Currency, Fixture.Inventory, Fixture.Instances),
                Fixture.Currency, new DaggerfallTransportPolicy(DaggerfallTuning.Defaults.Transport),
                new DaggerfallKnightlyOrderClaimRuntime(Fixture.GuildServices, new DaggerfallKnightlyOrderClaimState(), RandomMinimum.Create()),
                Sites, RandomMinimum.Create(), Player, () => Fixture.Calendar, () => Profile, () => null, () => Standing,
                (_, _) => false, (_, _) => true);
        }

        public void Dispose() => Fixture.Dispose();
    }
}
