namespace Daggerfall.Import.Arena2;

/// <summary>What one of a city block sub-record's two halves declares it places.</summary>
/// <param name="Objects">How many 3D object records it declares.</param>
/// <param name="Flats">How many flat object records it declares.</param>
/// <param name="Sections">How many section-3 records it declares.</param>
/// <param name="People">How many people records it declares.</param>
/// <param name="Doors">How many door records it declares.</param>
public sealed record RmbObjectCounts(int Objects, int Flats, int Sections, int People, int Doors)
{
    /// <summary>Bytes one 3D object record occupies.</summary>
    public const int ModelRecordBytes = 66;

    /// <summary>Bytes one flat object record occupies.</summary>
    public const int FlatRecordBytes = 17;

    /// <summary>Bytes one section-3 record occupies.</summary>
    public const int SectionRecordBytes = 16;

    /// <summary>Bytes one people record occupies.</summary>
    public const int PeopleRecordBytes = 17;

    /// <summary>Bytes one door record occupies.</summary>
    public const int DoorRecordBytes = 19;

    /// <summary>The bytes the records these counts declare occupy.</summary>
    public int BodyBytes => (Objects * ModelRecordBytes)
        + (Flats * FlatRecordBytes)
        + (Sections * SectionRecordBytes)
        + (People * PeopleRecordBytes)
        + (Doors * DoorRecordBytes);
}

/// <summary>One source ground tile in an RMB block's fixed header.</summary>
public sealed record RmbGroundTile(byte X, byte Y, byte TextureRecord, bool Rotated, bool Flipped);

/// <summary>One building sub-record of an RMB block header: its slot data and both of its halves.</summary>
/// <param name="Index">The slot's ordinal, which is the sub-record's ordinal in the block.</param>
/// <param name="X">The donor sub-record's X position within its RMB block.</param>
/// <param name="Z">The donor sub-record's Z position within its RMB block.</param>
/// <param name="YRotation">The donor sub-record yaw, in classic rotation units.</param>
/// <param name="ByteLength">The bytes the donor steps over for this sub-record, padding included.</param>
/// <param name="PaddingBytes">The bytes the sub-record reserves past what its halves occupy.</param>
/// <param name="BuildingType">The building type byte the slot carries.</param>
/// <param name="FactionId">The faction the slot names, or zero when it names none.</param>
/// <param name="Quality">The quality byte the slot carries.</param>
/// <param name="NameSeed">The seed the building's generated name is derived from.</param>
/// <param name="Exterior">What the sub-record's outside half declares.</param>
/// <param name="Interior">What the sub-record's inside half declares.</param>
public sealed record RmbBuildingSlot(
    int Index,
    int X,
    int Z,
    int YRotation,
    int ByteLength,
    int PaddingBytes,
    byte BuildingType,
    ushort FactionId,
    byte Quality,
    ushort NameSeed,
    RmbObjectCounts Exterior,
    RmbObjectCounts Interior);

/// <summary>
/// The bounded header summary of one RMB city or exterior block: what the block itself declares it
/// carries, without decoding a single placement.
/// </summary>
/// <param name="Name">The name the block states for itself.</param>
/// <param name="OtherNames">How many of the header's thirty-two other-name slots carry a name.</param>
/// <param name="DeclaredBlocks">How many building sub-records the header declares.</param>
/// <param name="Misc3dObjects">How many 3D objects the block places outside any sub-record.</param>
/// <param name="MiscFlatObjects">How many flat objects the block places outside any sub-record.</param>
/// <param name="Buildings">One entry per declared building sub-record, in the header's own order.</param>
/// <param name="TrailingBytes">
/// Bytes the record carries after the declared sub-records and the objects they are followed by. The
/// donor steps over the header and ignores whatever follows, so a difference is reported rather than
/// refused: it is the source's own shape, not a site the product reads.
/// </param>
public sealed record RmbBlockSummary(
    string Name,
    int OtherNames,
    int DeclaredBlocks,
    int Misc3dObjects,
    int MiscFlatObjects,
    IReadOnlyList<RmbBuildingSlot> Buildings,
    int TrailingBytes)
{
    /// <summary>All 16-by-16 ground tiles read from the source FLD header.</summary>
    public IReadOnlyList<RmbGroundTile> GroundTiles { get; init; } = [];

    /// <summary>The source 64-by-64 automap occupancy bytes used to conservatively retain outdoor ground.</summary>
    public IReadOnlyList<byte> AutoMapData { get; init; } = [];
}

