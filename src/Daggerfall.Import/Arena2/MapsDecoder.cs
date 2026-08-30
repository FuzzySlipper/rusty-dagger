namespace Daggerfall.Import.Arena2;

/// <summary>One source dungeon-block reference placed in a MAPDITEM layout.</summary>
public sealed record MapsDungeonBlock(string SourceName, sbyte X, sbyte Z, bool IsStart);

/// <summary>Decoded MAPS location facts and its linked RDB block references.</summary>
public sealed record MapsDungeonLayout(
    int Region,
    int LocationIndex,
    string LocationName,
    int MapId,
    uint LocationId,
    int Longitude,
    int Latitude,
    byte DungeonType,
    IReadOnlyList<MapsDungeonBlock> Blocks);

/// <summary>Decoder for region-linked MAPS.BSA source records.</summary>
public static class MapsDecoder
{
    private static readonly string[] RdbBlockLetters = ["N", "W", "L", "S", "B", "M"];

    /// <summary>Reads the exact location names stored in a MAPNAMES region record.</summary>
    public static IReadOnlyList<string> DecodeLocationNames(BsaArchive archive, int region)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ReadOnlyMemory<byte> data = GetNamedPayload(archive, "MAPNAMES", region);
        CheckedLittleEndianReader reader = new(data.Span, archive.Source);
        int count = CheckedCount(reader.ReadUInt32(), archive.Source, reader.Position - sizeof(uint), "MAPNAMES location count");
        int nameBytes = CheckedMultiply(count, 32, archive.Source, "MAPNAMES names");
        reader.ReadBytes(nameBytes);
        if (reader.Position != reader.Length)
        {
            throw reader.Error("MAPNAMES has trailing bytes after its location names");
        }

        List<string> names = new(count);
        CheckedLittleEndianReader namesReader = new(data.Span, archive.Source);
        namesReader.Seek(sizeof(uint));
        for (int index = 0; index < count; index++)
        {
            names.Add(namesReader.ReadNullTerminatedAscii(32));
        }

