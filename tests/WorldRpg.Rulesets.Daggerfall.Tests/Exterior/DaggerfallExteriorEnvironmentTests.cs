using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallExteriorEnvironmentTests
{
    [Fact]
    public void Ocean_clamp_publishes_queryable_water_volumes_in_the_cell_frame()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        environment.ReconcileSurfaces([cell], DaggerfallExteriorWorldOrigin.At(cell),
            _ => Surface(0F), Locations(), Grids(224, "temperate"));

        Assert.Single(environment.WaterVolumes);
        Assert.True(environment.TryReadWater(new Vector3(25F, 10F, -25F), out DaggerfallExteriorWaterVolume volume));
        Assert.Equal(cell, volume.Cell);
        Assert.Equal(40.8F, volume.Maximum.Y, 3);
        Assert.Equal(128, volume.TileWidth);
        Assert.Equal(128, volume.TileHeight);
        Assert.False(environment.TryReadWater(new Vector3(900F, 10F, 25F), out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_swimmer_floats_at_the_rivers_own_surface_down_or_up_a_wide_steep_diagonal_reach(bool upstream)
    {
        // Map pixel (3, 2) spans the heightmap's pixel (2, 1). The reach runs at 45 degrees, 40 m wide, gentle and then losing 20 m a section.
        const float size = DaggerfallExteriorCellResidency.CellSize, step = 34, wide = 20;
        (float Along, float Surface)[] course = [(0, 300), (1, 299.5F), (2, 299), (3, 279), (4, 259), (5, 239), (6, 238.5F)];
        float startX = 2.3F * size, startZ = 1.3F * size;
        DaggerfallTerrainRivers rivers = Rivers([.. course.Select(station => (startX + (station.Along * step), startZ + (station.Along * step), station.Surface, wide))]);
        DaggerfallExteriorCellId cell = new(3, 2);
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(cell);
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        environment.ReconcileSurfaces([cell], origin, _ => Surface(100F, rivers: rivers), Locations(), Grids(224, "temperate"));
        CharacterWaterVolume[] water = [.. environment.CharacterWaterVolumes(origin)];
        Assert.Equal(water.Length, water.Select(volume => volume.Trigger).Distinct().Count());

        const ulong player = 7;
        DaggerfallSwimmingPolicy policy = new(DaggerfallSwimmingTuning.Classic);
        HashSet<ulong> inside = [];
        int steps = (int)(5.8F * step * 2);
        for (int i = 0; i <= steps; i++)
        {
            float along = (upstream ? steps - i : i) / 2F / step + .1F;
            int at = Array.FindLastIndex(course, station => station.Along <= along);
            float surface = course[at].Surface + ((course[at + 1].Surface - course[at].Surface) * (along - course[at].Along));
            // Heightmap metres to the cell's frame, a little off the centre line, where the reach's boxes overlap most.
            Vector3 point = new(startX + (along * step) + 8 - ((cell.X - 1) * size), surface - 1.8F, startZ + (along * step) - 8 - (cell.Y * size));
            HashSet<ulong> now = [.. water.Where(volume => Inside(volume, point)).Select(volume => volume.Trigger)];
            SpatialTriggerFact[] facts = [.. inside.Except(now).Order().Select(trigger => new SpatialTriggerFact(false, trigger, player, 1, SpatialTriggerCause.Movement)),
                .. now.Except(inside).Order().Select(trigger => new SpatialTriggerFact(true, trigger, player, 1, SpatialTriggerCause.Movement))];
            policy.ObserveTriggers(facts, player, water);
            inside = now;

            CharacterWaterVolume? selected = policy.ActiveVolume(water);
            Assert.True(selected.HasValue, $"no water {along} sections along");
            CharacterWaterVolume? surfaced = environment.AtSurface(selected.Value, point, origin);
            Assert.True(surfaced.HasValue, $"no surface {along} sections along");
            Assert.InRange(surfaced.Value.Maximum.Y, surface - .1F, surface + .1F);
        }
    }

    [Fact]
    public void Past_a_rivers_banks_a_box_holds_no_water_and_other_water_keeps_its_own_top()
    {
        const float size = DaggerfallExteriorCellResidency.CellSize;
        DaggerfallTerrainRivers rivers = Rivers([(2.2F * size, 1.2F * size, 300, 30), (2.2F * size + 300, 1.2F * size + 300, 299, 30)]);
        DaggerfallExteriorCellId cell = new(3, 2);
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(cell);
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        environment.ReconcileSurfaces([cell], origin, _ => Surface(100F, rivers: rivers), Locations(), Grids(224, "temperate"));
        CharacterWaterVolume box = Assert.Single(environment.CharacterWaterVolumes(origin));

        // The corner of the box on a diagonal reach, far from the course.
        Assert.Null(environment.AtSurface(box, new(box.Minimum.X + 1, 298, box.Maximum.Z - 1), origin));
        CharacterWaterVolume sea = new(99, Vector3.Zero, new(10, 40.8F, 10));
        Assert.Equal(sea, environment.AtSurface(sea, new(5, 5, 5), origin));
    }

    private static bool Inside(CharacterWaterVolume volume, Vector3 point) =>
        point.X >= volume.Minimum.X && point.X <= volume.Maximum.X && point.Y >= volume.Minimum.Y && point.Y <= volume.Maximum.Y
        && point.Z >= volume.Minimum.Z && point.Z <= volume.Maximum.Z;

    [Fact]
    public void Water_tiles_project_through_the_current_origin_with_stable_trigger_ids()
    {
        DaggerfallExteriorCellId cell = new(12, 34);
        DaggerfallExteriorWorldOrigin origin = new(cell.X, cell.Y, new Vector3(4F, 5F, 6F));
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        environment.ReconcileSurfaces([cell], origin, _ => Surface(0F), Locations(), Grids(224, "temperate"));

        CharacterWaterVolume[] projected = [.. environment.CharacterWaterVolumes(origin)];
        Assert.Single(projected);
        Assert.Single(projected.Select(volume => volume.Trigger).Distinct());
        Assert.Contains(projected, volume => volume.Trigger == DaggerfallExteriorEnvironment.WaterObjectId(cell, 0, 0, 128, 128)
            && volume.Minimum == new Vector3(4F, 5F, 6F - DaggerfallExteriorCellResidency.CellSize));
        CharacterWaterVolume last = Assert.Single(projected);
        Assert.Equal(823.2F, last.Maximum.X, 3);
        Assert.Equal(45.8F, last.Maximum.Y, 3);
        Assert.Equal(6F, last.Maximum.Z, 3);
        Assert.All(projected, volume => volume.Validate());
    }

    [Fact]
    public void Water_object_ids_keep_cell_and_tile_coordinates_distinct()
    {
        ulong first = DaggerfallExteriorEnvironment.WaterObjectId(new(12, 34), 0, 0);
        ulong secondCell = DaggerfallExteriorEnvironment.WaterObjectId(new(12, 35), 0, 0);
        ulong secondTile = DaggerfallExteriorEnvironment.WaterObjectId(new(12, 34), 1, 0);
        ulong secondExtent = DaggerfallExteriorEnvironment.WaterObjectId(new(12, 34), 0, 0, 2, 1);

        Assert.NotEqual(first, secondCell);
        Assert.NotEqual(first, secondTile);
        Assert.NotEqual(first, secondExtent);
        Assert.Equal(first, DaggerfallExteriorEnvironment.WaterObjectId(new(12, 34), 0, 0));
    }

    [Fact]
    public void Water_rectangles_match_the_exact_generated_tile_occupancy_without_bridging_dry_tiles()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        environment.ReconcileSurfaces([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => SurfaceWithDrySample(64, 64),
            Locations(), Grids(224, "temperate"));

        IReadOnlyList<DaggerfallExteriorTerrainTile> terrain = environment.TerrainTilesFor(cell);
        int occupiedTiles = terrain.Count(tile => tile.TextureRecord == 0);
        int waterArea = environment.WaterVolumes.Sum(volume => volume.TileWidth * volume.TileHeight);

        Assert.InRange(occupiedTiles, 1, (128 * 128) - 1);
        Assert.Equal(occupiedTiles, waterArea);
        Assert.Equal(environment.WaterVolumes.Count, environment.WaterVolumes.Select(volume => volume.StableId).Distinct().Count());
    }

    [Fact]
    public void Terrain_tiles_preserve_source_ground_bits_and_generate_the_donor_mosaic()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallSiteExterior location = new(1, 0, 1, 1, 0, 0, false, 2, 0, 1, 0, 1)
        {
            GroundTiles =
                DaggerfallGroundTileGrid.FromBytes(CompactGroundTiles((byte)(7 | 0x40), (byte)(8 | 0x80)), "test"),
        };
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        environment.ReconcileSurfaces([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(100F),
            new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> { [cell] = location },
            Grids(231, "Woodlands"));

        IReadOnlyList<DaggerfallExteriorTerrainTile> tiles = environment.TerrainTilesFor(cell);
        Assert.Equal(128 * 128, tiles.Count);
        // Location and terrain rows share the same right-handed frame.
        Assert.Equal(new DaggerfallExteriorTerrainTile(7, true, false), tiles[0]);
        Assert.Equal(new DaggerfallExteriorTerrainTile(8, false, true), tiles[1]);
        Assert.All(tiles, tile => Assert.InRange(tile.TextureRecord, 0, 55));
    }

    [Fact]
    public void Authored_source_water_record_zero_is_distinct_from_an_absent_ground_tile()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallSiteExterior location = new(1, 0, 1, 1, 0, 0, false, 2, 0, 1, 0, 1)
        {
            GroundTiles = DaggerfallGroundTileGrid.FromBytes(CompactGroundTiles(0, 7), "test"),
        };
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        environment.ReconcileSurfaces([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(100F),
            new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> { [cell] = location },
            Grids(231, "Woodlands"));

        IReadOnlyList<DaggerfallExteriorTerrainTile> tiles = environment.TerrainTilesFor(cell);
        Assert.Equal(new DaggerfallExteriorTerrainTile(0, false, false), tiles[0]);
        Assert.Equal(new DaggerfallExteriorTerrainTile(7, false, false), tiles[1]);
    }

    [Fact]
    public void Reconcile_reuses_unchanged_cell_facts_when_only_the_origin_moves()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        int builds = 0;
        DaggerfallTerrainSurface Factory(DaggerfallExteriorCellId _) { builds++; return Surface(0F); }

        environment.ReconcileSurfaces([cell], DaggerfallExteriorWorldOrigin.At(cell), Factory, Locations(), Grids(224, "temperate"));
        environment.ReconcileSurfaces([cell], new DaggerfallExteriorWorldOrigin(cell.X + 1, cell.Y, new(2F, 0F, 0F)), Factory,
            Locations(), Grids(224, "temperate"));

        Assert.Equal(1, builds);
        Assert.Equal(new Vector3(2F, 0F, 0F), environment.Origin.Compensation);
    }

    [Fact]
    public void Cells_a_moving_window_brings_in_away_from_the_player_take_their_nature_one_a_step()
    {
        DaggerfallExteriorCellId center = new(0, 0), next = new(2, 0), last = new(3, 0);
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(center);
        DaggerfallExteriorEnvironment moving = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        DaggerfallExteriorEnvironment whole = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        whole.ReconcileSurfaces([center, next, last], origin, _ => Surface(100F), Locations(), Grids(224, "temperate"), center);

        moving.ReconcileSurfaces([center], origin, _ => Surface(100F), Locations(), Grids(224, "temperate"), center);
        moving.ReconcileSurfaces([center, next, last], origin, _ => Surface(100F), Locations(), Grids(224, "temperate"), center);
        Assert.Equal(whole.NatureFor(next), moving.NatureFor(next));
        Assert.Empty(moving.NatureFor(last));
        Assert.NotEmpty(moving.TerrainTilesFor(last));
        Assert.True(moving.NatureWaiting);

        moving.ReconcileSurfaces([center, next, last], origin, _ => Surface(100F), Locations(), Grids(224, "temperate"), center);
        Assert.NotEmpty(moving.NatureFor(last));
        Assert.False(moving.NatureWaiting);
        Assert.Equal(whole.NatureFor(last), moving.NatureFor(last));
    }

    [Fact]
    public void Nature_is_deterministic_and_excludes_the_expanded_location_footprint()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallSiteExterior location = new(1, 0, 1, 1, 56, 56, false, 2, 54, 73, 54, 73);
        Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> locations = new() { [cell] = location };
        DaggerfallExteriorEnvironment first = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        DaggerfallExteriorEnvironment second = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        first.ReconcileSurfaces([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(100F), locations, Grids(224, "temperate"));
        second.ReconcileSurfaces([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(100F), locations, Grids(224, "temperate"));

        Assert.Equal(first.NaturePlacements, second.NaturePlacements);
        Assert.NotEmpty(first.NaturePlacements);
        Assert.DoesNotContain(first.NaturePlacements, placement =>
            placement.TileX >= 56 && placement.TileX < 72 && placement.TileY >= 56 && placement.TileY < 72);
    }

    [Fact]
    public void Winter_variant_uses_the_donor_archive_offset_only_for_non_desert_climates()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallExteriorEnvironment temperate = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()), DaggerfallExteriorSeason.Winter);
        temperate.ReconcileSurfaces([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(0F), Locations(), Grids(231, "Woodlands"));
        DaggerfallExteriorEnvironment desert = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()), DaggerfallExteriorSeason.Winter);
        desert.ReconcileSurfaces([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(0F), Locations(), Grids(224, "Desert"));

        Assert.Equal(303, Assert.Single(temperate.TerrainVariants).GroundTextureArchive);
        Assert.Equal(2, Assert.Single(desert.TerrainVariants).GroundTextureArchive);
        Assert.Equal(505, Assert.Single(temperate.TerrainVariants).NatureTextureArchive);
        Assert.Equal(503, Assert.Single(desert.TerrainVariants).NatureTextureArchive);
    }

    [Fact]
    public void Nature_batch_ids_keep_cell_and_sprite_record_distinct()
    {
        ulong first = DaggerfallExteriorTerrainAppearance.NatureBatchObjectId(new(12, 34), 5);
        ulong secondCell = DaggerfallExteriorTerrainAppearance.NatureBatchObjectId(new(12, 35), 5);
        ulong secondRecord = DaggerfallExteriorTerrainAppearance.NatureBatchObjectId(new(12, 34), 6);

        Assert.NotEqual(first, secondCell);
        Assert.NotEqual(first, secondRecord);
        Assert.Equal(first, DaggerfallExteriorTerrainAppearance.NatureBatchObjectId(new(12, 34), 5));
    }

    /// <summary>
    /// Every nature batch and location identity the world map can produce is a distinct object identity the
    /// Engine publishes: within its safe range, and in a band no other presentation owner draws from.
    /// </summary>
    [Fact]
    public void Nature_and_location_object_ids_stay_in_their_bands_inside_the_engine_safe_range()
    {
        (int X, int Y)[] corners = [(0, 0), (999, 0), (0, 499), (999, 499), (106, 156)];
        HashSet<ulong> ids = [];
        foreach ((int x, int y) in corners)
        {
            foreach (int record in new[] { 0, 1, 31, 127 })
            {
                ulong nature = DaggerfallExteriorTerrainAppearance.NatureBatchObjectId(new(x, y), record);
                Assert.InRange(nature, 1UL << 49, (1UL << 50) - 1);
                Assert.True(ids.Add(nature));
            }
        }
        // Each location slot's meshes and visuals stay inside its own range, so the active location and every
        // resident neighbour can be drawn together.
        foreach (int slot in new[] { 0, 1, 2, DaggerfallPresentationObjectIds.LocationSlots - 1 })
        {
            foreach (int index in new[] { 0, 1, int.MaxValue })
                Assert.True(ids.Add(DaggerfallPresentationObjectIds.WorldMesh(slot, index)));
            foreach (ulong ordinal in new[] { 0UL, 1UL, (1UL << 31) - 1 })
                Assert.True(ids.Add(DaggerfallPresentationObjectIds.LocationVisual(slot, ordinal)));
        }
        Assert.All(ids.Where(id => id >= 1UL << 50), id => Assert.InRange(id, 1UL << 50, (1UL << 52) - 1));
        Assert.All(ids, id => Assert.InRange(id, 1UL, DaggerfallPresentationObjectIds.Maximum));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallExteriorTerrainAppearance.NatureBatchObjectId(new(4096, 0), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallExteriorTerrainAppearance.NatureBatchObjectId(new(0, -1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallExteriorTerrainAppearance.NatureBatchObjectId(new(0, 0), 128));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallPresentationObjectIds.WorldMesh(DaggerfallPresentationObjectIds.LocationSlots, 0));
        Assert.Throws<InvalidOperationException>(() => DaggerfallPresentationObjectIds.LocationVisual(0, 1UL << 31));
    }

    [Theory]
    [InlineData(0U, -0.55738306F)]
    [InlineData(1U, 0.11520767F)]
    [InlineData(64U, 0.92678118F)]
    [InlineData(16640U, -0.09306836F)]
    public void Beach_jitter_matches_Unity_Mathematics_indexed_first_draw(uint index, float expected)
    {
        Assert.Equal(expected, DaggerfallExteriorEnvironment.BeachJitter(index), 6);
    }

    [Theory]
    [InlineData(45F, false)]
    [InlineData(75F, true)]
    public void Nature_compares_beach_elevation_before_the_scene_vertical_scale(float sceneHeight, bool hasNature)
    {
        DaggerfallExteriorEnvironment environment = GrassEnvironment(sceneHeight, 128);
        Assert.Equal(hasNature, environment.NaturePlacements.Count > 0);
    }

    [Fact]
    public void Nature_density_uses_source_map_elevation_instead_of_the_interpolated_surface()
    {
        DaggerfallExteriorEnvironment low = GrassEnvironment(100F, 32);
        DaggerfallExteriorEnvironment high = GrassEnvironment(100F, 128);
        Assert.NotEmpty(low.NaturePlacements);
        Assert.True(high.NaturePlacements.Count > low.NaturePlacements.Count * 2);
    }

    private static DaggerfallExteriorEnvironment GrassEnvironment(float sceneHeight, int sourceWorldHeight)
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallSiteExterior location = new(1, 0, 1, 1, 0, 0, false, 2, 0, 1, 0, 1)
        {
            GroundTiles = DaggerfallGroundTileGrid.FromBytes(Enumerable.Repeat((byte)2, 128 * 128).ToArray(), "test"),
        };
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        environment.ReconcileSurfaces([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(sceneHeight, sourceWorldHeight),
            new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> { [cell] = location }, Grids(231, "Woodlands"));
        return environment;
    }

    private static DaggerfallTerrainRivers Rivers((float X, float Z, float Surface, float HalfWidth)[] stations)
    {
        byte[] bytes = new byte[12 + (stations.Length * 16)];
        "RVR1"u8.CopyTo(bytes);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), stations.Length);
        for (int s = 0; s < stations.Length; s++)
        {
            float[] values = [stations[s].X, stations[s].Z, stations[s].Surface, stations[s].HalfWidth];
            for (int v = 0; v < 4; v++) System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(12 + (s * 16) + (v * 4)), values[v]);
        }

        return DaggerfallTerrainRivers.Read(bytes);
    }

    private static DaggerfallTerrainSurface Surface(float height, int sourceWorldHeight = 128, DaggerfallTerrainRivers? rivers = null)
    {
        int count = DaggerfallTerrainSurfaceBuilder.SampleDimension * DaggerfallTerrainSurfaceBuilder.SampleDimension;
        Vector3[] vertices = Enumerable.Range(0, count)
            .Select(index => new Vector3(index % DaggerfallTerrainSurfaceBuilder.SampleDimension, height,
                index / DaggerfallTerrainSurfaceBuilder.SampleDimension)).ToArray();
        return new DaggerfallTerrainSurface(1, 0, vertices,
            Enumerable.Repeat(height / DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize, count).ToArray())
        {
            SourceWorldHeight = sourceWorldHeight,
            Rivers = rivers,
        };
    }

    private static Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> Locations() => [];

    private static byte[] CompactGroundTiles(byte first, byte second)
    {
        byte[] grid = Enumerable.Repeat(DaggerfallGroundTileGrid.GeneratedTerrainBitfield, 128 * 128).ToArray();
        grid[0] = first;
        grid[1] = second;
        return grid;
    }

    private static DaggerfallTerrainSurface SurfaceWithDrySample(int sampleX, int sampleY)
    {
        int dimension = DaggerfallTerrainSurfaceBuilder.SampleDimension;
        int count = dimension * dimension;
        float dryHeight = 100F;
        float dryNormalized = dryHeight / DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize;
        Vector3[] vertices = Enumerable.Range(0, count)
            .Select(index => new Vector3(index % dimension, 0F, index / dimension)).ToArray();
        float[] normalized = new float[count];
        int sampleIndex = (sampleY * dimension) + sampleX;
        vertices[sampleIndex] = new(vertices[sampleIndex].X, dryHeight, vertices[sampleIndex].Z);
        normalized[sampleIndex] = dryNormalized;
        return new DaggerfallTerrainSurface(1, 0, vertices, normalized);
    }

    private static DaggerfallWorldGridsSet Grids(int climateValue, string climateName)
    {
        DaggerfallClimateGridDefinition climate = new(3, 1, [0, 0, (byte)climateValue],
            [new DaggerfallClimateValueDefinition(climateValue, climateName, DaggerfallClimateDisposition.Named)]);
        DaggerfallPoliticGridDefinition politic = new(3, 1, [64, 64, 64], []);
        return new(climate, politic);
    }
}
