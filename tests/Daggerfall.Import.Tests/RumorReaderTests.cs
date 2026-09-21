using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>RUMOR.DAT layout: fixed-shape records closed by length-delimited classic text.</summary>
public sealed class RumorReaderTests
{
    [Fact]
    public void Reads_the_supplied_rumors_with_tokens_and_metadata()
    {
        RumorCatalog catalog = RumorReader.Read(
            File.ReadAllBytes(Corpus("RUMOR.DAT")), "local/arena2/RUMOR.DAT");

        Assert.Equal(31, catalog.Records.Count);
        RumorRecord first = catalog.Records[0];
        Assert.Equal(0, first.Index);
        Assert.Equal(0, first.Offset);
        Assert.Equal(508, first.Faction1);
        Assert.Equal(810, first.Faction2);
        Assert.Equal(27, first.Type);
        Assert.Equal(0, first.Region);
        Assert.Equal(1, first.Flags);
        Assert.Equal(0, first.QuestId);
        Assert.Equal(string.Empty, first.QuestName);
        Assert.Equal(0, first.Unknown);
        Assert.Equal(0, first.NpcId);
        Assert.Equal(107, first.TextLength);
        Assert.Equal(566670, first.TimeLimit);
        // The text tokenizes in the classic grammar: runs around the centering code.
        Assert.Contains(first.Tokens, token => token.Code == Arena2TextCode.Text);
        Assert.Contains(first.Tokens, token => token.Code == Arena2TextCode.JustifyCenter);
        Assert.DoesNotContain(first.Tokens, token => token.Code == Arena2TextCode.Unknown);
        // Every supplied type is one the donor's rumor-type table names.
        Assert.All(catalog.Records, record => Assert.True(RumorReader.IsKnownType(record.Type), $"rumor {record.Index} type {record.Type}"));
        // Ordinals, offsets and text ranges tile the file exactly.
        Assert.Equal(Enumerable.Range(0, 31), catalog.Records.Select(record => record.Index));
        for (int index = 1; index < catalog.Records.Count; index++)
        {
            Assert.True(catalog.Records[index].Offset > catalog.Records[index - 1].TextOffset);
        }

        Assert.Equal(3506, catalog.Records[^1].TextOffset + catalog.Records[^1].TextLength);
    }

    [Fact]
    public void Refuses_a_truncated_record_and_reports_foreign_bytes()
    {
        byte[] corpus = File.ReadAllBytes(Corpus("RUMOR.DAT"));
        Assert.Throws<Arena2FormatException>(() => RumorReader.Read(corpus.AsSpan(0, 100), "fixture/RUMOR.DAT"));

        // A text length past the file's end is a length the file cannot honor.
        byte[] overrun = (byte[])corpus.Clone();
        BitConverter.GetBytes((uint)9999).CopyTo(overrun, 26);
        Assert.Throws<Arena2FormatException>(() => RumorReader.Read(overrun, "fixture/RUMOR.DAT"));
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
