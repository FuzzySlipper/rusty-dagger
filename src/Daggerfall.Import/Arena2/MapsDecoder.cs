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

/// <summary>One RMB block in an exterior location's donor grid.</summary>
public sealed record MapsExteriorBlock(string SourceName, byte X, byte Y);

/// <summary>
/// Decoded MAPPITEM exterior facts and the RMB blocks laid out for one selected location.
/// </summary>
/// <remarks>
/// The grid is source order: <see cref="MapsExteriorBlock.X"/> advances first, then
/// <see cref="MapsExteriorBlock.Y"/>.  It deliberately preserves block-grid coordinates instead of
/// pretending that an RMB block has already been transformed into runtime world units.
/// </remarks>
public sealed record MapsExteriorLayout(
    int Region,
    int LocationIndex,
    string LocationName,
    int MapId,
    int Longitude,
    int Latitude,
    uint LocationId,
    byte Width,
    byte Height,
    char Letter1,
    IReadOnlyList<MapsExteriorBlock> Blocks);

/// <summary>Decoder for region-linked MAPS.BSA source records.</summary>
/// <summary>What happened when one region table was read.</summary>
public enum MapsTableState
{
    /// <summary>The table is present and its records agree with what the region declares.</summary>
    Read,

    /// <summary>The region group does not carry the table at all.</summary>
    Missing,

    /// <summary>
    /// The table is present with no bytes. The donor's <c>MapsFile</c> treats any of a region's four
    /// tables being zero-length as making the whole region unreadable and discards it, so an empty
    /// table means the region is unavailable rather than that it has no locations.
    /// </summary>
    Empty,

    /// <summary>The table is present and its contents disagree with the region, so its records are unusable as they stand.</summary>
    Malformed,
}

/// <summary>
/// One named table in a region's MAPS.BSA group: what it declares, and whether that agrees with
/// the rest of the group.
/// </summary>
/// <param name="Name">The table's source name, including its region index.</param>
/// <param name="Ordinal">The record's ordinal in the archive, which is its address.</param>
/// <param name="Length">The table's payload length in bytes.</param>
/// <param name="DeclaredRecords">The records the table's own bytes declare, or -1 when it declares none.</param>
/// <param name="State">Whether the table read, is absent, or disagrees.</param>
/// <param name="Reason">Why the state holds, naming the values that disagree.</param>
public sealed record MapsRegionTable(string Name, int Ordinal, int Length, int DeclaredRecords, MapsTableState State, string Reason);

/// <summary>One region group: the four tables a region index addresses.</summary>
/// <param name="Region">The source region index the table names carry.</param>
/// <param name="Tables">The group's tables, in the donor's own order.</param>
public sealed record MapsRegionGroup(int Region, IReadOnlyList<MapsRegionTable> Tables);

/// <summary>
/// One location a region's tables describe: its name, the map it draws, where it sits, and whether
/// the map is a dungeon.
/// </summary>
/// <param name="Region">The source region index the location belongs to, preserved from the table name.</param>
/// <param name="Index">The location's ordinal in the region's names table.</param>
/// <param name="Name">The exact name the names table carries.</param>
/// <param name="MapId">The map the location draws.</param>
/// <param name="Longitude">The location's authored longitude.</param>
/// <param name="Latitude">The location's authored latitude.</param>
/// <param name="DungeonType">The location's dungeon type byte, which is zero when it has no dungeon.</param>
/// <summary>What happened when one dungeon location's records were read.</summary>
public enum MapsDungeonState
{
    /// <summary>The location's exterior and dungeon records read, and its blocks are listed.</summary>
    Read,

    /// <summary>
    /// The location is a dungeon type and no dungeon record links it, which is the donor's own
    /// outcome for a location that has none: it sets <c>HasDungeon</c> false and returns rather than
    /// treating the absence as damage.
    /// </summary>
    NoDungeon,

    /// <summary>The location is a dungeon and its records disagree, so its blocks are unusable as they stand.</summary>
    Malformed,
}

