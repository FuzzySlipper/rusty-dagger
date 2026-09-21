using System.Text;

namespace Daggerfall.Import.Arena2;

/// <summary>
/// A code a classic text record carries between its runs of characters. Only codes that can arrive
/// from source bytes are named: the donor's highlight, question and answer codes are values its own
/// callers construct around a record rather than bytes a record holds, so a source record naming one
/// would be a claim this reader cannot check.
/// </summary>
public enum Arena2TextCode
{
    /// <summary>A run of printable characters, which is what the donor calls text.</summary>
    Text = -1,

    /// <summary>A byte this reader cannot name, retained with its value rather than dropped.</summary>
    Unknown = -2,

    NewLineOffset = 0x00,
    SameLineOffset = 0x01,
    PullPreceeding = 0x02,
    EndOfPage = 0xf6,
    InputCursorPositioner = 0xf8,
    FontPrefix = 0xf9,
    PositionPrefix = 0xfb,
    JustifyLeft = 0xfc,
    JustifyCenter = 0xfd,
    SubrecordSeparator = 0xff,
}

/// <summary>
/// One element of a text record: a run of characters or a code that qualifies the text around it.
/// </summary>
/// <param name="Code">The element's kind, which is <see cref="Arena2TextCode.Text"/> for a run.</param>
/// <param name="Value">
/// The source byte the element came from. A retained code carries its own byte rather than only the
/// name for it, so a byte this reader does not name keeps its identity, and a named code published
/// from the wrong byte is something a consumer can check rather than trust.
/// </param>
/// <param name="Text">The run's characters, empty for a code.</param>
/// <param name="X">The payload a position or font prefix states, otherwise zero.</param>
public sealed record Arena2TextToken(Arena2TextCode Code, int Value, string Text, int X);

/// <summary>Whether a text record's bytes could be read.</summary>
public enum Arena2TextState
{
    Read,
    Malformed,
}

/// <summary>
/// One record of a classic text resource: the key its directory entry gives it, the bytes it spans,
/// and the token stream and macros its text carries.
/// </summary>
/// <param name="Id">The record's own key, read from the directory.</param>
/// <param name="Index">The record's ordinal in the directory, which is the source's order.</param>
/// <param name="Offset">The byte the record's text starts at, as the directory declares it.</param>
/// <param name="ByteLength">The bytes the record spans including its terminator, zero when unreadable.</param>
/// <param name="Subrecords">How many variants the record's separator bytes divide it into, at least one.</param>
/// <param name="State">Whether the record's bytes could be read.</param>
/// <param name="Reason">Why the record could not be read, empty when it could.</param>
/// <param name="Macros">The distinct macro symbols the record's text carries, in first-appearance order.</param>
/// <param name="Tokens">The record's token stream, empty when it could not be read.</param>
public sealed record Arena2TextRecord(
    int Id,
    int Index,
    long Offset,
    int ByteLength,
    int Subrecords,
    Arena2TextState State,
    string Reason,
    IReadOnlyList<string> Macros,
    IReadOnlyList<Arena2TextToken> Tokens);

/// <summary>Every record a text resource declares, in the order its own directory lists them.</summary>
/// <param name="HeaderLength">The byte length the file's first field declares for its own directory.</param>
/// <param name="DataStart">The first byte past the directory, where record text begins.</param>
/// <param name="Records">The records, in directory order.</param>
public sealed record Arena2TextCatalog(int HeaderLength, int DataStart, IReadOnlyList<Arena2TextRecord> Records);

/// <summary>
/// Reads the classic TEXT.RSC record table.
/// </summary>
/// <remarks>
/// The file leads with the byte length of its own directory, and that length accounts for one entry
/// more than the records it describes: the donor derives its record count as
/// <c>(headerLength / 6) - 1</c>, which is the same slot this reader stops short of. Each record then
/// runs from the offset its entry declares to the next <c>0xfe</c>, so a record's length is a property
/// of the bytes rather than of the file's structure, and a record whose offset points into the directory
/// or which never terminates is published as malformed with the reason rather than dropped — the key
/// exists in the source either way, and a lookup that answers "no such text" for it would report a
/// source fact as an absence.
/// <para>
/// Entries are read independently, which is what the donor does and what its own callers rely on: two
/// entries may name one offset and read the same text, an entry may name a byte another record's run
/// passes through, and an entry naming another record's terminator reads a record with no words in it.
/// None of those is an overlap to refuse — the directory is the authority on where a record's text is,
/// and a rule invented here would publish text the donor reads as unreadable.
/// </para>
/// </remarks>
public static class TextResourceReader
{
    /// <summary>The file this reader reads.</summary>
    public const string FileName = "TEXT.RSC";

    /// <summary>The bytes the file's directory length occupies.</summary>
    public const int HeaderLengthBytes = Arena2FormatConstants.ClassicDirectorySizeBytes;

