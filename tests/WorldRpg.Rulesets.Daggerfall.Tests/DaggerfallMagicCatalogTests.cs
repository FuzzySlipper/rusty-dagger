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

        Assert.Contains("CNT-012", definitions.Magic.SourceRecords);
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

    [Fact]
    public void RejectsACatalogThatDisagreesWithItself()
    {
        // A link to a spell the catalog does not define, and a record that claims an identity is unique
        // while another record carries it, are both refused rather than resolved silently.
        JsonObject payload = JsonNode.Parse(PayloadJson())!.AsObject();
        JsonObject link = payload["magic"]!["magicItems"]!.AsArray()[0]!["enchantments"]!.AsArray()[0]!.AsObject();
        link["spell"] = "spell.999";
        JsonObject shared = payload["magic"]!["spells"]!.AsArray()
            .First(spell => spell!["identityShared"]!.GetValue<bool>())!.AsObject();
        shared["identityShared"] = false;
        DaggerfallContentException failure = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString())));

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
        JsonObject payload = JsonNode.Parse(PayloadJson())!.AsObject();
        Assert.True(payload.Remove("magic"), "the published payload carries no magic section to remove");
        DaggerfallContentException failure = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString())));

        Assert.True(
            failure.Diagnostics.Any(message => message.Contains("no magic catalog section", StringComparison.Ordinal)),
            $"the missing catalog was not named: {string.Join(" | ", failure.Diagnostics)}");
    }

    private static byte[] Payload() => System.Text.Encoding.UTF8.GetBytes(PayloadJson());

    private static string PayloadJson() => File.ReadAllText(Path.Combine(
        RepositoryRoot(), "content", "worldrpg", "payloads", "daggerfall.base.json"));

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }
}
