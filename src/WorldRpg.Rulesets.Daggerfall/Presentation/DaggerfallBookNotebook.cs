using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>A normalized book retained by its classic book identity, independent of its source item.</summary>
internal sealed record DaggerfallReadableBook(int BookId, string Title, string Author, string[] Pages);
internal sealed record DaggerfallUserNote(string Id, string Text);
internal sealed record DaggerfallBookReaderPresentation(int BookId, string Title, string Author, int Page, int PageCount, string Text);
internal sealed record DaggerfallNotebookPresentation(string Revision, DaggerfallBookReaderPresentation? Book, DaggerfallUserNote[] Notes);
internal sealed record DaggerfallNotebookSave(DaggerfallReadableBook[] Books, DaggerfallUserNote[] Notes, int? ActiveBookId, int Page, ulong NextNoteId)
{
    /// <summary>Rejects malformed retained references before session reconstruction begins.</summary>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Books);
        ArgumentNullException.ThrowIfNull(Notes);
        HashSet<int> bookIds = [];
        foreach (DaggerfallReadableBook book in Books)
        {
            ArgumentNullException.ThrowIfNull(book);
            // Empty pages are valid normalized BOK content: a few source pages carry layout tokens
            // without words, and that is still a readable page rather than a missing book.
            if (book.BookId < 0 || !bookIds.Add(book.BookId) || string.IsNullOrWhiteSpace(book.Title)
                || book.Pages is not { Length: > 0 } || book.Pages.Any(page => page is null))
                throw new ArgumentException("A retained book is malformed.");
        }
        HashSet<string> noteIds = new(StringComparer.Ordinal);
        foreach (DaggerfallUserNote note in Notes)
        {
            ArgumentNullException.ThrowIfNull(note);
            if (string.IsNullOrWhiteSpace(note.Id) || string.IsNullOrWhiteSpace(note.Text) || !noteIds.Add(note.Id))
                throw new ArgumentException("A notebook note is malformed.");
        }
        if (ActiveBookId is int active)
        {
            DaggerfallReadableBook? book = Books.SingleOrDefault(candidate => candidate.BookId == active);
            if (book is null || Page < 0 || Page >= book.Pages.Length)
                throw new ArgumentException("The retained book page is not available.");
        }
        else if (Page != 0) throw new ArgumentException("A closed reader cannot retain a page.");
    }

    /// <summary>Verifies each retained book against the selected catalog before it can be used.</summary>
    internal void Validate(DaggerfallDefinitions definitions, DaggerfallTextResolver text)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(text);
        Validate();
        foreach (DaggerfallReadableBook saved in Books)
        {
            if (!definitions.Books.Books.TryGetValue(saved.BookId, out DaggerfallBookDefinition? definition))
                throw new ArgumentException($"Retained book {saved.BookId} is not published by the selected catalog.");
            if (definition.Disposition != DaggerfallBookDisposition.Read)
                throw new ArgumentException($"Retained book {saved.BookId} is not readable in the selected catalog.");
            if (!string.Equals(saved.Title, definition.Title, StringComparison.Ordinal)
                || !string.Equals(saved.Author, definition.Author, StringComparison.Ordinal))
                throw new ArgumentException($"Retained book {saved.BookId} metadata does not match the selected catalog.");
            string[] pages = definition.PageKeys
                .Select(key => text.Resolve(new DaggerfallTextKey(DaggerfallTextKind.Book, key), DaggerfallTextContext.Empty).Text)
                .ToArray();
            if (!saved.Pages.SequenceEqual(pages, StringComparer.Ordinal))
                throw new ArgumentException($"Retained book {saved.BookId} text does not match the selected catalog.");
        }
    }
}

internal sealed record DaggerfallNotebookActionResult(bool Applied, string Message);

/// <summary>Durable player reading references and ordered personal notes; quest journals remain quest-owned.</summary>
internal sealed class DaggerfallBookNotebook(DaggerfallDefinitions definitions, DaggerfallTextResolver text)
{
    private readonly Dictionary<int, DaggerfallReadableBook> _books = [];
    private readonly List<DaggerfallUserNote> _notes = [];
    private int? _activeBookId;
    private int _page;
    private ulong _nextNoteId;
    private ulong _revision;

    internal IReadOnlyList<DaggerfallUserNote> Notes => _notes;

    internal DaggerfallReadableBook Open(int bookId)
    {
        DaggerfallBookDefinition definition = definitions.Books.ResolveMessage(bookId);
        if (_books.TryGetValue(definition.BookId, out DaggerfallReadableBook? retained))
        {
            Select(retained.BookId);
            return retained;
        }
        DaggerfallBookDefinition book = definition;
        if (book.Disposition != DaggerfallBookDisposition.Read) throw new InvalidOperationException("This book's text is not supplied.");
        string[] pages = book.PageKeys.Select(key => text.Resolve(new DaggerfallTextKey(DaggerfallTextKind.Book, key), DaggerfallTextContext.Empty).Text).ToArray();
        retained = new(book.BookId, book.Title, book.Author, pages);
        _books.Add(book.BookId, retained);
        Select(retained.BookId);
        return retained;
    }

