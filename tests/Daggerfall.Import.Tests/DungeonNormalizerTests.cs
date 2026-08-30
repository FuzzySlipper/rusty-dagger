using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DungeonNormalizerTests
{
    [Fact]
    public void NormalizesOneSyntheticLocationAcrossMapsBlocksArchPalettePakAndTextureSources()
    {
        DungeonNormalizationResult result = DungeonNormalizer.Normalize(Request(CreateSources()));

        result.Validate();
        NormalizedMesh mesh = Assert.Single(result.Document.Meshes);
        Assert.Equal("mesh/fixture-hold/texture-2-0/static", mesh.Id);
        Assert.Equal(3, mesh.Vertices.Count);
        Assert.Equal(new NormalizedVector3(51.2F, 0F, -51.2F), mesh.Vertices[0]);
        Assert.Equal(new NormalizedTriangle(0, 1, 2), Assert.Single(mesh.Triangles));
        Assert.All(mesh.MaterialGroups, group => Assert.True(group.ParticipatesInCollision));
        Assert.Equal("marker/start", result.Document.World.StartMarker!.Id);
        Assert.Single(result.Document.World.Lights);
        Assert.NotNull(result.Document.Navigation);
        Assert.All(result.Document.Navigation!.Cells, cell => Assert.True(cell.Walkable));
        Assert.Contains(result.RecordProvenance, record => record.Kind == "rdb-model");
        Assert.Contains(result.Document.Resources, resource => resource.Id == "material/texture-2-0");
        Assert.All(result.Document.Meshes, published => Assert.DoesNotContain("artifact/source", published.ArtifactId, StringComparison.Ordinal));
        Assert.All(result.Document.Artifacts, artifact => Assert.DoesNotContain("artifact/source", artifact.Id, StringComparison.Ordinal));
        Assert.Equal(3, result.SpatialPublication.Artifacts.Count);
        Assert.All(result.Document.Resources, resource => Assert.Equal(result.SpatialPublication.ResourceCatalog.Id, resource.ArtifactId));
        Assert.DoesNotContain("pixels", Encoding.UTF8.GetString(result.SpatialPublication.ResourceCatalog.Bytes.Span), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("encounter", Encoding.UTF8.GetString(NormalizedImportSerializer.Serialize(result.Document)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizationIsIndependentOfCallerSourceOrderingAndCopiesSourceBytes()
    {
        DungeonLogicalSource[] sources = CreateSources();
        byte[] originalMaps = sources.Single(source => source.Label == "MAPS.BSA").Bytes.ToArray();
        DungeonLogicalSource[] reversed = sources.Reverse().ToArray();
        DungeonNormalizationResult first = DungeonNormalizer.Normalize(Request(sources));
        DungeonNormalizationResult second = DungeonNormalizer.Normalize(Request(reversed));

        originalMaps[0] ^= 0x7F;
        Assert.Equal(NormalizedImportSerializer.Serialize(first.Document), NormalizedImportSerializer.Serialize(second.Document));
        Assert.Equal(first.RecordProvenance, second.RecordProvenance);
    }

    [Fact]
    public void FailsClosedForMalformedAndMissingReferencedSources()
    {
        DungeonLogicalSource[] malformed = CreateSources();
        Replace(malformed, "MAPS.BSA", [1, 2, 3]);
        Assert.Throws<Arena2FormatException>(() => DungeonNormalizer.Normalize(Request(malformed)));

        DungeonLogicalSource[] missingModel = CreateSources();
        Replace(missingModel, "ARCH3D.BSA", CreateNumericBsa((99U, CreateArch3dFixture())));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => DungeonNormalizer.Normalize(Request(missingModel)));
        Assert.Contains("missing numeric model", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnforcesExplicitQuotas()
    {
        DungeonNormalizationRequest request = Request(CreateSources()) with
        {
            Quotas = DungeonNormalizationQuotas.Default with { MaximumVertices = 2 },
        };

        Assert.Throws<InvalidOperationException>(() => DungeonNormalizer.Normalize(request));
    }

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x0063)]
    [InlineData(0x002A)]
    [InlineData(0x0100)]
    public void DoesNotInventActorsForReservedOrUnknownMobileLowBytes(ushort factionOrMobileId)
    {
        DungeonNormalizationResult result = DungeonNormalizer.Normalize(Request(CreateSourcesForFlat(2, 0, factionOrMobileId)));

        Assert.Empty(result.Document.World.Actors);
        Assert.DoesNotContain(result.Document.Resources, resource => resource.Id.StartsWith("actor/mobile-", StringComparison.Ordinal));
    }

    [Fact]
    public void RoutesOnlyFixedMobileMarkersAndRetainsOrdinaryLowByteMatchesAsBillboards()
    {
        DungeonNormalizationResult actor = DungeonNormalizer.Normalize(Request(CreateSourcesForFlat(
            RdbSourceClassification.EditorFlatArchive,
            RdbSourceClassification.FixedMobileMarkerRecord,
            0x0101)));
        DungeonNormalizationResult rat = DungeonNormalizer.Normalize(Request(CreateSourcesForFlat(
            RdbSourceClassification.EditorFlatArchive,
            RdbSourceClassification.FixedMobileMarkerRecord,
            0xAB00)));
        DungeonNormalizationResult invalidMarker = DungeonNormalizer.Normalize(Request(CreateSourcesForFlat(
            RdbSourceClassification.EditorFlatArchive,
            RdbSourceClassification.FixedMobileMarkerRecord,
            0x0063)));
        DungeonNormalizationResult ordinaryBillboard = DungeonNormalizer.Normalize(Request(CreateSourcesForFlat(2, 0, 0x0101)));
        DungeonNormalizationResult marker = DungeonNormalizer.Normalize(Request(CreateSourcesForFlat(
            RdbSourceClassification.EditorFlatArchive,
            RdbSourceClassification.StartMarkerRecord,
            1)));
        DungeonNormalizationResult treasure = DungeonNormalizer.Normalize(Request(CreateSourcesForFlat(
            RdbSourceClassification.EditorFlatArchive,
            RdbSourceClassification.RandomTreasureMarkerRecord,
            1)));

        Assert.Equal("actor/mobile-1", Assert.Single(actor.Document.World.Actors).ActorResourceId);
        Assert.Contains(actor.Document.Resources, resource => resource.Id == "actor/mobile-1");
        Assert.Equal("actor/mobile-0", Assert.Single(rat.Document.World.Actors).ActorResourceId);
        Assert.Empty(invalidMarker.Document.World.Actors);
        Assert.Empty(invalidMarker.Document.World.Billboards);
        Assert.Empty(ordinaryBillboard.Document.World.Actors);
        Assert.Single(ordinaryBillboard.Document.World.Billboards);
        Assert.NotNull(marker.Document.World.StartMarker);
        Assert.Empty(marker.Document.World.Actors);
        Assert.Single(treasure.Document.World.Treasures);
        Assert.Empty(treasure.Document.World.Actors);
    }

    [Fact]
    public void RetainsActionDoorGeometryForVisualPublicationButExcludesItFromCollision()
    {
        DungeonLogicalSource[] sources = CreateSources();
        Replace(sources, "BLOCKS.BSA", CreateNamedBsa(("S0000007.RDB", CreateRdbFixture(modelDescription: "DOR"))));

        DungeonNormalizationResult result = DungeonNormalizer.Normalize(Request(sources));

        Assert.Single(result.Document.World.Doors);
        NormalizedMesh visualDoor = Assert.Single(result.Document.Meshes, mesh => mesh.Id.EndsWith("/action-visual", StringComparison.Ordinal));
        Assert.All(visualDoor.MaterialGroups, group => Assert.False(group.ParticipatesInCollision));
        Assert.All(result.Document.World.MeshIds, meshId => Assert.Contains(result.Document.Meshes, mesh => mesh.Id == meshId));
        Assert.Equal([visualDoor.Id], Assert.Single(result.Document.World.Doors).VisualMeshIds);
        string collisionNavigation = Encoding.UTF8.GetString(result.SpatialPublication.CollisionNavigation.Bytes.Span);
        Assert.Contains("\"triangles\": []", collisionNavigation, StringComparison.Ordinal);
        Assert.Contains("\"positions\": []", collisionNavigation, StringComparison.Ordinal);
    }

    private static DungeonNormalizationRequest Request(IEnumerable<DungeonLogicalSource> sources) =>
        DungeonNormalizationRequest.Create(new DungeonLogicalSourceSet(sources), 17, "Fixture Hold");

    private static DungeonLogicalSource[] CreateSources() =>
    [
        new("MAPS.BSA", CreateNamedBsa(
            ("MAPNAMES.017", CreateMapNames()),
            ("MAPTABLE.017", CreateMapTable()),
            ("MAPPITEM.017", CreateMapPItem()),
            ("MAPDITEM.017", CreateMapDItem()))),
        new("BLOCKS.BSA", CreateNamedBsa(("S0000007.RDB", CreateRdbFixture()))),
        new("ARCH3D.BSA", CreateNumericBsa((42U, CreateArch3dFixture()))),
        new("PAL.PAL", new byte[768]),
        new("CLIMATE.PAK", CreateConstantPak(231)),
        new("TEXTURE.002", CreateTexture()),
    ];

    private static DungeonLogicalSource[] CreateSourcesForFlat(ushort textureArchive, ushort textureRecord, ushort factionOrMobileId) =>
    [
        new("MAPS.BSA", CreateNamedBsa(
            ("MAPNAMES.017", CreateMapNames()),
            ("MAPTABLE.017", CreateMapTable()),
            ("MAPPITEM.017", CreateMapPItem()),
            ("MAPDITEM.017", CreateMapDItem()))),
        new("BLOCKS.BSA", CreateNamedBsa(("S0000007.RDB", CreateRdbFixture(textureArchive, textureRecord, factionOrMobileId)))),
        new("ARCH3D.BSA", CreateNumericBsa((42U, CreateArch3dFixture()))),
        new("PAL.PAL", new byte[768]),
        new("CLIMATE.PAK", CreateConstantPak(231)),
        new("TEXTURE.002", CreateTexture()),
    ];

    private static void Replace(DungeonLogicalSource[] sources, string label, byte[] bytes)
    {
        int index = Array.FindIndex(sources, source => source.Label == label);
        Assert.True(index >= 0);
        sources[index] = new DungeonLogicalSource(label, bytes);
    }

    private static byte[] CreateMapNames()
    {
        byte[] names = new byte[36];
        BitConverter.GetBytes(1U).CopyTo(names, 0);
        Encoding.ASCII.GetBytes("Fixture Hold").CopyTo(names, 4);
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
        ditem[146] = 1;
        BitConverter.GetBytes((ushort)((3 << 11) | 0x400 | 7)).CopyTo(ditem, 147);
        return ditem;
    }

    private static byte[] CreateRdbFixture(
        ushort flatTextureArchive = RdbSourceClassification.EditorFlatArchive,
        ushort flatTextureRecord = RdbSourceClassification.StartMarkerRecord,
        ushort factionOrMobileId = 0,
        string modelDescription = "MOD")
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
        Encoding.ASCII.GetBytes("42\0").CopyTo(data, 20);
        Encoding.ASCII.GetBytes(modelDescription).CopyTo(data, 25);
        BitConverter.GetBytes(modelNode).CopyTo(data, roots);
        WriteNode(data, modelNode, flatNode, [0, 0, 0], 1, modelResource);
        WriteNode(data, flatNode, lightNode, [10, -20, 30], 3, flatResource);
        WriteNode(data, lightNode, -1, [1, -2, 3], 2, lightResource);
        BitConverter.GetBytes((ushort)0).CopyTo(data, modelResource + 12);
        BitConverter.GetBytes((ushort)((flatTextureArchive << 7) | flatTextureRecord)).CopyTo(data, flatResource);
        data[flatResource + 4] = (byte)factionOrMobileId;
        data[flatResource + 5] = (byte)(factionOrMobileId >> 8);
        BitConverter.GetBytes(-1).CopyTo(data, flatResource + 6);
        BitConverter.GetBytes((ushort)512).CopyTo(data, lightResource + 8);
        return data;
    }

    private static byte[] CreateArch3dFixture()
    {
        byte[] data = new byte[132];
        Encoding.ASCII.GetBytes("v2.6").CopyTo(data, 0);
        BitConverter.GetBytes(3).CopyTo(data, 4);
        BitConverter.GetBytes(1).CopyTo(data, 8);
        BitConverter.GetBytes(64).CopyTo(data, 48);
        BitConverter.GetBytes(100).CopyTo(data, 60);
        WriteVector(data, 64, [0, 0, 0]);
        WriteVector(data, 76, [256, 0, 0]);
        WriteVector(data, 88, [0, 0, 256]);
        data[100] = 3;
        BitConverter.GetBytes((ushort)((2 << 7) | 0)).CopyTo(data, 102);
        foreach ((int index, short u, short v) in new[] { (0, (short)0, (short)0), (1, (short)32, (short)0), (2, (short)0, (short)32) })
        {
            int offset = 108 + (index * 8);
            BitConverter.GetBytes(index * 12).CopyTo(data, offset);
            BitConverter.GetBytes(u).CopyTo(data, offset + 4);
            BitConverter.GetBytes(v).CopyTo(data, offset + 6);
        }

        return data;
    }

    private static byte[] CreateTexture()
    {
        const int recordOffset = 46;
        const int dataOffset = 28;
        byte[] bytes = new byte[recordOffset + dataOffset + 258];
        BitConverter.GetBytes((short)1).CopyTo(bytes, 0);
        BitConverter.GetBytes(recordOffset).CopyTo(bytes, 28);
        BitConverter.GetBytes((short)2).CopyTo(bytes, recordOffset + 4);
        BitConverter.GetBytes((short)2).CopyTo(bytes, recordOffset + 6);
        BitConverter.GetBytes((uint)dataOffset).CopyTo(bytes, recordOffset + 14);
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, recordOffset + 20);
        bytes[recordOffset + dataOffset] = 1;
        bytes[recordOffset + dataOffset + 1] = 2;
        bytes[recordOffset + dataOffset + 256] = 3;
        bytes[recordOffset + dataOffset + 257] = 4;
        return bytes;
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

    private static byte[] CreateNamedBsa(params (string Name, byte[] Payload)[] records)
    {
        List<byte> result = [];
        result.AddRange(BitConverter.GetBytes((short)records.Length));
        result.AddRange(BitConverter.GetBytes(Arena2FormatConstants.NamedBsaDirectoryType));
        foreach ((_, byte[] payload) in records) result.AddRange(payload);
        foreach ((string name, byte[] payload) in records)
        {
            result.AddRange(Encoding.ASCII.GetBytes(name));
            result.AddRange(new byte[14 - name.Length]);
            result.AddRange(BitConverter.GetBytes(payload.Length));
        }

        return result.ToArray();
    }

    private static byte[] CreateNumericBsa(params (uint Id, byte[] Payload)[] records)
    {
        List<byte> result = [];
        result.AddRange(BitConverter.GetBytes((short)records.Length));
        result.AddRange(BitConverter.GetBytes(Arena2FormatConstants.NumericBsaDirectoryType));
        foreach ((_, byte[] payload) in records) result.AddRange(payload);
        foreach ((uint id, byte[] payload) in records)
        {
            result.AddRange(BitConverter.GetBytes(id));
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
