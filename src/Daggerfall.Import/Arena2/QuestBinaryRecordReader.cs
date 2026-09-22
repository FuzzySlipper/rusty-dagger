namespace Daggerfall.Import.Arena2;

/// <summary>What the fixed-record decode could establish about a classic quest binary.</summary>
public enum QuestBinaryRecordDisposition
{
    /// <summary>Every decoded fixed record lies within the binary.</summary>
    Decoded,

    /// <summary>The binary ends before the fixed header or a declared record extent.</summary>
    Truncated,

    /// <summary>A declared record offset or count cannot describe the binary.</summary>
    Malformed,
}

/// <summary>The fields in the classic QBN header needed to locate fixed record sections.</summary>
public sealed record QuestBinaryHeader(
    ushort QuestId,
    ushort FactionId,
    ushort ResourceId,
    string ResourceFileName,
    byte HasDebugInfo,
    ushort ItemCount,
    ushort NpcCount,
    ushort LocationCount,
    ushort OpcodeCount,
    ushort ItemOffset,
    ushort NpcOffset,
    ushort LocationOffset,
    ushort OpcodeOffset);

/// <summary>A text-record reference stored by one fixed QBN resource record.</summary>
public sealed record QuestBinaryTextReference(string RecordKind, short RecordIndex, ushort FirstMessageId, ushort SecondMessageId);

/// <summary>The message field stored by one fixed QBN opcode record.</summary>
public sealed record QuestBinaryOpcodeReference(int RecordIndex, short Opcode, short MessageId);

