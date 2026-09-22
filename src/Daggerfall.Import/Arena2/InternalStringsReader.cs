using System.Text;

namespace Daggerfall.Import.Arena2;

/// <summary>One key/value record from Daggerfall Unity's localized internal-string table.</summary>
/// <param name="Key">The donor's stable internal-string key.</param>
/// <param name="Value">The decoded CSV value, preserving embedded line breaks.</param>
/// <param name="Index">The zero-based record ordinal after the CSV header.</param>
/// <param name="Offset">The byte at which this CSV record begins.</param>
/// <param name="ByteLength">The record's source span, including its terminating line break when present.</param>
public sealed record InternalStringRecord(string Key, string Value, int Index, long Offset, int ByteLength);

/// <summary>The parsed Internal_Strings.csv header and records.</summary>
public sealed record InternalStringsCatalog(int HeaderLength, IReadOnlyList<InternalStringRecord> Records);

/// <summary>
/// Reads Daggerfall Unity's <c>Internal_Strings.csv</c> exactly as a two-column UTF-8 CSV table.
/// The table is donor knowledge: it supplies localization data which classic <c>TEXT.RSC</c> does
/// not carry, including the building-name fragments and titles.
/// </summary>
public static class InternalStringsReader
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Reads the supplied table and refuses malformed CSV rather than dropping a localized key.</summary>
    public static InternalStringsCatalog Read(byte[] bytes, string label)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        int position = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        int headerStart = position;
        (string headerKey, bool headerEnded) = Field(bytes, ref position, label, headerStart);
        RequireDelimiter(bytes, ref position, label, headerStart);
        (string headerValue, bool finalHeader) = Field(bytes, ref position, label, headerStart);
        if (headerEnded || !finalHeader || headerKey != "Key" || headerValue != "Value")
        {
            throw new InvalidOperationException($"Internal strings source '{label}' must begin with the exact CSV header 'Key,Value'.");
        }

        int headerLength = position;
        List<InternalStringRecord> records = [];
        HashSet<string> keys = new(StringComparer.Ordinal);
        while (position < bytes.Length)
        {
            int start = position;
            (string key, bool keyEnded) = Field(bytes, ref position, label, start);
            if (keyEnded)
            {
                throw new InvalidOperationException($"Internal strings source '{label}' record {records.Count} ends after its key, so it carries no value.");
            }

            RequireDelimiter(bytes, ref position, label, start);
            (string value, bool valueFinal) = Field(bytes, ref position, label, start);
            if (!valueFinal)
            {
                // Field only returns a non-final value when it stopped at a comma. This is defensive
                // against accepting an unfinished row if the parser changes.
                throw new InvalidOperationException($"Internal strings source '{label}' record {records.Count} carries more than two columns.");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException($"Internal strings source '{label}' record {records.Count} carries no key.");
            }

            if (!keys.Add(key))
            {
                throw new InvalidOperationException($"Internal strings source '{label}' declares key '{key}' twice.");
            }

            records.Add(new InternalStringRecord(key, value, records.Count, start, position - start));
        }

        if (records.Count == 0)
        {
            throw new InvalidOperationException($"Internal strings source '{label}' carries no records.");
        }

        return new InternalStringsCatalog(headerLength, records);
    }

    private static (string Value, bool EndedRecord) Field(byte[] bytes, ref int position, string label, int recordStart)
    {
        int valueStart = position;
        bool quoted = position < bytes.Length && bytes[position] == (byte)'"';
        if (quoted) position++;
        List<byte>? quotedBytes = quoted ? [] : null;

        while (position < bytes.Length)
        {
            byte current = bytes[position++];
            if (quoted)
            {
                if (current == (byte)'"')
                {
                    if (position < bytes.Length && bytes[position] == (byte)'"')
                    {
                        quotedBytes!.Add((byte)'"');
                        position++;
                        continue;
                    }

                    string value = Decode(quotedBytes!, label, recordStart);
                    if (position == bytes.Length) return (value, true);
                    if (bytes[position] == (byte)',') return (value, false);
                    if (bytes[position] == (byte)'\n')
                    {
                        position++;
                        return (value, true);
                    }

                    if (bytes[position] == (byte)'\r' && position + 1 < bytes.Length && bytes[position + 1] == (byte)'\n')
                    {
                        position += 2;
                        return (value, true);
                    }

                    throw new InvalidOperationException($"Internal strings source '{label}' record at byte {recordStart} has text after a closing CSV quote.");
                }

                quotedBytes!.Add(current);
                continue;
            }

            if (current == (byte)',')
            {
                position--;
                return (Decode(bytes, valueStart, position - valueStart, label, recordStart), false);
            }
            if (current == (byte)'\n') return (Decode(bytes, valueStart, position - valueStart - 1, label, recordStart), true);
            if (current == (byte)'\r')
            {
                if (position >= bytes.Length || bytes[position] != (byte)'\n')
                {
                    throw new InvalidOperationException($"Internal strings source '{label}' record at byte {recordStart} uses a carriage return not followed by a line feed.");
                }

                position++;
                return (Decode(bytes, valueStart, position - valueStart - 2, label, recordStart), true);
            }
            if (current == (byte)'"')
            {
                throw new InvalidOperationException($"Internal strings source '{label}' record at byte {recordStart} starts an unquoted field containing a quote.");
            }
        }

        if (quoted)
        {
            throw new InvalidOperationException($"Internal strings source '{label}' record at byte {recordStart} ends inside a quoted CSV field.");
        }

        return (Decode(bytes, valueStart, position - valueStart, label, recordStart), true);
    }

    private static void RequireDelimiter(byte[] bytes, ref int position, string label, int recordStart)
    {
        if (position >= bytes.Length || bytes[position] != (byte)',')
        {
            throw new InvalidOperationException($"Internal strings source '{label}' record at byte {recordStart} does not separate its Key and Value with a comma.");
        }

        position++;
    }

    private static string Decode(byte[] bytes, int start, int length, string label, int recordStart)
    {
        try
        {
            return Utf8.GetString(bytes, start, length);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException($"Internal strings source '{label}' record at byte {recordStart} is not valid UTF-8.", exception);
        }
    }

    private static string Decode(IReadOnlyCollection<byte> bytes, string label, int recordStart)
    {
        try
        {
            return Utf8.GetString([.. bytes]);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException($"Internal strings source '{label}' record at byte {recordStart} is not valid UTF-8.", exception);
        }
    }
}