    internal DaggerfallNotebookPresentation Read()
    {
        DaggerfallBookReaderPresentation? book = _activeBookId is int id && _books.TryGetValue(id, out DaggerfallReadableBook? retained)
            ? new(retained.BookId, retained.Title, retained.Author, _page, retained.Pages.Length, retained.Pages[_page])
            : null;
        return new(_revision.ToString(System.Globalization.CultureInfo.InvariantCulture), book, [.. _notes]);
    }

    internal DaggerfallNotebookActionResult Apply(DaggerfallPlayerUiAction action)
    {
        if (!MatchesRevision(action.Revision)) return new(false, "Notebook changed. Choose the page or note again.");
        try
        {
            switch (action.Action)
            {
                case "notebook-page":
                    if (action.Page is not int page) return new(false, "Choose a readable book page.");
                    SetPage(page);
                    return new(true, "Book page turned.");
                case "notebook-add":
                    Add(action.Text!);
                    return new(true, "Note added.");
                case "notebook-edit":
                    Edit(action.Note!, action.Text!);
                    return new(true, "Note updated.");
                case "notebook-remove":
                    Remove(action.Note!);
                    return new(true, "Note removed.");
                case "notebook-move":
                    Move(action.Note!, action.Destination!.Value);
                    return new(true, "Note reordered.");
                default:
                    return new(false, "That notebook action is not available.");
            }
        }
        catch (ArgumentException error)
        {
            return new(false, error.Message);
        }
    }

    internal void Add(string text)
    {
        string id = $"note:{checked(++_nextNoteId)}";
        Add(id, text);
    }

    private void Add(string id, string text)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text)) throw new ArgumentException("A note needs an identity and text.");
        if (_notes.Any(note => note.Id == id)) throw new ArgumentException("That note already exists.");
        _notes.Add(new(id, text));
        _revision++;
    }
    private void Edit(string id, string text)
    {
        int index = _notes.FindIndex(note => note.Id == id);
        if (index < 0 || string.IsNullOrWhiteSpace(text)) throw new ArgumentException("That note is not available.");
        _notes[index] = _notes[index] with { Text = text };
        _revision++;
    }
    private void Remove(string id)
    {
        _notes.RemoveAt(_notes.FindIndex(note => note.Id == id) is int index and >= 0 ? index : throw new ArgumentException("That note is not available."));
        _revision++;
    }
    private void Move(string id, int destination)
    {
        int source = _notes.FindIndex(note => note.Id == id);
        if (source < 0 || destination < 0 || destination >= _notes.Count) throw new ArgumentException("That note position is not available.");
        // The DOM sends the desired final zero-based position. The donor's pixel-line destinations
        // are adapted here to that direct semantic value rather than copying its window hit testing.
        DaggerfallUserNote note = _notes[source]; _notes.RemoveAt(source); _notes.Insert(destination, note);
        _revision++;
    }

    internal DaggerfallNotebookSave Capture() => new([.. _books.Values.OrderBy(book => book.BookId)], [.. _notes], _activeBookId, _page, _nextNoteId);
    internal void Restore(DaggerfallNotebookSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate(definitions, text);
        _books.Clear(); _notes.Clear(); _activeBookId = null; _page = 0; _nextNoteId = 0;
        foreach (DaggerfallReadableBook book in saved.Books)
        {
            _books.Add(book.BookId, new(book.BookId, book.Title, book.Author ?? string.Empty, [.. book.Pages]));
        }
        foreach (DaggerfallUserNote note in saved.Notes)
        {
            _notes.Add(note);
            if (note.Id.StartsWith("note:", StringComparison.Ordinal) && ulong.TryParse(note.Id.AsSpan("note:".Length), out ulong ordinal))
                _nextNoteId = Math.Max(_nextNoteId, ordinal);
        }
        _nextNoteId = Math.Max(_nextNoteId, saved.NextNoteId);
        if (saved.ActiveBookId is int active)
        {
            _activeBookId = active;
            _page = saved.Page;
        }
        _revision++;
    }

    private bool MatchesRevision(string? revision) => ulong.TryParse(revision, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out ulong value) && value == _revision;

    private void Select(int bookId)
    {
        _activeBookId = bookId;
        _page = 0;
        _revision++;
    }

    private void SetPage(int page)
    {
        if (_activeBookId is not int active || !_books.TryGetValue(active, out DaggerfallReadableBook? book)
            || page < 0 || page >= book.Pages.Length) throw new ArgumentException("That book page is not available.");
        _page = page;
        _revision++;
    }
}
