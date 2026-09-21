using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>NAMEGEN.DAT layout: eleven banks of up to six fragment sets under an offset/count header.</summary>
public sealed class NameGenReaderTests
{
    [Fact]
    public void Reads_the_supplied_banks_sets_and_fragments()
    {
        NameGenCatalog catalog = NameGenReader.Read(
            File.ReadAllBytes(Corpus("NAMEGEN.DAT")), "local/arena2/NAMEGEN.DAT");

        Assert.Equal(11, catalog.Banks.Count);
        // The donor's curated database carries these tables fragment for fragment; the bank order
        // is the donor's bank order, so a consumer composes names against bank indices directly.
        Assert.Equal([13, 13, 13, 13, 13, 13], catalog.Banks[0].Sets.Select(set => set?.Parts.Count ?? 0));
        Assert.Equal("Theod", catalog.Banks[0].Sets[0]?.Parts[0]);
        Assert.Equal("istyr", catalog.Banks[0].Sets[1]?.Parts[12]);
        Assert.Equal("Morg", catalog.Banks[0].Sets[2]?.Parts[0]);
        Assert.Equal("ona", catalog.Banks[0].Sets[3]?.Parts[12]);
        Assert.Null(catalog.Banks[1].Sets[5]);
        Assert.Null(catalog.Banks[2].Sets[4]);
        Assert.Equal(785, catalog.Banks.Sum(bank => bank.Sets.Sum(set => set?.Parts.Count ?? 0)));
        // Noble titles close the last bank the way the donor's database states them.
        Assert.Equal(["Lord", "Count", "Baron", "Viscount", "Marquis", "Duke"], catalog.Banks[10].Sets[3]?.Parts);
    }

    [Fact]
    public void Refuses_a_short_header_a_gapped_table_and_non_ascii()
    {
        byte[] corpus = File.ReadAllBytes(Corpus("NAMEGEN.DAT"));
        Assert.Throws<Arena2FormatException>(() => NameGenReader.Read(corpus.AsSpan(0, 100), "fixture/NAMEGEN.DAT"));

        // A set that starts past the previous set's end leaves untabled bytes no consumer addresses.
        byte[] gapped = (byte[])corpus.Clone();
        BitConverter.GetBytes((uint)700).CopyTo(gapped, 4);
        Assert.Throws<Arena2FormatException>(() => NameGenReader.Read(gapped, "fixture/NAMEGEN.DAT"));

        // A trailing byte past the last set's end is not a fragment table either.
        byte[] trailed = [.. corpus, (byte)'x'];
        Assert.Throws<Arena2FormatException>(() => NameGenReader.Read(trailed, "fixture/NAMEGEN.DAT"));

        byte[] sour = (byte[])corpus.Clone();
        sour[528] = 0xC3;
        Assert.Throws<Arena2FormatException>(() => NameGenReader.Read(sour, "fixture/NAMEGEN.DAT"));
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