/// <summary>
/// One dungeon location's block references: which exterior location it pairs with, which dungeon it
/// addresses, and the blocks that dungeon is built from.
/// </summary>
/// <param name="Region">The source region index.</param>
/// <param name="Index">The location's ordinal in the region's names table.</param>
/// <param name="Name">The exact name the names table carries.</param>
/// <param name="ExteriorLocationId">The exterior location this dungeon is paired with.</param>
/// <param name="DungeonLocationId">The dungeon record's own identity.</param>
/// <param name="Blocks">The blocks the dungeon is built from, in record order.</param>
/// <param name="State">Whether the records read or disagreed.</param>
/// <param name="Reason">Why the state holds, naming the values that disagree.</param>
public sealed record MapsDungeonLocation(
    int Region,
    int Index,
    string Name,
    uint ExteriorLocationId,
    uint DungeonLocationId,
    IReadOnlyList<MapsDungeonBlock> Blocks,
    MapsDungeonState State,
    string Reason);

public sealed record MapsLocationRecord(
    int Region,
    int Index,
    string Name,
    int MapId,
    int Longitude,
    int Latitude,
    byte DungeonType,
    int LocationType,
    bool Discovered);

public static class MapsDecoder
{
    private static readonly string[] RdbBlockLetters = ["N", "W", "L", "S", "B", "M"];

    /// <summary>
    /// The four tables a region group carries, in the order the donor's <c>MapsFile</c> reads them:
    /// the exterior location items, the dungeon items, the map table indexed by location, and the
    /// location names. The donor's region record declares them in the opposite order; the order here
    /// is the one its reader uses.
    /// </summary>
    public static readonly string[] RegionTables = ["MAPPITEM", "MAPDITEM", "MAPTABLE", "MAPNAMES"];

    /// <summary>Bytes one MAPTABLE entry occupies, one per location.</summary>
    private const int MapTableEntryBytes = 17;

    /// <summary>The bits of a map table entry's bitfield that carry longitude, as the donor masks them.</summary>
    private const uint LongitudeMask = 0x1FF_FFFFu;

    /// <summary>The bits of the following word that carry latitude.</summary>
    private const int LatitudeMask = 0x00FF_FFFF;

