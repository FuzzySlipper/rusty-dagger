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

        // A placement whose mesh the archive cannot serve is preserved as an unresolved reference rather
        // than thrown: only when nothing at all can be built does normalization fail on empty geometry.
        DungeonLogicalSource[] missingModel = CreateSources();
        Replace(missingModel, "ARCH3D.BSA", CreateNumericBsa((99U, CreateArch3dFixture())));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => DungeonNormalizer.Normalize(Request(missingModel)));
        Assert.Contains("produced no static geometry", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_logical_source_reports_its_typed_name_for_loader_discovery()
    {
        // The retry coordinator discovers lazy sources from this data, never from the message.
        DungeonLogicalSourceSet set = new(CreateSources());
        MissingArena2SourceException missing = Assert.Throws<MissingArena2SourceException>(() => set.Require("TEXTURE.999"));
        Assert.Equal("TEXTURE.999", missing.SourceName);
    }

    [Fact]
    public void Reports_a_missing_mesh_number_once_however_often_the_block_places_it()
    {
        // The same number placed twice is one fact about the archive rather than two: the pack names the
        // reference it could not serve once, because the number is what the archive could not serve.
        DungeonLogicalSource[] sources = CreateSources();
        Replace(sources, "BLOCKS.BSA", CreateNamedBsa(("S0000007.RDB", CreateRdbFixtureWithModels(["42", "99", "99"]))));

        DungeonNormalizationResult result = DungeonNormalizer.Normalize(Request(sources));

        GeometryUnresolvedMeshReference unresolved = Assert.Single(result.UnresolvedMeshReferences);
        Assert.Equal("99", unresolved.MeshId);
        Assert.Contains("carries no numeric model '99'", unresolved.Reason, StringComparison.Ordinal);
        Assert.Equal(["42", "99"], result.ReferencedMeshIds);
        Assert.Single(result.Document.Meshes);
    }

    [Fact]
    public void Reports_a_placement_whose_record_declares_no_drawable_plane()
    {
        // A record that decodes but states no polygon is not geometry: the placement is reported with the
        // archive's own reason while the valid placement beside it still normalizes and still draws.
        DungeonLogicalSource[] sources = CreateSources();
        Replace(sources, "BLOCKS.BSA", CreateNamedBsa(("S0000007.RDB", CreateRdbFixtureWithModels(["42", "99"]))));
        Replace(sources, "ARCH3D.BSA", CreateNumericBsa((42U, CreateArch3dFixture()), (99U, CreateArch3dFixture(planeCount: 0))));

        DungeonNormalizationResult result = DungeonNormalizer.Normalize(Request(sources));

        GeometryUnresolvedMeshReference unresolved = Assert.Single(result.UnresolvedMeshReferences);
        Assert.Equal("99", unresolved.MeshId);
        Assert.Contains("declares no drawable plane", unresolved.Reason, StringComparison.Ordinal);
        Assert.Equal("mesh/fixture-hold/texture-2-0/static", Assert.Single(result.Document.Meshes).Id);
        Assert.Equal(["42", "99"], result.ReferencedMeshIds);
    }

    [Fact]
    public void Reports_a_placement_whose_record_declares_only_a_degenerate_plane()
    {
        // The edge is a plane that cannot draw, not a record that states no planes at all: a record that
        // decodes with one two-point plane states no polygon either, and is reported by the same reason
        // rather than dropped silently while the valid placement beside it still normalizes and still draws.
        DungeonLogicalSource[] sources = CreateSources();
        Replace(sources, "BLOCKS.BSA", CreateNamedBsa(("S0000007.RDB", CreateRdbFixtureWithModels(["42", "99"]))));
        Replace(sources, "ARCH3D.BSA", CreateNumericBsa((42U, CreateArch3dFixture()), (99U, CreateArch3dFixture(planePointCount: 2))));

        DungeonNormalizationResult result = DungeonNormalizer.Normalize(Request(sources));

        GeometryUnresolvedMeshReference unresolved = Assert.Single(result.UnresolvedMeshReferences);
        Assert.Equal("99", unresolved.MeshId);
        Assert.Contains("declares no drawable plane", unresolved.Reason, StringComparison.Ordinal);
        Assert.Equal("mesh/fixture-hold/texture-2-0/static", Assert.Single(result.Document.Meshes).Id);
        Assert.Equal(["42", "99"], result.ReferencedMeshIds);
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

    [Fact]
    public void Normalizes_classic_action_door_lock_selector_and_linked_special_door_kind()
    {
        DungeonLogicalSource[] ordinarySources = CreateSources();
        Replace(ordinarySources, "BLOCKS.BSA", CreateNamedBsa(("S0000007.RDB", CreateRdbFixture(modelDescription: "DOR", triggerFlagStartingLock: 0xD0, actionFlags: 0x10))));
        NormalizedDoorPlacement ordinary = Assert.Single(DungeonNormalizer.Normalize(Request(ordinarySources)).Document.World.Doors);

        Assert.Equal("normal", ordinary.Kind);
        Assert.Equal(50, ordinary.StartingLockValue);
        Assert.Equal((byte)0x10, ordinary.Action!.Flags);

        DungeonLogicalSource[] specialSources = CreateSources();
        Replace(specialSources, "BLOCKS.BSA", CreateNamedBsa(("S0000007.RDB", CreateRdbFixture(modelDescription: "MOD", actionFlags: 0x12))));
        NormalizedDoorPlacement special = Assert.Single(DungeonNormalizer.Normalize(Request(specialSources)).Document.World.Doors);

        Assert.Equal("special", special.Kind);
        Assert.Equal(0, special.StartingLockValue);
        Assert.Equal((byte)0x12, special.Action!.Flags);
    }

    [Fact]
    public void Repeated_rdb_block_placements_keep_each_action_door_and_visual_identity_distinct()
    {
        DungeonLogicalSource[] sources = CreateSources();
        Replace(sources, "MAPS.BSA", CreateNamedBsa(
            ("MAPNAMES.017", CreateMapNames()),
            ("MAPTABLE.017", CreateMapTable()),
            ("MAPPITEM.017", CreateMapPItem()),
            ("MAPDITEM.017", CreateMapDItem((1, 1), (2, 1)))));
        Replace(sources, "BLOCKS.BSA", CreateNamedBsa(("S0000007.RDB", CreateRdbFixture(modelDescription: "DOR"))));

        DungeonNormalizationResult result = DungeonNormalizer.Normalize(Request(sources));

        Assert.Equal(
        [
            "door/s0000007-rdb/1/1/0",
            "door/s0000007-rdb/2/1/0",
        ], result.Document.World.Doors.Select(door => door.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(2, result.SpatialPublication.DoorVisuals.Select(visual => visual.Artifact.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, result.SpatialPublication.DoorVisuals.Select(visual => visual.Artifact.RelativePath).Distinct(StringComparer.Ordinal).Count());
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

    private static byte[] CreateMapDItem(params (sbyte X, sbyte Z)[] blocks)
    {
        if (blocks.Length == 0) blocks = [(1, 1)];
        byte[] ditem = new byte[145 + (blocks.Length * 4)];
        BitConverter.GetBytes(1U).CopyTo(ditem, 0);
        BitConverter.GetBytes(0U).CopyTo(ditem, 4);
        BitConverter.GetBytes((ushort)1).CopyTo(ditem, 8);
        BitConverter.GetBytes((ushort)7).CopyTo(ditem, 10);
        BitConverter.GetBytes(0U).CopyTo(ditem, 12);
        BitConverter.GetBytes(99U).CopyTo(ditem, 49);
        BitConverter.GetBytes((ushort)blocks.Length).CopyTo(ditem, 138);
        for (int index = 0; index < blocks.Length; index++)
        {
            int offset = 145 + (index * 4);
            ditem[offset] = unchecked((byte)blocks[index].X);
            ditem[offset + 1] = unchecked((byte)blocks[index].Z);
            BitConverter.GetBytes((ushort)((3 << 11) | 0x400 | 7)).CopyTo(ditem, offset + 2);
        }
        return ditem;
    }

    private static byte[] CreateRdbFixture(
        ushort flatTextureArchive = RdbSourceClassification.EditorFlatArchive,
        ushort flatTextureRecord = RdbSourceClassification.StartMarkerRecord,
        ushort factionOrMobileId = 0,
        string modelDescription = "MOD",
        uint triggerFlagStartingLock = 0,
        byte actionFlags = 0) =>
        CreateRdbFixtureWithModels(["42"], flatTextureArchive, flatTextureRecord, factionOrMobileId, modelDescription, triggerFlagStartingLock, actionFlags);

    /// <summary>
    /// Builds an RDB block whose single cell places one model per entry of <paramref name="modelIds"/>, in
    /// order, so a block can name the same model number twice and can name one the archive cannot serve.
    /// Model-reference entry <c>i</c> carries <c>modelIds[i]</c>, and the object list links one model node
    /// per entry into the block's flat and light.
    /// </summary>
    private static byte[] CreateRdbFixtureWithModels(
        IReadOnlyList<string> modelIds,
        ushort flatTextureArchive = RdbSourceClassification.EditorFlatArchive,
        ushort flatTextureRecord = RdbSourceClassification.StartMarkerRecord,
        ushort factionOrMobileId = 0,
        string modelDescription = "MOD",
        uint triggerFlagStartingLock = 0,
        byte actionFlags = 0)
    {
        // The classic RDB layout the decoder reads: a 20-byte header, a fixed 750-entry model-reference
        // table, one cell root, then 25-byte object nodes and their resources.
        const int headerBytes = 20;
        const int modelReferences = 750;
        const int referenceBytes = 8;
        const int nodeBytes = 25;
        const int modelResourceBytes = 23;
        const int flatResourceBytes = 11;
        const int lightResourceBytes = 10;
        int roots = headerBytes + (modelReferences * referenceBytes);
        int firstModelNode = roots + sizeof(int);
        int flatNode = firstModelNode + (modelIds.Count * nodeBytes);
        int lightNode = flatNode + nodeBytes;
        int firstModelResource = lightNode + nodeBytes;
        int flatResource = firstModelResource + (modelIds.Count * modelResourceBytes);
        int lightResource = flatResource + flatResourceBytes;
        int actionResource = lightResource + lightResourceBytes;
        byte[] data = new byte[actionFlags == 0 ? actionResource : actionResource + 10];
        BitConverter.GetBytes(1U).CopyTo(data, 4);
        BitConverter.GetBytes(1U).CopyTo(data, 8);
        BitConverter.GetBytes((uint)roots).CopyTo(data, 12);
        BitConverter.GetBytes(firstModelNode).CopyTo(data, roots);
        for (int index = 0; index < modelIds.Count; index++)
        {
            int reference = headerBytes + (index * referenceBytes);
            Encoding.ASCII.GetBytes(modelIds[index] + "\0").CopyTo(data, reference);
            Encoding.ASCII.GetBytes(modelDescription).CopyTo(data, reference + 5);
            int resource = firstModelResource + (index * modelResourceBytes);
            BitConverter.GetBytes((ushort)index).CopyTo(data, resource + 12);
            BitConverter.GetBytes(triggerFlagStartingLock).CopyTo(data, resource + 14);
            if (actionFlags != 0) BitConverter.GetBytes(actionResource).CopyTo(data, resource + 19);
            int next = index + 1 < modelIds.Count ? firstModelNode + ((index + 1) * nodeBytes) : flatNode;
            WriteNode(data, firstModelNode + (index * nodeBytes), next, [index, 0, 0], 1, resource);
        }

        WriteNode(data, flatNode, lightNode, [10, -20, 30], 3, flatResource);
        WriteNode(data, lightNode, -1, [1, -2, 3], 2, lightResource);
        BitConverter.GetBytes((ushort)((flatTextureArchive << 7) | flatTextureRecord)).CopyTo(data, flatResource);
        data[flatResource + 4] = (byte)factionOrMobileId;
        data[flatResource + 5] = (byte)(factionOrMobileId >> 8);
        BitConverter.GetBytes(-1).CopyTo(data, flatResource + 6);
        BitConverter.GetBytes((ushort)512).CopyTo(data, lightResource + 8);
        if (actionFlags != 0)
        {
            data[actionResource] = 1;
            BitConverter.GetBytes((ushort)30).CopyTo(data, actionResource + 1);
            BitConverter.GetBytes((ushort)90).CopyTo(data, actionResource + 3);
            BitConverter.GetBytes(-1).CopyTo(data, actionResource + 5);
            data[actionResource + 9] = actionFlags;
        }
        return data;
    }

    private static byte[] CreateArch3dFixture(int planeCount = 1, int planePointCount = 3)
    {
        byte[] data = new byte[132];
        Encoding.ASCII.GetBytes("v2.6").CopyTo(data, 0);
        BitConverter.GetBytes(3).CopyTo(data, 4);
        BitConverter.GetBytes(planeCount).CopyTo(data, 8);
        BitConverter.GetBytes(64).CopyTo(data, 48);
        BitConverter.GetBytes(100).CopyTo(data, 60);
        WriteVector(data, 64, [0, 0, 0]);
        WriteVector(data, 76, [256, 0, 0]);
        WriteVector(data, 88, [0, 0, 256]);
        // The plane header states how many points the polygon has, so the fixture writes the entries the
        // caller asks the record to declare: a plane of fewer than three points is the record's own claim.
        data[100] = (byte)planePointCount;
        BitConverter.GetBytes((ushort)((2 << 7) | 0)).CopyTo(data, 102);
        for (int index = 0; index < planePointCount; index++)
        {
            int offset = 108 + (index * 8);
            BitConverter.GetBytes(index * 12).CopyTo(data, offset);
            BitConverter.GetBytes((short)(index == 1 ? 32 : 0)).CopyTo(data, offset + 4);
            BitConverter.GetBytes((short)(index == 2 ? 32 : 0)).CopyTo(data, offset + 6);
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
