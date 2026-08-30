namespace Daggerfall.Import.Arena2;

/// <summary>One BSA directory record and its stable source identity.</summary>
public sealed record BsaRecord(int Ordinal, string? Name, uint? NumericId, int Offset, int Length)
{
    /// <summary>Whether this record came from the named BSA directory variant.</summary>
    public bool IsNamed => Name is not null;
}

/// <summary>Read-only BSA archive parsed from supplied bytes.</summary>
public sealed class BsaArchive
{
    private readonly byte[] data;
    private readonly Dictionary<string, BsaRecord> recordsByName;
    private readonly Dictionary<uint, BsaRecord> recordsById;

    private BsaArchive(byte[] data, string source, IReadOnlyList<BsaRecord> records)
    {
        this.data = data;
        Source = source;
        Records = records;
        recordsByName = records
            .Where(static record => record.Name is not null)
            .ToDictionary(static record => record.Name!, StringComparer.Ordinal);
        recordsById = records
            .Where(static record => record.NumericId.HasValue)
            .ToDictionary(static record => record.NumericId!.Value);
    }

    /// <summary>Logical source identity supplied to <see cref="Parse"/>.</summary>
    public string Source { get; }

    /// <summary>Directory-order records, preserving ordinal, name, and numeric-ID distinctions.</summary>
    public IReadOnlyList<BsaRecord> Records { get; }

    /// <summary>Parses a BSA archive from immutable caller-supplied source bytes.</summary>
    public static BsaArchive Parse(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        byte[] ownedData = bytes.ToArray();
        CheckedLittleEndianReader header = new(ownedData, source);
        short signedCount = header.ReadInt16();
        ushort directoryType = header.ReadUInt16();
        if (signedCount < 0)
        {
            throw header.Error($"negative BSA record count {signedCount}");
        }

        int count = signedCount;
        return directoryType switch
        {
            Arena2FormatConstants.NamedBsaDirectoryType => ParseNamed(ownedData, source, count),
            Arena2FormatConstants.NumericBsaDirectoryType => ParseNumeric(ownedData, source, count),
            _ => throw header.Error($"unsupported BSA directory type 0x{directoryType:X4}"),
        };
    }

    /// <summary>Gets a record by its exact stored named-directory key.</summary>
    public bool TryGetByName(string name, out BsaRecord? record)
    {
        ArgumentNullException.ThrowIfNull(name);
        return recordsByName.TryGetValue(name, out record);
    }

    /// <summary>Gets a record by its numeric-directory ID.</summary>
    public bool TryGetByNumericId(uint numericId, out BsaRecord? record)
    {
        return recordsById.TryGetValue(numericId, out record);
    }

    /// <summary>Gets a record by physical directory order.</summary>
    public bool TryGetByOrdinal(int ordinal, out BsaRecord? record)
    {
        if ((uint)ordinal < (uint)Records.Count)
        {
            record = Records[ordinal];
            return true;
        }

        record = null;
        return false;
    }

    /// <summary>Returns the immutable payload for a record that belongs to this archive.</summary>
    public ReadOnlyMemory<byte> GetPayload(BsaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!TryGetByOrdinal(record.Ordinal, out BsaRecord? ownedRecord) || ownedRecord != record)
        {
            throw new ArgumentException("The record does not belong to this archive.", nameof(record));
        }