    /// <summary>
    /// Reads every region group the archive carries, with each group's four tables and whether their
    /// records agree.
    /// </summary>
    /// <remarks>
    /// Region indices are discovered from the record names rather than assumed to be a contiguous
    /// range, because an assumption about which regions exist is exactly the kind of claim that goes
    /// wrong silently. A group whose table is absent is reported as missing rather than skipped, and a
    /// table whose records disagree with the rest of its group is reported as malformed with the
    /// disagreeing values named - so a region cannot quietly lose a table, and a map table cannot
    /// quietly describe a different number of locations than the region names.
    /// </remarks>
    public static IReadOnlyList<MapsRegionGroup> DecodeRegionGroups(BsaArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        Dictionary<int, Dictionary<string, BsaRecord>> groups = [];
        foreach (BsaRecord record in archive.Records)
        {
            if (record.Name is not { } name) continue;
            int separator = name.LastIndexOf('.');
            if (separator <= 0) continue;
            string stem = name[..separator];
            if (!RegionTables.Contains(stem, StringComparer.Ordinal)) continue;
            if (!int.TryParse(name.AsSpan(separator + 1), out int region)) continue;
            if (!groups.TryGetValue(region, out Dictionary<string, BsaRecord>? group))
            {
                group = new Dictionary<string, BsaRecord>(StringComparer.Ordinal);
                groups.Add(region, group);
            }

            group[stem] = record;
        }

        List<MapsRegionGroup> result = [];
        foreach ((int region, Dictionary<string, BsaRecord> group) in groups.OrderBy(entry => entry.Key))
        {
            // The location count is the region's own declaration, and the other tables are checked
            // against it rather than trusted independently.
            int names = -1;
            string namesReason = string.Empty;
            bool namesRead = false;
            if (group.TryGetValue("MAPNAMES", out BsaRecord? namesRecord) && namesRecord.Length != 0)
            {
                try
                {
                    names = ReadDeclaredCount(archive, namesRecord, "MAPNAMES location count");
                    namesRead = true;
                }
                catch (Arena2FormatException error)
                {
                    namesReason = error.Message;
                }
            }

            List<MapsRegionTable> tables = [];
            foreach (string stem in RegionTables)
            {
                if (!group.TryGetValue(stem, out BsaRecord? record))
                {
                    tables.Add(new MapsRegionTable($"{stem}.{region:000}", -1, 0, -1, MapsTableState.Missing, "The region group does not carry this table."));
                    continue;
                }

                if (record.Length == 0)
                {
                    tables.Add(new MapsRegionTable(record.Name ?? $"{stem}.{region:000}", record.Ordinal, 0, -1, MapsTableState.Empty,
                        "The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region."));
                    continue;
                }

                (int declared, MapsTableState state, string reason) = stem switch
                {
                    "MAPNAMES" when namesRead => (names, MapsTableState.Read, "The declared location count reads as it stands."),
                    "MAPNAMES" => (-1, MapsTableState.Malformed, namesReason),
                    "MAPTABLE" => MapTable(record, names, namesRead),
                    "MAPPITEM" => ExteriorItems(record, names, namesRead),
                    "MAPDITEM" => DungeonItems(archive, record),
                    _ => (-1, MapsTableState.Malformed, $"'{stem}' has no reader here."),
                };
                tables.Add(new MapsRegionTable(record.Name ?? $"{stem}.{region:000}", record.Ordinal, record.Length, declared, state, reason));
            }

            result.Add(new MapsRegionGroup(region, tables));
        }

        return result;
    }

    /// <summary>One MAPTABLE entry is one location, so its count is its length and must agree with the names.</summary>
    private static (int Declared, MapsTableState State, string Reason) MapTable(BsaRecord record, int names, bool namesRead)
    {
        int entries = record.Length / MapTableEntryBytes;
        if (record.Length % MapTableEntryBytes != 0)
        {
            return (entries, MapsTableState.Malformed,
                $"MAPTABLE is {record.Length} bytes, which is not a whole number of {MapTableEntryBytes}-byte location entries.");
        }

        if (!namesRead)
        {
            return (entries, MapsTableState.Read, $"The map table carries {entries} location entries; the names table did not read, so it cannot be compared.");
        }

        // A table with room to spare still indexes every location: only too few entries make a
        // location unaddressable, and that is the case worth refusing.
        return entries >= names
            ? (entries, MapsTableState.Read, entries == names
                ? $"One {MapTableEntryBytes}-byte entry for each of the region's {names} locations."
                : $"{entries} {MapTableEntryBytes}-byte entries for the region's {names} locations, so every location is addressable and the table has {entries - names} to spare.")
            : (entries, MapsTableState.Malformed,
                $"MAPTABLE carries {entries} location entries for the region's {names} locations, so {names - entries} of them cannot be addressed.");
    }

    /// <summary>The exterior item table is indexed by location, so it needs room for one offset each.</summary>
    private static (int Declared, MapsTableState State, string Reason) ExteriorItems(BsaRecord record, int names, bool namesRead)
    {
        if (!namesRead)
        {
            return (-1, MapsTableState.Read, "The exterior item table's offsets are indexed by location, which the names table did not supply.");
        }

        int indexBytes = names * sizeof(uint);
        return record.Length >= indexBytes
            ? (names, MapsTableState.Read, $"An offset table of {names} entries at {indexBytes} bytes within {record.Length}.")
            : (-1, MapsTableState.Malformed,
                $"MAPPITEM is {record.Length} bytes but its offsets need {indexBytes} for the region's {names} locations.");
    }

