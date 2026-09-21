namespace Daggerfall.Import.Arena2;

/// <summary>One default-biography line: the prose the classic data file states, or why it could not be read.</summary>
/// <param name="Index">The line's ordinal in the file, which is the source's order.</param>
/// <param name="Offset">The byte the line starts at.</param>
/// <param name="Text">The line's prose, empty when it could not be read.</param>
/// <param name="Reason">Why the line could not be read, empty when it could.</param>
public sealed record BioDatLine(int Index, int Offset, string Text, string Reason);

/// <summary>Every line a default-biography file declares, in file order.</summary>
/// <param name="Lines">The lines, in file order, trailing empties kept the way the donor keeps them.</param>
public sealed record BioDatCatalog(IReadOnlyList<BioDatLine> Lines);

/// <summary>
/// Reads the classic default biography: NUL-separated prose lines the donor loads verbatim into
/// its line list, trailing empties included. A line holding bytes outside ASCII is not prose the
/// contract can carry, so it is published as a malformed line with its reason rather than dropped.
/// </summary>
public static class BioDatReader
{
    public const string FileName = "BIO.DAT";

    /// <summary>Reads every line, or reports the first byte that is not a default biography.</summary>
    public static BioDatCatalog Read(ReadOnlySpan<byte> bytes, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        List<BioDatLine> lines = [];
        int offset = 0;
        int index = 0;
        while (true)
        {
            // An empty file still states one empty line, and a trailing NUL still closes its line
            // with an empty one after it, because the donor splits rather than trims.
            int terminator = offset < bytes.Length ? bytes[offset..].IndexOf((byte)0) : -1;
            int end = terminator >= 0 ? offset + terminator : bytes.Length;
            ReadOnlySpan<byte> text = bytes[offset..end];
            string? failure = null;
            foreach (byte value in text)
            {
                if (value > 0x7F)
                {
                    failure = $"non-ASCII byte {value} in biography line";
                    break;
                }
            }

            lines.Add(new BioDatLine(
                index,
                offset,
                failure is null ? System.Text.Encoding.ASCII.GetString(text) : string.Empty,
                failure ?? string.Empty));
            if (terminator < 0)
            {
                break;
            }

            index++;
            offset = end + 1;
        }

        return new BioDatCatalog(lines);
    }
}
