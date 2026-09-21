using System.Text;

namespace Daggerfall.Import.Arena2;

/// <summary>One name fragment table: a bank's set of interchangeable parts.</summary>
/// <param name="Set">The set's position in its bank, which is what the donor's composition names.</param>
/// <param name="Offset">The byte the set's fields start at.</param>
/// <param name="Parts">The set's fragments, in file order.</param>
public sealed record NameGenSet(int Set, int Offset, IReadOnlyList<string> Parts);

/// <summary>One classic name bank: up to six fragment sets, absent sets stated as absent.</summary>
/// <param name="Bank">The bank's position in the file, which matches the donor's bank order.</param>
/// <param name="Sets">The six set slots; an absent slot carries no offset and no parts.</param>
public sealed record NameGenBank(int Bank, IReadOnlyList<NameGenSet?> Sets);

/// <summary>Every name bank a name table declares, in file order.</summary>
/// <param name="Banks">The eleven banks, in file order.</param>
public sealed record NameGenCatalog(IReadOnlyList<NameGenBank> Banks);

/// <summary>
/// Reads the classic name-generation tables: eleven banks of up to six fragment sets, addressed
/// by a header of offsets and counts with fixed-width NUL-padded fields. The donor never reads
/// this file at runtime — its curated database carries the same tables — but the bytes match that
/// database fragment for fragment, so the composition the donor documents (male first from sets
/// 0+1, female first from 2+3, surnames from 4+5, with the Nord, Redguard and monster variants)
/// reads against these tables exactly.
/// </summary>
public static class NameGenReader
{
    public const string FileName = "NAMEGEN.DAT";

    /// <summary>How many banks the header declares.</summary>
    public const int BankCount = 11;

    /// <summary>How many set slots each bank declares.</summary>
    public const int SetsPerBank = 6;

    /// <summary>How many bytes the bank header spans.</summary>
    public const int HeaderBytes = BankCount * SetsPerBank * 2 * sizeof(uint);

    /// <summary>How many bytes one fragment field spans.</summary>
    public const int FieldBytes = 10;

    /// <summary>Reads every bank, or reports the first byte that is not a name table.</summary>
    public static NameGenCatalog Read(ReadOnlySpan<byte> bytes, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (bytes.Length < HeaderBytes)
        {
            throw new Arena2FormatException(label, bytes.Length, $"name table spans {bytes.Length} bytes, below its {HeaderBytes}-byte header");
        }

        CheckedLittleEndianReader header = new(bytes[..HeaderBytes], label);
        int[][] offsets = new int[BankCount][];
        int[][] counts = new int[BankCount][];
        for (int bank = 0; bank < BankCount; bank++)
        {
            offsets[bank] = new int[SetsPerBank];
            counts[bank] = new int[SetsPerBank];
            for (int set = 0; set < SetsPerBank; set++)
            {
                offsets[bank][set] = checked((int)ReadBounded(ref header, label, $"name bank {bank} set {set} offset"));
            }

            for (int set = 0; set < SetsPerBank; set++)
            {
                counts[bank][set] = checked((int)ReadBounded(ref header, label, $"name bank {bank} set {set} fragment count"));
            }
        }

        // Present sets tile the data region without gaps or overlaps: each one starts where the
        // previous ended, the first at the header's end and the last at the file's end. A set that
        // starts anywhere else is not a fragment table, and silent padding would be bytes no
        // consumer could address.
        List<NameGenBank> banks = [];
        int cursor = HeaderBytes;
        for (int bank = 0; bank < BankCount; bank++)
        {
            List<NameGenSet?> sets = [];
            for (int set = 0; set < SetsPerBank; set++)
            {
                int offset = offsets[bank][set];
                int count = counts[bank][set];
                if (count == 0)
                {
                    if (offset != 0)
                    {
                        throw new Arena2FormatException(label, cursor, $"name bank {bank} set {set} is empty but points at byte {offset}");
                    }

                    sets.Add(null);
                    continue;
                }

                if (offset != cursor)
                {
                    throw new Arena2FormatException(label, cursor, $"name bank {bank} set {set} starts at byte {offset}, past {cursor - offset} untabled bytes");
                }

                int end = checked(offset + count * FieldBytes);
                if (end > bytes.Length)
                {
                    throw new Arena2FormatException(label, cursor, $"name bank {bank} set {set} runs {end - bytes.Length} bytes past the file's {bytes.Length} bytes");
                }

                List<string> parts = [];
                for (int index = 0; index < count; index++)
                {
                    parts.Add(ReadField(bytes.Slice(offset + index * FieldBytes, FieldBytes), label, offset + index * FieldBytes));
                }

                sets.Add(new NameGenSet(set, offset, parts));
                cursor = end;
            }

            banks.Add(new NameGenBank(bank, sets));
        }

        if (cursor != bytes.Length)
        {
            throw new Arena2FormatException(label, cursor, $"name table ends at byte {cursor} with {bytes.Length - cursor} trailing bytes");
        }

        return new NameGenCatalog(banks);
    }

    private static uint ReadBounded(ref CheckedLittleEndianReader header, string label, string what)
    {
        uint value = header.ReadUInt32();
        if (value > int.MaxValue)
        {
            throw new Arena2FormatException(label, header.Position, $"{what} {value} is outside the addressable source");
        }

        return value;
    }

    private static string ReadField(ReadOnlySpan<byte> field, string label, int offset)
    {
        int terminator = field.IndexOf((byte)0);
        ReadOnlySpan<byte> text = terminator >= 0 ? field[..terminator] : field;
        foreach (byte value in text)
        {
            if (value > 0x7F)
            {
                throw new Arena2FormatException(label, offset, $"non-ASCII byte {value} in name fragment");
            }
        }

        return Encoding.ASCII.GetString(text);
    }
}
