using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The tavern's meals and its rooms, built over real service transactions, lodging and currency, without the session.</summary>
public sealed class DaggerfallTavernTests
{
    private static readonly DaggerfallInteriorBuilding Tavern = new(0, 0, new("TEST.RMB", 0), DaggerfallLodgingState.TavernBuildingType, 0);

    [Fact]
    public void A_tavern_keeper_serves_the_menu_only_in_a_tavern_and_a_meal_is_paid_then_fills_the_player()
    {
        using GuildMakerFixture f = new();
        DaggerfallTavernService tavern = new(f.Services, f.Stats, () => f.Calendar);
        DaggerfallNpc keeper = f.Person(DaggerfallNpcServiceFacts.TavernService);

        Assert.Null(tavern.ReadAt(null, () => Tavern));
        Assert.Null(tavern.ReadAt(keeper, () => Tavern with { BuildingType = 7 }));
        Assert.Null(tavern.ReadAt(keeper with { Services = ["talk"] }, () => throw new InvalidOperationException("No building is read for a person who keeps no tavern.")));
        DaggerfallTavernView view = Assert.IsType<DaggerfallTavernView>(tavern.ReadAt(keeper, () => Tavern));
        Assert.True(view.Hungry);

        DaggerfallTavernOption ale = view.Menu.Single(option => option.Id == "ale");
        f.GrantGold(10);
        Assert.Equal("The tavern menu changed. Check the current price.", tavern.BuyMeal("page", keeper, "ale", ale.Price + 1));
        Assert.Equal("You enjoy the ale.", tavern.BuyMeal("page", keeper, "ale", ale.Price));
        Assert.Equal(f.Calendar.ToAbsoluteSeconds() / 60, tavern.LastMealMinute);
        Assert.Equal("You are not hungry.", tavern.BuyMeal("page", keeper, "ale", ale.Price));
    }

    [Fact]
    public void A_room_is_quoted_for_the_chosen_days_and_booked_at_the_quoted_price_with_carried_gold()
    {
        using GuildMakerFixture f = new();
        (DaggerfallSiteId site, DaggerfallSiteBuildingSource building) = f.Definitions.Locations.Records
            .SelectMany(record => record.Exterior!.Buildings.Values
                .Where(value => value.Source.BuildingType == DaggerfallLodgingState.TavernBuildingType)
                .Select(value => (record.Id, value)))
            .First();
        DaggerfallSiteContext sites = new(f.Definitions.Locations, site, null, []);
        sites.AdmitBuildingNames(RandomMinimum.Create(), f.Definitions, DaggerfallBlocksContent.Read(TestContentFiles.ReadCopy(
            Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))));
        DaggerfallInteriorBuilding inn = new(building.Id.BlockX, building.Id.BlockY, building.Source.Id,
            DaggerfallLodgingState.TavernBuildingType, building.Source.FactionId);
        DaggerfallWorldProfileKey profile = new(site, DaggerfallWorldProfileKind.Interior, "inn");
        DaggerfallInteriorBuilding? standing = inn;
        DaggerfallLodgingState lodging = new();
        DaggerfallRoomRental rooms = new(lodging, sites,
            new DaggerfallGuildMembershipPolicy(f.Social, _ => 100, DaggerfallConcreteGuildCatalog.AllMembershipPolicies),
            f.Stats, f.Currency, () => f.Calendar, () => profile, () => standing);

        DaggerfallLodgingView first = Assert.IsType<DaggerfallLodgingView>(rooms.View);
        Assert.Equal((DaggerfallLodgingState.Key(site, building.Id), 1, 0L, true), (first.Key, first.Days, first.RemainingHours, first.CanBook));
        Assert.Equal(DaggerfallLodgingState.Quote(1, f.Calendar, building.Quality,
            Math.Clamp(f.Stats.GetStat(StatId.Parse("mercantile")).ValueInt, 0, 100),
            Math.Clamp(f.Stats.GetStat(StatId.Parse("personality")).ValueInt, 0, 100), free: false), first.Price);

        Assert.Equal("This tavern is no longer available.", rooms.Choose("elsewhere", 3, book: false, null));
        Assert.Null(rooms.Choose(first.Key, 3, book: false, null));
        DaggerfallLodgingView three = rooms.View!;
        Assert.Equal(3, three.Days);
        Assert.Equal("The room price changed. Please check the new quote.", rooms.Choose(first.Key, 3, book: true, three.Price + 1));
        Assert.Equal("You do not have enough gold to rent this room.", rooms.Choose(first.Key, 3, book: true, three.Price));
        f.GrantGold(three.Price);
        Assert.Equal("Room booked for 3 more day(s).", rooms.Choose(first.Key, 3, book: true, three.Price));
        Assert.Equal(72L, rooms.View!.RemainingHours);

        standing = inn with { BuildingType = 7 };
        Assert.Null(rooms.View);
        profile = profile with { Kind = DaggerfallWorldProfileKind.Exterior };
        standing = inn;
        Assert.Null(rooms.View);
    }
}
