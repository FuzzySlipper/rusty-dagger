using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>How a classic book identity is accounted for.</summary>
public enum DaggerfallBookDisposition
{
    /// <summary>The file is supplied and its pages are published.</summary>
    Read,
    /// <summary>The identity is inside the documented range and no file is supplied for it.</summary>
    NotSupplied,
    /// <summary>The file is supplied and does not parse.</summary>
    Malformed,
}

/// <summary>One published book: its header facts and the text keys its pages read through.</summary>
/// <param name="BookId">The book identity the classic message field names.</param>
/// <param name="FileName">The supplied file.</param>
/// <param name="Title">The book's title.</param>
/// <param name="Author">The book's internal author name.</param>
/// <param name="IsNaughty">Whether the file carries the adult-content flag.</param>
/// <param name="FilePrice">The price the file states; the donor re-rolls it at open.</param>
/// <param name="PageCount">How many pages the file declares.</param>
/// <param name="PageKeys">The text key of each page, in order.</param>
/// <param name="Disposition">Whether the book's pages are published.</param>
/// <param name="Reason">Why not, when they are not.</param>
public sealed record DaggerfallBook(
    int BookId,
    string FileName,
    string Title,
    string Author,
    bool IsNaughty,
    uint FilePrice,
    int PageCount,
    IReadOnlyList<string> PageKeys,
    DaggerfallBookDisposition Disposition,
    string Reason)
{
    public void Validate()
    {
        if (BookId is < 0 or > 0xff)
        {
            throw new ArgumentOutOfRangeException(nameof(BookId), BookId, "A published book identity fits the message byte that names it.");
        }

        NormalizedImportDocument.RequireLogicalPath(FileName, nameof(FileName));
        if (Disposition == DaggerfallBookDisposition.Read)
        {
            if (Reason.Length != 0)
            {
                throw new ArgumentException($"Book {BookId} reads with the reason '{Reason}'.", nameof(Reason));
            }

            if (PageKeys.Count != PageCount)
            {
                throw new InvalidOperationException($"Book {BookId} publishes {PageKeys.Count} page keys for {PageCount} pages.");
            }
        }
        else if (Reason.Length == 0)
        {
            throw new ArgumentException($"Book {BookId} carries no reason for its disposition.", nameof(Reason));
        }
    }
}

/// <summary>The normalized book catalog: every documented identity with its header, pages and message.</summary>
/// <param name="Source">The documented inventory family the books are read under.</param>
/// <param name="Books">The books in identity order, supplied or not.</param>
public sealed record DaggerfallBooks(DaggerfallTextSource Source, IReadOnlyList<DaggerfallBook> Books)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        Source.Validate();
        if (Books.Count == 0)
        {
            throw new InvalidOperationException("The book catalog carries no books.");
        }

        NormalizedImportDocument.ValidateUnique(Books, book => book.BookId.ToString(), "books");
        foreach (DaggerfallBook book in Books)
        {
            book.Validate();
        }
    }
}

/// <summary>
/// Builds the normalized book catalog from the supplied book files. Every documented identity is
/// published whether its file is supplied or not; a missing file is a disposition, not an absence,
/// because the message byte names 256 books and the corpus supplies 90. Page prose publishes
/// through text records the builder returns beside the catalog; the catalog itself carries no
/// prose, only the keys pages read through.
/// </summary>
public static class DaggerfallBooksBuilder
{
    /// <summary>The inventory family the supplied books are documented under.</summary>
    public const string BooksFamily = "CNT-015";

    /// <summary>The highest book identity the corpus documents.</summary>
    public const int MaximumBookId = 111;

