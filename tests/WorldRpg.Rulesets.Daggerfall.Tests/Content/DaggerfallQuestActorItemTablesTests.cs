using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallQuestActorItemTablesTests
{
    [Fact]
    public void Admitted_pack_preserves_quest_actor_item_rows_and_aliases()
    {
        DaggerfallQuestActorItemTables tables = DaggerfallQuestWorldTablesTests.Tables().ActorItemTables;
        Assert.Equal(120, tables.Items.Rows.Count);
        Assert.Equal((5, 0), (tables.Items.Resolve("Masque_of_Clavicus_Vile").P1, tables.Items.Resolve("Masque_of_Clavicus_Vile").P2));
        Assert.Equal((5, 23), (tables.Items.Resolve("Shifters_Shirt").P1, tables.Items.Resolve("Shifters_Shirt").P2));
        Assert.Equal(428, tables.Factions.Rows.Count(row => row.Active));
        Assert.Equal(10, tables.Factions.Rows.Count(row => !row.Active));
        DaggerfallQuestFactionTableRow commoner = tables.Factions.Rows.Single(row => row.Name == "Commoner");
        Assert.False(commoner.Active);
        Assert.Equal("?", commoner.P2Text);
        Assert.Null(commoner.P2);
        Assert.Equal(62, tables.Foes.Rows.Count);
        Assert.Equal(131, tables.Foes.Resolve("Sorceror").Id);
        Assert.Equal(131, tables.Foes.Resolve("Sorcerer").Id);
        Assert.Equal(145, tables.Foes.Resolve("Knight").Id);
    }

    [Fact]
    public void Source_name_lookups_preserve_aliases_and_unresolved_faction_parameters()
    {
        DaggerfallQuestFoeTable foes = new([
            new(131, "Sorceror", true),
            new(131, "Sorcerer", true),
            new(145, "Knight", true),
        ]);
        DaggerfallQuestFactionTable factions = new([
            new("Commoner", 0, "?", null, 0, false),
        ]);

        Assert.Equal(131, foes.Resolve("sorceror").Id);
        Assert.Equal(131, foes.Resolve("SORCERER").Id);
        Assert.Equal(145, foes.Resolve("Knight").Id);
        DaggerfallQuestFactionTableRow commoner = Assert.Single(factions.Rows);
        Assert.Equal("?", commoner.P2Text);
        Assert.Null(commoner.P2);
        Assert.Throws<KeyNotFoundException>(() => factions.Resolve("Commoner"));
    }
}