        return names;
    }

    /// <summary>Resolves one exact source location name to its linked dungeon block layout.</summary>
    public static MapsDungeonLayout DecodeDungeonLayout(BsaArchive archive, int region, string locationName)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(locationName);
        IReadOnlyList<string> names = DecodeLocationNames(archive, region);
        int locationIndex = FindExactLocation(names, locationName, archive.Source);
        (int mapId, int longitude, int latitude, byte dungeonType) = DecodeMapTable(GetNamedPayload(archive, "MAPTABLE", region), archive.Source, locationIndex);
        uint exteriorLocationId = DecodeExteriorLocationId(GetNamedPayload(archive, "MAPPITEM", region), archive.Source, names.Count, locationIndex);
        (uint locationId, IReadOnlyList<MapsDungeonBlock> blocks) = DecodeDungeonRecord(GetNamedPayload(archive, "MAPDITEM", region), archive.Source, exteriorLocationId);
        return new MapsDungeonLayout(region, locationIndex, locationName, mapId, locationId, longitude, latitude, dungeonType, blocks);
    }

    /// <summary>Applies the donor's world-coordinate to 1000-by-500 map-pixel projection.</summary>
    public static (int X, int Y) ToMapPixel(int longitude, int latitude) => (longitude / 128, 499 - (latitude / 128));

    private static ReadOnlyMemory<byte> GetNamedPayload(BsaArchive archive, string stem, int region)
    {
        if (region < 0 || region > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(region), region, "MAPS region must be within 0..999.");
        }

        string name = $"{stem}.{region:000}";
        if (!archive.TryGetByName(name, out BsaRecord? record) || record is null)
        {
            throw new Arena2FormatException(archive.Source, 0, $"MAPS record {name} was not found");
        }

        return archive.GetPayload(record);
    }

    private static int FindExactLocation(IReadOnlyList<string> names, string locationName, string source)
    {
        for (int index = 0; index < names.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(names[index], locationName))
            {
                return index;
            }
        }

        throw new Arena2FormatException(source, 0, $"MAPS location '{locationName}' was not found");
    }

    private static (int MapId, int Longitude, int Latitude, byte DungeonType) DecodeMapTable(ReadOnlyMemory<byte> data, string source, int locationIndex)
    {
        const int entryBytes = 17;
        int offset = CheckedMultiply(locationIndex, entryBytes, source, "MAPTABLE entry");
        CheckedLittleEndianReader reader = At(data.Span, source, offset, "MAPTABLE entry");
        int mapId = reader.ReadInt32();
        uint longitudeBits = reader.ReadUInt32();
        int latitudeBits = reader.ReadInt32();
        byte dungeonType = reader.ReadByte();
        return (mapId, (int)((longitudeBits & 0x1F_FFFFu) >> 8), (latitudeBits & 0x00FF_FFFF) >> 8, dungeonType);
    }

    private static uint DecodeExteriorLocationId(ReadOnlyMemory<byte> data, string source, int locationCount, int locationIndex)
    {
        int tableBytes = CheckedMultiply(locationCount, sizeof(uint), source, "MAPPITEM offset table");
        int indexOffset = CheckedMultiply(locationIndex, sizeof(uint), source, "MAPPITEM offset entry");
        uint relativeOffset = At(data.Span, source, indexOffset, "MAPPITEM offset entry").ReadUInt32();
        int recordOffset = CheckedAdd(tableBytes, CheckedCount(relativeOffset, source, indexOffset, "MAPPITEM record offset"), source, "MAPPITEM record");
        CheckedLittleEndianReader reader = At(data.Span, source, recordOffset, "MAPPITEM location record");
        int doorCount = CheckedCount(reader.ReadUInt32(), source, recordOffset, "MAPPITEM door count");
        reader.ReadBytes(CheckedMultiply(doorCount, 6, source, "MAPPITEM doors"));
        reader.ReadBytes(33);
        return reader.ReadUInt16();
    }

    private static (uint LocationId, IReadOnlyList<MapsDungeonBlock> Blocks) DecodeDungeonRecord(ReadOnlyMemory<byte> data, string source, uint exteriorLocationId)
    {
        CheckedLittleEndianReader header = new(data.Span, source);
        int dungeonCount = CheckedCount(header.ReadUInt32(), source, 0, "MAPDITEM dungeon count");
        int tableBytes = CheckedMultiply(dungeonCount, 8, source, "MAPDITEM table");
        header.ReadBytes(tableBytes);
        int linkedOffset = -1;
        for (int index = 0; index < dungeonCount; index++)
        {
            CheckedLittleEndianReader entry = At(data.Span, source, CheckedAdd(sizeof(uint), CheckedMultiply(index, 8, source, "MAPDITEM table"), source, "MAPDITEM table"), "MAPDITEM table entry");
            uint relativeOffset = entry.ReadUInt32();
            _ = entry.ReadUInt16();
            uint exteriorId = entry.ReadUInt16();
            if (exteriorId == exteriorLocationId)
            {
                linkedOffset = CheckedCount(relativeOffset, source, entry.Position - 8, "MAPDITEM dungeon offset");
                break;
            }
        }

        if (linkedOffset < 0)
        {
            throw new Arena2FormatException(source, 0, $"MAPDITEM has no dungeon linked to exterior location ID {exteriorLocationId}");
        }

        int recordOffset = CheckedAdd(CheckedAdd(sizeof(uint), tableBytes, source, "MAPDITEM record base"), linkedOffset, source, "MAPDITEM dungeon record");
        CheckedLittleEndianReader reader = At(data.Span, source, recordOffset, "MAPDITEM dungeon record");
        int doorCount = CheckedCount(reader.ReadUInt32(), source, recordOffset, "MAPDITEM door count");
        reader.ReadBytes(CheckedMultiply(doorCount, 6, source, "MAPDITEM doors"));
        reader.ReadBytes(33);
        uint locationId = reader.ReadUInt32();
        reader.ReadBytes(75);
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        int blockCount = reader.ReadUInt16();
        reader.ReadBytes(5);
        List<MapsDungeonBlock> blocks = new(blockCount);
        for (int index = 0; index < blockCount; index++)
        {
            sbyte x = unchecked((sbyte)reader.ReadByte());
            sbyte z = unchecked((sbyte)reader.ReadByte());
            ushort bitfield = reader.ReadUInt16();
            int letterIndex = bitfield >> 11;
            if ((uint)letterIndex >= RdbBlockLetters.Length)
            {
                throw reader.Error($"MAPDITEM block {index} has unsupported RDB letter index {letterIndex}");
            }

            int number = bitfield & 0x03FF;
            blocks.Add(new MapsDungeonBlock($"{RdbBlockLetters[letterIndex]}{number:0000000}.RDB", x, z, (bitfield & 0x0400) != 0));
        }

        return (locationId, blocks);
    }

    private static CheckedLittleEndianReader At(ReadOnlySpan<byte> data, string source, int offset, string context)
    {
        CheckedLittleEndianReader reader = new(data, source);
        try
        {
            reader.Seek(offset);
        }
        catch (Arena2FormatException exception)
        {
            throw new Arena2FormatException(source, exception.Offset, $"{context}: {exception.Message}");
        }

        return reader;
    }

    private static int CheckedCount(uint value, string source, int offset, string context)
    {
        if (value > int.MaxValue)
        {
            throw new Arena2FormatException(source, offset, $"{context} exceeds the importer quota");
        }

        return (int)value;
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

    private static int CheckedAdd(int left, int right, string source, string context)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw new Arena2FormatException(source, left, $"{context} offset arithmetic overflows a 32-bit offset");
        }
    }
}
