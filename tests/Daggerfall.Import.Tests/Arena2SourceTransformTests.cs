using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2SourceTransformTests
{
    [Fact]
    public void ConvertsArch3dPointsFromSubUnitsAndFlipsY()
    {
        Arena2ImportPoint point = Arena2SourceTransform.ToImportPoint(new Arch3dPoint(256, -512, 128, 0, 0));

        Assert.Equal(0.025F, point.XMetres);
        Assert.Equal(0.05F, point.YMetres);
        Assert.Equal(0.0125F, point.ZMetres);
    }

    [Fact]
    public void ConvertsRdbPointsAndPlacesThemAtTheClassicBlockOrigin()
    {
        MapsDungeonBlock block = new("S0000007.RDB", 1, -1, true);
        Arena2ImportPoint origin = Arena2SourceTransform.ToBlockOrigin(block);
        Arena2ImportPoint point = Arena2SourceTransform.PlaceInBlock(Arena2SourceTransform.ToImportPoint(10, -20, 30), block);

        Assert.Equal(new Arena2ImportPoint(51.2F, 0F, -51.2F), origin);
        Assert.Equal(new Arena2ImportPoint(51.45F, 0.5F, -50.45F), point);
    }

    [Fact]
    public void ConvertsNegatedRdbRotationsFrom2048UnitsPerTurn()
    {
        RdbModelSource model = new(0, 0, 0, 512, -1024, 2048, 0, "", "", 0, null);

        Arena2EulerDegrees rotation = Arena2SourceTransform.ToEulerDegrees(model);

        Assert.Equal(-90F, rotation.X);
        Assert.Equal(180F, rotation.Y);
        Assert.Equal(-360F, rotation.Z);
    }

    [Fact]
    public void ConvertsArch3dUvSubUnitsToTextureFractions()
    {
        Arena2TextureUv uv = Arena2SourceTransform.ToTextureUv(new Arch3dPoint(0, 0, 0, 32, 48), 64, 32);

        Assert.Equal(0.03125F, uv.U);
        Assert.Equal(0.09375F, uv.V);
    }

    [Fact]
    public void ReversesWindingExplicitlyForTheMirroredTargetSpace()
    {
        Arena2Triangle triangle = Arena2SourceTransform.ReverseWinding(0, 2, 1);

        Assert.Equal(new Arena2Triangle(0, 1, 2), triangle);
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    [InlineData(-1, 16)]
    public void RejectsMalformedTextureDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena2SourceTransform.ToTextureUv(default, width, height));
    }

    [Theory]
    [InlineData(-1, 1, 2)]
    [InlineData(0, 0, 2)]
    public void RejectsMalformedTriangleIndices(int first, int second, int third)
    {
        Assert.ThrowsAny<ArgumentException>(() => Arena2SourceTransform.ReverseWinding(first, second, third));
    }

    [Fact]
    public void RejectsNonFiniteLocalBlockPoint()
    {
        MapsDungeonBlock block = new("S0000007.RDB", 0, 0, false);
        Arena2ImportPoint nonFinite = new(float.NaN, 0F, 0F);

        Assert.Throws<ArgumentOutOfRangeException>(() => Arena2SourceTransform.PlaceInBlock(nonFinite, block));
    }
}
