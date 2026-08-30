namespace Daggerfall.Import.Arena2;

/// <summary>Raw action data referenced by an RDB model source record.</summary>
public sealed record RdbActionSource(byte Axis, ushort Duration, ushort Magnitude, int NextObjectOffset, byte Flags);

/// <summary>Raw RDB model source record. It does not prescribe runtime behavior.</summary>
public sealed record RdbModelSource(
    int X,
    int Y,
    int Z,
    int XRotation,
    int YRotation,
    int ZRotation,
    ushort ModelIndex,
    string ModelId,
    string Description,
    byte SoundIndex,
    RdbActionSource? Action);

/// <summary>Raw RDB flat source record. Marker and mobile fields retain their source meaning only.</summary>
public sealed record RdbFlatSource(
    int X,
    int Y,
    int Z,
    ushort TextureArchive,
    ushort TextureRecord,
    ushort Flags,
    byte Magnitude,
    byte SoundIndex,
    ushort FactionOrMobileId,
    int NextObjectOffset,
    byte Action);

/// <summary>Raw RDB light source record.</summary>
public sealed record RdbLightSource(int X, int Y, int Z, ushort Radius);

/// <summary>Decoded RDB block facts, without runtime entity or spatial interpretation.</summary>
public sealed record RdbBlockSource(
    string Source,
    uint Width,
    uint Height,
    IReadOnlyList<RdbModelSource> Models,
    IReadOnlyList<RdbFlatSource> Flats,
    IReadOnlyList<RdbLightSource> Lights);

/// <summary>Importer-local classification of Daggerfall source markers and model tags.</summary>
public static class RdbSourceClassification
{
    /// <summary>Classic editor-flat texture archive.</summary>
    public const ushort EditorFlatArchive = 199;

    /// <summary>Classic editor-flat start-marker record.</summary>
    public const ushort StartMarkerRecord = 10;

    /// <summary>Classic editor-flat enter-marker record.</summary>
    public const ushort EnterMarkerRecord = 8;

    /// <summary>Classic editor-flat random-treasure marker record.</summary>
    public const ushort RandomTreasureMarkerRecord = 19;

    /// <summary>Determines whether a flat is a classic editor start-marker source fact.</summary>
    public static bool IsStartMarker(RdbFlatSource flat)
    {
        ArgumentNullException.ThrowIfNull(flat);
        return flat.TextureArchive == EditorFlatArchive && flat.TextureRecord == StartMarkerRecord;
    }

    /// <summary>Determines whether a flat is a classic editor enter-marker source fact.</summary>
    public static bool IsEnterMarker(RdbFlatSource flat)
    {
        ArgumentNullException.ThrowIfNull(flat);
        return flat.TextureArchive == EditorFlatArchive && flat.TextureRecord == EnterMarkerRecord;
    }

    /// <summary>Determines whether a flat is a classic editor treasure-marker source fact.</summary>
    public static bool IsRandomTreasureMarker(RdbFlatSource flat)
    {
        ArgumentNullException.ThrowIfNull(flat);
        return flat.TextureArchive == EditorFlatArchive && flat.TextureRecord == RandomTreasureMarkerRecord;
    }

    /// <summary>Determines whether a model has a classic action-door tag; it does not create a door runtime.</summary>
    public static bool HasActionDoorTag(RdbModelSource model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return !StringComparer.Ordinal.Equals(model.ModelId, "72100")
            && model.Description is "DOR" or "DDR" or "NEW" or "CAV";
    }
}

/// <summary>Decoder for RDB source blocks and their bounded object lists.</summary>
public static class RdbDecoder
{
    private const int ModelReferenceCount = 750;
    private const int ModelReferenceBytes = 8;
    private const int HeaderBytes = 20;
    private const int ObjectNodeBytes = 25;
    private const int ModelResourceBytes = 23;
    private const int LightResourceBytes = 10;
    private const int FlatResourceBytes = 11;
    private const int ActionResourceBytes = 10;
    private const int MaximumCells = 4096;

