using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallQuestCatalogTests
{
    [Fact]
    public void Classic_catalog_keeps_every_row_and_names_quests_the_pack_lacks()
    {
        var sources = DaggerfallQuestWorldTablesTests.Content().QuestSources;
        var catalog = sources.Catalog;
        Assert.Equal(187, catalog.Rows.Count(row => row.Active));
        Assert.Equal(23, catalog.Rows.Count(row => !row.Active));
        Assert.Equal(new[] { "M0B40Y04", "N0C00Y01", "A0C00Y04", "R0C40Y23", "80C00Y00" },
            catalog.Rows.Where(row => !sources.Quests.ContainsKey(row.Name + ".txt")).Select(row => row.Name));
        Assert.Equal("M0C00Y11", catalog.Rows[0].Name);
        Assert.True(catalog.Rows.Single(row => row.Name == "M0B1XY01").Adult);
        Assert.True(catalog.Rows.Single(row => row.Name == "M0B11Y18").OneTime);
        Assert.Equal("M", catalog.Rows.Single(row => row.Name == "M0B11Y18").Membership);
    }
}