    /// <summary>Reads the dungeon count the dungeon item table declares.</summary>
    private static (int Declared, MapsTableState State, string Reason) DungeonItems(BsaArchive archive, BsaRecord record)
    {
        try
        {
            int dungeons = ReadDeclaredCount(archive, record, "MAPDITEM dungeon count");
            int tableBytes = dungeons * 8;
            return record.Length >= sizeof(uint) + tableBytes
                ? (dungeons, MapsTableState.Read, $"A {dungeons}-entry dungeon table at {tableBytes} bytes within {record.Length}.")
                : (-1, MapsTableState.Malformed,
                    $"MAPDITEM declares {dungeons} dungeons, whose {tableBytes}-byte table does not fit its {record.Length} bytes.");
        }
        catch (Arena2FormatException error)
        {
            return (-1, MapsTableState.Malformed, error.Message);
        }
    }

    /// <summary>
    /// Whether a dungeon record links an exterior location.
    /// </summary>
    /// <remarks>
    /// The donor's own dungeon lookup sets its "has dungeon" flag false and returns when no entry
    /// matches, so the absence of a link is a location without a dungeon rather than a damaged table,
    /// and it is reported as one. This walks the same offset table the record decoder does.
    /// </remarks>
    private static bool LinksDungeon(ReadOnlyMemory<byte> data, string source, uint exteriorLocationId)
    {
        // A table with no bytes links nothing: the donor returns with "has dungeon" false before it
        // reads a count, so an empty table is a location without a dungeon rather than damage.
        if (data.Length == 0)
        {
            return false;
        }

        CheckedLittleEndianReader header = new(data.Span, source);
        int dungeons = CheckedCount(header.ReadUInt32(), source, 0, "MAPDITEM dungeon count");
        for (int index = 0; index < dungeons; index++)
        {
            CheckedLittleEndianReader entry = At(data.Span, source, CheckedAdd(sizeof(uint), CheckedMultiply(index, 8, source, "MAPDITEM table"), source, "MAPDITEM table"), "MAPDITEM table entry");
            _ = entry.ReadUInt32();
            _ = entry.ReadUInt16();
            if (entry.ReadUInt16() == exteriorLocationId)
            {
                return true;
            }
        }

        return false;
    }

    private static int ReadDeclaredCount(BsaArchive archive, BsaRecord record, string what)
    {
        ReadOnlyMemory<byte> data = archive.GetPayload(record);
        CheckedLittleEndianReader reader = new(data.Span, archive.Source);
        return CheckedCount(reader.ReadUInt32(), archive.Source, 0, what);
    }

    /// <summary>
    /// Reads every location a region's own tables describe, one record per name.
    /// </summary>
    /// <remarks>
    /// The names table decides how many locations a region has and the map table is indexed by
    /// position in it, so the two are read together: a location's name and its map, position and
    /// dungeon type come from the same index, and the region index the table names carry is preserved
    /// on every record. The block references a dungeon location owns are a further read of the same
    /// index and are not claimed here.
    /// </remarks>
    /// <param name="archive">The MAPS.BSA archive.</param>
    /// <param name="region">The source region index to read.</param>
    public static IReadOnlyList<MapsLocationRecord> DecodeRegionLocations(BsaArchive archive, int region)
    {
        ArgumentNullException.ThrowIfNull(archive);
        IReadOnlyList<string> names = DecodeLocationNames(archive, region);
        ReadOnlyMemory<byte> mapTable = GetNamedPayload(archive, "MAPTABLE", region);
        List<MapsLocationRecord> locations = new(names.Count);
        for (int index = 0; index < names.Count; index++)
        {
            (int mapId, int longitude, int latitude, byte dungeonType, int locationType, bool discovered) = DecodeMapTable(mapTable, archive.Source, index);
            locations.Add(new MapsLocationRecord(region, index, names[index], mapId, longitude, latitude, dungeonType, locationType, discovered));
        }

        return locations;
    }