        return data.AsMemory(record.Offset, record.Length);
    }

    private static BsaArchive ParseNamed(byte[] data, string source, int count)
    {
        int directoryBytes = CheckedProduct(count, Arena2FormatConstants.NamedBsaDirectoryEntryBytes, source, "named BSA directory");
        int directoryStart = CheckedDirectoryStart(data.Length, directoryBytes, source, "named BSA directory");
        List<BsaRecord> records = new(count);
        int payloadOffset = Arena2FormatConstants.BsaHeaderBytes;

        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            int entryOffset = CheckedOffset(directoryStart, ordinal, Arena2FormatConstants.NamedBsaDirectoryEntryBytes, source, "named BSA directory entry");
            CheckedLittleEndianReader entry = new(data, source);
            entry.Seek(entryOffset);
            string name = entry.ReadNullTerminatedAscii(14);
            int signedLength = entry.ReadInt32();
            int length = CheckedLength(signedLength, source, entry.Position - sizeof(int), $"named BSA record {ordinal} length");
            records.Add(new BsaRecord(ordinal, name, null, payloadOffset, length));
            payloadOffset = CheckedEnd(payloadOffset, length, source, $"named BSA record {ordinal}");
        }

        ValidatePayloadBoundary(payloadOffset, directoryStart, source, "named BSA");
        ValidateDistinctNames(records, source);
        return new BsaArchive(data, source, records);
    }

    private static BsaArchive ParseNumeric(byte[] data, string source, int count)
    {
        int directoryBytes = CheckedProduct(count, Arena2FormatConstants.NumericBsaDirectoryEntryBytes, source, "numeric BSA directory");
        int directoryStart = CheckedDirectoryStart(data.Length, directoryBytes, source, "numeric BSA directory");
        List<BsaRecord> records = new(count);
        int payloadOffset = Arena2FormatConstants.BsaHeaderBytes;

        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            int entryOffset = CheckedOffset(directoryStart, ordinal, Arena2FormatConstants.NumericBsaDirectoryEntryBytes, source, "numeric BSA directory entry");
            CheckedLittleEndianReader entry = new(data, source);
            entry.Seek(entryOffset);
            uint numericId = entry.ReadUInt32();
            int signedLength = entry.ReadInt32();
            int length = CheckedLength(signedLength, source, entry.Position - sizeof(int), $"numeric BSA record {ordinal} length");
            records.Add(new BsaRecord(ordinal, null, numericId, payloadOffset, length));
            payloadOffset = CheckedEnd(payloadOffset, length, source, $"numeric BSA record {ordinal}");
        }

        ValidatePayloadBoundary(payloadOffset, directoryStart, source, "numeric BSA");
        ValidateDistinctNumericIds(records, source);
        return new BsaArchive(data, source, records);
    }

    private static int CheckedProduct(int left, int right, string source, string context)
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

    private static int CheckedDirectoryStart(int dataLength, int directoryBytes, string source, string context)
    {
        if (directoryBytes > dataLength - Arena2FormatConstants.BsaHeaderBytes)
        {
            throw new Arena2FormatException(source, dataLength, $"{context} exceeds source length {dataLength}");
        }

        return dataLength - directoryBytes;
    }

    private static int CheckedOffset(int baseOffset, int ordinal, int stride, string source, string context)
    {
        return CheckedEnd(baseOffset, CheckedProduct(ordinal, stride, source, context), source, context);
    }

    private static int CheckedLength(int value, string source, int offset, string context)
    {
        if (value < 0)
        {
            throw new Arena2FormatException(source, offset, $"{context} is negative ({value})");
        }

        return value;
    }

    private static int CheckedEnd(int offset, int length, string source, string context)
    {
        try
        {
            return checked(offset + length);
        }
        catch (OverflowException)
        {
            throw new Arena2FormatException(source, offset, $"{context} offset arithmetic overflows a 32-bit offset");
        }
    }

    private static void ValidatePayloadBoundary(int payloadEnd, int directoryStart, string source, string context)
    {
        if (payloadEnd != directoryStart)
        {
            throw new Arena2FormatException(source, payloadEnd, $"{context} payload ends at {payloadEnd}, but directory begins at {directoryStart}");
        }
    }

    private static void ValidateDistinctNames(IEnumerable<BsaRecord> records, string source)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (BsaRecord record in records)
        {
            if (!names.Add(record.Name!))
            {
                throw new Arena2FormatException(source, record.Offset, $"duplicate named BSA record {record.Name}");
            }
        }
    }

    private static void ValidateDistinctNumericIds(IEnumerable<BsaRecord> records, string source)
    {
        HashSet<uint> ids = [];
        foreach (BsaRecord record in records)
        {
            if (!ids.Add(record.NumericId!.Value))
            {
                throw new Arena2FormatException(source, record.Offset, $"duplicate numeric BSA record ID {record.NumericId}");
            }
        }
    }
}
