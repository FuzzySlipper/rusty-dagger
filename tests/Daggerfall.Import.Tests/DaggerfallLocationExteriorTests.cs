using Daggerfall.Import.Arena2;
using System.Buffers.Binary;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DaggerfallLocationExteriorTests
{
    [CorpusFact]
    public void Index_keyed_exterior_layout_preserves_duplicate_names_in_the_same_region()
    {
        BsaArchive maps = BsaArchive.Parse(File.ReadAllBytes(TestData.Corpus("MAPS.BSA")), "arena2/MAPS.BSA");
        MapsLocationRecord[] duplicates = MapsDecoder.DecodeRegionLocations(maps, 17)
            .GroupBy(location => location.Name, StringComparer.Ordinal)
            .First(group => group.Count() > 1)
            .ToArray();

        MapsExteriorLayout first = MapsDecoder.DecodeExteriorLayout(maps, 17, duplicates[0].Index);
        MapsExteriorLayout second = MapsDecoder.DecodeExteriorLayout(maps, 17, duplicates[1].Index);

        Assert.Equal(duplicates[0].Index, first.LocationIndex);
        Assert.Equal(duplicates[1].Index, second.LocationIndex);
        Assert.NotEqual(first.LocationIndex, second.LocationIndex);
        Assert.Equal(duplicates[0].Name, first.LocationName);
        Assert.Equal(duplicates[1].Name, second.LocationName);
    }

    [CorpusFact]
    public void Publishes_complete_exterior_metadata_for_every_real_location()
    {
        BsaArchive maps = BsaArchive.Parse(File.ReadAllBytes(TestData.Corpus("MAPS.BSA")), "arena2/MAPS.BSA");
        BsaArchive blocks = BsaArchive.Parse(File.ReadAllBytes(TestData.Corpus("BLOCKS.BSA")), "arena2/BLOCKS.BSA");

        BsaArchive models = BsaArchive.Parse(File.ReadAllBytes(TestData.Corpus("ARCH3D.BSA")), "arena2/ARCH3D.BSA");
        DaggerfallLocations locations = DaggerfallLocationBuilder.Build(maps, blocks, models);

        Assert.Equal(15251, locations.Locations.Count);
        Assert.Equal(15251, locations.Locations.Count(location => location.Exterior is not null));
        Assert.Equal(444274, locations.Locations.Sum(location => location.Exterior!.Buildings.Count));
        Assert.All(locations.Locations, location => Assert.Empty(location.Exterior!.MissingCityBuildings));
        DaggerfallLocationMap charing = Assert.Single(locations.Locations, location => location.Region == 17 && location.Index == 4);
        DaggerfallLocationBuilding[] reusedArmorer = [.. charing.Exterior!.Buildings.Where(building => building.SourceKey == "ARMRAL00.RMB" && building.BuildingIndex == 0)];
        Assert.Equal(2, reusedArmorer.Length);
        Assert.Equal((4, 3, 510, 15941, 16), (reusedArmorer[0].BlockX, reusedArmorer[0].BlockY, reusedArmorer[0].FactionId, reusedArmorer[0].NameSeed, reusedArmorer[0].Quality));
        Assert.Equal((3, 5, 510, 18089, 15), (reusedArmorer[1].BlockX, reusedArmorer[1].BlockY, reusedArmorer[1].FactionId, reusedArmorer[1].NameSeed, reusedArmorer[1].Quality));
        MapsExteriorLayout rawLayout = MapsDecoder.DecodeExteriorLayout(maps, charing.Region, charing.Index);
        Assert.Equal(rawLayout.Buildings.Select(value => (value.LocationId, value.Sector)),
            charing.Exterior!.BuildingReferences.Select(value => (value.LocationId, value.Sector)));
        foreach (DaggerfallLocationBuilding building in charing.Exterior.Buildings)
        {
            Assert.True(blocks.TryGetByName(building.SourceKey, out BsaRecord? rawBlock));
            byte[] raw = blocks.GetPayload(rawBlock!).ToArray();
            int offset = 643 + building.BuildingIndex * 26 + 22;
            Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(offset)), building.SourceLocationId);
        }
        Assert.Equal(["arena2/MAPS.BSA", "arena2/BLOCKS.BSA", "arena2/ARCH3D.BSA"], locations.Sources);
        Assert.Contains(locations.Locations, value => value.Exterior!.PortTownAndUnknown > 0);
        Assert.Contains(locations.Locations, value => value.Exterior!.PortTownAndUnknown == 0);
        var pricedHouse = locations.Locations.SelectMany(value => value.Exterior!.Buildings)
            .First(value => value.BuildingType == 1 && value.ModelRadius > 0);
        Assert.True(models.TryGetByNumericId(uint.Parse(pricedHouse.ModelId!), out BsaRecord? model));
        // Classic DFMesh radius is the uint header at byte12 / 256, independent of mesh bounds.
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(models.GetPayload(model!).Span[12..]) / 256f, pricedHouse.ModelRadius);
        Assert.All(locations.Locations, location =>
        {
            DaggerfallLocationExterior exterior = Assert.IsType<DaggerfallLocationExterior>(location.Exterior);
            Assert.Equal(checked(exterior.Width * exterior.Height), exterior.Blocks.Count);
            Assert.InRange(exterior.MapPixelX, 0, 999);
            Assert.InRange(exterior.MapPixelY, 0, 499);
        });

        DaggerfallLocationMap hold = Assert.Single(locations.Locations, location => location.Region == 17 && location.Index == 179);
        DaggerfallLocationExterior holdExterior = Assert.IsType<DaggerfallLocationExterior>(hold.Exterior);
        Assert.Equal("Privateer's Hold", hold.Name);
        Assert.Equal(109, holdExterior.MapPixelX);
        Assert.Equal(158, holdExterior.MapPixelY);
        Assert.Equal(2, holdExterior.BlendClearance);
        Assert.Equal(72, holdExterior.TileOriginX);
        Assert.Equal(55, holdExterior.TileOriginY);
        Assert.True(holdExterior.UsesCustomLocationPosition);
        Assert.Equal(new DaggerfallLocationTerrainRect(70, 89, 53, 72), holdExterior.FlattenRect);
        Assert.NotEmpty(holdExterior.Blocks);
    }
}
