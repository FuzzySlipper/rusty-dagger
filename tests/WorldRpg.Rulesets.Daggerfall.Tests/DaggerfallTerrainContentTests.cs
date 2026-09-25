using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published wilderness terrain through the pack: heights and cell samples by map pixel,
/// with the donor's edge clamp.
/// </summary>
public sealed class DaggerfallTerrainContentTests
{
    [Fact]
    public void Reads_heights_and_samples_by_map_pixel()
    {
        DaggerfallDefinitions definitions = Definitions();

        Assert.Equal(1000, definitions.Terrain.Width);
        Assert.Equal(500, definitions.Terrain.Height);
        Assert.Equal(500000, definitions.Terrain.Heightmap.Length);
        Assert.Equal(12500000, definitions.Terrain.Samples.Length);

        // Privateer's Hold stands at height 25 on its map pixel.
        Assert.Equal(25, definitions.Terrain.GetHeight(109, 158));
        byte[] samples = definitions.Terrain.GetSamples(109, 158);
        Assert.Equal(25, samples.Length);

        // Coordinates past the map clamp to its edge, the way the donor reads.
        Assert.Equal(definitions.Terrain.GetHeight(0, 0), definitions.Terrain.GetHeight(-5, -7));
        Assert.Equal(definitions.Terrain.GetHeight(999, 499), definitions.Terrain.GetHeight(1004, 502));
    }

    private static DaggerfallDefinitions Definitions()
    {
        string root = RepositoryRoot();
        return TestPayload.Definitions;
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
