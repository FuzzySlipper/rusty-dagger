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
            SpellTable(), MagicItemTable(), "arena2/SPELLS.STD", "arena2/MAGIC.DEF");
        JsonObject document = JsonNode.Parse(publication.Json)!.AsObject();

        Assert.Equal(["arena2/SPELLS.STD", "arena2/MAGIC.DEF"], document["sources"]!.AsArray().Select(source => source!["path"]!.GetValue<string>()));
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
        Assert.All(sources, source => Assert.True(source!["byteLength"]!.GetValue<long>() > 0));

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
    public void Publishes_the_item_maker_settings_with_donor_costs_and_display_metadata()
    {
        JsonArray settings = JsonNode.Parse(Arena2MagicCatalogDocument.Build(
            SpellTable(), MagicItemTable(), "arena2/SPELLS.STD", "arena2/MAGIC.DEF").Json)!["enchantmentSettings"]!.AsArray();
        Assert.Equal(164, settings.Count);
        var absorption=Assert.Single(settings,row=>row!["type"]!.GetValue<int>()==9)!;
        Assert.Equal(-1,absorption["param"]!.GetValue<int>());
        Assert.Equal(1500,absorption["cost"]!.GetValue<int>());
        Assert.Equal("spell-absorption",absorption["meaning"]!.GetValue<string>());
        Assert.Equal("AbsorbsSpells",absorption["textKey"]!.GetValue<string>());
        Assert.Equal(164, settings.Select(row => row!["key"]!.GetValue<string>()).Distinct().Count());
        foreach ((int type, int count, string source) in new[] {
            (10, 35, "EnhancesSkill"), (3, 11, "ExtraSpellPts"),
            (7, 2, "IncreasedWeightAllowance"), (13, 3, "ImprovesTalents"),
            (21, 3, "HealthLeech"), (20, 4, "LowDamageVs"), (4, 4, "PotentVs"), (6, 2, "VampiricEffect"),
            (14, 6, "GoodRepWith"), (25, 6, "BadRepWith"), (22, 3, "BadReactionsFrom") })
        {
            JsonNode[] family = [.. settings.Where(row => row!["type"]!.GetValue<int>() == type).Select(row => row!)];
            Assert.Equal(count, family.Length);
            Assert.Equal(Enumerable.Range(0, count), family.Select(row => row["param"]!.GetValue<int>()));
            Assert.All(family, row =>
            {
                Assert.Equal(Enumerable.Range(0, count), row["parameterVariants"]!.AsArray().Select(value => value!.GetValue<int>()));
                Assert.Equal(source, row["textKey"]!.GetValue<string>());
                Assert.Contains($"/{source}.cs#GetEnchantmentSettings", row["sourceClass"]!.GetValue<string>(), StringComparison.Ordinal);
                Assert.False(string.IsNullOrWhiteSpace(row["displayName"]!.GetValue<string>()));
                Assert.False(string.IsNullOrWhiteSpace(row["parameterTextKey"]!.GetValue<string>()));
            });
        }
        Assert.Equal(new[] {500,500,500,500,200,200,200,700,800,900,1000},
            settings.Where(row => row!["type"]!.GetValue<int>() == 3).Select(row => row!["cost"]!.GetValue<int>()));
        Assert.Equal(new[] {500,600,600},
            settings.Where(row => row!["type"]!.GetValue<int>() == 13).Select(row => row!["cost"]!.GetValue<int>()));
        Assert.Equal(new[] {1000,1000,1000,1000,1000,5000},
            settings.Where(row => row!["type"]!.GetValue<int>() == 14).Select(row => row!["cost"]!.GetValue<int>()));
        Assert.Equal(new[] {-1000,-1000,-1000,-1000,-1000,-5000},
            settings.Where(row => row!["type"]!.GetValue<int>() == 25).Select(row => row!["cost"]!.GetValue<int>()));
        Assert.Equal(new[] {-120,-80,-120},
            settings.Where(row => row!["type"]!.GetValue<int>() == 22).Select(row => row!["cost"]!.GetValue<int>()));
    }

    [CorpusFact("SPELLS.STD", "MAGIC.DEF")]
    public void ReportsTheRealCorpusCountsAndLeavesUnresolvedLinksLegible()
    {
        string spellPath = TestData.Corpus("SPELLS.STD");
        string magicPath = TestData.Corpus("MAGIC.DEF");

        Arena2MagicCatalogPublication publication = Arena2MagicCatalogDocument.Build(
            File.ReadAllBytes(spellPath), File.ReadAllBytes(magicPath), "arena2/SPELLS.STD", "arena2/MAGIC.DEF");
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

    [Fact]
    public void PublishesOneCostRowForEveryEffectVariantASpellUses()
    {
        Arena2MagicCatalogPublication publication = Arena2MagicCatalogDocument.Build(
            SpellTable(), MagicItemTable(), "arena2/SPELLS.STD", "arena2/MAGIC.DEF", Arena2MagicEffectCostTable.Read(Formulas()));
        JsonArray costs = JsonNode.Parse(publication.Json)!["effectCosts"]!.AsArray();

        // Both spells carry one effect without a subtype, so each type resolves through its first slot.
        Assert.Equal([4, 7, 7, 10, 10, 10, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11, 11, 11, 11, 13, 16, 23, 24, 26, 31, 33, 33, 33, 33, 34, 39, 39, 39, 40], costs.Select(cost => cost!["type"]!.GetValue<int>()));
        JsonObject first = costs.Single(cost => cost!["type"]!.GetValue<int>() == 16)!.AsObject();
        Assert.Equal(-1, first["subType"]!.GetValue<int>());
        Assert.Equal(3, first["settingsType"]!.GetValue<int>());
        Assert.Equal("mysticism", first["school"]!.GetValue<string>());
        Assert.Equal(5, first["coefficients"]!["first"]!.GetValue<int>());
        Assert.Equal(25, first["coefficients"]!["second"]!.GetValue<int>());
        JsonObject illusion = costs.Single(cost => cost!["type"]!.GetValue<int>() == 31)!.AsObject();
        Assert.Equal("illusion", illusion["school"]!.GetValue<string>());
        Assert.Equal(2, illusion["coefficients"]!["first"]!.GetValue<int>());

        // Without the donor's tables the catalog states no cost rather than inventing one.
        Assert.Null(JsonNode.Parse(Arena2MagicCatalogDocument.Build(
            SpellTable(), MagicItemTable(), "arena2/SPELLS.STD", "arena2/MAGIC.DEF").Json)!["effectCosts"]);
        Assert.Throws<InvalidOperationException>(() => Arena2MagicEffectCostTable.Read(Formulas()).Resolve(51, -1));
    }

    [CorpusAndDonorFact(["SPELLS.STD", "MAGIC.DEF"], [Arena2MagicEffectCostTable.DonorSourcePath])]
    public void TheRealSpellsResolveEveryEffectThroughTheDonorCostTables()
    {
        Arena2MagicCatalogPublication publication = Arena2MagicCatalogDocument.Build(
            File.ReadAllBytes(TestData.Corpus("SPELLS.STD")), File.ReadAllBytes(TestData.Corpus("MAGIC.DEF")),
            "arena2/SPELLS.STD", "arena2/MAGIC.DEF",
            Arena2MagicEffectCostTable.Read(File.ReadAllText(TestData.Donor(Arena2MagicEffectCostTable.DonorSourcePath))));
        JsonObject document = JsonNode.Parse(publication.Json)!.AsObject();
        HashSet<(int, int)> rows = [.. document["effectCosts"]!.AsArray().Select(cost => (cost!["type"]!.GetValue<int>(), cost["subType"]!.GetValue<int>()))];
        HashSet<(int, int)> used = [.. document["spells"]!.AsArray().SelectMany(spell => spell!["effects"]!.AsArray())
            .Select(effect => (effect!["type"]!.GetValue<int>(), effect["subType"]!.GetValue<int>()))];

        Assert.Equal(89, rows.Count);
        used.Add((2, -1)); used.Add((12, -1));
        used.Add((4, 1));
        used.Add((13, 1)); used.Add((23, 1)); used.Add((24, 1));
        for (int subtype = 0; subtype < 8; subtype++) { used.Add((10, subtype)); used.Add((11, subtype)); }
        for (int subtype = 0; subtype < 3; subtype++) used.Add((39, subtype));
        for (int subtype = 0; subtype < 4; subtype++) used.Add((33, subtype));
        used.Add((34, -1)); used.Add((40, -1));
        Assert.Contains((26, -1), rows);
        used.Add((26, -1));
        used.Add((7, 4)); used.Add((7, 7));
        for (int subtype = 0; subtype < 8; subtype++) Assert.Contains((7, subtype), rows);
        Assert.True(used.SetEquals(rows));
        // Paralysis: settings type 1, alteration, the donor's first coefficient row.
        JsonObject paralysis = document["effectCosts"]!.AsArray()[0]!.AsObject();
        Assert.Equal((0, -1, 1, "alteration"), (paralysis["type"]!.GetValue<int>(), paralysis["subType"]!.GetValue<int>(), paralysis["settingsType"]!.GetValue<int>(), paralysis["school"]!.GetValue<string>()));
        Assert.Equal([7, 25, 7, 25], paralysis["coefficients"]!.AsObject().Select(pair => pair.Value!.GetValue<int>()));
    }

    /// <summary>
    /// A donor-shaped excerpt of the four spell-cost tables covering the two effect types the spell table
    /// uses: type 16 reads coefficient row 1 and type 31 row 2.
    /// </summary>
    [CorpusAndDonorFact(["SPELLS.STD", "MAGIC.DEF"], ["Assets/Scripts/Game/MagicAndEffects/Effects/Enchanting/CastWhenUsed.cs", "Assets/Scripts/Game/MagicAndEffects/Effects/Enchanting/CastWhenHeld.cs", "Assets/Scripts/Game/MagicAndEffects/Effects/Enchanting/CastWhenStrikes.cs"])]
    public void Every_retained_trigger_setting_matches_exact_donor_identity_cost_and_normalized_spell_link()
    {
        var document = JsonNode.Parse(Arena2MagicCatalogDocument.Build(
            File.ReadAllBytes(TestData.Corpus("SPELLS.STD")), File.ReadAllBytes(TestData.Corpus("MAGIC.DEF")), "arena2/SPELLS.STD", "arena2/MAGIC.DEF").Json)!;
        var spells = document["spells"]!.AsArray();
        var settings = document["enchantmentSettings"]!.AsArray();
        foreach (var (type, source, count) in new[] { (0, "CastWhenUsed", 36), (1, "CastWhenHeld", 25), (2, "CastWhenStrikes", 12) })
        {
            var donor = File.ReadAllText(TestData.Donor($"Assets/Scripts/Game/MagicAndEffects/Effects/Enchanting/{source}.cs"));
            int[] ReadArray(string name)
            {
                string body = System.Text.RegularExpressions.Regex.Match(donor, $@"static short\[\] {name}\s*=\s*\{{(.*?)\}}", System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
                return System.Text.RegularExpressions.Regex.Matches(body, @"^\s*(\d+),", System.Text.RegularExpressions.RegexOptions.Multiline)
                    .Select(match => int.Parse(match.Groups[1].Value)).ToArray();
            }
            int[] ids = ReadArray("classicSpellIDs"), costs = ReadArray("classicSpellCosts");
            var rows = settings.Where(value => value!["type"]!.GetValue<int>() == type).ToArray();
            Assert.Equal(count, rows.Length); Assert.Equal(count, ids.Length);
            for (int index = 0; index < ids.Length; index++)
            {
                Assert.Equal(ids[index], rows[index]!["param"]!.GetValue<int>());
                Assert.Equal(costs[index], rows[index]!["cost"]!.GetValue<int>());
                Assert.Equal(ids, rows[index]!["parameterVariants"]!.AsArray().Select(value => value!.GetValue<int>()));
                var spell = spells.First(value => value!["identity"]!.GetValue<int>() == ids[index])!;
                Assert.Equal(spell["key"]!.GetValue<string>(), rows[index]!["spell"]!.GetValue<string>());
                Assert.Equal(spell["identityShared"]!.GetValue<bool>(), rows[index]!["spellIdentityShared"]!.GetValue<bool>());
            }
        }
    }

    private static string Formulas()
    {
        int[] indices = new int[51 * 12];
        indices[16 * 12] = 1;
        indices[31 * 12] = 2;
        int[] settings = new int[51];
        settings[16] = 3;
        settings[31] = 1;
        int[] schools = new int[51];
        schools[16] = 3;
        schools[31] = 5;
        return $$"""
            byte[] effectIndices = { {{string.Join(", ", indices.Select(value => $"0x{value:X2}"))}} };
            ushort[] effectCoefficients = {
                0x07, 0x19, 0x07, 0x19, // first row
                0x05, 0x19, 0x07, 0x1E, // type 16
                0x02, 0x0A, 0x00, 0x00 }; // type 31
            byte[] effectMagicSchools = { {{string.Join(", ", schools)}} };
            byte[] settingsTypes = { {{string.Join(", ", settings)}} };
            """;
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
