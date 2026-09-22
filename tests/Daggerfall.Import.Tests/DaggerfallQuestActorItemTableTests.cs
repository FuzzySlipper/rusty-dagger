using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DaggerfallQuestActorItemTableTests
{
    [Fact]
    public void Preserves_artifact_endpoints_and_item_parameter_aliases()
    {
        string source = Donor("Quests-Items.txt");
        // The full donor corpus is optional in portable developer and CI checkouts.
        if (!File.Exists(source)) return;
        byte[] bytes = File.ReadAllBytes(source);
        DaggerfallQuestItemTable items = DaggerfallQuestActorItemTableReader.ReadItems(bytes, "Tables/Quests-Items.txt");

        Assert.Equal(120, items.Rows.Count);
        Assert.Equal((5, 0), (items.Rows.Single(row => row.Name == "Masque_of_Clavicus_Vile").P1,
            items.Rows.Single(row => row.Name == "Masque_of_Clavicus_Vile").P2));
        Assert.Equal((5, 23), (items.Rows.Single(row => row.Name == "Shifters_Shirt").P1,
            items.Rows.Single(row => row.Name == "Shifters_Shirt").P2));
        Assert.Equal(24, items.Rows.Count(row => row.P1 == 5));
        Assert.Equal(new[] { "portrait", "Lysandus_death" }, items.Rows.Where(row => row.P1 == 13 && row.P2 == 0).Select(row => row.Name));
        Assert.Equal("Tables/Quests-Items.txt", items.Source.SourcePath);
        Assert.Equal(bytes.Length, items.Source.ByteLen);
        Assert.Equal(ContentDigest.Compute(bytes), items.Source.ContentHash);
        Assert.True(items.Rows.Zip(items.Rows.Skip(1)).All(pair => pair.First.SourceLine < pair.Second.SourceLine));
    }

    [Fact]
    public void Preserves_disabled_faction_uncertainty_as_rows_and_source_comments()
    {
        string source = Donor("Quests-Factions.txt");
        // The full donor corpus is optional in portable developer and CI checkouts.
        if (!File.Exists(source)) return;
        DaggerfallQuestFactionTable factions = DaggerfallQuestActorItemTableReader.ReadFactions(
            File.ReadAllBytes(source), "Tables/Quests-Factions.txt");

        DaggerfallQuestFactionTableRow commoner = factions.Rows.Single(row => row.Name == "Commoner");
        Assert.False(commoner.Active);
        Assert.Equal("?", commoner.P2Text);
        Assert.Null(commoner.P2);
        Assert.Equal(40, commoner.SourceLine);
        Assert.Contains(factions.Comments, comment => comment.SourceLine == 34 && comment.Text.Contains("almost certainly broken", StringComparison.Ordinal));
        Assert.Contains(factions.Comments, comment => comment.SourceLine == 38 && comment.Text.Contains("presently unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void Preserves_foe_aliases_and_knight_source_disposition()
    {
        string source = Donor("Quests-Foes.txt");
        // The full donor corpus is optional in portable developer and CI checkouts.
        if (!File.Exists(source)) return;
        DaggerfallQuestFoeTable foes = DaggerfallQuestActorItemTableReader.ReadFoes(
            File.ReadAllBytes(source), "Tables/Quests-Foes.txt");

        Assert.Equal(62, foes.Rows.Count);
        Assert.Equal(new[] { "Sorceror", "Sorcerer" }, foes.Rows.Where(row => row.Id == 131).Select(row => row.Name));
        Assert.Equal(145, foes.Rows.Single(row => row.Name == "Knight").Id);
        Assert.Contains(foes.Comments, comment => comment.SourceLine == 60 && comment.Text.Contains("Sorceror", StringComparison.Ordinal));
        Assert.Contains(foes.Comments, comment => comment.SourceLine == 77 && comment.Text.Contains("no knight", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parses_disabled_uncertainty_comments_and_numeric_aliases_without_the_donor_corpus()
    {
        DaggerfallQuestActorItemTables tables = DaggerfallQuestActorItemTableReader.Read(
            Encoding.UTF8.GetBytes("schema: *name,p1,p2\nportrait, 13, 0\nLysandus_death, 13, 0"), "Tables/Quests-Items.txt",
            Encoding.UTF8.GetBytes("-- social mapping remains unknown\nschema: *name,p1,p2,p3\n-Commoner, 0, ?, 0"), "Tables/Quests-Factions.txt",
            Encoding.UTF8.GetBytes("-- classic spelling is retained\nschema: id,*name\n131, Sorceror\n131, Sorcerer\n-- Monster.bsa has no knight in it.\n145, Knight"), "Tables/Quests-Foes.txt");

        Assert.Equal(new[] { "portrait", "Lysandus_death" }, tables.Items.Rows.Select(row => row.Name));
        Assert.All(tables.Items.Rows, row => Assert.Equal((13, 0), (row.P1, row.P2)));
        DaggerfallQuestFactionTableRow commoner = Assert.Single(tables.Factions.Rows);
        Assert.False(commoner.Active);
        Assert.Equal("?", commoner.P2Text);
        Assert.Null(commoner.P2);
        Assert.Contains(tables.Factions.Comments, comment => comment.Text.Contains("unknown", StringComparison.Ordinal));
        Assert.Equal(new[] { "Sorceror", "Sorcerer" }, tables.Foes.Rows.Where(row => row.Id == 131).Select(row => row.Name));
        Assert.Contains(tables.Foes.Comments, comment => comment.Text.Contains("no knight", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Refuses_duplicate_canonical_source_names_with_the_duplicate_source_span()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => DaggerfallQuestActorItemTableReader.ReadFoes(
            Encoding.UTF8.GetBytes("schema: id,*name\n1, Alias\n2, alias"), "Tables/Quests-Foes.txt"));

        Assert.Equal("Tables/Quests-Foes.txt", error.SourceName);
        Assert.Equal(3, error.Offset);
        Assert.Contains("duplicates line 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_active_faction_row_with_an_unresolved_parameter()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => DaggerfallQuestActorItemTableReader.ReadFactions(
            Encoding.UTF8.GetBytes("schema: *name,p1,p2,p3\nCommoner, 0, ?, 0"), "Tables/Quests-Factions.txt"));

        Assert.Equal(2, error.Offset);
        Assert.Contains("Only a disabled", error.Message, StringComparison.Ordinal);
    }

    private static string Donor(string file) => Path.Combine("/home/research/daggerfall-unity/Assets/StreamingAssets/Tables", file);
}
