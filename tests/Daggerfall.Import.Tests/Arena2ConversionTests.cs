using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2ConversionTests
{
    [Fact]
    public void ClassicRandomMatchesTheSeedOneDonorVector()
    {
        ClassicDaggerfallRandom random = new(1);

        uint[] values = Enumerable.Range(0, 5).Select(_ => random.Next()).ToArray();

        Assert.Equal([16838U, 5758U, 10113U, 17515U, 31051U], values);
    }

    [Fact]
    public void ClassicRandomMatchesThePrivateersHoldDonorVector()
    {
        ClassicDaggerfallRandom random = new(50050);

        uint[] values = Enumerable.Range(0, 5).Select(_ => random.Next()).ToArray();

        Assert.Equal([29809U, 1548U, 11675U, 10363U, 3991U], values);
    }

    [Fact]
    public void ClassicRandomUsesAValidatedInclusiveRange()
    {
        ClassicDaggerfallRandom random = new(1);

        Assert.Equal(3, random.NextInclusive(0, 4));
        Assert.Equal(0, random.NextInclusive(-3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInclusive(3, 2));
    }

    [Fact]
    public void ClassicTextureTableMatchesThePrivateersHoldDonorGolden()
    {
        ushort[] table = DungeonTextureTableTransform.CreateClassic(50050, 231);

        Assert.Equal([23, 22, 19, 22, 20, 368], table);
        Assert.NotEqual(DungeonTextureTableTransform.CreateDefaultTable(), table);
    }

    [Fact]
    public void ClassicTextureTableMapsOceanToTheSwampClimatePath()
    {
        Assert.Equal(
            DungeonTextureTableTransform.CreateClassic(50050, 228),
            DungeonTextureTableTransform.CreateClassic(50050, 223));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(222)]
    [InlineData(233)]
    [InlineData(255)]
    public void ClassicTextureTableRejectsClimateOutsideTheSourceRange(byte worldClimate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DungeonTextureTableTransform.CreateClassic(50050, worldClimate));
    }

    [Theory]
    [InlineData(223)]
    [InlineData(224)]
    [InlineData(225)]
    [InlineData(226)]
    [InlineData(227)]
    [InlineData(228)]
    [InlineData(229)]
    [InlineData(230)]
    [InlineData(231)]
    [InlineData(232)]
    public void ClassicTextureTableAcceptsEverySourceClimate(byte worldClimate)
    {
        Assert.Equal(6, DungeonTextureTableTransform.CreateClassic(50050, worldClimate).Length);
    }

    [Fact]
    public void ClassicTextureTableReplacesTheInvalidRandomOffset()
    {
        Assert.Equal([123, 122, 119, 122, 120, 468], DungeonTextureTableTransform.CreateClassic(50050, 223));
        Assert.Equal((ushort)23, DungeonTextureTableTransform.CreateClassic(3, 231)[0]);
    }

    [Fact]
    public void ArchiveRemapUsesTheTableAndDoorClimateRule()
    {
        ushort[] table = [23, 22, 19, 22, 20, 368];

        Assert.Equal((ushort)23, DungeonTextureTableTransform.RemapArchive(119, table, 300));
        Assert.Equal((ushort)368, DungeonTextureTableTransform.RemapArchive(168, table, 300));
        Assert.Equal((ushort)374, DungeonTextureTableTransform.RemapArchive(74, table, 300));
        Assert.Equal((ushort)199, DungeonTextureTableTransform.RemapArchive(199, table, 300));
        Assert.Throws<ArgumentException>(() => DungeonTextureTableTransform.RemapArchive(119, [23], 300));
    }
}
