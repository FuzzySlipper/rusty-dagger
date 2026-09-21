using System.Globalization;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The supplied books: header facts, page tokens, message mapping, and the identities no file is
/// supplied for. The donor addresses files by the message's low byte and re-rolls the filed price
/// at open; the catalog publishes the filed facts and the pages read through text records.
/// </summary>
public sealed class DaggerfallBooksTests
{
    [Fact]
    public void Reads_header_pages_and_message_mapping()
    {
        IReadOnlyList<SourceInventoryRow> inventory = Inventory();
        (DaggerfallBooks books, _, IReadOnlyList<DaggerfallTextRecord> records) = DaggerfallBooksBuilder.Build(
            [(0, "local/arena2/books/BOK00000.TXT", File.ReadAllBytes(Book("BOK00000.TXT"))), (59, "local/arena2/books/BOK00059.TXT", File.ReadAllBytes(Book("BOK00059.TXT")))], inventory, "en");

        Assert.Equal(112, books.Books.Count);
        DaggerfallBook first = books.Books.Single(entry => entry.BookId == 0);
        Assert.Equal(DaggerfallBookDisposition.Read, first.Disposition);
        Assert.Equal("BOK00000.TXT", first.FileName);
        Assert.Equal("The First Scroll of Baan Dar", first.Title);
        Assert.NotEmpty(first.Author);
        DaggerfallBook book = books.Books.Single(entry => entry.BookId == 59);
        Assert.Equal(DaggerfallBookDisposition.Read, book.Disposition);
        Assert.Equal("BOK00059.TXT", book.FileName);
        Assert.Equal("Biography of Queen Barenziah, Vol. I", book.Title);
        Assert.Equal(book.PageCount, book.PageKeys.Count);
        Assert.All(book.PageKeys, key => Assert.StartsWith("Book:00059-p", key, StringComparison.Ordinal));

        // Every page of the book resolves through the text records the build returns.
        IReadOnlyList<DaggerfallTextRecord> pages = records.Where(record => record.Key.Kind == DaggerfallTextKind.Book && book.PageKeys.Contains(record.Key.ToString())).ToArray();
        Assert.Equal(book.PageCount, pages.Count);
        Assert.All(pages, page => Assert.Equal(Arena2TextState.Read, page.State));
        Assert.All(pages, page => Assert.NotEmpty(page.Tokens));
        HashSet<string> keys = [.. pages.Select(page => page.Key.ToString())];
        Assert.Subset(keys, book.PageKeys.ToHashSet());
    }

    [Fact]
    public void Records_missing_and_malformed_identities_explicitly()
    {
        IReadOnlyList<SourceInventoryRow> inventory = Inventory();
        (DaggerfallBooks books, _, _) = DaggerfallBooksBuilder.Build(
            [(0, "local/arena2/books/BOK00000.TXT", File.ReadAllBytes(Book("BOK00000.TXT"))), (1, "local/arena2/books/BOK00001.TXT", [1, 2, 3])], inventory, "en");

        DaggerfallBook missing = books.Books.Single(entry => entry.BookId == 90);
        Assert.Equal(DaggerfallBookDisposition.NotSupplied, missing.Disposition);
        Assert.NotEmpty(missing.Reason);
        Assert.Empty(missing.PageKeys);
        DaggerfallBook malformed = books.Books.Single(entry => entry.BookId == 1);
        Assert.Equal(DaggerfallBookDisposition.Malformed, malformed.Disposition);
        Assert.NotEmpty(malformed.Reason);

        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallBooksBuilder.Build([(112, "local/arena2/books/BOK00112.TXT", [])], inventory, "en"));
        Assert.Throws<InvalidOperationException>(() => DaggerfallBooksBuilder.Build(
            [(0, "local/arena2/books/BOK00000.TXT", File.ReadAllBytes(Book("BOK00000.TXT"))), (0, "local/arena2/books/BOK00000.TXT", File.ReadAllBytes(Book("BOK00000.TXT")))], inventory, "en"));
        Assert.Throws<InvalidOperationException>(() => DaggerfallBooksBuilder.Build([(0, "local/arena2/books/BOK00000.TXT", File.ReadAllBytes(Book("BOK00000.TXT")))], [], "en"));
    }

    [Fact]
    public void Reads_all_ninety_supplied_books_end_to_end()
    {
        string directory = BooksDirectory();
        if (!Directory.Exists(directory)) return;

        List<(int BookId, string Label, byte[] Bytes)> supplied = [];
        foreach (string path in Directory.EnumerateFiles(directory, "BOK*.TXT").Order(StringComparer.Ordinal))
        {
            supplied.Add((int.Parse(Path.GetFileNameWithoutExtension(path)[3..], CultureInfo.InvariantCulture), $"local/arena2/books/{Path.GetFileName(path)}", File.ReadAllBytes(path)));
        }

        Assert.Equal(90, supplied.Count);
        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));
        (DaggerfallBooks books, _, IReadOnlyList<DaggerfallTextRecord> records) = DaggerfallBooksBuilder.Build(supplied, inventory, "en");
        books.Validate();

        Assert.Equal(90, books.Books.Count(book => book.Disposition == DaggerfallBookDisposition.Read));
        Assert.Equal(22, books.Books.Count(book => book.Disposition == DaggerfallBookDisposition.NotSupplied));
        Assert.DoesNotContain(books.Books, book => book.Disposition == DaggerfallBookDisposition.Malformed);
        Assert.Equal(840, records.Count);
        // Every readable book's pages resolve through the records the build returns.
        HashSet<string> keys = [.. records.Select(record => record.Key.ToString())];
        foreach (DaggerfallBook book in books.Books.Where(book => book.Disposition == DaggerfallBookDisposition.Read))
        {
            Assert.Subset(keys, book.PageKeys.ToHashSet());
        }
    }

    private static string Book(string fileName) => Path.Combine(BooksDirectory(), fileName);

    private static string BooksDirectory() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../local/arena2/books"));

    private static IReadOnlyList<SourceInventoryRow> Inventory() =>
    [
        new SourceInventoryRow("CNT-015", "family", "CNT-015", "books", "local/arena2/books", string.Empty, "pending-import", string.Empty),
        new SourceInventoryRow("CNT-015.file.books/BOK00000.TXT", "file", "CNT-015", "source-file", "local/arena2/books/BOK00000.TXT", "BOK00000", "unused", string.Empty),
        new SourceInventoryRow("CNT-015.file.books/BOK00001.TXT", "file", "CNT-015", "source-file", "local/arena2/books/BOK00001.TXT", "BOK00001", "unused", string.Empty),
        new SourceInventoryRow("CNT-015.file.books/BOK00059.TXT", "file", "CNT-015", "source-file", "local/arena2/books/BOK00059.TXT", "BOK00059", "unused", string.Empty),
    ];

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
