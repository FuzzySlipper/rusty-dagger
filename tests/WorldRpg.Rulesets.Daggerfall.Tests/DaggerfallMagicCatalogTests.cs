using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published magical catalogs are read from the pack, so a spell and an item's enchantment link
/// resolve without the source corpus being present, and a catalog that disagrees with itself fails loudly
/// instead of resolving the wrong spell.
/// </summary>
public sealed class DaggerfallMagicCatalogTests
{
    [Fact]
    public void ResolvesASpellAndAnItemLinkFromThePublishedPackAlone()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(Payload());

        Assert.Contains("arena2/SPELLS.STD", definitions.Magic.SourcePaths);
        DaggerfallSpellDefinition spell = Assert.Contains("spell.001", definitions.Magic.Spells);
        Assert.Equal("Fenrik's Door Jam", spell.Name);
        Assert.Equal(1, spell.Identity);
        Assert.False(spell.IdentityShared);
        Assert.Equal(4, spell.Element);
        DaggerfallSpellEffectDefinition effect = Assert.Single(spell.Effects);
        Assert.Equal(16, effect.Type);
        Assert.Equal(-1, effect.SubType);
        // The source's own duration, chance and magnitude triples reach the consumer intact.
        Assert.Equal((1, 1, 25), (effect.DurationBase, effect.DurationMod, effect.DurationPerLevel));
        Assert.Equal((6, 1, 10), (effect.ChanceBase, effect.ChanceMod, effect.ChancePerLevel));
        Assert.Equal(1, effect.MagnitudeBaseLow);

        // The corpus repeats one identity byte; both records that claim it say so, so a consumer knows
        // the identity alone cannot address them.
        DaggerfallSpellDefinition[] shared = [.. definitions.Magic.Spells.Values.Where(candidate => candidate.IdentityShared)];
        Assert.Equal(2, shared.Length);
        Assert.Single(shared.Select(candidate => candidate.Identity).Distinct());

