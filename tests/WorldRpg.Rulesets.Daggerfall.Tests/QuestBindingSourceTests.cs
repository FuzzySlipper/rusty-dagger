using System.Text;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestBindingSourceTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(-1, false)]
    [InlineData(2, false)]
    public void Fixed_building_matches_raw_sector_and_last_raw_rmb_id(int sector, bool available)
    {
        DaggerfallSiteRecord source = TestPayload.Definitions.Locations.Records.First(value => value.Exterior is not null);
        DaggerfallSiteBuildingSource Building(int x, int index, int rawId) => new(new(x, 0, index), new(new("TEST.RMB", index), 11, 414, 123), 9)
        { SourceLocationId = rawId };
        DaggerfallSiteBuildingSource[] buildings = [Building(0, 0, 42), Building(1, 0, 42), Building(1, 1, 42), Building(1, 2, 43)];
        DaggerfallSiteRecord site = source with { Exterior = source.Exterior! with
        {
            Width = 2, Height = 1,
            Blocks = [new("TEST.RMB", 0, 0), new("TEST.RMB", 1, 0)],
            BuildingReferences = [new(42, sector)],
            Buildings = buildings.ToDictionary(value => value.Id),
        } };
        DaggerfallSiteContext sites = new(TestPayload.Definitions.Locations with { Records = [site] });
        Assert.Equal(available, sites.TryResolveQuestBuilding(site.Id, 42, out var selected, out var unavailable));
        if (available)
        {
            Assert.Same(buildings[2], selected);
            Assert.Null(unavailable);
        }
        else
        {
            Assert.Null(selected);
            Assert.Contains("MAPS sector", unavailable);
        }
        Assert.False(sites.TryResolveQuestBuilding(site.Id, 0, out _, out _));
        Assert.False(sites.TryResolveQuestBuilding(site.Id, 43, out _, out _));
    }

    [Fact]
    public void Normalized_quest_markers_retain_source_order_kind_position_and_block_identity()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {"world":{"questMarkers":[
              {"id":"quest/source/5","kind":"item","position":{"x":2,"y":3,"z":-4},"sourceKey":"SOURCE.RDB","buildingIndex":null,"sourceOrdinal":5,"blockX":-2,"blockZ":1},
              {"id":"quest/source/1","kind":"spawn","position":{"x":7,"y":8,"z":-9},"sourceKey":"SOURCE.RDB","buildingIndex":null,"sourceOrdinal":1,"blockX":-2,"blockZ":1}
            ]}}
            """);
        DaggerfallContentDiagnostics diagnostics = new();
        var markers = DaggerfallQuestMarkerContent.ReadWorld(bytes, diagnostics);
        diagnostics.ThrowIfAny();
        Assert.Equal([5, 1], markers.Select(value => value.SourceOrdinal));
        Assert.Equal([DaggerfallSiteMarkerKind.QuestItem, DaggerfallSiteMarkerKind.QuestSpawn], markers.Select(value => value.Kind));
        Assert.Equal(new WorldPoint(2, 3, -4), markers[0].Position);
        Assert.Equal((-2, 1), (markers[0].BlockX, markers[0].BlockZ));
        Assert.Equal("SOURCE.RDB", markers[0].SourceKey);
        Assert.Null(markers[0].BuildingIndex);
    }

    [Fact]
    public void Unknown_marker_kind_is_a_content_error()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {"world":{"questMarkers":[{"id":"quest/0","kind":"invented","position":{"x":0,"y":0,"z":0},"sourceKey":"SOURCE.RDB","buildingIndex":null,"sourceOrdinal":0,"blockX":0,"blockZ":0}]}}
            """);
        DaggerfallContentDiagnostics diagnostics = new();
        _ = DaggerfallQuestMarkerContent.ReadWorld(bytes, diagnostics);
        Assert.Throws<DaggerfallContentException>(diagnostics.ThrowIfAny);
    }
}
