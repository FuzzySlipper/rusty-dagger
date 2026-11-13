using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The exterior's off-update ground builds on their own, without a session: a cell built on a worker from what the update
/// handed it is the cell the update builds itself, and a cell let go of is no longer held.
/// </summary>
public sealed class DaggerfallExteriorCellGroundsTests
{
    [Fact]
    public void A_cell_built_on_a_worker_is_the_cell_the_update_builds_and_one_let_go_is_not_held()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> locations = definitions.Locations.Records
            .Where(record => record.Exterior is not null)
            .GroupBy(record => new DaggerfallExteriorCellId(record.Exterior!.MapPixelX, record.Exterior.MapPixelY))
            .ToDictionary(group => group.Key, group => group.Single().Exterior!);
        DaggerfallExteriorCellId town = locations.Keys.First();
        DaggerfallExteriorCellId open = new(town.X + 1, town.Y);
        while (locations.ContainsKey(open)) open = new(open.X + 1, open.Y);
        DaggerfallGroundTuning tuning = DaggerfallTuning.Defaults.Ground;

        using DaggerfallExteriorCellGrounds held = new(definitions.Terrain, locations, tuning);
        using DaggerfallExteriorCellGrounds onTheUpdate = new(definitions.Terrain, locations, tuning);
        held.Hold([town, open]);
        Assert.True(held.Holds(town) && held.Holds(open));

        foreach (DaggerfallExteriorCellId cell in new[] { town, open })
        {
            DaggerfallExteriorCellGround worker = held.Ground(cell), update = onTheUpdate.Ground(cell);
            Assert.Equal(update.Terrain.Count, worker.Terrain.Count);
            Assert.Equal(update.Water.Count, worker.Water.Count);
            Assert.Equal(update.NatureSites.Count, worker.NatureSites.Count);
            for (int step = 0; step <= 8; step++)
            {
                float at = step / 8F;
                Assert.Equal(DaggerfallTerrainSurfaceBuilder.SampleWorldHeight(update.Surface, at, 1F - at),
                    DaggerfallTerrainSurfaceBuilder.SampleWorldHeight(worker.Surface, at, 1F - at));
            }
        }

        held.Hold([open]);
        Assert.False(held.Holds(town));
        Assert.True(held.Holds(open));
        held.Clear();
        Assert.False(held.Holds(open));
    }
}