/// <summary>
/// The source-backed part of a classic QBN: header identity plus the text references from
/// item, person, place, and opcode records. This is deliberately an identity reader, not
/// an action interpreter; it never makes a binary executable.
/// </summary>
public sealed record QuestBinaryRecords(
    string Path,
    int Length,
    QuestBinaryHeader? Header,
    IReadOnlyList<QuestBinaryTextReference> ResourceReferences,
    IReadOnlyList<QuestBinaryOpcodeReference> OpcodeReferences,
    QuestBinaryRecordDisposition Disposition,
    string Note)
{
    /// <summary>Bytes before the count and offset table.</summary>
    public const int HeaderBytes = 60;
    private const int ItemBytes = 19;
    private const int NpcBytes = 20;
    private const int LocationBytes = 24;
    private const int OpcodeBytes = 87;

    /// <summary>Reads the fixed QBN records whose fields reference QRC message identities.</summary>
    public static QuestBinaryRecords Decode(ReadOnlySpan<byte> bytes, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (bytes.Length < HeaderBytes)
        {
            return Invalid(path, bytes.Length, QuestBinaryRecordDisposition.Truncated,
                $"The file is {bytes.Length} bytes where the fixed QBN header is {HeaderBytes} bytes.");
        }

        try
        {
            CheckedLittleEndianReader headerReader = new(bytes, path);
            QuestBinaryHeader header = ReadHeader(ref headerReader);
            List<QuestBinaryTextReference> resources = [];
            ReadItems(bytes, path, header, resources);
            ReadNpcs(bytes, path, header, resources);
            ReadLocations(bytes, path, header, resources);
            List<QuestBinaryOpcodeReference> opcodes = [];
            ReadOpcodes(bytes, path, header, opcodes);
            return new QuestBinaryRecords(path, bytes.Length, header, resources, opcodes,
                QuestBinaryRecordDisposition.Decoded,
                $"Decoded {resources.Count} resource records and {opcodes.Count} opcode records from fixed QBN sections.");
        }
        catch (Arena2FormatException exception)
        {
            return Invalid(path, bytes.Length, QuestBinaryRecordDisposition.Malformed, exception.Message);
        }
    }

    private static QuestBinaryHeader ReadHeader(ref CheckedLittleEndianReader reader)
    {
        ushort questId = reader.ReadUInt16();
        ushort factionId = reader.ReadUInt16();
        ushort resourceId = reader.ReadUInt16();
        string resourceFileName = reader.ReadNullTerminatedAscii(9);
        byte hasDebugInfo = reader.ReadByte();
        ushort itemCount = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // Unknown fixed section 1.
        _ = reader.ReadUInt16(); // Unknown fixed section 2.
        ushort npcCount = reader.ReadUInt16();
        ushort locationCount = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // Unknown fixed section 5.
        _ = reader.ReadUInt16(); // Timer records do not carry QRC identities.
        _ = reader.ReadUInt16(); // Mob records do not carry QRC identities.
        ushort opcodeCount = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // State records do not carry QRC identities.
        ushort itemOffset = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // Unknown fixed section 1.
        _ = reader.ReadUInt16(); // Unknown fixed section 2.
        ushort npcOffset = reader.ReadUInt16();
        ushort locationOffset = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // Unknown fixed section 5.
        _ = reader.ReadUInt16(); // Timers.
        _ = reader.ReadUInt16(); // Mobs.
        ushort opcodeOffset = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // States.
        _ = reader.ReadUInt16(); // Text variables.
        _ = reader.ReadUInt16(); // Observed trailing header field.
        return new QuestBinaryHeader(questId, factionId, resourceId, resourceFileName, hasDebugInfo,
            itemCount, npcCount, locationCount, opcodeCount, itemOffset, npcOffset, locationOffset, opcodeOffset);
    }

    private static void ReadItems(ReadOnlySpan<byte> bytes, string path, QuestBinaryHeader header, List<QuestBinaryTextReference> target)
    {
        CheckedLittleEndianReader reader = Section(bytes, path, "item", header.ItemOffset, header.ItemCount, ItemBytes);
        for (int index = 0; index < header.ItemCount; index++)
        {
            short recordIndex = reader.ReadInt16();
            _ = reader.ReadByte();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            target.Add(new QuestBinaryTextReference("item", recordIndex, reader.ReadUInt16(), reader.ReadUInt16()));
        }
    }

    private static void ReadNpcs(ReadOnlySpan<byte> bytes, string path, QuestBinaryHeader header, List<QuestBinaryTextReference> target)
    {
        CheckedLittleEndianReader reader = Section(bytes, path, "person", header.NpcOffset, header.NpcCount, NpcBytes);
        for (int index = 0; index < header.NpcCount; index++)
        {
            short recordIndex = reader.ReadInt16();
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            target.Add(new QuestBinaryTextReference("person", recordIndex, reader.ReadUInt16(), reader.ReadUInt16()));
        }
    }

    private static void ReadLocations(ReadOnlySpan<byte> bytes, string path, QuestBinaryHeader header, List<QuestBinaryTextReference> target)
    {
        CheckedLittleEndianReader reader = Section(bytes, path, "place", header.LocationOffset, header.LocationCount, LocationBytes);
        for (int index = 0; index < header.LocationCount; index++)
        {
            short recordIndex = reader.ReadInt16();
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadUInt16();
            _ = reader.ReadInt16();
            _ = reader.ReadInt16();
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            target.Add(new QuestBinaryTextReference("place", recordIndex, reader.ReadUInt16(), reader.ReadUInt16()));
        }
    }

    private static void ReadOpcodes(ReadOnlySpan<byte> bytes, string path, QuestBinaryHeader header, List<QuestBinaryOpcodeReference> target)
    {
        CheckedLittleEndianReader reader = Section(bytes, path, "opcode", header.OpcodeOffset, header.OpcodeCount, OpcodeBytes);
        for (int index = 0; index < header.OpcodeCount; index++)
        {
            short opcode = reader.ReadInt16();
            _ = reader.ReadInt16();
            short argumentCount = reader.ReadInt16();
            if (argumentCount < 0 || argumentCount > 5)
            {
                throw reader.Error($"opcode {index} declares {argumentCount} arguments where the fixed record stores zero through five.");
            }

            // The format reserves five 15-byte argument slots regardless of ArgCount.
            _ = reader.ReadBytes(5 * 15);
            target.Add(new QuestBinaryOpcodeReference(index, opcode, reader.ReadInt16()));
            _ = reader.ReadUInt32();
        }
    }

    private static CheckedLittleEndianReader Section(ReadOnlySpan<byte> bytes, string path, string name, ushort offset, ushort count, int recordBytes)
    {
        if (count == 0)
        {
            return new CheckedLittleEndianReader(ReadOnlySpan<byte>.Empty, path);
        }

        long end = (long)offset + ((long)count * recordBytes);
        if (offset < HeaderBytes || end > bytes.Length)
        {
            throw new Arena2FormatException(path, offset,
                $"{name} section declares {count} record(s) of {recordBytes} bytes from {offset} through {end}, outside source length {bytes.Length}.");
        }

        CheckedLittleEndianReader reader = new(bytes, path);
        reader.Seek(offset);
        return reader;
    }

    private static QuestBinaryRecords Invalid(string path, int length, QuestBinaryRecordDisposition disposition, string note) =>
        new(path, length, null, [], [], disposition, note);
}