    /// <summary>Builds the book catalog and the page text records its pages read through.</summary>
    /// <param name="books">The supplied files: identity, logical path and bytes, in any order.</param>
    /// <param name="inventory">The documented inventory the catalog cites.</param>
    /// <param name="language">The language tag the sources' text is written in.</param>
    public static (DaggerfallBooks Books, IReadOnlyList<DaggerfallTextSource> Sources, IReadOnlyList<DaggerfallTextRecord> PageRecords) Build(
        IReadOnlyList<(int BookId, string Label, byte[] Bytes)> books,
        IReadOnlyList<SourceInventoryRow> inventory,
        string language)
    {
        ArgumentNullException.ThrowIfNull(books);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        SourceInventoryRow family = SourceInventoryRow.RequireFamily(inventory, BooksFamily);

        Dictionary<int, (string Label, byte[] Bytes)> supplied = [];
        foreach ((int bookId, string label, byte[] bytes) in books)
        {
            if (bookId is < 0 or > MaximumBookId)
            {
                throw new ArgumentOutOfRangeException(nameof(books), bookId, $"Book identity {bookId} is outside the documented 0..{MaximumBookId} range.");
            }

            if (!supplied.TryAdd(bookId, (label, bytes)))
            {
                throw new InvalidOperationException($"Book identity {bookId} is supplied twice.");
            }
        }

        List<DaggerfallBook> published = [];
        List<DaggerfallTextSource> sources = [];
        List<DaggerfallTextRecord> records = [];
        foreach (int bookId in Enumerable.Range(0, MaximumBookId + 1))
        {
            if (!supplied.TryGetValue(bookId, out (string Label, byte[] Bytes) suppliedBook))
            {
                published.Add(new DaggerfallBook(bookId, $"BOK{bookId:D5}.TXT", string.Empty, string.Empty, false, 0, 0, [], DaggerfallBookDisposition.NotSupplied, "No file is supplied for this identity."));
                continue;
            }

            ClassicBook book;
            try
            {
                book = BookReader.Read(suppliedBook.Bytes, suppliedBook.Label);
            }
            catch (Arena2FormatException failure)
            {
                published.Add(new DaggerfallBook(bookId, $"BOK{bookId:D5}.TXT", string.Empty, string.Empty, false, 0, 0, [], DaggerfallBookDisposition.Malformed, failure.Message));
                continue;
            }

            sources.Add(new DaggerfallTextSource(
                DaggerfallTextKind.Book, RequireFile(inventory, suppliedBook.Label), suppliedBook.Label, language, suppliedBook.Bytes.LongLength, 0, book.Header.PageCount));
            List<string> pageKeys = [];
            for (int page = 0; page < book.Header.PageCount; page++)
            {
                DaggerfallTextKey pageKey = new(DaggerfallTextKind.Book, $"{bookId:D5}-p{page:D2}");
                pageKeys.Add(pageKey.ToString());
                BookPage bookPage = book.Pages[page];
                records.Add(new DaggerfallTextRecord(
                    pageKey,
                    suppliedBook.Label,
                    page,
                    bookPage.Offset,
                    bookPage.ByteLength,
                    1,
                    Arena2TextState.Read,
                    string.Empty,
                    [.. PageMacros(bookPage.Tokens)],
                    [.. bookPage.Tokens.Select(Publish)]));
            }

            published.Add(new DaggerfallBook(
                bookId,
                $"BOK{bookId:D5}.TXT",
                book.Header.Title,
                book.Header.Author,
                book.Header.IsNaughty,
                book.Header.Price,
                book.Header.PageCount,
                pageKeys,
                DaggerfallBookDisposition.Read,
                string.Empty));
        }

        DaggerfallBooks catalog = new(
            new DaggerfallTextSource(DaggerfallTextKind.Book, family.Id, family.PathOrPattern, language, supplied.Values.Sum(entry => (long)entry.Bytes.Length), 0, published.Count(book => book.Disposition == DaggerfallBookDisposition.Read)),
            published);
        catalog.Validate();
        return (catalog, sources, records);
    }

    private static string RequireFile(IReadOnlyList<SourceInventoryRow> inventory, string label)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory.FirstOrDefault(row => row.FamilyId == BooksFamily && StringComparer.Ordinal.Equals(row.PathOrPattern, label))?.Id
            ?? throw new InvalidOperationException($"The documented inventory does not carry '{label}', so the books cite no provenance.");
    }

    private static DaggerfallTextToken Publish(Arena2TextToken token) => new(
        token.Code,
        token.Code == Arena2TextCode.Text ? token.Text : null,
        token.Code == Arena2TextCode.Unknown ? token.Value : null,
        token.Code is Arena2TextCode.FontPrefix or Arena2TextCode.PositionPrefix ? token.X : null);

    private static IEnumerable<string> PageMacros(IReadOnlyList<Arena2TextToken> tokens)
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
}
