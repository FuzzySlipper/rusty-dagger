using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>BIO.DAT layout: NUL-separated prose lines, trailing empties kept.</summary>
public sealed class BioDatReaderTests
{
    [Fact]
    public void Reads_the_supplied_default_biography_lines()
    {
        BioDatCatalog catalog = BioDatReader.Read(
            File.ReadAllBytes(Corpus("BIO.DAT")), "local/arena2/BIO.DAT");

        Assert.Equal(34, catalog.Lines.Count);
        Assert.Equal(Enumerable.Range(0, 34), catalog.Lines.Select(line => line.Index));
        Assert.StartsWith("Your parents travelled with a carnival", catalog.Lines[0].Text);
        Assert.All(catalog.Lines, line => Assert.Equal(string.Empty, line.Reason));
        // The donor keeps every split, so the trailing empties are records too.
        Assert.Equal(string.Empty, catalog.Lines[32].Text);
        Assert.Equal(string.Empty, catalog.Lines[33].Text);
        // Offsets tile the file: each line starts where the previous NUL ended.
        Assert.Equal(0, catalog.Lines[0].Offset);
        for (int index = 1; index < catalog.Lines.Count; index++)
        {
            Assert.True(catalog.Lines[index].Offset > catalog.Lines[index - 1].Offset);
        }
    }

    [Fact]
    public void An_empty_file_states_one_empty_line_and_foreign_bytes_are_malformed()
    {
        BioDatCatalog empty = BioDatReader.Read([], "fixture/BIO.DAT");
        BioDatLine only = Assert.Single(empty.Lines);
        Assert.Equal(0, only.Index);
        Assert.Equal(string.Empty, only.Text);
        Assert.Equal(string.Empty, only.Reason);

        BioDatCatalog sour = BioDatReader.Read([(byte)'a', 0xC3, 0xA9, 0], "fixture/BIO.DAT");
        Assert.Equal(2, sour.Lines.Count);
        Assert.Equal(string.Empty, sour.Lines[0].Text);
        Assert.Contains("non-ASCII", sour.Lines[0].Reason, StringComparison.Ordinal);
        Assert.Equal(string.Empty, sour.Lines[1].Text);
        Assert.Equal(string.Empty, sour.Lines[1].Reason);
    }

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