/// <summary>
/// Reads the fixed part of an RMB block header: its counts, the building sub-records it declares and
/// what each of those sub-records states for itself.
/// </summary>
/// <remarks>
/// <para>
/// The header is a fixed 6776-byte prefix whose shape was established against the supplied corpus rather
/// than assumed: for all 920 supplied RMB records the name field at offset 6347 equals the archive key
/// the record is stored under, and the bytes after the header account exactly for the declared
/// sub-records plus the block's own object records at 66 and 17 bytes each. That identity is checked
/// here, so a record whose header does not add up is published as malformed with the mismatch rather
/// than summarized from offsets that happen to land in the middle of something else.
/// </para>
/// <para>
/// Each declared sub-record is a pair: an outside half and an inside half, each led by a
/// seventeen-byte header (five counts and six two-byte words this reader does not interpret) and
/// followed by the records those counts declare, at the sizes the donor reads them. Both halves are
/// read, because a summary that stopped at the first would state one object for a building that declares
/// seven. The declared size may reserve a byte past the two halves — the donor steps over it — so the
/// remainder is published rather than assumed away.
/// </para>
/// <para>
/// This reader retains the ground tiles and automap because RMB exterior assembly needs their source
/// collision/navigation facts. Ground scenery, the header's other name slots, and the placements inside
/// either half remain outside this bounded header summary.
/// </para>
/// </remarks>
public static class RmbBlockSummaryReader
{
    /// <summary>The bytes of the fixed header, up to and including the block's own name.</summary>
    public const int NameOffset = 6347;

    /// <summary>The bytes the header occupies before the declared sub-records begin.</summary>
    public const int HeaderBytes = 6776;
    private const int GroundTilesOffset = 1739;
    private const int GroundTileCount = 16 * 16;
    private const int AutoMapOffset = 2251;
    private const int AutoMapBytes = 64 * 64;

    /// <summary>Bytes one sub-record's own header occupies: five counts and six uninterpreted words.</summary>
    public const int SubRecordHeaderBytes = 17;

    /// <summary>Name slots the header carries besides its own name.</summary>
    public const int OtherNameSlots = 32;

    /// <summary>Bytes one other-name slot occupies.</summary>
    public const int NameSlotBytes = 13;

    /// <summary>Sub-record slots the header carries, which is also the largest count it can declare.</summary>
    public const int SubRecordSlots = 32;

    private const int CountsBytes = 3;
    private const int BlockPositionsBytes = 32 * 20;
    private const int BuildingDataBytes = 32 * 26;
    private const int Section2Bytes = 32 * 4;
    private const int BlockDataSizesOffset = CountsBytes + BlockPositionsBytes + BuildingDataBytes + Section2Bytes;
    private const int BuildingDataOffset = CountsBytes + BlockPositionsBytes;
    private const int BuildingDataSlotBytes = 26;

    /// <summary>Reads one RMB block's header summary, or reports why its header cannot be read.</summary>
    public static bool TryRead(byte[] bytes, string source, int offset, int length, out RmbBlockSummary? summary, out string reason)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        summary = null;
        reason = string.Empty;
        if (length < HeaderBytes)
        {
            reason = $"is {length} bytes, too short to carry the {HeaderBytes}-byte block header it declares";
            return false;
        }

        int declaredBlocks = bytes[offset];
        int misc3dObjects = bytes[offset + 1];
        int miscFlatObjects = bytes[offset + 2];
        if (declaredBlocks > SubRecordSlots)
        {
            reason = $"declares {declaredBlocks} building sub-records, more than the header's {SubRecordSlots} slots";
            return false;
        }

