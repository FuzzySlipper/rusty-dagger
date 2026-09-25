using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTerrainSurfaceBuilderTests
{
    [Fact]
    public void Builds_the_donor_grid_with_ocean_clamp_vertical_scale_and_upward_winding()
    {
        DaggerfallTerrainSurface surface = DaggerfallTerrainSurfaceBuilder.Build(FlatTerrain(4, 4, 0, 0), 1, 1);

        Assert.Equal(129 * 129, surface.Vertices.Length);
        Assert.Equal(128 * 128 * 2, surface.Triangles.Length);
        Assert.Equal(surface.Vertices.Length, surface.NormalizedHeights.Length);

        float expectedNormalizedOcean = DaggerfallTerrainSurfaceBuilder.OceanElevation
            / DaggerfallTerrainSurfaceBuilder.MaxTerrainHeight;
        float expectedWorldHeight = DaggerfallTerrainSurfaceBuilder.OceanElevation
            * DaggerfallTerrainSurfaceBuilder.TerrainScale;
        Assert.InRange(MathF.Abs(surface.NormalizedHeights[0] - expectedNormalizedOcean), 0F, .000001F);
        Assert.InRange(MathF.Abs(surface.Vertices[0].Y - expectedWorldHeight), 0F, .00001F);
        Assert.InRange(MathF.Abs(surface.Vertices[0].X), 0F, .000001F);
        Assert.InRange(MathF.Abs(surface.Vertices[0].Z), 0F, .000001F);
        Assert.InRange(MathF.Abs(surface.Vertices[128].X - DaggerfallTerrainSurfaceBuilder.HorizontalSize), 0F, .00001F);
        Assert.InRange(MathF.Abs(surface.Vertices[129].Z - DaggerfallTerrainSurfaceBuilder.SampleSpacing), 0F, .00001F);

        Assert.Equal(new Triangle(0, 130, 1), surface.Triangles[0]);
        Assert.Equal(new Triangle(0, 129, 130), surface.Triangles[1]);
        Vector3 firstNormal = Vector3.Cross(
            surface.Vertices[130] - surface.Vertices[0],
            surface.Vertices[1] - surface.Vertices[0]);
        Assert.True(firstNormal.Y > 0F, "The donor-oriented surface triangles must face upward.");
    }

    [Fact]
    public void Reuses_the_same_generated_height_on_adjacent_map_pixel_borders()
    {
        DaggerfallTerrainSet terrain = FlatTerrain(8, 8, 18, 11);
        DaggerfallTerrainSurface west = DaggerfallTerrainSurfaceBuilder.Build(terrain, 2, 2);
        DaggerfallTerrainSurface east = DaggerfallTerrainSurfaceBuilder.Build(terrain, 3, 2);
        DaggerfallTerrainSurface north = DaggerfallTerrainSurfaceBuilder.Build(terrain, 2, 1);

        for (int y = 0; y < DaggerfallTerrainSurfaceBuilder.SampleDimension; y++)
        {
            int westIndex = Index(DaggerfallTerrainSurfaceBuilder.SampleDimension - 1, y);
            int eastIndex = Index(0, y);
            Assert.InRange(MathF.Abs(west.NormalizedHeights[westIndex] - east.NormalizedHeights[eastIndex]), 0F, .000001F);
        }

        for (int x = 0; x < DaggerfallTerrainSurfaceBuilder.SampleDimension; x++)
        {
            int southIndex = Index(x, DaggerfallTerrainSurfaceBuilder.SampleDimension - 1);
            int northIndex = Index(x, 0);
            Assert.InRange(MathF.Abs(north.NormalizedHeights[northIndex] - west.NormalizedHeights[southIndex]), 0F, .000001F);
        }
    }

    [Fact]
    public void Repeated_builds_are_deterministic_for_the_normalized_source()
    {
        DaggerfallTerrainSet terrain = FlatTerrain(8, 8, 31, 23);
        DaggerfallTerrainSurface first = DaggerfallTerrainSurfaceBuilder.Build(terrain, 4, 4);
        DaggerfallTerrainSurface second = DaggerfallTerrainSurfaceBuilder.Build(terrain, 4, 4);

        Assert.Equal(first.NormalizedHeights, second.NormalizedHeights);
        Assert.Equal(first.Vertices, second.Vertices);
        Assert.Equal(first.Triangles, second.Triangles);
    }

    [Fact]
    public void Builds_the_published_privateers_hold_cell_and_keeps_its_real_borders_continuous()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallTerrainSet terrain = definitions.Terrain;
        Assert.Equal(25, terrain.GetHeight(109, 158));
        Assert.Equal(25, terrain.GetSamples(109, 158).Length);

        DaggerfallTerrainSurface cell = DaggerfallTerrainSurfaceBuilder.Build(terrain, 109, 158);
        DaggerfallTerrainSurface east = DaggerfallTerrainSurfaceBuilder.Build(terrain, 110, 158);
        DaggerfallTerrainSurface north = DaggerfallTerrainSurfaceBuilder.Build(terrain, 109, 157);

        Assert.Equal(129 * 129, cell.Vertices.Length);
        Assert.All(cell.NormalizedHeights, height => Assert.InRange(height, 0F, 1F));
        for (int y = 0; y < DaggerfallTerrainSurfaceBuilder.SampleDimension; y++)
        {
            Assert.InRange(
                MathF.Abs(cell.NormalizedHeights[Index(DaggerfallTerrainSurfaceBuilder.SampleDimension - 1, y)]
                    - east.NormalizedHeights[Index(0, y)]),
                0F,
                .000001F);
        }
        for (int x = 0; x < DaggerfallTerrainSurfaceBuilder.SampleDimension; x++)
        {
            Assert.InRange(
                MathF.Abs(cell.NormalizedHeights[Index(x, DaggerfallTerrainSurfaceBuilder.SampleDimension - 1)]
                    - north.NormalizedHeights[Index(x, 0)]),
                0F,
                .000001F);
        }
    }

    [Fact]
    public void Flattens_the_location_rectangle_to_the_preblend_average_with_donor_edge_strength()
    {
        DaggerfallTerrainSurface source = DaggerfallTerrainSurfaceBuilder.Build(GradientTerrain(8, 8), 3, 3);
        float target = source.NormalizedHeights.Average();
        DaggerfallTerrainLocationFlattening flattening = new(32, 96, 32, 96);

        DaggerfallTerrainSurface flattened = DaggerfallTerrainSurfaceBuilder.ApplyLocationFlattening(source, flattening);

        // Interior samples are exactly the generated pre-blend average.
        Assert.InRange(MathF.Abs(flattened.NormalizedHeights[Index(64, 64)] - target), 0F, .000001F);
        Assert.InRange(
            MathF.Abs(flattened.Vertices[Index(64, 64)].Y - (target * DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize)),
            0F,
            .00001F);

        // At x=16,y=64 the donor is half way through the left edge blend space.
        float sourceLeftEdge = source.NormalizedHeights[Index(16, 64)];
        float expectedLeftEdge = sourceLeftEdge + ((target - sourceLeftEdge) * .5F);
        Assert.InRange(MathF.Abs(flattened.NormalizedHeights[Index(16, 64)] - expectedLeftEdge), 0F, .000001F);

        // At x=16,y=16 both blend factors are .5, and the donor bilinear corner reduces to .25.
        float sourceCorner = source.NormalizedHeights[Index(16, 16)];
        float expectedCorner = sourceCorner + ((target - sourceCorner) * .25F);
        Assert.InRange(MathF.Abs(flattened.NormalizedHeights[Index(16, 16)] - expectedCorner), 0F, .000001F);

        // The far corner has no blend strength and remains the source height.
        Assert.InRange(MathF.Abs(flattened.NormalizedHeights[Index(0, 0)] - source.NormalizedHeights[Index(0, 0)]), 0F, .000001F);
    }

    [Fact]
    public void The_flattening_build_overload_matches_the_explicit_blend_pass()
    {
        DaggerfallTerrainSet terrain = GradientTerrain(8, 8);
        DaggerfallTerrainLocationFlattening flattening = new(24, 88, 28, 100);
        DaggerfallTerrainSurface explicitPass = DaggerfallTerrainSurfaceBuilder.ApplyLocationFlattening(
            DaggerfallTerrainSurfaceBuilder.Build(terrain, 3, 3),
            flattening);
        DaggerfallTerrainSurface overload = DaggerfallTerrainSurfaceBuilder.Build(terrain, 3, 3, flattening);

        Assert.Equal(explicitPass.NormalizedHeights, overload.NormalizedHeights);
        Assert.Equal(explicitPass.Vertices, overload.Vertices);
        Assert.Equal(explicitPass.Triangles, overload.Triangles);
    }

    private static DaggerfallTerrainSet FlatTerrain(int width, int height, byte heightValue, byte sampleValue)
    {
        return new DaggerfallTerrainSet(
            width,
            height,
            Enumerable.Repeat(heightValue, checked(width * height)).ToArray(),
            Enumerable.Repeat(sampleValue, checked(width * height * 25)).ToArray());
    }

    private static DaggerfallTerrainSet GradientTerrain(int width, int height)
    {
        byte[] heights = new byte[checked(width * height)];
        byte[] samples = new byte[checked(width * height * 25)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int cell = (y * width) + x;
            heights[cell] = checked((byte)(80 + (x * 7) + (y * 9)));
            for (int sample = 0; sample < 25; sample++)
                samples[(cell * 25) + sample] = checked((byte)(20 + (x * 3) + (y * 5) + sample));
        }

        return new DaggerfallTerrainSet(width, height, heights, samples);
    }

    private static int Index(int x, int y) => x + (y * DaggerfallTerrainSurfaceBuilder.SampleDimension);

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
                return current.FullName;
        }

        throw new InvalidOperationException("repository root not found");
    }
}
