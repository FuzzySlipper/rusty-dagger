using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallBlocksContentTests
{
    [Fact]
    public void Projects_real_rmb_buildings_from_the_normalized_block_payload()
    {
        string payload = Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.blocks.json");

        DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(payload));

        DaggerfallRmbBuildingSource wall = blocks.RmbBuildings[new DaggerfallRmbBuildingId("WALLAA03.RMB", 0)];
        Assert.Equal(23, wall.BuildingType);
        Assert.Equal(0, wall.FactionId);
        Assert.Equal(0, wall.NameSeed);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
