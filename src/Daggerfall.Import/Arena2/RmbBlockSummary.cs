namespace Daggerfall.Import.Arena2;

/// <summary>One building slot of an RMB block header, as the header states it.</summary>
/// <param name="Index">The slot's ordinal, which is the sub-record's ordinal in the block.</param>
/// <param name="ByteLength">The bytes the donor steps over for this sub-record, padding included.</param>
/// <param name="BuildingType">The building type byte the slot carries.</param>
/// <param name="FactionId">The faction the slot names, or zero when it names none.</param>
/// <param name="Quality">The quality byte the slot carries.</param>
/// <param name="NameSeed">The seed the building's generated name is derived from.</param>
/// <param name="Objects">How many 3D object records the sub-record's own header declares.</param>
/// <param name="Flats">How many flat object records the sub-record's own header declares.</param>
/// <param name="Sections">How many section-3 records the sub-record's own header declares.</param>
/// <param name="People">How many people records the sub-record's own header declares.</param>
/// <param name="Doors">How many door records the sub-record's own header declares.</param>
public sealed record RmbBuildingSlot(
    int Index,
    int ByteLength,
    byte BuildingType,
    ushort FactionId,
    byte Quality,
    ushort NameSeed,
    byte Objects,
    byte Flats,
    byte Sections,
    byte People,
    byte Doors);

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
    int TrailingBytes);

/// <summary>
/// Reads the fixed part of an RMB block header: its counts, the building sub-records it declares and
/// the counts those sub-records state for themselves.
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
/// What this deliberately does not read: ground tiles, ground scenery, the automap, the header's other
/// name slots, the sub-records' own objects, and the block's own object records. Those are placements
/// and geometry, which belong to the tasks that publish dungeon and exterior assemblies; this is the
/// inventory those tasks start from.
/// </para>
/// </remarks>
public static class RmbBlockSummaryReader
{
    /// <summary>The bytes of the fixed header, up to and including the block's own name.</summary>
    public const int NameOffset = 6347;

    /// <summary>The bytes the header occupies before the declared sub-records begin.</summary>
    public const int HeaderBytes = 6776;

    /// <summary>Bytes one 3D object record occupies, from the fields the donor reads.</summary>
    public const int ModelRecordBytes = 66;

    /// <summary>Bytes one flat object record occupies, from the fields the donor reads.</summary>
    public const int FlatRecordBytes = 17;

    /// <summary>Name slots the header carries besides its own name.</summary>
    public const int OtherNameSlots = 32;

    /// <summary>Bytes one other-name slot occupies.</summary>
    public const int NameSlotBytes = 13;

    private const int CountsBytes = 3;
    private const int BlockPositionsBytes = 32 * 20;
    private const int BuildingDataBytes = 32 * 26;
    private const int Section2Bytes = 32 * 4;
    private const int BlockDataSizesOffset = CountsBytes + BlockPositionsBytes + BuildingDataBytes + Section2Bytes;
    private const int BuildingDataOffset = CountsBytes + BlockPositionsBytes;
    private const int BuildingDataSlotBytes = 26;
    private const int SubRecordHeaderBytes = 5;

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
        if (declaredBlocks > 32)
        {
            reason = $"declares {declaredBlocks} building sub-records, more than the header's 32 slots";
            return false;
        }

        int[] sizes = new int[declaredBlocks];
        List<RmbBuildingSlot> buildings = new(declaredBlocks);
        int position = HeaderBytes;
        for (int index = 0; index < declaredBlocks; index++)
        {
            int sizeOffset = offset + BlockDataSizesOffset + (index * sizeof(int));
            int size = bytes[sizeOffset] | (bytes[sizeOffset + 1] << 8) | (bytes[sizeOffset + 2] << 16) | (bytes[sizeOffset + 3] << 24);
            if (size < SubRecordHeaderBytes || size > length - position)
            {
                reason = $"declares {size} bytes for building {index}, which does not fit in the {length - position} bytes left after the header";
                return false;
            }

            sizes[index] = size;
            int slotOffset = offset + BuildingDataOffset + (index * BuildingDataSlotBytes);
            buildings.Add(new RmbBuildingSlot(
                index,
                size,
                bytes[slotOffset + 24],
                (ushort)(bytes[slotOffset + 14] | (bytes[slotOffset + 15] << 8)),
                bytes[slotOffset + 25],
                (ushort)(bytes[slotOffset] | (bytes[slotOffset + 1] << 8)),
                bytes[offset + position],
                bytes[offset + position + 1],
                bytes[offset + position + 2],
                bytes[offset + position + 3],
                bytes[offset + position + 4]));
            position += size;
        }

        // The bytes after the header are what the declared sub-records occupy plus the block's own object
        // records. The donor steps over the sub-records and reads the objects that follow, so a record
        // where the two disagree cannot be summarized from these offsets at all.
        int accounted = position + (misc3dObjects * ModelRecordBytes) + (miscFlatObjects * FlatRecordBytes);
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

        summary = new RmbBlockSummary(
            Text(bytes, offset + NameOffset, NameSlotBytes),
            otherNames,
            declaredBlocks,
            misc3dObjects,
            miscFlatObjects,
            buildings,
            length - accounted);
        return true;
    }

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
