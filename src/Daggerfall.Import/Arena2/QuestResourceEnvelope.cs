namespace Daggerfall.Import.Arena2;

/// <summary>What the directory decode could establish about one quest resource file.</summary>
public enum QuestResourceEnvelopeDisposition
{
    /// <summary>The directory and every record boundary hold.</summary>
    Decoded,

    /// <summary>The file is shorter than its directory claims.</summary>
    Truncated,

    /// <summary>The file is long enough and its directory does not hold together.</summary>
    Malformed,
}

/// <summary>
/// One resource record: the id the directory stores for it, its extent, and its raw
/// bytes. What the id names — a message token, an action, something else — is not
/// established here.
/// </summary>
public sealed record QuestResourceRecord(ushort Id, int Offset, int Length, ReadOnlyMemory<byte> Payload);

/// <summary>
/// The envelope of one classic quest resource companion. The observed layout, verified
/// across all 303 supplied files, is a directory followed by the records it indexes: a
/// little-endian directory size in bytes, then that many bytes of six-byte entries — a
/// two-byte id and the record's offset — ending with the <c>0xffff</c> sentinel entry whose offset
/// is the end of the file.
/// </summary>
/// <remarks>
/// The record payloads are delivered as bytes and no text encoding is asserted or
/// validated here: a consumer that needs text decodes it itself, so a record whose bytes
/// are not valid text is data rather than an envelope defect. What an id means, how its
/// text is parsed and which action or resource it names belong to the quest-source
/// inventory and the worker that consumes these records, not to this envelope.
/// </remarks>
public sealed record QuestResourceEnvelope(
    string Path,
    int Length,
    int DirectoryBytes,
    IReadOnlyList<QuestResourceRecord> Records,
    QuestResourceEnvelopeDisposition Disposition,
    string Note)
{
    /// <summary>The size of one directory entry: a two-byte token id and a four-byte offset.</summary>
    public const int DirectoryEntryBytes = 6;

    /// <summary>The sentinel id that ends every directory.</summary>
    public const ushort SentinelId = 0xffff;

    /// <summary>The bytes the directory size field occupies.</summary>
    public const int DirectorySizeBytes = 2;

    /// <summary>Decodes one quest resource companion's envelope.</summary>
    public static QuestResourceEnvelope Decode(ReadOnlySpan<byte> bytes, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (bytes.Length < DirectorySizeBytes)
        {
            return Invalid(path, bytes.Length, directoryBytes: 0, [], QuestResourceEnvelopeDisposition.Truncated, $"The file is {bytes.Length} bytes where the directory size field is {DirectorySizeBytes}.");
        }

        CheckedLittleEndianReader directory = new(bytes, path);
        int directoryBytes = directory.ReadUInt16();
        if (directoryBytes == 0 || directoryBytes % DirectoryEntryBytes != 0)
        {
            return Invalid(path, bytes.Length, directoryBytes, [], QuestResourceEnvelopeDisposition.Malformed, $"The directory declares {directoryBytes} bytes, which is not a positive multiple of {DirectoryEntryBytes}.");
        }

        long dataStart = (long)DirectorySizeBytes + directoryBytes;
        if (dataStart > bytes.Length)
        {
            return Invalid(path, bytes.Length, directoryBytes, [], QuestResourceEnvelopeDisposition.Truncated, $"The directory ends at {dataStart} in a file of {bytes.Length} bytes.");
        }

        int entryCount = directoryBytes / DirectoryEntryBytes;
        List<(ushort Id, int Offset)> entries = new(entryCount);
        for (int index = 0; index < entryCount; index++)
        {
            directory.Seek(DirectorySizeBytes + (index * DirectoryEntryBytes));
            ushort id = directory.ReadUInt16();
            int offset = directory.ReadInt32();
            if (offset < dataStart || offset > bytes.Length)
            {
                return Invalid(path, bytes.Length, directoryBytes, [], QuestResourceEnvelopeDisposition.Malformed, $"Entry {index} (id {id}) points at {offset}, outside the record region [{dataStart}, {bytes.Length}].");
            }

            if (entries.Count != 0 && offset < entries[^1].Offset)
            {
                return Invalid(path, bytes.Length, directoryBytes, [], QuestResourceEnvelopeDisposition.Malformed, $"Entry {index} (id {id}) points at {offset}, before the previous record at {entries[^1].Offset}.");
            }

            entries.Add((id, offset));
        }

        // Every supplied file ends with the sentinel entry pointing at the end of the
        // file, so a directory without one has not described its own extent.
        if (entries[^1].Id != SentinelId || entries[^1].Offset != bytes.Length)
        {
            return Invalid(path, bytes.Length, directoryBytes, [], QuestResourceEnvelopeDisposition.Malformed, $"The directory ends at id {entries[^1].Id} pointing at {entries[^1].Offset} where the corpus ends with the sentinel {SentinelId} at {bytes.Length}.");
        }

        List<QuestResourceRecord> records = new(entries.Count - 1);
        for (int index = 0; index < entries.Count - 1; index++)
        {
            int offset = entries[index].Offset;
            int length = entries[index + 1].Offset - offset;
            records.Add(new QuestResourceRecord(entries[index].Id, offset, length, bytes.Slice(offset, length).ToArray()));
        }

        // One supplied file starts its first record a byte past the end of its directory.
        // That is recoverable — every record is still located — but it is disclosed rather
        // than normalized away, because the byte belongs to no record the directory names.
        int orphanBytes = entries[0].Offset - (int)dataStart;
        string gap = orphanBytes == 0
            ? string.Empty
            : $" The record region begins at {dataStart} and the first record at {entries[0].Offset}, so {orphanBytes} byte(s) belong to no record.";
        return new QuestResourceEnvelope(path, bytes.Length, directoryBytes, records, QuestResourceEnvelopeDisposition.Decoded, $"{records.Count} records.{gap}");
    }

    private static QuestResourceEnvelope Invalid(string path, int length, int directoryBytes, IReadOnlyList<QuestResourceRecord> records, QuestResourceEnvelopeDisposition disposition, string note) =>
        new(path, length, directoryBytes, records, disposition, note);
}
