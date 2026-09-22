using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DaggerfallQuestTableTests
{
    [Fact]
    public void Preserves_numeric_identity_case_aliases_lines_and_source_bytes()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("-- comment\r\nschema: id,*name\r\n1006, RumorsPostfailure\r\n1006, RumorsPostFailure -- alias\r\n1045, QuestTimeLapse");
        DaggerfallQuestTable table = DaggerfallQuestTableReader.Read(bytes, "Tables/messages.txt");
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(new[] { 3, 4, 5 }, table.Rows.Select(row => row.SourceLine));
        Assert.Equal("RumorsPostfailure", table.Rows[0].Name);
        Assert.Equal("RumorsPostFailure", table.Rows[1].Name);
        Assert.Equal(1006, table.Lookup["RUMORSPOSTFAILURE"]);
        Assert.Equal(1045, table.Lookup["QuestTimeLapse"]);
        Assert.Equal(bytes.Length, table.Source.ByteLen);
        Assert.Equal(ContentDigest.Compute(bytes), table.Source.ContentHash);
        Assert.Equal("Tables/messages.txt", table.Source.SourcePath);
    }

    [Fact]
    public void Global_aliases_use_declared_slots_not_row_positions()
    {
        string text = "schema: id,*name\n" + string.Join("\n", Enumerable.Range(0, 64).Reverse().Select(id => $"{id}, Slot{id}"))
            + "\n5, TookTheCure\n10, OpenedShapeshifters";
        DaggerfallQuestTable table = DaggerfallQuestTableReader.Read(Encoding.UTF8.GetBytes(text), "Tables/globals.txt", globals: true);
        Assert.Equal(66, table.Rows.Count);
        Assert.Equal(5, table.Lookup["TookTheCure"]);
        Assert.Equal(10, table.Lookup["OpenedShapeshifters"]);
        Assert.Equal(63, table.Lookup["Slot63"]);
        QuestSourceDocument quest = QuestSourceReader.Read("quest: Q\nqrc:\nMessage: 1\ntext\nqbn:\nTookTheCure _done_\n", "q.txt", new Dictionary<string, int> { ["Message"] = 0 }, table.Lookup);
        Assert.Equal(5, Assert.Single(quest.Blocks).Global);
    }

    [Theory]
    [InlineData("[9999]", 1000)]
    [InlineData("[offer]", 1000)]
    [InlineData("9999", 9999)]
    public void Fixed_message_headers_use_table_identity_and_bare_headers_keep_explicit_ids(string header, int expected)
    {
        DaggerfallQuestTable messages = DaggerfallQuestTableReader.Read(Encoding.UTF8.GetBytes("schema: id,*name\n1000, QuestorOffer"), "Tables/messages.txt");
        QuestSourceDocument quest = QuestSourceReader.Read($"quest: Q\nqrc:\nquestoroffer: {header}\ntext\nqbn:\nclock a\n",
            "q.txt", messages.Lookup, new Dictionary<string, int>());
        Assert.Equal(expected, Assert.Single(quest.Messages).Id);
    }

    [Theory]
    [InlineData("schema: id,*name\n1006, Alias\n1007, ALIAS")]
    [InlineData("schema: id,*name\nno, Alias")]
    [InlineData("schema: id,*name\n1, ")]
    [InlineData("schema: name,*id\n1, Alias")]
    [InlineData("1, Alias")]
    public void Refuses_ambiguous_or_malformed_tables(string text) =>
        Assert.Throws<Arena2FormatException>(() => DaggerfallQuestTableReader.Read(Encoding.UTF8.GetBytes(text), "Tables/messages.txt"));

    [Fact]
    public void Refuses_missing_global_slots() =>
        Assert.Throws<Arena2FormatException>(() => DaggerfallQuestTableReader.Read(Encoding.UTF8.GetBytes("schema: id,*name\n5, Alias"), "Tables/globals.txt", globals: true));
}
