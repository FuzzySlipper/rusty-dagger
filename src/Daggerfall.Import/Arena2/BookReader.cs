using System.Text;

namespace Daggerfall.Import.Arena2;

/// <summary>One parsed classic book header: the fields the file states before its pages.</summary>
/// <param name="Title">The book's title.</param>
/// <param name="Author">The book's internal author name.</param>
/// <param name="IsNaughty">Whether the file carries the adult-content flag.</param>
/// <param name="Price">The price the file states, which the donor re-rolls at open.</param>
/// <param name="Unknown1">The first retained unknown field.</param>
/// <param name="Unknown2">The second retained unknown field.</param>
/// <param name="Unknown3">The third retained unknown field.</param>
/// <param name="PageCount">How many pages the file declares.</param>
/// <param name="PageOffsets">The byte offset of each page's tokens, in order.</param>
public sealed record BookHeader(
    string Title,
    string Author,
    bool IsNaughty,
    uint Price,
    ushort Unknown1,
    ushort Unknown2,
    ushort Unknown3,
    ushort PageCount,
    IReadOnlyList<uint> PageOffsets);

/// <summary>One decoded book page: where its bytes start, how many they span, and its tokens.</summary>
/// <param name="Offset">The byte offset the page reads from.</param>
/// <param name="ByteLength">How many bytes the page spans, including the terminator when one ends it.</param>
/// <param name="Tokens">The page's tokens, without the terminator.</param>
public sealed record BookPage(int Offset, int ByteLength, IReadOnlyList<Arena2TextToken> Tokens);

/// <summary>One decoded classic book: its header and the tokens of each page.</summary>
/// <param name="Source">Logical source identity supplied at parse time.</param>
/// <param name="Header">The parsed header.</param>
/// <param name="Pages">The pages in order.</param>
public sealed record ClassicBook(string Source, BookHeader Header, IReadOnlyList<BookPage> Pages);

/// <summary>Reader for classic BOK book files: header, page offsets and page tokens.</summary>
public static class BookReader
{
    public const int TitleBytes = 64;
    public const int AuthorBytes = 64;
    public const int NaughtyBytes = 8;
    public const int NullBytes = 88;
    public const int HeaderBytes = TitleBytes + AuthorBytes + NaughtyBytes + NullBytes + sizeof(uint) + (4 * sizeof(ushort));

    private const string NaughtyFlag = "naughty";

    /// <summary>The page terminator: pages read to the first one, the way the donor reads them.</summary>
    public const byte EndOfPage = 0xf6;

    /// <summary>
    /// Reads one book: the header the file states and the tokens of each page it declares. A page
    /// reads from its offset to the first page terminator, or to the file's end when the terminator
    /// is missing: the donor breaks at the first terminator without bounding the read at the next
    /// offset, so a page that carries none reads into what follows until the terminator or the end.
    /// </summary>
    public static ClassicBook Read(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (bytes.Length < HeaderBytes)
        {
            throw new Arena2FormatException(source, bytes.Length, $"book requires a {HeaderBytes}-byte header");
        }

        string title = ReadCString(bytes, 0, TitleBytes);
        string author = ReadCString(bytes, TitleBytes, AuthorBytes);
        bool naughty = ReadCString(bytes, TitleBytes + AuthorBytes, NaughtyBytes) == NaughtyFlag;
        uint price = ReadUInt32(bytes, TitleBytes + AuthorBytes + NaughtyBytes + NullBytes);
        ushort unknown1 = ReadUInt16(bytes, TitleBytes + AuthorBytes + NaughtyBytes + NullBytes + sizeof(uint));
        ushort unknown2 = ReadUInt16(bytes, TitleBytes + AuthorBytes + NaughtyBytes + NullBytes + sizeof(uint) + sizeof(ushort));
        ushort unknown3 = ReadUInt16(bytes, TitleBytes + AuthorBytes + NaughtyBytes + NullBytes + sizeof(uint) + (2 * sizeof(ushort)));
        ushort pageCount = ReadUInt16(bytes, HeaderBytes - sizeof(ushort));
        if (HeaderBytes + (pageCount * sizeof(uint)) > bytes.Length)
        {
            throw new Arena2FormatException(source, bytes.Length, $"book declares {pageCount} page offsets past its {bytes.Length} bytes");
        }

        List<uint> offsets = [];
        for (int page = 0; page < pageCount; page++)
        {
            uint offset = ReadUInt32(bytes, HeaderBytes + (page * sizeof(uint)));
            if (offset > bytes.Length)
            {
                throw new Arena2FormatException(source, HeaderBytes + (page * sizeof(uint)), $"book page {page} starts past its {bytes.Length} bytes");
            }

            offsets.Add(offset);
        }

        BookHeader header = new(title, author, naughty, price, unknown1, unknown2, unknown3, pageCount, offsets);
        byte[] copy = bytes.ToArray();
        List<BookPage> pages = [];
        for (int page = 0; page < pageCount; page++)
        {
            int start = checked((int)offsets[page]);
            int end = copy.Length;
            for (int position = start; position < copy.Length; position++)
            {
                if (copy[position] == EndOfPage)
                {
                    end = position;
                    break;
                }
            }

            if (end < start)
            {
                throw new Arena2FormatException(source, start, $"book page {page} ends before it starts");
            }

            // The span includes the terminator when one ends the page, the way sibling text records
            // span their terminator: a consumer re-slicing the source range reads the same bytes the
            // donor's page read ends at.
            int length = end - start + (end < copy.Length ? 1 : 0);
            pages.Add(new BookPage(start, length, TextResourceReader.TokenizeRange(copy, start, end)));
        }

        return new ClassicBook(source, header, pages);
    }

    private static string ReadCString(ReadOnlySpan<byte> bytes, int offset, int width)
    {
        int length = 0;
        while (length < width && bytes[offset + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(bytes.Slice(offset, length));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
}
