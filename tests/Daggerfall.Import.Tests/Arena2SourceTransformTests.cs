using System.Numerics;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
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

    [Theory]
    [InlineData(0, 512, 0)]
    [InlineData(256, -1024, 0)]
    [InlineData(1024, 512, 1024)]
    [InlineData(-300, 700, 150)]
    public void TheRightHandedRotationTurnsAPointAsTheImportPlacesIt(int x, int y, int z)
    {
        // The import places an RDB model's points by the donor's Rz * Rx * Ry in its left-handed frame, then flips Z;
        // the published rotation turns the flipped point to the same place.
        RdbModelSource model = new(0, 0, 0, x, y, z, 0, "", "", 0, null);
        RdbBlockModel placed = new(0, model, false, false, 0);
        Quaternion rotation = Arena2SourceTransform.ToRightHandedRotation(Arena2SourceTransform.ToEulerDegrees(model));
        foreach (Arena2ImportPoint point in new Arena2ImportPoint[] { new(1F, 0F, 0F), new(0F, 1F, 0F), new(0F, 0F, 1F), new(.3F, -2F, 1.5F) })
        {
            NormalizedVector3 expected = MeshGeometry.ToRightHanded(placed.Rotate(point));
            NormalizedVector3 flipped = MeshGeometry.ToRightHanded(point);
            Vector3 turned = Vector3.Transform(new Vector3(flipped.X, flipped.Y, flipped.Z), rotation);
            Assert.Equal(expected.X, turned.X, 4);
            Assert.Equal(expected.Y, turned.Y, 4);
            Assert.Equal(expected.Z, turned.Z, 4);
        }

        Assert.Equal(placed.Rotation, NormalizedQuaternion.From(rotation));
    }

    [Theory]
    [InlineData(90F)]
    [InlineData(-35F)]
    [InlineData(180F)]
    public void ARightHandedYawTurnsAPointAsAnRmbPlacementDoes(float sourceYaw)
    {
        Quaternion rotation = Arena2SourceTransform.ToRightHandedYaw(sourceYaw);
        foreach (Arena2ImportPoint point in new Arena2ImportPoint[] { new(1F, 0F, 0F), new(0F, 0F, 1F), new(.3F, -2F, 1.5F) })
        {
            NormalizedVector3 expected = MeshGeometry.ToRightHanded(RmbYaw.Degrees(sourceYaw).Transform(point));
            NormalizedVector3 flipped = MeshGeometry.ToRightHanded(point);
            Vector3 turned = Vector3.Transform(new Vector3(flipped.X, flipped.Y, flipped.Z), rotation);
            Assert.Equal(expected.X, turned.X, 4);
            Assert.Equal(expected.Y, turned.Y, 4);
            Assert.Equal(expected.Z, turned.Z, 4);
        }
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
