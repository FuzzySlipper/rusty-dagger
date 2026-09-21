using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published world grids through the pack: coordinate lookups name the climate and the region
/// or ocean each cell states, and coordinates past the grid answer explicitly.
/// </summary>
public sealed class DaggerfallWorldGridsContentTests
{
    [Fact]
    public void Names_the_climate_and_region_of_published_locations()
    {
        DaggerfallWorldGridsSet grids = Grids();

        // Privateer's Hold stands in woodlands in region 17.
        DaggerfallClimateCell hold = grids.Climate.GetCell(109, 158);
        Assert.Equal(DaggerfallClimateCoordinateDisposition.Found, hold.Disposition);
        Assert.Equal(231, hold.Value);
        Assert.Equal("Woodlands", hold.Name);
        DaggerfallPoliticCell holdPolitic = grids.Politic.GetCell(109, 158);
        Assert.Equal(DaggerfallPoliticDisposition.Region, holdPolitic.Disposition);
        Assert.Equal(145, holdPolitic.Value);
        Assert.Equal(17, holdPolitic.Region);

        // A desert city in region 0 agrees with its own location record.
        DaggerfallClimateCell desert = grids.Climate.GetCell(553, 395);
        Assert.Equal("Desert", desert.Name);
        Assert.Equal(0, grids.Politic.GetCell(553, 395).Region);
    }

    [Fact]
    public void Answers_the_ocean_and_the_edge_explicitly()
    {
        DaggerfallWorldGridsSet grids = Grids();

        // The sentinel column is ocean in both grids.
        DaggerfallPoliticCell ocean = grids.Politic.GetCell(0, 250);
        Assert.Equal(DaggerfallPoliticDisposition.Ocean, ocean.Disposition);
        Assert.Equal(64, ocean.Value);
        Assert.Equal(-1, ocean.Region);
        Assert.Equal("Ocean", grids.Climate.GetCell(0, 250).Name);

        // Coordinates past the grid are an answer, not a refusal.
        Assert.Equal(DaggerfallClimateCoordinateDisposition.OutOfBounds, grids.Climate.GetCell(-1, 0).Disposition);
        Assert.Equal(DaggerfallClimateCoordinateDisposition.OutOfBounds, grids.Climate.GetCell(1001, 0).Disposition);
        Assert.Equal(DaggerfallClimateCoordinateDisposition.OutOfBounds, grids.Climate.GetCell(0, 500).Disposition);
        Assert.Equal(-1, grids.Climate.GetCell(-1, 0).Value);
        Assert.Equal(DaggerfallPoliticDisposition.OutOfBounds, grids.Politic.GetCell(-1, -1).Disposition);
        Assert.Equal(-1, grids.Politic.GetCell(1001, 500).Region);
    }

    [Fact]
    public void Keeps_every_published_cell_inside_a_named_value()
    {
        DaggerfallDefinitions definitions = Definitions();
        Assert.Equal(500 * 1001, definitions.Grids.Climate.Cells.Length);
        Assert.Equal(500 * 1001, definitions.Grids.Politic.Cells.Length);
        HashSet<int> climateValues = [.. definitions.Grids.Climate.Cells.Select(cell => (int)cell)];
        HashSet<int> politicValues = [.. definitions.Grids.Politic.Cells.Select(cell => (int)cell)];
        Assert.Subset(climateValues, definitions.Grids.Climate.Values.Select(value => value.Value).ToHashSet());
        Assert.Subset(politicValues, definitions.Grids.Politic.Values.Select(value => value.Value).ToHashSet());
    }

    private static DaggerfallWorldGridsSet Grids() => Definitions().Grids;

    private static DaggerfallDefinitions Definitions()
    {
        string root = RepositoryRoot();
        return DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
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