    /// <summary>One directory entry: a record's key and the offset its text starts at.</summary>
    public const int DirectoryEntryBytes = Arena2FormatConstants.ClassicDirectoryEntryBytes;

    /// <summary>
    /// The directory slot the format reserves past the records it describes. It is the format's sentinel
    /// entry — the id the format reserves, pointing at the end of the file — so the record count is one
    /// less than the declared directory accounts for and the slot being skipped is verified below rather
    /// than assumed.
    /// </summary>
    public const int DirectoryExtraEntries = 1;

    /// <summary>The byte that ends a record.</summary>
    public const byte Terminator = 0xfe;

    /// <summary>The first byte of a run of characters.</summary>
    public const byte FirstCharacter = 0x20;

    /// <summary>The last byte of a run of characters.</summary>
    public const byte LastCharacter = 0x7f;

    /// <summary>Reads every record the supplied bytes declare.</summary>
    public static Arena2TextCatalog Read(byte[] bytes, string label)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (bytes.Length < HeaderLengthBytes + (DirectoryEntryBytes * 2))
        {
            throw new Arena2FormatException(label, 0, $"is {bytes.Length} bytes, too short to declare a record directory and one record");
        }

        int headerLength = bytes[0] | (bytes[1] << 8);
        if (headerLength % DirectoryEntryBytes != 0)
        {
            throw new Arena2FormatException(label, 0, $"declares a {headerLength}-byte directory, which is not a whole number of {DirectoryEntryBytes}-byte entries");
        }

        // The directory length covers one entry the file does not use as a record, so a file declaring
        // room for no records at all carries no text and is refused rather than published as an empty
        // catalog that would read to a consumer as a corpus with nothing in it.
        int count = (headerLength / DirectoryEntryBytes) - DirectoryExtraEntries;
        int dataStart = HeaderLengthBytes + headerLength;
        if (count < 1)
        {
            throw new Arena2FormatException(label, 0, $"declares room for {count} records, so it carries no text");
        }

        if (dataStart > bytes.Length)
        {
            throw new Arena2FormatException(label, 0, $"declares a directory ending at byte {dataStart}, past its {bytes.Length} bytes");
        }

        (int Id, long Offset)[] directory = new (int, long)[count];
        Dictionary<int, int> byId = [];
        for (int index = 0; index < count; index++)
        {
            int position = HeaderLengthBytes + (index * DirectoryEntryBytes);
            int id = bytes[position] | (bytes[position + 1] << 8);

            // The source states an unsigned 32-bit offset, so it is read over that whole range: a corrupt
            // entry is published as the byte it declares rather than wrapping into a negative one that
            // the file never stated, which would then fail the published record's own validation.
            long offset = (uint)(bytes[position + 2]
                | (bytes[position + 3] << 8)
                | (bytes[position + 4] << 16)
                | (bytes[position + 5] << 24));

            // Two records claiming one key would leave one of them unreachable through every lookup the
            // pack offers, which is a collision rather than a recoverable difference, so the file is
            // refused where the donor's own identity dictionary would also refuse it.
            if (byId.TryGetValue(id, out int first))
            {
                throw new Arena2FormatException(label, position, $"declares record {id} twice, at index {first} and index {index}, so one of them would be unreachable");
            }

            byId.Add(id, index);
            directory[index] = (id, offset);
        }

        // The slot the record count stops short of is the format's sentinel: the reserved id pointing at
        // the end of the file, which is how the quest companion reader establishes the same layout for its
        // own directories. A file whose directory does not end that way has not described its own extent,
        // and reading one record fewer than it declares would drop its last record without saying so.
        int sentinel = HeaderLengthBytes + (count * DirectoryEntryBytes);
        int sentinelId = bytes[sentinel] | (bytes[sentinel + 1] << 8);
        long sentinelOffset = (uint)(bytes[sentinel + 2]
            | (bytes[sentinel + 3] << 8)
            | (bytes[sentinel + 4] << 16)
            | (bytes[sentinel + 5] << 24));
        if (sentinelId != Arena2FormatConstants.ClassicDirectorySentinelId || sentinelOffset != bytes.Length)
        {
            throw new Arena2FormatException(label, sentinel, $"ends its directory at id {sentinelId} pointing at {sentinelOffset} where the format ends it with the sentinel {Arena2FormatConstants.ClassicDirectorySentinelId} at {bytes.Length}, so the record it describes would be dropped");
        }

        List<Arena2TextRecord> records = new(count);
        for (int index = 0; index < count; index++)
        {
            records.Add(ReadRecord(bytes, directory[index].Id, index, directory[index].Offset, dataStart));
        }

