using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published faction catalog through the pack: filed relations resolve, names answer the
/// first identity, and politic cells resolve to faction records or to the explicit unclaimed
/// region.
/// </summary>
public sealed class DaggerfallFactionsContentTests
{
    [Fact]
    public void Resolves_filed_relations_names_and_politic_regions()
    {
        DaggerfallDefinitions definitions = Definitions();

        Assert.Equal(366, definitions.Factions.Factions.Count);
        Assert.Equal(62, definitions.Factions.Regions.Count);

        // A representative record loads with its filed relations and bindings.
        DaggerfallFactionDefinition oblivion = definitions.Factions.Factions[17];
        Assert.Equal("Oblivion", oblivion.Name);
        Assert.Equal("Group", oblivion.TypeName);
        Assert.All(oblivion.Allies, ally => Assert.Contains(ally, definitions.Factions.Factions.Keys));
        Assert.All(oblivion.Enemies, enemy => Assert.Contains(enemy, definitions.Factions.Factions.Keys));

        // Names answer the first identity the file states.
        Assert.Equal(76, definitions.Factions.Names["The Master of Initiates"]);

        // Privateer's Hold stands in region 17: its politic cell resolves to factions that name
        // the same region the locations record carries.
        DaggerfallPoliticCell hold = definitions.Grids.Politic.GetCell(109, 158);
        Assert.Equal(DaggerfallPoliticDisposition.Region, hold.Disposition);
        DaggerfallRegionFactionDefinition claimed = definitions.Factions.ResolveRegion(hold.Region);
        Assert.Equal(DaggerfallRegionFactionDisposition.Claimed, claimed.Disposition);
        Assert.NotEmpty(claimed.FactionIds);
        Assert.All(claimed.FactionIds, id => Assert.Equal(17, definitions.Factions.Factions[id].Region));

        // A cell no faction claims is itself the miss a social consumer reports.
        DaggerfallRegionFactionDefinition empty = definitions.Factions.ResolveRegion(2);
        Assert.Equal(DaggerfallRegionFactionDisposition.Unclaimed, empty.Disposition);
        Assert.Empty(empty.FactionIds);
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
