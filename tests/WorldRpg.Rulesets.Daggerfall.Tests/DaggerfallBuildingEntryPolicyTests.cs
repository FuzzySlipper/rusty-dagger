using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The donor's PlayerActivate.BuildingIsUnlocked, IsBuildingOpen and GetBuildingLockValue, case by case.</summary>
public sealed class DaggerfallBuildingEntryPolicyTests
{
    private const int GeneralStore = 9, Bank = 3, Library = 10, GuildHall = 11, Temple = 14, Tavern = 15, Palace = 16;
    private const int House1 = 17, House2 = 18, House4 = 20, House5 = 21, Town23 = 23, Ship = 24, HouseForSale = 1;

    private static DaggerfallBuildingEntry At(int type, int hour, int quality = 10, int faction = 0, int holiday = 0,
        bool owned = false, bool quest = false, bool hallAnytime = false, bool member = false, bool ship = false) =>
        DaggerfallBuildingEntryPolicy.Evaluate(new(type, faction, quality, hour, holiday, owned, quest, hallAnytime, member, ship));

    [Theory]
    [InlineData(Tavern, 0, true)]
    [InlineData(Tavern, 23, true)]
    [InlineData(Temple, 3, true)]
    [InlineData(GeneralStore, 5, false)]
    [InlineData(GeneralStore, 6, true)]
    [InlineData(GeneralStore, 22, true)]
    [InlineData(GeneralStore, 23, false)]
    [InlineData(Bank, 8, true)]
    [InlineData(Bank, 15, false)]
    [InlineData(Library, 9, true)]
    [InlineData(Palace, 9, false)]
    [InlineData(Palace, 10, true)]
    [InlineData(Palace, 16, false)]
    [InlineData(GuildHall, 10, false)]
    [InlineData(GuildHall, 11, true)]
    [InlineData(House1, 12, false)]
    [InlineData(House2, 5, false)]
    [InlineData(House2, 6, true)]
    [InlineData(House4, 17, true)]
    [InlineData(House4, 18, false)]
    [InlineData(House5, 12, false)]
    [InlineData(Town23, 12, false)]
    [InlineData(Ship, 12, false)]
    public void Opening_hours_decide_ordinary_entry(int type, int hour, bool unlocked)
    {
        DaggerfallBuildingEntry entry = At(type, hour, quality: 13);
        Assert.Equal(unlocked, entry.Unlocked);
        Assert.Equal(unlocked ? 0 : 6, entry.LockValue);
    }

    [Fact]
    public void A_locked_building_resists_at_its_quality_over_two()
    {
        Assert.Equal(10, At(House1, 12, quality: 20).LockValue);
        Assert.Equal(3, At(House1, 12, quality: 7).LockValue);
        // A quality-one building is still locked: the least lock the door owner can hold.
        Assert.Equal(1, At(House1, 12, quality: 1).LockValue);
    }

    [Fact]
    public void Shops_close_on_suns_rest_but_taverns_and_temples_do_not()
    {
        Assert.False(At(GeneralStore, 12, holiday: DaggerfallBuildingEntryPolicy.SunsRestHoliday).Unlocked);
        Assert.True(At(GeneralStore, 12, holiday: 30).Unlocked);
        Assert.True(At(Tavern, 12, holiday: DaggerfallBuildingEntryPolicy.SunsRestHoliday).Unlocked);
        Assert.True(At(Bank, 12, holiday: DaggerfallBuildingEntryPolicy.SunsRestHoliday).Unlocked);
    }

    [Fact]
    public void Guild_halls_open_at_any_hour_to_members_with_hall_access()
    {
        Assert.False(At(GuildHall, 2, faction: 40).Unlocked);
        Assert.True(At(GuildHall, 2, faction: 40, hallAnytime: true).Unlocked);
        // Membership alone is not hall access; it is the guild's own rank rule.
        Assert.False(At(GuildHall, 2, faction: 40, member: true).Unlocked);
    }

    [Fact]
    public void Guild_houses_admit_only_their_members_at_any_hour()
    {
        Assert.False(At(House2, 12, faction: 42).Unlocked);
        Assert.True(At(House2, 2, faction: 42, member: true).Unlocked);
        Assert.True(At(House2, 2, faction: 108, member: true).Unlocked);
    }

    [Fact]
    public void Owned_houses_quest_residences_and_an_owned_ship_are_always_open()
    {
        Assert.True(At(House1, 2, owned: true).Unlocked);
        Assert.True(At(House1, 2, quest: true).Unlocked);
        Assert.True(At(House5, 2, quest: true).Unlocked);
        // An active quest opens residences only.
        Assert.False(At(GeneralStore, 2, quest: true).Unlocked);
        Assert.True(At(Ship, 2, ship: true).Unlocked);
    }

    [Fact]
    public void Closed_stores_and_guilds_carry_their_hours_and_nothing_else_does()
    {
        Assert.Equal(new DaggerfallBuildingClosedNotice(false, 6, 23), At(GeneralStore, 2).Closed);
        Assert.Equal(new DaggerfallBuildingClosedNotice(true, 11, 23), At(GuildHall, 2).Closed);
        Assert.Equal(new DaggerfallBuildingClosedNotice(false, 8, 15), At(Bank, 2).Closed);
        Assert.Null(At(GeneralStore, 12).Closed);
        Assert.Null(At(HouseForSale, 2).Closed);
        Assert.Null(At(Palace, 2).Closed);
        Assert.Null(At(House1, 2).Closed);
    }
}
