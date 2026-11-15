using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallAmbientZonesTests
{
    private static readonly DaggerfallAmbientZone[] Zones =
    [
        new("castle", DaggerfallAmbientZoneKind.Castle, new(0, 0, 0), new(10, 4, 10)),
        new("special", DaggerfallAmbientZoneKind.SpecialArea, new(20, 0, 20), new(30, 4, 30)),
    ];

    [Fact]
    public void Only_a_dungeons_castle_zone_holds_the_player_inside_a_castle()
    {
        Assert.True(DaggerfallAmbientZones.InsideCastle(DaggerfallWorldProfileKind.Dungeon, Zones, new WorldPoint(5, 2, 5)));
        // A special area is not a castle, and outside every zone is outside the castle.
        Assert.False(DaggerfallAmbientZones.InsideCastle(DaggerfallWorldProfileKind.Dungeon, Zones, new WorldPoint(25, 2, 25)));
        Assert.False(DaggerfallAmbientZones.InsideCastle(DaggerfallWorldProfileKind.Dungeon, Zones, new WorldPoint(15, 2, 15)));
        // Only a dungeon has castle blocks, and a player with no position stands in none.
        Assert.False(DaggerfallAmbientZones.InsideCastle(DaggerfallWorldProfileKind.Interior, Zones, new WorldPoint(5, 2, 5)));
        Assert.False(DaggerfallAmbientZones.InsideCastle(DaggerfallWorldProfileKind.Dungeon, Zones, null));
    }
}
