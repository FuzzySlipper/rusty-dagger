using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallQuestMagicTablesTests
{
    [Fact]
    public void Disease_ids_and_spell_aliases_preserve_source_identity()
    {
        var tables = DaggerfallQuestWorldTablesTests.Tables();
        Assert.Equal(Enumerable.Range(0, 17), tables.Diseases.Rows.Select(row => row.Id));
        Assert.Equal(0, tables.Diseases.Lookup["Witches'_Pox"]);
        Assert.Equal(16, tables.Diseases.Lookup["Wizard_Fever"]);
        Assert.Equal(89, tables.Spells.Rows.Count);
        Assert.Equal(58, tables.Spells.Lookup["Holy_Word"]);
        Assert.Equal(58, tables.Spells.Lookup["Holy_Touch"]);
        Assert.Equal(2, tables.Spells.Rows.Count(row => row.Id == 58));
        Assert.Equal(71, tables.Spells.Lookup["Nux_Vomica"]);
        Assert.Equal(72, tables.Spells.Lookup["Arsenic"]);
        Assert.Equal(73, tables.Spells.Lookup["Moonseed"]);
        Assert.Equal(74, tables.Spells.Lookup["Drothweed"]);
        Assert.Equal(75, tables.Spells.Lookup["Somnalius"]);
        Assert.Equal(76, tables.Spells.Lookup["Pyrrhic_Acid"]);
        Assert.Equal(92, tables.Spells.Lookup["Lycanthropy"]);
        Assert.DoesNotContain(tables.Spells.Rows, row => row.Id is 21 or 43 or 48);
        Assert.Equal("Tables/Quests-Diseases.txt", tables.Diseases.SourcePath);
        Assert.Equal("Tables/Quests-Spells.txt", tables.Spells.SourcePath);
    }
}
