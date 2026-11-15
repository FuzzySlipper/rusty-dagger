using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallConcreteGuildPolicyTests
{
    [Fact]
    public void Holy_place_uses_the_admitted_interior_building_not_the_location_kind()
    {
        static DaggerfallInteriorBuilding Building(int type, int faction) =>
            new(0, 0, new DaggerfallRmbBuildingId("TEST.RMB", 0), type, faction);

        Assert.True(DaggerfallConcreteGuildPolicy.HolyPlace(DaggerfallWorldProfileKind.Interior, Building(14, 0)));
        Assert.True(DaggerfallConcreteGuildPolicy.HolyPlace(DaggerfallWorldProfileKind.Interior, Building(18, 849)));
        Assert.False(DaggerfallConcreteGuildPolicy.HolyPlace(DaggerfallWorldProfileKind.Interior, Building(18, 0)));
        Assert.False(DaggerfallConcreteGuildPolicy.HolyPlace(DaggerfallWorldProfileKind.Exterior, Building(14, 0)));
        Assert.False(DaggerfallConcreteGuildPolicy.HolyPlace(DaggerfallWorldProfileKind.Dungeon, Building(14, 0)));
        Assert.False(DaggerfallConcreteGuildPolicy.HolyPlace(DaggerfallWorldProfileKind.Interior, null));
    }
}
