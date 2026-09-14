using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The published magic catalog is what a later effect task resolves through, so its keys, its source
/// identities and its item-to-spell links are pinned here rather than left to the first consumer.
/// </summary>
public sealed class Arena2MagicCatalogDocumentTests
{
    [Fact]
    public void PublishesKeysIdentitiesAndItemToSpellLinks()
    {
        Arena2MagicCatalogPublication publication = Arena2MagicCatalogDocument.Build(
            SpellTable(), MagicItemTable(), "local/arena2/SPELLS.STD", "local/arena2/MAGIC.DEF");
        JsonObject document = JsonNode.Parse(publication.Json)!.AsObject();

        Assert.Equal(1, document["schemaVersion"]!.GetValue<int>());
        Assert.Equal(2, publication.Spells);
        Assert.Equal(1, publication.MagicItems);
        Assert.Equal(2, publication.Enchantments);

        JsonArray spells = document["spells"]!.AsArray();
        // Keys are ordinals because the file's own identity byte repeats in this corpus; the identity is
        // published beside the key, with the sharing said out loud.
        Assert.Equal("spell.001", spells[0]!["key"]!.GetValue<string>());
        Assert.Equal("spell.002", spells[1]!["key"]!.GetValue<string>());
        Assert.Equal(7, spells[0]!["identity"]!.GetValue<int>());
        Assert.Equal(7, spells[1]!["identity"]!.GetValue<int>());
        Assert.All(spells, spell => Assert.True(spell!["identityShared"]!.GetValue<bool>()));
        Assert.Equal("Fenrik's Door Jam", spells[0]!["name"]!.GetValue<string>());

        JsonArray sources = document["sources"]!.AsArray();
        Assert.Equal(2, sources.Count);
        Assert.All(sources, source => Assert.Equal("CNT-012", source!["recordId"]!.GetValue<string>()));

        // One enchantment names a published spell; the other names an identity no spell carries, so the
        // link is reported rather than assumed.
        JsonArray item = document["magicItems"]!.AsArray();
        JsonArray enchantments = item[0]!["enchantments"]!.AsArray();
        Assert.Equal("spell.001", enchantments[0]!["spell"]!.GetValue<string>());
        Assert.True(enchantments[0]!["spellIdentityShared"]!.GetValue<bool>());
        Assert.Equal("cast-when-used", enchantments[0]!["paramMeaning"]!.GetValue<string>());
        Assert.Null(enchantments[1]!["spell"]);
        JsonObject unresolved = Assert.Single(document["unresolvedLinks"]!.AsArray())!.AsObject();
        Assert.Equal(99, unresolved["spellIdentity"]!.GetValue<int>());
        Assert.Contains("no published spell", unresolved["reason"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(1, publication.UnresolvedLinks);
    }

    [Fact]
    public void ReportsTheRealCorpusCountsAndLeavesUnresolvedLinksLegible()
    {
        string arena2 = Path.Combine(RepositoryRoot(), "local", "arena2");
        string spellPath = Path.Combine(arena2, "SPELLS.STD");
        string magicPath = Path.Combine(arena2, "MAGIC.DEF");
        Assert.True(File.Exists(spellPath) && File.Exists(magicPath), "the classic corpus is required for this check");

        Arena2MagicCatalogPublication publication = Arena2MagicCatalogDocument.Build(
            File.ReadAllBytes(spellPath), File.ReadAllBytes(magicPath), "local/arena2/SPELLS.STD", "local/arena2/MAGIC.DEF");
        Assert.Equal(89, publication.Spells);
        Assert.Equal(59, publication.MagicItems);
        Assert.Equal(84, publication.Enchantments);
        Assert.Equal(0, publication.Dispositions);

        JsonObject document = JsonNode.Parse(publication.Json)!.AsObject();
        // The corpus itself carries links to spell identities nothing defines; they are published as
        // unresolved rather than dropped, so a consumer sees the same gap the source has.
        Assert.Equal(publication.UnresolvedLinks, document["unresolvedLinks"]!.AsArray().Count);
        Assert.All(document["unresolvedLinks"]!.AsArray(), link =>
            Assert.Contains("no published spell", link!["reason"]!.GetValue<string>(), StringComparison.Ordinal));
        // The repeated identity in the corpus is visible where a consumer would otherwise collide.
        Assert.Contains(document["spells"]!.AsArray(), spell => spell!["identityShared"]!.GetValue<bool>());
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }

    /// <summary>Two spells sharing one identity byte, which is the corpus' own shape.</summary>
    private static byte[] SpellTable()
    {
        byte[] bytes = new byte[Arena2MagicReader.SpellRecordSize * 2];
        WriteSpell(bytes, 0, index: 7, name: "Fenrik's Door Jam", effectType: 16);
        WriteSpell(bytes, Arena2MagicReader.SpellRecordSize, index: 7, name: "Buoyancy", effectType: 31);
        return bytes;

        static void WriteSpell(byte[] target, int offset, int index, string name, int effectType)
        {
            target[offset] = (byte)effectType;
            target[offset + 1] = 0xFF;
            target[offset + 2] = 0xFF;
            target[offset + 3] = 0xFF;
            target[offset + 4] = 0xFF;
            target[offset + 5] = 0xFF;
            int nameOffset = offset + 6 + 8 + 9 + 9 + 15;
            System.Text.Encoding.Latin1.GetBytes(name).CopyTo(target, nameOffset);
            target[nameOffset + 25] = 1;
            target[nameOffset + 26] = (byte)index;
        }
    }

    /// <summary>One template whose first enchantment names a published spell and whose second names none.</summary>
    private static byte[] MagicItemTable()
    {
        byte[] bytes = new byte[sizeof(int) + Arena2MagicReader.MagicItemRecordSize];
        BitConverter.GetBytes(1).CopyTo(bytes, 0);
        int offset = sizeof(int);
        System.Text.Encoding.Latin1.GetBytes("The Masque of Clavicus").CopyTo(bytes, offset);
        bytes[offset + Arena2MagicReader.MagicItemNameLength] = 2;
        int enchantments = offset + Arena2MagicReader.MagicItemNameLength + 3;
        // The first slot is a cast-when-used enchantment naming spell identity 7, which both published
        // spells carry; only a spell-carrying type is read as a link at all.
        bytes[enchantments] = 0;
        bytes[enchantments + 1] = 7;
        bytes[enchantments + 2] = unchecked((byte)-1);
        bytes[enchantments + 3] = unchecked((byte)-1);
        for (int slot = 2; slot < 10; slot++)
        {
            bytes[enchantments + (slot * 2)] = 0xFF;
            bytes[enchantments + (slot * 2) + 1] = 0xFF;
        }

        bytes[enchantments + 2] = unchecked((byte)-1);
        bytes[enchantments + 3] = unchecked((byte)-1);
        // The second slot is a cast-when-strikes enchantment naming spell identity 99, which no published
        // spell carries, so it is the one link that stays unresolved.
        bytes[enchantments + 4] = 2;
        bytes[enchantments + 5] = unchecked((byte)99);
        return bytes;
    }
}
