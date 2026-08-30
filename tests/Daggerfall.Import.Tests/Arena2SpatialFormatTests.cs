using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2SpatialFormatTests
{
    [Fact]
    public void Arch3dDecodesV26AndV25PointOffsetsAndTriangleUvDeltas()
    {
        Arch3dMesh v26 = Arch3dDecoder.Decode(CreateArch3dFixture("v2.6", 12), "arch-v26", 61000);
        Assert.Equal("v2.6", v26.Version);
        Assert.Equal((2, 3), ((int)v26.Planes[0].TextureArchive, (int)v26.Planes[0].TextureRecord));
        Assert.Equal(new Arch3dPoint(256, 0, 0, 15, 26), v26.Planes[0].Points[1]);
        Assert.Equal(new Arch3dPoint(256, 256, 0, 22, 34), v26.Planes[0].Points[2]);

        Arch3dMesh v25 = Arch3dDecoder.Decode(CreateArch3dFixture("v2.5", 4), "arch-v25", 61000);
        Assert.Equal(new Arch3dPoint(256, 256, 0, 22, 34), v25.Planes[0].Points[2]);

        byte[] malformed = CreateArch3dFixture("v2.6", 12);
        malformed[108..112].CopyTo(malformed.AsSpan(0, 4));
        Assert.Throws<Arena2FormatException>(() => Arch3dDecoder.Decode(malformed[..120], "truncated-arch", 61000));
    }

    [Fact]
    public void MapsLinksLocationToDungeonBlockAndRejectsTruncatedNames()
    {
        BsaArchive archive = BsaArchive.Parse(CreateNamedBsa(
            ("MAPNAMES.017", CreateMapNames()),
            ("MAPTABLE.017", CreateMapTable()),
            ("MAPPITEM.017", CreateMapPItem()),
            ("MAPDITEM.017", CreateMapDItem())), "maps-fixture");
        MapsDungeonLayout layout = MapsDecoder.DecodeDungeonLayout(archive, 17, "Fixture Hold");

        Assert.Equal((42, 99U, 1280, 2560, (byte)2), (layout.MapId, layout.LocationId, layout.Longitude, layout.Latitude, layout.DungeonType));
        Assert.Equal(new MapsDungeonBlock("S0000007.RDB", 1, -1, true), Assert.Single(layout.Blocks));
        Assert.Equal((10, 479), MapsDecoder.ToMapPixel(1280, 2560));

        BsaArchive truncated = BsaArchive.Parse(CreateNamedBsa(("MAPNAMES.017", BitConverter.GetBytes(1U))), "truncated-maps");
        Assert.Throws<Arena2FormatException>(() => MapsDecoder.DecodeLocationNames(truncated, 17));
    }

    [Fact]
    public void PakPreservesTheSentinelColumnAndRejectsInvalidRuns()
    {
        byte[] bytes = CreateConstantPak(231);
        PakMap map = PakDecoder.Decode(bytes, "climate.pak");
        Assert.True(map.TryGetPixel(1000, 499, out byte sentinel));
        Assert.Equal((byte)231, sentinel);
        Assert.False(map.TryGetPixel(1001, 499, out _));

        bytes[2000..2002].CopyTo(bytes.AsSpan(0, 2));
        Assert.Throws<Arena2FormatException>(() => PakDecoder.Decode(bytes, "bad-pak"));
    }

    [Fact]
    public void RdbRetainsRawSourceFactsAndRejectsObjectListCycles()
    {
        byte[] fixture = CreateRdbFixture();
        RdbBlockSource block = RdbDecoder.Decode(fixture, "block.rdb");
        RdbModelSource model = Assert.Single(block.Models);
        Assert.Equal("42", model.ModelId);
        Assert.True(RdbSourceClassification.HasActionDoorTag(model));
        Assert.True(RdbSourceClassification.IsStartMarker(Assert.Single(block.Flats)));
        Assert.Equal((ushort)512, Assert.Single(block.Lights).Radius);

        fixture[6024..6028].CopyTo(fixture.AsSpan(6024, 4));
        BitConverter.GetBytes(6024).CopyTo(fixture, 6024);
        Assert.Throws<Arena2FormatException>(() => RdbDecoder.Decode(fixture, "cyclic.rdb"));
    }

    [Fact]
    public void RdbRejectsOneObjectChainSharedAcrossCellRoots()
    {
        byte[] fixture = CreateRdbFixture();
        const int sharedRoots = 6143;
        Array.Resize(ref fixture, sharedRoots + (2 * sizeof(int)));
        BitConverter.GetBytes(2U).CopyTo(fixture, 4);
        BitConverter.GetBytes(1U).CopyTo(fixture, 8);
        BitConverter.GetBytes((uint)sharedRoots).CopyTo(fixture, 12);
        BitConverter.GetBytes(6024).CopyTo(fixture, sharedRoots);
        BitConverter.GetBytes(6024).CopyTo(fixture, sharedRoots + sizeof(int));

        Assert.Throws<Arena2FormatException>(() => RdbDecoder.Decode(fixture, "shared-chain.rdb"));
    }

    private static byte[] CreateArch3dFixture(string version, int pointOffsetStride)
    {
        byte[] data = new byte[132];
        System.Text.Encoding.ASCII.GetBytes(version).CopyTo(data, 0);
        BitConverter.GetBytes(3).CopyTo(data, 4);
        BitConverter.GetBytes(1).CopyTo(data, 8);
        BitConverter.GetBytes(64).CopyTo(data, 48);
        BitConverter.GetBytes(100).CopyTo(data, 60);
        WriteVector(data, 64, [0, 0, 0]);
        WriteVector(data, 76, [256, 0, 0]);
        WriteVector(data, 88, [256, 256, 0]);
        data[100] = 3;
        BitConverter.GetBytes((ushort)((2 << 7) | 3)).CopyTo(data, 102);
        foreach ((int index, short u, short v) in new[] { (0, (short)10, (short)20), (1, (short)5, (short)6), (2, (short)7, (short)8) })
        {
            int offset = 108 + (index * 8);
            BitConverter.GetBytes(index * pointOffsetStride).CopyTo(data, offset);
            BitConverter.GetBytes(u).CopyTo(data, offset + 4);
            BitConverter.GetBytes(v).CopyTo(data, offset + 6);
        }

        return data;
    }

    private static byte[] CreateMapNames()
    {
        byte[] names = new byte[36];
        BitConverter.GetBytes(1U).CopyTo(names, 0);
        System.Text.Encoding.ASCII.GetBytes("Fixture Hold").CopyTo(names, 4);
        return names;
    }

    private static byte[] CreateMapTable()
    {
        byte[] table = new byte[17];
        BitConverter.GetBytes(42).CopyTo(table, 0);
        BitConverter.GetBytes(1280U << 8).CopyTo(table, 4);
        BitConverter.GetBytes(2560 << 8).CopyTo(table, 8);
        table[12] = 2;
        return table;
    }

    private static byte[] CreateMapPItem()
    {
        byte[] pitem = new byte[47];
        BitConverter.GetBytes(0U).CopyTo(pitem, 0);
        BitConverter.GetBytes(7).CopyTo(pitem, 41);
        return pitem;
    }

    private static byte[] CreateMapDItem()
    {
        byte[] ditem = new byte[149];
        BitConverter.GetBytes(1U).CopyTo(ditem, 0);
        BitConverter.GetBytes(0U).CopyTo(ditem, 4);
        BitConverter.GetBytes((ushort)1).CopyTo(ditem, 8);
        BitConverter.GetBytes((ushort)7).CopyTo(ditem, 10);
        BitConverter.GetBytes(0U).CopyTo(ditem, 12);
        BitConverter.GetBytes(99U).CopyTo(ditem, 49);
        BitConverter.GetBytes((ushort)1).CopyTo(ditem, 138);
        ditem[145] = 1;
        ditem[146] = 255;
        BitConverter.GetBytes((ushort)((3 << 11) | 0x400 | 7)).CopyTo(ditem, 147);
        return ditem;
    }

    private static byte[] CreateConstantPak(byte value)
    {
        const int tableBytes = PakMap.Height * sizeof(uint);
        byte[] result = new byte[tableBytes + (PakMap.Height * 3)];
        for (int row = 0; row < PakMap.Height; row++)
        {
            int runOffset = tableBytes + (row * 3);
            BitConverter.GetBytes((uint)runOffset).CopyTo(result, row * sizeof(uint));
            BitConverter.GetBytes((ushort)PakMap.Width).CopyTo(result, runOffset);
            result[runOffset + 2] = value;
        }

        return result;
    }

    private static byte[] CreateRdbFixture()
    {
        const int roots = 6020;
        const int modelNode = 6024;
        const int flatNode = 6049;
        const int lightNode = 6074;
        const int modelResource = 6099;
        const int flatResource = 6122;
        const int lightResource = 6133;
        byte[] data = new byte[6143];
        BitConverter.GetBytes(1U).CopyTo(data, 4);
        BitConverter.GetBytes(1U).CopyTo(data, 8);
        BitConverter.GetBytes((uint)roots).CopyTo(data, 12);
        System.Text.Encoding.ASCII.GetBytes("42\0").CopyTo(data, 20);
        System.Text.Encoding.ASCII.GetBytes("DOR").CopyTo(data, 25);
        BitConverter.GetBytes(modelNode).CopyTo(data, roots);
        WriteNode(data, modelNode, flatNode, [100, -200, 300], 1, modelResource);
        WriteNode(data, flatNode, lightNode, [10, -20, 30], 3, flatResource);
        WriteNode(data, lightNode, -1, [1, -2, 3], 2, lightResource);
        BitConverter.GetBytes((ushort)0).CopyTo(data, modelResource + 12);
        BitConverter.GetBytes((ushort)((RdbSourceClassification.EditorFlatArchive << 7) | RdbSourceClassification.StartMarkerRecord)).CopyTo(data, flatResource);
        data[flatResource + 4] = 7;
        BitConverter.GetBytes(-1).CopyTo(data, flatResource + 6);
        BitConverter.GetBytes((ushort)512).CopyTo(data, lightResource + 8);
        return data;
    }

    private static byte[] CreateNamedBsa(params (string Name, byte[] Payload)[] records)
    {
        List<byte> result = [];
        result.AddRange(BitConverter.GetBytes((short)records.Length));
        result.AddRange(BitConverter.GetBytes(Arena2FormatConstants.NamedBsaDirectoryType));
        foreach ((_, byte[] payload) in records)
        {
            result.AddRange(payload);
        }

        foreach ((string name, byte[] payload) in records)
        {
            result.AddRange(System.Text.Encoding.ASCII.GetBytes(name));
            result.AddRange(new byte[14 - name.Length]);
            result.AddRange(BitConverter.GetBytes(payload.Length));
        }

        return result.ToArray();
    }

    private static void WriteNode(byte[] data, int offset, int next, int[] position, byte type, int resourceOffset)
    {
        BitConverter.GetBytes(next).CopyTo(data, offset);
        BitConverter.GetBytes(-1).CopyTo(data, offset + 4);
        WriteVector(data, offset + 8, position);
        data[offset + 20] = type;
        BitConverter.GetBytes(resourceOffset).CopyTo(data, offset + 21);
    }

    private static void WriteVector(byte[] data, int offset, int[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            BitConverter.GetBytes(values[index]).CopyTo(data, offset + (index * sizeof(int)));
        }
    }
}