    /// <summary>Decodes one RDB block into bounded raw source facts.</summary>
    public static RdbBlockSource Decode(ReadOnlySpan<byte> bytes, string source)
    {
        CheckedLittleEndianReader header = new(bytes, source);
        int minimumBytes = checked(HeaderBytes + (ModelReferenceCount * ModelReferenceBytes));
        if (bytes.Length < minimumBytes)
        {
            throw header.Error($"RDB requires at least {minimumBytes} bytes, got {bytes.Length}");
        }

        _ = header.ReadUInt32();
        uint width = header.ReadUInt32();
        uint height = header.ReadUInt32();
        uint rawObjectRootOffset = header.ReadUInt32();
        _ = header.ReadUInt32();
        if (width == 0 || height == 0)
        {
            throw header.Error($"RDB dimensions must be nonzero, got {width}x{height}");
        }

        ulong cells64 = (ulong)width * height;
        if (cells64 > MaximumCells)
        {
            throw header.Error($"RDB dimensions {width}x{height} exceed the {MaximumCells}-cell importer quota");
        }

        int cellCount = (int)cells64;
        int objectRootOffset = CheckedOffset(rawObjectRootOffset, source, header.Position - sizeof(uint), "RDB object root offset");
        int rootBytes = CheckedMultiply(cellCount, sizeof(int), source, "RDB object root list");
        RequireRange(bytes, objectRootOffset, rootBytes, source, "RDB object root list");
        (string[] modelIds, string[] descriptions) = ReadModelReferences(bytes, source);
        List<RdbModelSource> models = [];
        List<RdbFlatSource> flats = [];
        List<RdbLightSource> lights = [];
        int maximumNodesPerList = Math.Max(1, bytes.Length / ObjectNodeBytes);
        int maximumDecodedObjects = maximumNodesPerList;
        HashSet<int> decodedNodeOffsets = [];
        for (int cell = 0; cell < cellCount; cell++)
        {
            int rootOffset = objectRootOffset + (cell * sizeof(int));
            int nodeOffset = At(bytes, source, rootOffset, "RDB object root").ReadInt32();
            if (nodeOffset < 0)
            {
                continue;
            }

            DecodeObjectList(
                bytes,
                source,
                nodeOffset,
                maximumNodesPerList,
                maximumDecodedObjects,
                decodedNodeOffsets,
                modelIds,
                descriptions,
                models,
                flats,
                lights);
        }

        return new RdbBlockSource(source, width, height, models, flats, lights);
    }

    private static (string[] ModelIds, string[] Descriptions) ReadModelReferences(ReadOnlySpan<byte> bytes, string source)
    {
        string[] modelIds = new string[ModelReferenceCount];
        string[] descriptions = new string[ModelReferenceCount];
        for (int index = 0; index < ModelReferenceCount; index++)
        {
            int offset = HeaderBytes + (index * ModelReferenceBytes);
            CheckedLittleEndianReader reader = At(bytes, source, offset, "RDB model reference");
            modelIds[index] = ReadModelReferenceField(ref reader, 5);
            descriptions[index] = ReadModelReferenceField(ref reader, 3);
        }

        return (modelIds, descriptions);
    }

    /// <summary>
    /// Reads one fixed-width model-reference field. Classic RDB leaves an unused table entry as
    /// an all-<c>0xFF</c> field, unlike the ASCII/NUL encoding used by populated entries.
    /// </summary>
    private static string ReadModelReferenceField(ref CheckedLittleEndianReader reader, int width)
    {
        ReadOnlySpan<byte> field = reader.ReadBytes(width);
        bool allUnusedPadding = true;
        foreach (byte value in field)
        {
            if (value != byte.MaxValue)
            {
                allUnusedPadding = false;
            }

            if (value > 0x7F && value != byte.MaxValue)
            {
                throw reader.Error($"non-ASCII byte {value} in RDB model-reference field");
            }
        }

        if (allUnusedPadding)
        {
            return string.Empty;
        }

        foreach (byte value in field)
        {
            if (value == byte.MaxValue)
            {
                throw reader.Error("mixed 0xFF padding in RDB model-reference field");
            }
        }

        int terminator = field.IndexOf((byte)0);
        ReadOnlySpan<byte> text = terminator >= 0 ? field[..terminator] : field;
        return System.Text.Encoding.ASCII.GetString(text);
    }

    private static void DecodeObjectList(
        ReadOnlySpan<byte> bytes,
        string source,
        int initialOffset,
        int maximumNodes,
        int maximumDecodedObjects,
        ISet<int> decodedNodeOffsets,
        IReadOnlyList<string> modelIds,
        IReadOnlyList<string> descriptions,
        ICollection<RdbModelSource> models,
        ICollection<RdbFlatSource> flats,
        ICollection<RdbLightSource> lights)
    {
        HashSet<int> visited = [];
        int nodeOffset = initialOffset;
        while (true)
        {
            if (!visited.Add(nodeOffset))
            {
                throw new Arena2FormatException(source, nodeOffset, "RDB object linked list contains a cycle");
            }

            if (visited.Count > maximumNodes)
            {
                throw new Arena2FormatException(source, nodeOffset, "RDB object linked list exceeds its byte-derived importer quota");
            }

            if (!decodedNodeOffsets.Add(nodeOffset))
            {
                throw new Arena2FormatException(source, nodeOffset, "RDB object node is shared by multiple cell-rooted lists");
            }

            if (decodedNodeOffsets.Count > maximumDecodedObjects)
            {
                throw new Arena2FormatException(source, nodeOffset, "RDB decoded-object count exceeds its byte-derived importer quota");
            }

            CheckedLittleEndianReader node = At(bytes, source, nodeOffset, "RDB object node");
            int nextOffset = node.ReadInt32();
            _ = node.ReadInt32();
            int x = node.ReadInt32();
            int y = node.ReadInt32();
            int z = node.ReadInt32();
            byte type = node.ReadByte();
            int resourceOffset = CheckedOffset(node.ReadUInt32(), source, node.Position - sizeof(uint), "RDB object resource offset");
            switch (type)
            {
                case 0x01:
                    models.Add(DecodeModel(bytes, source, resourceOffset, x, y, z, modelIds, descriptions));
                    break;
                case 0x02:
                    lights.Add(DecodeLight(bytes, source, resourceOffset, x, y, z));
                    break;
                case 0x03:
                    flats.Add(DecodeFlat(bytes, source, resourceOffset, x, y, z));
                    break;
                default:
                    throw new Arena2FormatException(source, nodeOffset + 20, $"unsupported RDB object type 0x{type:X2}");
            }

            if (nextOffset < 0)
            {
                return;
            }

            nodeOffset = nextOffset;
        }
    }