        return new Arena2TextCatalog(headerLength, dataStart, records);
    }

    private static Arena2TextRecord ReadRecord(byte[] bytes, int id, int index, long offset, int dataStart)
    {
        if (offset < dataStart)
        {
            return Unreadable(id, index, offset, $"names byte {offset} for its text, which is inside the directory ending at byte {dataStart}");
        }

        if (offset >= bytes.Length)
        {
            return Unreadable(id, index, offset, $"names byte {offset} for its text, past the file's {bytes.Length} bytes");
        }

        int terminator = Array.IndexOf(bytes, Terminator, (int)offset);
        if (terminator < 0)
        {
            return Unreadable(id, index, offset, $"carries no {Terminator:X2} terminator from byte {offset}");
        }

        int byteLength = terminator - (int)offset + 1;
        IReadOnlyList<Arena2TextToken> tokens = Tokenize(bytes, (int)offset, terminator);
        return new Arena2TextRecord(
            id,
            index,
            offset,
            byteLength,
            Subrecords(tokens),
            Arena2TextState.Read,
            string.Empty,
            [.. DistinctMacros(tokens)],
            tokens);
    }

    private static Arena2TextRecord Unreadable(int id, int index, long offset, string reason) =>
        new(id, index, offset, 0, 0, Arena2TextState.Malformed, reason, [], []);

    /// <summary>
    /// Tokenizes one record exactly as the donor does, over the record's bytes including its
    /// terminator.
    /// </summary>
    /// <remarks>
    /// The terminator is part of the buffer rather than a bound on it, which is what the donor's own
    /// reader does and why a position prefix reads the byte after it unconditionally: a prefix whose
    /// payload is the terminator consumes the end of the record, and a font prefix at the very end
    /// takes the terminator as its payload rather than reading past the record. Both are reproduced
    /// rather than corrected, because the bytes decide where a record ends and a reader that disagreed
    /// with the donor would tokenize text the donor never produces.
    /// </remarks>
    private static IReadOnlyList<Arena2TextToken> Tokenize(byte[] bytes, int offset, int terminator) =>
        TokenizeRange(bytes, offset, terminator + 1);

    /// <summary>
    /// Tokenizes an explicitly bounded span with the same grammar: runs of characters, named and
    /// unnamed codes, and a stop at the record terminator wherever the bound puts it. Length-delimited
    /// sources such as rumor texts carry no terminator of their own, and the donor reads those the
    /// same way it reads a record — <c>TextFile.ReadTokens</c> loops to the buffer's end and still
    /// breaks at the end token — so one loop serves both shapes.
    /// </summary>
    internal static IReadOnlyList<Arena2TextToken> TokenizeRange(byte[] bytes, int offset, int exclusiveEnd)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (offset < 0 || exclusiveEnd < offset || exclusiveEnd > bytes.Length)
        {
            throw new Arena2FormatException("text", offset, $"token range [{offset}, {exclusiveEnd}) is outside {bytes.Length} bytes");
        }

        List<Arena2TextToken> tokens = [];
        int position = offset;
        while (position < exclusiveEnd)
        {
            byte value = bytes[position];
            if (value == Terminator)
            {
                break;
            }

            if (IsCharacter(value))
            {
                int start = position;
                while (position < exclusiveEnd && IsCharacter(bytes[position]))
                {
                    position++;
                }

                tokens.Add(new Arena2TextToken(Arena2TextCode.Text, (int)Arena2TextCode.Text, Encoding.ASCII.GetString(bytes, start, position - start), 0));
                continue;
            }

            int x = 0;
            position++;
            switch (value)
            {
                case (byte)Arena2TextCode.FontPrefix:
                    if (position < exclusiveEnd)
                    {
                        x = bytes[position++];
                    }

                    break;
                case (byte)Arena2TextCode.PositionPrefix:
                    if (position < exclusiveEnd)
                    {
                        x = bytes[position++];
                    }

                    break;
            }

            tokens.Add(new Arena2TextToken(Code(value), value, string.Empty, x));
        }

        return tokens;
    }

    /// <summary>
    /// How many variants the record's separator bytes divide it into. The donor selects one variant at
    /// random when it answers with a random record, and a separator at the end leaves the empty variant
    /// the donor itself steps back past, so the count is what a consumer must be able to see.
    /// </summary>
    private static int Subrecords(IReadOnlyList<Arena2TextToken> tokens) =>
        1 + tokens.Count(token => token.Code == Arena2TextCode.SubrecordSeparator);

    /// <summary>The distinct macro symbols the record's text runs carry, in the order they appear.</summary>
    private static IEnumerable<string> DistinctMacros(IReadOnlyList<Arena2TextToken> tokens)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (Arena2TextToken token in tokens)
        {
            if (token.Code != Arena2TextCode.Text)
            {
                continue;
            }

            foreach (string macro in TextMacroScanner.Distinct(token.Text))
            {
                if (seen.Add(macro))
                {
                    yield return macro;
                }
            }
        }
    }

    private static bool IsCharacter(byte value) => value is >= FirstCharacter and <= LastCharacter;

    private static Arena2TextCode Code(byte value) =>
        Enum.IsDefined((Arena2TextCode)value) ? (Arena2TextCode)value : Arena2TextCode.Unknown;

}
