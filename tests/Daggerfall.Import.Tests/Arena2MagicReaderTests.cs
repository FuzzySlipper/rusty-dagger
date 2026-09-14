using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The classic magical catalogs are fixed-stride tables; these checks decode the real corpus and pin the
/// identities and the refusals that keep a partial catalog from being published as a complete one.
/// </summary>
public sealed class Arena2MagicReaderTests
{
    private const string SpellFile = "SPELLS.STD";

    private const string MagicItemFile = "MAGIC.DEF";

    [Fact]
    public void DecodesTheClassicSpellTableWithItsOwnRecordIdentities()
    {
        byte[] bytes = ReadArena2(SpellFile);
        Arena2SpellCatalog catalog = Arena2MagicReader.ReadSpells(bytes, SpellFile);

        // The file is exactly a whole number of records and every one of them carries a spell.
        Assert.Equal(0, bytes.Length % Arena2MagicReader.SpellRecordSize);
        Assert.Equal(bytes.Length / Arena2MagicReader.SpellRecordSize, catalog.Spells.Count);
        Assert.Empty(catalog.Dispositions);

        Arena2Spell first = catalog.Spells[0];
        Assert.Equal("Fenrik's Door Jam", first.Name);
        Assert.Equal(1, first.Index);
        Assert.Equal(4, first.Element);
        Assert.Equal(0, first.RangeType);
        Assert.Equal(16, Assert.Single(first.Effects).Type);
        // The identity byte is the file's own and is not the record's position: the last two records sit
        // at positions 88 and 89 and carry the identities 98 and 99.
        Assert.Equal("Tame", catalog.Spells[^1].Name);
        Assert.Equal(99, catalog.Spells[^1].Index);
        // The corpus itself repeats one identity, so a consumer keyed on the index byte alone would
        // collide; the reader republishes what the file says rather than smoothing it over.
        Assert.Equal(2, catalog.Spells.Count(spell => spell.Index == 58));
        // Stability: the same bytes decode to the same identities.
        Assert.Equal(
            catalog.Spells.Select(spell => (spell.Index, spell.Name)),
            Arena2MagicReader.ReadSpells(bytes, SpellFile).Spells.Select(spell => (spell.Index, spell.Name)));

        Arena2Spell levitate = catalog.Spells.Single(spell => spell.Name == "Levitate");
        Assert.Equal(50, levitate.Cost);
        Assert.Equal(4, levitate.Element);
    }

    [Fact]
    public void DecodesTheClassicMagicItemTableAgainstItsOwnRecordCount()
    {
        byte[] bytes = ReadArena2(MagicItemFile);
        Arena2MagicItemCatalog catalog = Arena2MagicReader.ReadMagicItems(bytes, MagicItemFile);

        Assert.Equal(59, catalog.Items.Count);
        Assert.Empty(catalog.Dispositions);
        Assert.Equal("The Masque of Clavicus", catalog.Items[0].Name);
        Assert.Equal(26, catalog.Items[0].Enchantments[0].Type);
        Assert.NotEmpty(catalog.Items[0].Enchantments);
        Assert.All(catalog.Items, item => Assert.Equal(sizeof(int) + (catalog.Items.Count * Arena2MagicReader.MagicItemRecordSize), bytes.Length));
        // A template's offset in the file is its identity, so two templates cannot claim one.
        Assert.Equal(catalog.Items.Count, catalog.Items.Select(item => item.Index).Distinct().Count());
    }

    [Fact]
    public void RefusesARecordTableThatDisagreesWithItsOwnLength()
    {
        byte[] spells = ReadArena2(SpellFile);
        byte[] truncatedSpells = spells[..(spells.Length - 1)];
        InvalidOperationException spellFailure = Assert.Throws<InvalidOperationException>(() => Arena2MagicReader.ReadSpells(truncatedSpells, SpellFile));
        Assert.Contains("not a whole number", spellFailure.Message, StringComparison.Ordinal);
        Assert.Contains("published incomplete", spellFailure.Message, StringComparison.Ordinal);

        byte[] items = ReadArena2(MagicItemFile);
        byte[] miscounted = [.. items, 0];
        InvalidOperationException itemFailure = Assert.Throws<InvalidOperationException>(() => Arena2MagicReader.ReadMagicItems(miscounted, MagicItemFile));
        Assert.Contains("states 59 magic-item records", itemFailure.Message, StringComparison.Ordinal);
        Assert.Contains($"{miscounted.Length} bytes", itemFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsARecordThatCarriesNoSpellAsAStatedDisposition()
    {
        // An all-empty effect block is the donor's own malformed case; it is reported with its offset
        // rather than dropped, so the catalog says what it did not carry.
        byte[] record = new byte[Arena2MagicReader.SpellRecordSize];
        for (int slot = 0; slot < 3; slot++)
        {
            record[slot * Arena2MagicReader.SpellEffectSlotSize] = 0xFF;
            record[(slot * Arena2MagicReader.SpellEffectSlotSize) + 1] = 0xFF;
        }

        Arena2SpellCatalog catalog = Arena2MagicReader.ReadSpells(record, "synthetic");
        Assert.Empty(catalog.Spells);
        Arena2MagicDisposition disposition = Assert.Single(catalog.Dispositions);
        Assert.Equal(0, disposition.Offset);
        Assert.Contains("empty", disposition.Reason, StringComparison.Ordinal);
    }

    private static byte[] ReadArena2(string name)
    {
        string path = Path.Combine(RepositoryRoot(), "local", "arena2", name);
        Assert.True(File.Exists(path), $"{name} is not staged at {path}; the classic corpus is required for this check.");
        return File.ReadAllBytes(path);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }
}
