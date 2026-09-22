using System.Text;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DaggerfallQuestPlaceTests
{
    [Fact]
    public void Preserves_source_alias_code_and_teleport_byte()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("schema: *name,p1,p2,p3\nMantellan_Crux, 0xc352, 0xfa01, -1\nMantellanCrux, 0xc350, 0xfa01, -1\nmagery, 0, 11, 40");
        DaggerfallQuestPlaces places = DaggerfallQuestPlaceReader.Read(bytes, "Tables/places.txt");
        Assert.Equal("Mantellan_Crux", places.Rows[0].Name);
        Assert.Equal("MantellanCrux", places.Rows[0].CanonicalName);
        Assert.Equal(0xc352fa01u, places.Rows[0].LocationKey);
        Assert.Equal((byte)1, places.Rows[0].TeleportTransfer);
        Assert.Equal(-1, places.Rows[0].P3);
        Assert.Equal(40, places.Rows[2].P3);
        Assert.Null(places.Rows[2].LocationKey);
        Assert.Null(places.Rows[2].TeleportTransfer);
        Assert.Equal(ContentDigest.Compute(bytes), places.Source.ContentHash);
        Assert.Equal(new[] { 2, 3, 4 }, places.Rows.Select(row => row.SourceLine));
    }
}
