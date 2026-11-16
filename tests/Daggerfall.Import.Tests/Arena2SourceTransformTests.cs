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
    public void ConvertsRdbPointsFromWholeUnitsAndFlipsY()
    {
        Assert.Equal(new Arena2ImportPoint(0.25F, 0.5F, 0.75F), Arena2SourceTransform.ToImportPoint(10, -20, 30));
    }

    [Fact]
    public void PreservesTheDonorRmbPointsAndBuildingOrigins()
    {
        RmbBuildingSlot building = new(0, 800, 1200, 512, 34, 0, 0, 0, 0, 0, new(0, 0, 0, 0, 0), new(0, 0, 0, 0, 0));
        Arena2ImportPoint buildingOrigin = Arena2SourceTransform.ToRmbBuildingOrigin(building);

        Assert.Equal(new Arena2ImportPoint(2.5F, 1F, 5F), Arena2SourceTransform.ToRmbImportPoint(100, -40, 200));
        Assert.Equal(new Arena2ImportPoint(20F, 0F, 72.4F), buildingOrigin);
        Assert.Equal(-90F, Arena2SourceTransform.ToRmbYawDegrees(512));
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

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    [InlineData(-1, 16)]
    public void RejectsMalformedTextureDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena2SourceTransform.ToTextureUv(default, width, height));
    }
}