    private static RdbModelSource DecodeModel(ReadOnlySpan<byte> bytes, string source, int offset, int x, int y, int z, IReadOnlyList<string> modelIds, IReadOnlyList<string> descriptions)
    {
        RequireRange(bytes, offset, ModelResourceBytes, source, "RDB model resource");
        CheckedLittleEndianReader reader = At(bytes, source, offset, "RDB model resource");
        int xRotation = reader.ReadInt32();
        int yRotation = reader.ReadInt32();
        int zRotation = reader.ReadInt32();
        ushort modelIndex = reader.ReadUInt16();
        _ = reader.ReadUInt32();
        byte soundIndex = reader.ReadByte();
        int actionOffset = reader.ReadInt32();
        if ((uint)modelIndex >= modelIds.Count)
        {
            throw reader.Error($"RDB model index {modelIndex} exceeds the source model-reference table");
        }

        RdbActionSource? action = actionOffset > 0 ? DecodeAction(bytes, source, actionOffset) : null;
        return new RdbModelSource(x, y, z, xRotation, yRotation, zRotation, modelIndex, modelIds[modelIndex], descriptions[modelIndex], soundIndex, action);
    }

    private static RdbActionSource DecodeAction(ReadOnlySpan<byte> bytes, string source, int offset)
    {
        RequireRange(bytes, offset, ActionResourceBytes, source, "RDB action resource");
        CheckedLittleEndianReader reader = At(bytes, source, offset, "RDB action resource");
        return new RdbActionSource(reader.ReadByte(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadInt32(), reader.ReadByte());
    }

    private static RdbLightSource DecodeLight(ReadOnlySpan<byte> bytes, string source, int offset, int x, int y, int z)
    {
        RequireRange(bytes, offset, LightResourceBytes, source, "RDB light resource");
        CheckedLittleEndianReader reader = At(bytes, source, offset, "RDB light resource");
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        return new RdbLightSource(x, y, z, reader.ReadUInt16());
    }

    private static RdbFlatSource DecodeFlat(ReadOnlySpan<byte> bytes, string source, int offset, int x, int y, int z)
    {
        RequireRange(bytes, offset, FlatResourceBytes, source, "RDB flat resource");
        CheckedLittleEndianReader reader = At(bytes, source, offset, "RDB flat resource");
        ushort bitfield = reader.ReadUInt16();
        ushort flags = reader.ReadUInt16();
        byte magnitude = reader.ReadByte();
        byte soundIndex = reader.ReadByte();
        int nextObjectOffset = reader.ReadInt32();
        byte action = reader.ReadByte();
        return new RdbFlatSource(x, y, z, (ushort)(bitfield >> 7), (ushort)(bitfield & 0x7F), flags, magnitude, soundIndex, (ushort)(magnitude | (soundIndex << 8)), nextObjectOffset, action);
    }

    private static CheckedLittleEndianReader At(ReadOnlySpan<byte> bytes, string source, int offset, string context)
    {
        RequireRange(bytes, offset, 0, source, context);
        CheckedLittleEndianReader reader = new(bytes, source);
        reader.Seek(offset);
        return reader;
    }

    private static int CheckedOffset(uint offset, string source, int errorOffset, string context)
    {
        if (offset > int.MaxValue)
        {
            throw new Arena2FormatException(source, errorOffset, $"{context} exceeds the importer quota");
        }

        return (int)offset;
    }

    private static int CheckedMultiply(int left, int right, string source, string context)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            throw new Arena2FormatException(source, 0, $"{context} size overflows a 32-bit offset");
        }
    }

    private static void RequireRange(ReadOnlySpan<byte> bytes, int offset, int length, string source, string context)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length || length > bytes.Length - offset)
        {
            throw new Arena2FormatException(source, offset, $"{context} exceeds source length {bytes.Length}");
        }
    }
}