        List<RmbBuildingSlot> buildings = new(declaredBlocks);
        int position = HeaderBytes;
        for (int index = 0; index < declaredBlocks; index++)
        {
            int sizeOffset = offset + BlockDataSizesOffset + (index * sizeof(int));
            int size = bytes[sizeOffset] | (bytes[sizeOffset + 1] << 8) | (bytes[sizeOffset + 2] << 16) | (bytes[sizeOffset + 3] << 24);
            if (size < SubRecordHeaderBytes * 2 || size > length - position)
            {
                reason = $"declares {size} bytes for building {index}, which cannot hold its two {SubRecordHeaderBytes}-byte halves in the {length - position} bytes left after the header";
                return false;
            }

            // Each declared size spans an outside half and an inside half, and the donor steps over the
            // whole of it. A half whose own counts run past the declared end would put the next read in
            // the middle of a record, so it is refused rather than summarized from a guessed offset.
            RmbObjectCounts exterior = Counts(bytes, offset + position);
            int interiorOffset = offset + position + SubRecordHeaderBytes + exterior.BodyBytes;
            if (interiorOffset + SubRecordHeaderBytes > offset + position + size)
            {
                reason = $"declares {exterior.BodyBytes} bytes of records in building {index}'s outside half, which leaves no room for its inside half in {size} bytes";
                return false;
            }

            RmbObjectCounts interior = Counts(bytes, interiorOffset);
            int used = (SubRecordHeaderBytes * 2) + exterior.BodyBytes + interior.BodyBytes;
            if (used > size)
            {
                reason = $"declares {used} bytes of records and headers in building {index}, past the {size} bytes it reserves";
                return false;
            }

            int positionOffset = offset + CountsBytes + (index * 20);
            int slotOffset = offset + BuildingDataOffset + (index * BuildingDataSlotBytes);
            buildings.Add(new RmbBuildingSlot(
                index,
                ReadInt32(bytes, positionOffset + 8),
                ReadInt32(bytes, positionOffset + 12),
                ReadInt32(bytes, positionOffset + 16),
                size,
                size - used,
                bytes[slotOffset + 24],
                (ushort)(bytes[slotOffset + 18] | (bytes[slotOffset + 19] << 8)),
                bytes[slotOffset + 25],
                (ushort)(bytes[slotOffset] | (bytes[slotOffset + 1] << 8)),
                exterior,
                interior));
            position += size;
        }

        // The bytes after the header are what the declared sub-records occupy plus the block's own object
        // records. The donor steps over the sub-records and reads the objects that follow, so a record
        // where the two disagree cannot be summarized from these offsets at all.
        long accounted = position + (misc3dObjects * (long)RmbObjectCounts.ModelRecordBytes) + (miscFlatObjects * (long)RmbObjectCounts.FlatRecordBytes);
        if (accounted > length)
        {
            reason = $"accounts for {accounted} bytes from its header, past the record's {length}";
            return false;
        }

        int otherNames = 0;
        for (int index = 0; index < OtherNameSlots; index++)
        {
            int nameOffset = offset + NameOffset + NameSlotBytes + (index * NameSlotBytes);
            if (bytes[nameOffset] != 0)
            {
                otherNames++;
            }
        }

        List<RmbGroundTile> groundTiles = new(GroundTileCount);
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            byte bitfield = bytes[offset + GroundTilesOffset + (y * 16) + x];
            groundTiles.Add(new RmbGroundTile((byte)x, (byte)y, (byte)(bitfield & 0x3f), (bitfield & 0x40) != 0, (bitfield & 0x80) != 0));
        }
        summary = new RmbBlockSummary(
            Text(bytes, offset + NameOffset, NameSlotBytes),
            otherNames,
            declaredBlocks,
            misc3dObjects,
            miscFlatObjects,
            buildings,
            (int)(length - accounted))
        {
            GroundTiles = groundTiles,
            AutoMapData = bytes.Skip(offset + AutoMapOffset).Take(AutoMapBytes).ToArray(),
        };
        return true;
    }

    /// <summary>Reads one half's five declared counts, which lead its seventeen-byte header.</summary>
    private static RmbObjectCounts Counts(byte[] bytes, int offset) =>
        new(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3], bytes[offset + 4]);

    private static int ReadInt32(byte[] bytes, int offset) => bytes[offset]
        | (bytes[offset + 1] << 8)
        | (bytes[offset + 2] << 16)
        | (bytes[offset + 3] << 24);

    /// <summary>Reads one fixed thirteen-byte name slot, which the source terminates with a zero byte.</summary>
    private static string Text(byte[] bytes, int offset, int length)
    {
        int end = offset;
        int stop = offset + length;
        while (end < stop && bytes[end] != 0)
        {
            end++;
        }

        return System.Text.Encoding.ASCII.GetString(bytes, offset, end - offset);
    }
}
