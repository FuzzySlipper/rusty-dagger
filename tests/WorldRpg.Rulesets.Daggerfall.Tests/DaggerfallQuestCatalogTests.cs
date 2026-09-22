using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallQuestCatalogTests
{
    [Fact]
    public void Published_classic_catalog_accounts_for_all_rows_and_missing_sources()
    {
        var catalog = DaggerfallQuestWorldTablesTests.Content().QuestSources.Catalog;
        Assert.Equal("Tables/QuestList-Classic.txt", catalog.SourcePath);
        Assert.Equal(187, catalog.Rows.Count(row => row.Active));
        Assert.Equal(23, catalog.Rows.Count(row => !row.Active));
        Assert.Equal(new[] { "M0B40Y04", "N0C00Y01", "A0C00Y04", "R0C40Y23", "80C00Y00" },
            catalog.Rows.Where(row => row.SourceDisposition == "missing").Select(row => row.Name));
        Assert.Equal(catalog.Rows.OrderBy(row => row.SourceLine), catalog.Rows);
        Assert.Equal("M0C00Y11", catalog.Rows[0].Name);
        Assert.True(catalog.Rows.Single(row => row.Name == "M0B1XY01").Adult);
        Assert.True(catalog.Rows.Single(row => row.Name == "M0B11Y18").OneTime);
        Assert.Equal("M", catalog.Rows.Single(row => row.Name == "M0B11Y18").Membership);
    }
}
