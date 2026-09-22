namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>How a classic book identity is accounted for.</summary>
internal enum DaggerfallBookDisposition
{
    Read,
    NotSupplied,
    Malformed,
}

/// <summary>One published book: its header facts and the text keys its pages read through.</summary>
/// <param name="BookId">The book identity the classic message field names.</param>
/// <param name="FileName">The supplied file.</param>
/// <param name="Title">The book's title.</param>
/// <param name="Author">The book's internal author name.</param>
/// <param name="IsNaughty">Whether the file carries the adult-content flag.</param>
/// <param name="FilePrice">The price the file states; the donor re-rolls it at open.</param>
/// <param name="RuntimePrice">The donor-compatible price calculated during import.</param>
/// <param name="PageCount">How many pages the file declares.</param>
/// <param name="PageKeys">The text key of each page, in order.</param>
/// <param name="Disposition">Whether the book's pages are published.</param>
internal sealed record DaggerfallBookDefinition(
    int BookId,
    string FileName,
    string Title,
    string Author,
    bool IsNaughty,
    uint FilePrice,
    uint RuntimePrice,
    int PageCount,
    IReadOnlyList<string> PageKeys,
    DaggerfallBookDisposition Disposition);

/// <summary>The normalized book catalog, loaded from the pack alone.</summary>
/// <param name="Books">The books by identity.</param>
internal sealed record DaggerfallBooksSet(IReadOnlyDictionary<int, DaggerfallBookDefinition> Books)
{
    /// <summary>
    /// Resolves a classic book message to the book it names: the donor addresses files by the
    /// message's low byte, so the message resolves to that identity whether or not a file is
    /// supplied for it. A message whose book has no pages is itself the miss a consumer reports.
    /// </summary>
    internal DaggerfallBookDefinition ResolveMessage(int message) => Books[message & 0xff];
}