    /// <summary>
    /// Reads the block references of every dungeon a region describes.
    /// </summary>
    /// <remarks>
    /// A location is a dungeon when its map table says so, and its blocks come from the dungeon item
    /// table through the exterior location it pairs with. A record that disagrees with the rest of
    /// the region is reported as malformed with the values named rather than being allowed to fail
    /// the whole region: one damaged dungeon is not a reason to lose sixty others.
    /// </remarks>
    /// <param name="archive">The MAPS.BSA archive.</param>
    /// <param name="region">The source region index to read.</param>
    public static IReadOnlyList<MapsDungeonLocation> DecodeRegionDungeons(BsaArchive archive, int region)
    {
        ArgumentNullException.ThrowIfNull(archive);
        IReadOnlyList<MapsLocationRecord> locations = DecodeRegionLocations(archive, region);
        ReadOnlyMemory<byte> exterior = GetNamedPayload(archive, "MAPPITEM", region);
        ReadOnlyMemory<byte> dungeons = GetNamedPayload(archive, "MAPDITEM", region);
        List<MapsDungeonLocation> result = [];
        foreach (MapsLocationRecord location in locations.Where(location => location.DungeonType != 0))
        {
            try
            {
                uint exteriorLocationId = DecodeExteriorLocationId(exterior, archive.Source, locations.Count, location.Index);
                if (!LinksDungeon(dungeons, archive.Source, exteriorLocationId))
                {
                    result.Add(new MapsDungeonLocation(region, location.Index, location.Name, exteriorLocationId, 0, [], MapsDungeonState.NoDungeon,
                        $"Dungeon type {location.DungeonType} pairs with exterior location {exteriorLocationId}, which no dungeon record links, so the location has none."));
                    continue;
                }

                (uint locationId, IReadOnlyList<MapsDungeonBlock> blocks) = DecodeDungeonRecord(dungeons, archive.Source, exteriorLocationId);
                result.Add(new MapsDungeonLocation(region, location.Index, location.Name, exteriorLocationId, locationId, blocks, MapsDungeonState.Read,
                    $"Dungeon type {location.DungeonType} pairs with exterior location {exteriorLocationId} and {blocks.Count} blocks."));
            }
            catch (Arena2FormatException failure)
            {
                result.Add(new MapsDungeonLocation(region, location.Index, location.Name, 0, 0, [], MapsDungeonState.Malformed, failure.Message));
            }
        }

        return result;
    }

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
        (int mapId, int longitude, int latitude, byte dungeonType, int _, bool _) = DecodeMapTable(GetNamedPayload(archive, "MAPTABLE", region), archive.Source, locationIndex);
        uint exteriorLocationId = DecodeExteriorLocationId(GetNamedPayload(archive, "MAPPITEM", region), archive.Source, names.Count, locationIndex);
        ReadOnlyMemory<byte> dungeonItems = GetNamedPayload(archive, "MAPDITEM", region);

        // The same bytes read in bulk report a location with no linked dungeon, so this reports it the
        // same way rather than refusing what the bulk path calls an ordinary outcome: no dungeon, no
        // identity and no blocks, with the exterior location still named.
        if (!LinksDungeon(dungeonItems, archive.Source, exteriorLocationId))
        {
            return new MapsDungeonLayout(region, locationIndex, locationName, mapId, 0, longitude, latitude, dungeonType, []);
        }

