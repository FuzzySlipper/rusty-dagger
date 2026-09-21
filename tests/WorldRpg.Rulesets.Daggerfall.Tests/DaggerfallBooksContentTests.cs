using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published book catalog through the pack: classic messages resolve to the books they name,
/// and each readable book's pages resolve through the shared text contract.
/// </summary>
public sealed class DaggerfallBooksContentTests
{
    [Fact]
    public void Resolves_a_published_book_by_message_key_through_the_text_contract()
    {
        DaggerfallDefinitions definitions = Definitions();

        // All 90 supplied files are represented; the 22 missing identities stay explicit.
        Assert.Equal(112, definitions.Books.Books.Count);
        Assert.Equal(90, definitions.Books.Books.Values.Count(book => book.Disposition == DaggerfallBookDisposition.Read));
        Assert.Equal(22, definitions.Books.Books.Values.Count(book => book.Disposition == DaggerfallBookDisposition.NotSupplied));

        // A message resolves by its low byte, the way the donor maps messages to filenames.
        DaggerfallBookDefinition scroll = definitions.Books.ResolveMessage(59);
        Assert.Equal(59, scroll.BookId);
        Assert.Equal("BOK00059.TXT", scroll.FileName);
        Assert.Equal("Biography of Queen Barenziah, Vol. I", scroll.Title);
        Assert.Equal(scroll.PageCount, scroll.PageKeys.Count);
        DaggerfallBookDefinition aliased = definitions.Books.ResolveMessage(59 + 256);
        Assert.Equal(59, aliased.BookId);

        // Every readable page resolves through the text set the catalog cites. Six pages carry
        // only line-offset codes and no words; they resolve as readable with no runs, the way the
        // text contract distinguishes an empty value from a missing one.
        List<string> wordless = [];
        foreach (DaggerfallBookDefinition book in definitions.Books.Books.Values.Where(book => book.Disposition == DaggerfallBookDisposition.Read))
        {
            foreach (string key in book.PageKeys)
            {
                DaggerfallTextValue page = definitions.Text.Require(Parse(key));
                if (!page.TextRuns.Any())
                {
                    wordless.Add(key);
                }
            }
        }

        Assert.Equal(["Book:00061-p06", "Book:00088-p34", "Book:00088-p35", "Book:00088-p36", "Book:00110-p17", "Book:00110-p18"], wordless.OrderBy(key => key, StringComparer.Ordinal));

        // A message with no supplied file is itself the miss a consumer reports.
        DaggerfallBookDefinition missing = definitions.Books.ResolveMessage(90);
        Assert.Equal(DaggerfallBookDisposition.NotSupplied, missing.Disposition);
        Assert.Empty(missing.PageKeys);
    }

    private static DaggerfallTextKey Parse(string key)
    {
        string[] parts = key.Split(':');
        return new DaggerfallTextKey(Enum.Parse<DaggerfallTextKind>(parts[0], ignoreCase: true), parts[1]);
    }

    private static DaggerfallDefinitions Definitions()
    {
        string root = RepositoryRoot();
        return DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
    }

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
