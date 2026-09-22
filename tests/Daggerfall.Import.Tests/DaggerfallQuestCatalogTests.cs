using System.Text;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DaggerfallQuestCatalogTests
{
    [Fact]
    public void Catalog_preserves_order_flags_missing_sources_and_disabled_absence()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("-- introduction\nschema: *name, group, membership, minReq, flag, notes\nA, FightersGuild, M, 2, X, first, with comma\n-B, Commoners, F, 12, 1, disabled\n-C, Oblivion, 0, 0, omitted\nD, Nobility, N, 3, 0, last");
        var catalog = DaggerfallQuestCatalogReader.Read(bytes, "Tables/QuestList-Classic.txt", ["a.txt", "D.txt"]);
        Assert.Equal(new[] { "A", "B", "C", "D" }, catalog.Rows.Select(row => row.Name));
        Assert.Equal(new[] { "rank", "reputation", "unspecified", "level" }, catalog.Rows.Select(row => row.RequirementKind));
        Assert.Equal(new[] { 3, 4, 5, 6 }, catalog.Rows.Select(row => row.SourceLine));
        Assert.True(catalog.Rows[0].Adult);
        Assert.Equal("first, with comma", catalog.Rows[0].Notes);
        Assert.True(catalog.Rows[1].OneTime);
        Assert.False(catalog.Rows[1].Active);
        Assert.Null(catalog.Rows[2].Membership);
        Assert.Equal(new[] { "present", "missing", "missing", "present" }, catalog.Rows.Select(row => row.SourceDisposition));
        Assert.Equal(ContentDigest.Compute(bytes), catalog.Source.ContentHash);
    }
}
