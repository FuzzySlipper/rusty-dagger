using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using System.Text.Json;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallBookNotebookTests
{
    [Fact]
    public void Retained_normalized_book_and_ordered_notes_restore_without_the_source_item()
    {
        DaggerfallDefinitions definitions = ReadDefinitions();
        DaggerfallBookNotebook original = new(definitions, new DaggerfallTextResolver(definitions.Text));

        // The classic message's high byte is not book identity. The reader retains the normalized
        // book record, so subsequent presentation no longer needs its originating quest item.
        DaggerfallReadableBook book = original.Open(59 + 256);
        Assert.Equal(59, book.BookId);
        Assert.NotEmpty(book.Pages);
        DaggerfallNotebookPresentation opened = original.Read();
        Assert.Equal(book.Title, opened.Book!.Title);
        Assert.Equal(book.Pages[0], opened.Book.Text);

        Assert.True(original.Apply(new("notebook-add", opened.Revision, Text: "First account.")).Applied);
        Assert.True(original.Apply(new("notebook-add", original.Read().Revision, Text: "Second account.")).Applied);
        DaggerfallNotebookPresentation beforeMove = original.Read();
        Assert.True(original.Apply(new("notebook-move", beforeMove.Revision, Note: beforeMove.Notes[1].Id, Destination: 0)).Applied);
        DaggerfallNotebookPresentation beforeEdit = original.Read();
        Assert.True(original.Apply(new("notebook-edit", beforeEdit.Revision, Note: beforeEdit.Notes[0].Id, Text: "Revised account.")).Applied);

        DaggerfallNotebookSave saved = original.Capture();
        Assert.Single(saved.Books);
        Assert.Equal(book.Pages, saved.Books[0].Pages);
        DaggerfallNotebookSave serialized = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(saved, DaggerfallSaveJsonContext.Default.DaggerfallNotebookSave),
            DaggerfallSaveJsonContext.Default.DaggerfallNotebookSave)!;
        DaggerfallBookNotebook restored = new(definitions, new DaggerfallTextResolver(definitions.Text));
        restored.Restore(serialized);

        DaggerfallNotebookPresentation view = restored.Read();
        Assert.Equal(book.BookId, view.Book!.BookId);
        Assert.Equal(book.Title, view.Book.Title);
        Assert.Equal(book.Pages[0], view.Book.Text);
        Assert.Equal(["Revised account.", "First account."], view.Notes.Select(note => note.Text));
    }

    [Fact]
    public void Stale_page_or_note_requests_leave_the_current_reader_and_unrelated_notes_unchanged()
    {
        DaggerfallDefinitions definitions = ReadDefinitions();
        DaggerfallBookNotebook notebook = new(definitions, new DaggerfallTextResolver(definitions.Text));
        _ = notebook.Open(59);
        string stale = notebook.Read().Revision;
        Assert.True(notebook.Apply(new("notebook-add", stale, Text: "Keep this.")).Applied);
        DaggerfallNotebookPresentation afterAdd = notebook.Read();

        DaggerfallNotebookActionResult stalePage = notebook.Apply(new("notebook-page", stale, Page: 1));
        DaggerfallNotebookActionResult staleRemove = notebook.Apply(new("notebook-remove", stale, Note: afterAdd.Notes[0].Id));

        Assert.False(stalePage.Applied);
        Assert.False(staleRemove.Applied);
        DaggerfallNotebookPresentation current = notebook.Read();
        Assert.Equal(0, current.Book!.Page);
        Assert.Equal(["Keep this."], current.Notes.Select(note => note.Text));
    }

    [Fact]
    public void Restore_rejects_retained_books_that_are_missing_or_not_readable_in_the_catalog()
    {
        DaggerfallDefinitions definitions = ReadDefinitions();
        DaggerfallTextResolver text = new(definitions.Text);
        DaggerfallBookNotebook source = new(definitions, text);
        DaggerfallReadableBook readable = source.Open(59);
        DaggerfallBookNotebook restored = new(definitions, text);

        Assert.Contains("not published", Assert.Throws<ArgumentException>(() => restored.Restore(
            new([readable with { BookId = 999999 }], [], null, 0, 0))).Message, StringComparison.Ordinal);

        DaggerfallBookDefinition unavailable = definitions.Books.Books.Values
            .First(book => book.Disposition != DaggerfallBookDisposition.Read);
        Assert.Contains("not readable", Assert.Throws<ArgumentException>(() => restored.Restore(
            new([new DaggerfallReadableBook(unavailable.BookId, "Unavailable", "Unknown", ["forged page"])], [], null, 0, 0))).Message, StringComparison.Ordinal);
    }

    private static DaggerfallDefinitions ReadDefinitions()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
        return TestPayload.Definitions;
    }
}