        // An item enchantment resolves to the key of the spell it names, through the pack alone.
        DaggerfallMagicEnchantmentDefinition link = definitions.Magic.ResolvedLinks.First();
        DaggerfallSpellDefinition linked = Assert.Contains(link.SpellKey!, definitions.Magic.Spells);
        Assert.Equal("cast-when-used", link.ParamMeaning);
        Assert.True(linked.IdentityShared == link.SpellIdentityShared, "an ambiguous identity must be flagged on the link as well as on the spell");
        Assert.Contains(definitions.Magic.MagicItems.Values, item => item.Enchantments.Contains(link));
        // The first template is an artifact whose enchantment names no spell at all: its parameter means
        // an artifact effect, and the catalog publishes that instead of a link.
        DaggerfallMagicItemDefinition first = Assert.Contains("magic-item.0000", definitions.Magic.MagicItems);
        Assert.Equal("The Masque of Clavicus", first.Name);
        Assert.Equal("artifact-effect", Assert.Single(first.Enchantments).ParamMeaning);
        Assert.Null(Assert.Single(first.Enchantments).SpellKey);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Rejects_trigger_content_without_a_normalized_spell_link(bool setting)
    {
        var payload = TestPayload.Sections("magic");
        JsonNode row = setting
            ? payload["magic"]!["enchantmentSettings"]!.AsArray().First(value => value!["type"]!.GetValue<int>() is 0 or 1 or 2)!
            : payload["magic"]!["magicItems"]!.AsArray().SelectMany(value => value!["enchantments"]!.AsArray())
                .First(value => value!["type"]!.GetValue<int>() is 0 or 1 or 2)!;
        row["spell"] = null;
        var failure = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));
        Assert.Contains(failure.Diagnostics, value => value.Contains(setting ? "trigger metadata" : "normalized spell link"));
    }

    [Fact]
    public void RejectsACatalogThatDisagreesWithItself()
    {
        // A link to a spell the catalog does not define, and a record that claims an identity is unique
        // while another record carries it, are both refused rather than resolved silently.
        JsonObject payload = TestPayload.Sections("magic");
        JsonObject link = payload["magic"]!["magicItems"]!.AsArray()[0]!["enchantments"]!.AsArray()[0]!.AsObject();
        link["spell"] = "spell.999";
        JsonObject shared = payload["magic"]!["spells"]!.AsArray()
            .First(spell => spell!["identityShared"]!.GetValue<bool>())!.AsObject();
        shared["identityShared"] = false;
        DaggerfallContentException failure = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));

        Assert.True(
            failure.Diagnostics.Any(message => message.Contains("'spell.999'", StringComparison.Ordinal) && message.Contains("does not define", StringComparison.Ordinal)),
            $"the dangling link was not named: {string.Join(" | ", failure.Diagnostics)}");
        Assert.True(
            failure.Diagnostics.Any(message => message.Contains("reports identity", StringComparison.Ordinal) && message.Contains("more than one record", StringComparison.Ordinal)),
            $"the identity disagreement was not named: {string.Join(" | ", failure.Diagnostics)}");
    }

    [Fact]
    public void RefusesAPayloadThatPublishesNoMagicCatalog()
    {
        // No spell can resolve through a payload that carries no catalog, so the loss is named where the
        // payload is read rather than surfacing later as an empty resolution.
        JsonObject payload = TestPayload.Sections("magic");
        // A magic value that is not a catalog object is refused by the same check as an absent one.
        Assert.True(payload.ContainsKey("magic"), "the published payload carries no magic section to replace");
        payload["magic"] = null;
        DaggerfallContentException failure = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));

        Assert.True(
            failure.Diagnostics.Any(message => message.Contains("no magic catalog section", StringComparison.Ordinal)),
            $"the missing catalog was not named: {string.Join(" | ", failure.Diagnostics)}");
    }

    [Fact]
    public void Settings_are_loaded_with_display_text_variants_and_donor_provenance()
    {
        DaggerfallMagicCatalogSet magic = TestPayload.Definitions.Magic;
        foreach ((int type, int count) in new[] { (10, 35), (3, 11), (7, 2), (13, 3) })
        {
            DaggerfallEnchantmentSetting[] family = [.. magic.EnchantmentSettings.Values.Where(row => row.Type == type)];
            Assert.Equal(count, family.Length);
            Assert.All(family, row =>
            {
                Assert.Equal(Enumerable.Range(0, count), row.ParameterVariants);
                Assert.False(string.IsNullOrWhiteSpace(row.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(row.ParameterTextKey));
                Assert.Contains($"/{row.TextKey}.cs#GetEnchantmentSettings", row.SourceClass, StringComparison.Ordinal);
            });
        }
        DaggerfallEnchantmentSetting winter = magic.EnchantmentSettings["enchantment.3.0"];
        Assert.Equal((500, "duringWinter", "Extra spell pts: during Winter"),
            (winter.Cost, winter.ParameterTextKey, winter.DisplayName));
    }

    [Theory]
    [InlineData("param")]
    [InlineData("meaning")]
    [InlineData("duplicate")]
    [InlineData("parameterVariants")]
    public void Malformed_published_settings_fail_at_content_admission(string field)
    {
        JsonObject payload = TestPayload.Sections("magic");
        JsonArray settings = payload["magic"]!["enchantmentSettings"]!.AsArray();
        JsonObject row = settings.First(value => value!["type"]!.GetValue<int>() == 3)!.AsObject();
        if (field == "param") { row["param"] = 11; row["key"] = "enchantment.3.11"; }
        else if (field == "meaning") row["meaning"] = "unresolved";
        else if (field == "parameterVariants") row["parameterVariants"] = new JsonArray(0, 11);
        else settings.Add(row.DeepClone());
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallBaseContent.Read(TestPayload.Splice(payload)));
        Assert.Contains(error.Diagnostics, message => message.Contains("Enchantment setting", StringComparison.Ordinal));
    }

    [Fact]
    public void Normalized_custom_offer_requires_explicit_source_sale_eligibility_and_keeps_classic_rows()
    {
        var payload = TestPayload.Sections("magic");
        var spells = payload["magic"]!["spells"]!.AsArray();
        var source = spells[0]!.DeepClone();
        var entry = source["effects"]![0]!.DeepClone(); entry["type"] = 26; entry["subType"] = -1;
        source["effects"] = new JsonArray(entry);
        source["key"] = "authored.custom.freedom"; source["identity"] = -1; source["identityShared"] = false;
        source["name"] = "Authored freedom"; source["isCustom"] = true; source["spellsForSale"] = true;
        var effect = source["effects"]![0]!;
        source["element"] = 4; source["rangeType"] = 0; source["icon"] = 1;
        effect["duration"] = new JsonObject { ["base"] = 1, ["mod"] = 1, ["perLevel"] = 1 };
        spells.Add(source);
        var privateRow = source.DeepClone(); privateRow["key"] = "authored.custom.private";
        privateRow.AsObject().Remove("spellsForSale"); spells.Add(privateRow);
        var definitions = DaggerfallBaseContent.Read(TestPayload.Splice(payload));
        var session = definitions.ForSession([]);
        Assert.True(session.Magic.Spells["authored.custom.freedom"].SpellsForSale);
        Assert.False(session.Magic.Spells["authored.custom.private"].SpellsForSale);
        Assert.False(session.Magic.Spells["authored.custom.freedom"].IsPlayerCreated);
        Assert.True(session.Magic.Spells["spell.023"].SpellsForSale);
    }

    private static byte[] Payload() => TestPayload.CombinedBytes;
}
