using System.Text;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallInteriorBuildingTests
{
    [Fact]
    public void Reads_placed_identity_and_access_facts_without_interpreting_profile_names()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
            {"world":{"interiorBuilding":{"blockX":1,"blockY":2,"sourceKey":"TEST.RMB","buildingIndex":3,"buildingType":17,"factionId":41}}}
            """);
        DaggerfallInteriorBuilding building = Assert.IsType<DaggerfallInteriorBuilding>(DaggerfallInteriorBuilding.Read(json, DaggerfallWorldProfileKind.Interior));
        Assert.Equal(new DaggerfallInteriorBuilding(1, 2, new("TEST.RMB", 3), 17, 41), building);
        DaggerfallBlocksSnapshot blocks = new(new Dictionary<DaggerfallRmbBuildingId, DaggerfallRmbBuildingSource>
        {
            [building.Building] = new(building.Building, 17, 41, 0),
        });
        building.ValidateAgainst(blocks);
        Assert.Throws<ArgumentException>(() => (building with { FactionId = 40 }).ValidateAgainst(blocks));
        Assert.Throws<ArgumentException>(() => DaggerfallInteriorBuilding.Read(json, DaggerfallWorldProfileKind.Exterior));
        Assert.NotEqual(building, building with { BlockX = 2 });
    }

    [Fact]
    public void Missing_building_fact_does_not_invent_access()
    {
        Assert.Null(DaggerfallInteriorBuilding.Read(Encoding.UTF8.GetBytes("{\"world\":{}}"), DaggerfallWorldProfileKind.Interior));
    }
}