        (uint locationId, IReadOnlyList<MapsDungeonBlock> blocks) = DecodeDungeonRecord(dungeonItems, archive.Source, exteriorLocationId);
        return new MapsDungeonLayout(region, locationIndex, locationName, mapId, locationId, longitude, latitude, dungeonType, blocks);
    }

    /// <summary>
    /// Reads the exact RMB-grid source layout of one named exterior location.
    /// </summary>
    /// <remarks>
    /// This is the MAPPITEM path from the donor's <c>ReadMapPItem</c>, including its RMB-name
    /// construction rules.  It is source admission only; the normalizer owns the later geometry and
    /// coordinate conversion rather than this decoder inventing a runtime block size or collision
    /// volume.
    /// </remarks>
    public static MapsExteriorLayout DecodeExteriorLayout(BsaArchive archive, int region, string locationName)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(locationName);
        IReadOnlyList<string> names = DecodeLocationNames(archive, region);
        int locationIndex = FindExactLocation(names, locationName, archive.Source);
        return DecodeExteriorLayout(archive, region, locationIndex);
    }

    /// <summary>
    /// Reads the exact RMB-grid source layout of the location at a region/index identity. The index
    /// overload is the lossless path for normalized publication because MAPNAMES legitimately repeats
    /// names within one region.
    /// </summary>
    public static MapsExteriorLayout DecodeExteriorLayout(BsaArchive archive, int region, int locationIndex)
    {
        ArgumentNullException.ThrowIfNull(archive);
        IReadOnlyList<string> names = DecodeLocationNames(archive, region);
        if ((uint)locationIndex >= (uint)names.Count)
        {
            throw new Arena2FormatException(archive.Source, 0, $"MAPS location index {locationIndex} is outside region {region}'s {names.Count} names");
        }

        string locationName = names[locationIndex];
        (int mapId, int longitude, int latitude, _, _, _) = DecodeMapTable(GetNamedPayload(archive, "MAPTABLE", region), archive.Source, locationIndex);
        return DecodeExteriorRecord(GetNamedPayload(archive, "MAPPITEM", region), archive.Source, names.Count, region, locationIndex, locationName, mapId, longitude, latitude);
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

    private static (int MapId, int Longitude, int Latitude, byte DungeonType, int LocationType, bool Discovered) DecodeMapTable(ReadOnlyMemory<byte> data, string source, int locationIndex)
    {
        const int entryBytes = 17;
        int offset = CheckedMultiply(locationIndex, entryBytes, source, "MAPTABLE entry");
        CheckedLittleEndianReader reader = At(data.Span, source, offset, "MAPTABLE entry");
        int mapId = reader.ReadInt32();
        uint longitudeBits = reader.ReadUInt32();
        int latitudeBits = reader.ReadInt32();
        byte dungeonType = reader.ReadByte();
        // The donor's own masks: longitude is the low twenty-five bits of the bitfield, latitude the
        // low twenty-four of the next word, and the same word carries the location type and whether
        // the location is discovered.
        return (
            mapId,
            (int)((longitudeBits & LongitudeMask) >> 8),
            (latitudeBits & LatitudeMask) >> 8,
            dungeonType,
            (int)((4 * longitudeBits) >> 27),
            ((longitudeBits >> 24) & 0x40) != 0);
    }

    private static uint DecodeExteriorLocationId(ReadOnlyMemory<byte> data, string source, int locationCount, int locationIndex)
    {
        CheckedLittleEndianReader reader = ExteriorRecordReader(data, source, locationCount, locationIndex);
        int recordOffset = reader.Position;
        int doorCount = CheckedCount(reader.ReadUInt32(), source, recordOffset, "MAPPITEM door count");
        reader.ReadBytes(CheckedMultiply(doorCount, 6, source, "MAPPITEM doors"));
        reader.ReadBytes(33);
        return reader.ReadUInt16();
    }

    private static MapsExteriorLayout DecodeExteriorRecord(
        ReadOnlyMemory<byte> data,
        string source,
        int locationCount,
        int region,
        int locationIndex,
        string locationName,
        int mapId,
        int longitude,
        int latitude)
    {
        CheckedLittleEndianReader reader = ExteriorRecordReader(data, source, locationCount, locationIndex);
        int doorCount = CheckedCount(reader.ReadUInt32(), source, reader.Position - sizeof(uint), "MAPPITEM door count");
        reader.ReadBytes(CheckedMultiply(doorCount, 6, source, "MAPPITEM doors"));

        // LocationRecordElement.Header, shared with MAPDITEM.  The fields before and after the
        // location id are intentionally skipped here because the exterior normalization only owns
        // its stable location identity and RMB grid; DecodeExteriorLocationId follows the same
        // donor header offset.
        reader.ReadBytes(33);
        uint locationId = reader.ReadUInt16();
        reader.ReadBytes(77);

        int buildingCount = reader.ReadUInt16();
        reader.ReadBytes(5);
        reader.ReadBytes(CheckedMultiply(buildingCount, 26, source, "MAPPITEM building records"));

        _ = reader.ReadNullTerminatedAscii(32); // another exterior name; the MAPNAMES identity is authoritative here.
        _ = reader.ReadInt32(); // duplicate map id in the exterior payload
        _ = reader.ReadUInt32(); // duplicate exterior location id
        byte width = reader.ReadByte();
        byte height = reader.ReadByte();
        int blockCount = CheckedMultiply(width, height, source, "MAPPITEM exterior block grid");
        if (blockCount > 64)
            throw reader.Error($"MAPPITEM exterior grid {width}x{height} exceeds its 64 source block slots");
        reader.ReadBytes(4);
        char letter1 = (char)reader.ReadByte();
        reader.ReadBytes(2);
        ReadOnlySpan<byte> blockIndices = reader.ReadBytes(64);
        ReadOnlySpan<byte> blockNumbers = reader.ReadBytes(64);
        ReadOnlySpan<byte> blockCharacters = reader.ReadBytes(64);

        List<MapsExteriorBlock> blocks = new(blockCount);
        for (int index = 0; index < blockCount; index++)
        {
            blocks.Add(new MapsExteriorBlock(
                ResolveRmbBlockName(blockIndices[index], blockNumbers[index], blockCharacters[index], letter1, reader),
                checked((byte)(index % width)),
                checked((byte)(index / width))));
        }

        return new MapsExteriorLayout(region, locationIndex, locationName, mapId, longitude, latitude, locationId, width, height, letter1, blocks);
    }

    private static CheckedLittleEndianReader ExteriorRecordReader(ReadOnlyMemory<byte> data, string source, int locationCount, int locationIndex)
    {
        int tableBytes = CheckedMultiply(locationCount, sizeof(uint), source, "MAPPITEM offset table");
        int indexOffset = CheckedMultiply(locationIndex, sizeof(uint), source, "MAPPITEM offset entry");
        uint relativeOffset = At(data.Span, source, indexOffset, "MAPPITEM offset entry").ReadUInt32();
        int recordOffset = CheckedAdd(tableBytes, CheckedCount(relativeOffset, source, indexOffset, "MAPPITEM record offset"), source, "MAPPITEM record");
        return At(data.Span, source, recordOffset, "MAPPITEM location record");
    }

    private static string ResolveRmbBlockName(byte blockIndex, byte blockNumber, byte blockCharacter, char letter1, CheckedLittleEndianReader reader)
    {
        if (blockIndex >= BlockRecordInventoryReader.RmbBlockPrefixes.Count)
            throw reader.Error($"MAPPITEM block has unsupported RMB prefix index {blockIndex}");
        // DFU evaluates this through a byte before shifting: the source field is an
        // 8-bit bitfield, so values in its high half wrap before selecting the
        // second-letter quadrant. Keeping the byte cast matters for the full MAPS
        // corpus (several exterior records carry 0x80+ here).
        int letter2Index = unchecked((byte)(2 * blockCharacter)) >> 6;
        if ((uint)letter2Index >= BlockRecordInventoryReader.RmbLetters2.Count)
            throw reader.Error($"MAPPITEM block has unsupported RMB second-letter index {letter2Index}");

        char first = (blockCharacter & 0x10) != 0 ? letter1 : 'A';
        char second = BlockRecordInventoryReader.RmbLetters2[letter2Index];
        string suffix = blockIndex is 13 or 14
            ? string.Concat((char)((blockCharacter & 0x0F) + 'A'), blockNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : blockNumber.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        return $"{BlockRecordInventoryReader.RmbBlockPrefixes[blockIndex]}{first}{second}{suffix}.RMB";
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
