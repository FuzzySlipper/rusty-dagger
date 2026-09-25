using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallMagicCostPolicyTests
{
    [Fact]
    public void Published_spell_effects_have_explicit_donor_cost_rows_and_quote_with_classic_ordering()
    {
        DaggerfallDefinitions definitions = Load();
        (int Type, int SubType)[] retainedEffects = definitions.Magic.Spells.Values.SelectMany(spell => spell.Effects)
            .Select(effect => (effect.Type, effect.SubType)).Distinct().Order().ToArray();
        Assert.Equal(60, retainedEffects.Length);
        Assert.Equal(retainedEffects, definitions.Magic.EffectCosts.Keys.Order().ToArray());

        DaggerfallSpellDefinition doorJam = definitions.Magic.Spells["spell.001"];
        Assert.Equal(28, DaggerfallMagicCostPolicy.QuoteCasting(definitions.Magic, doorJam, new Dictionary<string, int>(), enchantingItem: true).SpellPoints);
        Assert.Equal(280, DaggerfallMagicCostPolicy.SpellEnchantPoints(definitions.Magic, doorJam));
    }

    [Fact]
    public void Target_and_material_multipliers_preserve_classic_truncation_and_bounds()
    {
        Assert.Equal(new DaggerfallMagicCost(7, 7), DaggerfallMagicCostPolicy.ApplyTargetMultiplier(new(3, 3), DaggerfallSpellTarget.AreaAtRange));
        Assert.Equal(0, DaggerfallMagicCostPolicy.ApplyTargetMultiplier(new(0, 0), DaggerfallSpellTarget.CasterOnly).SpellPoints);
        foreach ((string material, int quarter) in new[]
        {
            ("iron", -1), ("steel", 0), ("silver", 3), ("elven", 1), ("dwarven", 2),
            ("mithril", 1), ("adamantium", 3), ("ebony", 4), ("orcish", 6), ("daedric", 8),
        })
        {
            Assert.Equal(quarter, DaggerfallMagicCostPolicy.WeaponEnchantmentMultiplierQuarter(material));
            Assert.Equal(quarter, DaggerfallMagicCostPolicy.ArmorEnchantmentMultiplierQuarter(material));
        }
    }

    [Fact]
    public void Regular_effect_costs_and_classic_spell_costs_keep_their_distinct_truncation_orders()
    {
        DaggerfallSpellEffectDefinition effect = Effect(durationBase: 2, durationMod: 7, durationPerLevel: 3,
            magnitudeBaseLow: 3, magnitudeBaseHigh: 8, magnitudeLevelBase: 2, magnitudeLevelHigh: 9, magnitudePerLevel: 4);
        DaggerfallMagicEffectCostDefinition row = new(99, -1, 6, "destruction", 11, 13, 17, 19,
            new(new(22, 11, 13), new(3, 17, 19), new(5, 7, 23)));
        DaggerfallMagicCatalogSet catalog = Catalog(effect, row);
        IReadOnlyDictionary<string, int> skills = new Dictionary<string, int> { ["destruction"] = 50 };
        DaggerfallSpellDefinition spell = new("test", 0, false, "Test", 0, 4, 0, 0, [effect]);

        Assert.Equal(new DaggerfallMagicCost(172, 25), DaggerfallMagicCostPolicy.CalculateEffectCosts(catalog, effect, skills));
        Assert.Equal(new DaggerfallMagicCost(430, 62), DaggerfallMagicCostPolicy.CalculateTotalEffectCosts(catalog, [effect], DaggerfallSpellTarget.AreaAtRange, skills));
        Assert.Equal(235, DaggerfallMagicCostPolicy.QuoteCasting(catalog, spell, skills).SpellPoints);
        Assert.Equal(2350, DaggerfallMagicCostPolicy.SpellEnchantPoints(catalog, spell));
    }

    [Fact]
    public void Settings_type_seven_uses_the_donors_left_to_right_integer_division_order()
    {
        DaggerfallSpellEffectDefinition effect = Effect(durationBase: 2, durationMod: 7,
            magnitudeBaseLow: 3, magnitudeBaseHigh: 8, magnitudeLevelBase: 2, magnitudeLevelHigh: 9, magnitudePerLevel: 4);
        DaggerfallMagicCatalogSet catalog = Catalog(effect, new(99, -1, 7, "illusion", 11, 13, 0, 0,
            new(null, null, new(0, 11, 13))));
        IReadOnlyDictionary<string, int> skills = new Dictionary<string, int> { ["illusion"] = 50 };

        Assert.Equal(new DaggerfallMagicCost(68, 10), DaggerfallMagicCostPolicy.CalculateEffectCosts(catalog, effect, skills));
        Assert.Equal(35, DaggerfallMagicCostPolicy.QuoteCasting(catalog,
            new("test", 0, false, "Test", 0, 0, 0, 0, [effect]), skills).SpellPoints);
    }

    [Fact]
    public void Settings_type_seven_divides_the_odd_level_sum_before_multiplying_the_coefficient()
    {
        DaggerfallSpellEffectDefinition effect = Effect(durationBase: 1, durationMod: 2,
            magnitudeBaseLow: 1, magnitudeBaseHigh: 2, magnitudeLevelBase: 1, magnitudeLevelHigh: 2,
            magnitudePerLevel: 2);
        DaggerfallMagicCatalogSet catalog = Catalog(effect, new(99, -1, 7, "illusion", 11, 3, 0, 0));
        IReadOnlyDictionary<string, int> skills = new Dictionary<string, int> { ["illusion"] = 50 };

        // Donor order: 1*11 + 3*3/2/2*1/2 = 12, then 12*60/100 = 7.
        Assert.Equal(7, DaggerfallMagicCostPolicy.QuoteCasting(catalog,
            new("test", 0, false, "Test", 0, 0, 0, 0, [effect]), skills).SpellPoints);
    }

    [Fact]
    public void Invalid_classic_settings_targets_and_elements_are_rejected_before_they_become_payments()
    {
        DaggerfallSpellEffectDefinition zeroDivisor = Effect(durationPerLevel: 0);
        DaggerfallMagicCatalogSet catalog = Catalog(zeroDivisor, new(99, -1, 3, "alteration", 1, 1, 0, 0,
            new(new(0, 1, 1), null, null)));
        IReadOnlyDictionary<string, int> skills = new Dictionary<string, int> { ["alteration"] = 50 };
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallMagicCostPolicy.CalculateEffectCosts(catalog, zeroDivisor, skills));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallMagicCostPolicy.CalculateTotalEffectCosts(catalog, [zeroDivisor], (DaggerfallSpellTarget)99, skills));

        DaggerfallSpellEffectDefinition aboveByte = Effect(durationBase: 256);
        DaggerfallMagicCatalogSet aboveByteCatalog = Catalog(aboveByte, new(99, -1, 3, "alteration", 1, 1, 0, 0,
            new(new(0, 1, 1), null, null)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallMagicCostPolicy.CalculateEffectCosts(aboveByteCatalog, aboveByte, skills));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallMagicCostPolicy.QuoteCasting(Catalog(Effect(), new(99, -1, 3, "alteration", 1, 1, 0, 0)),
            new("test", 0, false, "Test", 5, 0, 0, 0, [Effect()]), skills));
    }

    [Fact]
    public void Every_retained_effect_variant_has_regular_component_metadata_and_uses_zero_component_fudge()
    {
        DaggerfallDefinitions definitions = Load();
        IReadOnlyDictionary<string, int> skills = new Dictionary<string, int>
        {
            ["alteration"] = 50, ["restoration"] = 50, ["destruction"] = 50,
            ["mysticism"] = 50, ["thaumaturgy"] = 50, ["illusion"] = 50,
        };
        foreach (DaggerfallSpellEffectDefinition effect in definitions.Magic.Spells.Values.SelectMany(spell => spell.Effects)
                     .GroupBy(effect => (effect.Type, effect.SubType)).Select(group => group.First()))
        {
            DaggerfallMagicEffectCostDefinition row = definitions.Magic.RequireEffectCost(effect);
            Assert.NotNull(row.RegularComponents);
            bool invalidDivisor = row.RegularComponents.Duration is not null && effect.DurationPerLevel == 0
                || row.RegularComponents.Chance is not null && effect.ChancePerLevel == 0
                || row.RegularComponents.Magnitude is not null && effect.MagnitudePerLevel == 0;
            if (invalidDivisor)
                Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallMagicCostPolicy.CalculateEffectCosts(definitions.Magic, effect, skills));
            else
            {
                DaggerfallMagicCost cost = DaggerfallMagicCostPolicy.CalculateEffectCosts(definitions.Magic, effect, skills);
                Assert.True(cost.Gold >= 0 && cost.SpellPoints >= 0, $"{effect.Type}/{effect.SubType} produced {cost}.");
            }
        }

        DaggerfallSpellEffectDefinition teleport = definitions.Magic.Spells["spell.086"].Effects.Single();
        Assert.Equal(new DaggerfallMagicCost(320, 48), DaggerfallMagicCostPolicy.CalculateEffectCosts(definitions.Magic, teleport, skills));
        DaggerfallSpellEffectDefinition doorJam = definitions.Magic.Spells["spell.001"].Effects.Single();
        Assert.Equal(new DaggerfallMagicCost(288, 43), DaggerfallMagicCostPolicy.CalculateEffectCosts(definitions.Magic, doorJam, skills));
    }

    [Fact]
    public void Item_power_floors_negative_iron_adjustments_and_regular_nonspell_templates_have_explicit_costs()
    {
        DaggerfallDefinitions definitions = Load();
        DaggerfallItemDefinition baseItem = definitions.RequireItem(new DaggerfallItemId("template-113-iron"));
        DaggerfallItemDefinition oddPower = baseItem with { Template = baseItem.Template! with { EnchantmentPoints = 101 } };
        DaggerfallItemInstanceMetadata iron = new(oddPower.Id.Value, "iron", 0, 1, 1, true, false, null, null, null, DaggerfallItemOwner.Player);
        DaggerfallItemDefinition normalPower = definitions.RequireItem(new DaggerfallItemId("template-113-iron"));
        DaggerfallItemDefinition sufficientPower = normalPower with { Template = normalPower.Template! with { EnchantmentPoints = 2000 } };
        DaggerfallItemInstanceMetadata normalIron = iron with { ItemId = normalPower.Id.Value };

        Assert.Equal(75, DaggerfallMagicCostPolicy.ItemEnchantmentPower(oddPower, iron));
        DaggerfallItemDefinition ironBuckler = definitions.RequireItem(new DaggerfallItemId("template-109-iron"));
        DaggerfallItemDefinition daedricBuckler = definitions.RequireItem(new DaggerfallItemId("template-109-daedric"));
        Assert.Equal(375, DaggerfallMagicCostPolicy.ItemEnchantmentPower(ironBuckler,
            iron with { ItemId = ironBuckler.Id.Value }));
        Assert.Equal(1500, DaggerfallMagicCostPolicy.ItemEnchantmentPower(daedricBuckler,
            iron with { ItemId = daedricBuckler.Id.Value, Material = "daedric" }));
        foreach ((string key, int expected) in new[] { ("magic-item.0018", 1000), ("magic-item.0022", 1500), ("magic-item.0029", 900) })
        {
            DaggerfallItemEnchantmentQuote quote = DaggerfallMagicCostPolicy.QuoteItemEnchantment(definitions, sufficientPower, normalIron,
                definitions.Magic.MagicItems[key]);
            Assert.Equal(expected, quote.RequiredPoints);
            Assert.True(quote.Eligible, quote.Reason);
        }
        Assert.False(DaggerfallMagicCostPolicy.QuoteItemEnchantment(definitions, normalPower, normalIron,
            definitions.Magic.MagicItems["magic-item.0000"]).Eligible);
    }

    private static DaggerfallSpellEffectDefinition Effect(int durationBase = 1, int durationMod = 1, int durationPerLevel = 1,
        int chanceBase = 1, int chanceMod = 1, int chancePerLevel = 1, int magnitudeBaseLow = 1, int magnitudeBaseHigh = 1,
        int magnitudeLevelBase = 1, int magnitudeLevelHigh = 1, int magnitudePerLevel = 1) =>
        new("test.effect", 99, -1, durationBase, durationMod, durationPerLevel, chanceBase, chanceMod, chancePerLevel,
            magnitudeBaseLow, magnitudeBaseHigh, magnitudeLevelBase, magnitudeLevelHigh, magnitudePerLevel);

    private static DaggerfallMagicCatalogSet Catalog(DaggerfallSpellEffectDefinition effect, DaggerfallMagicEffectCostDefinition row) =>
        new(new Dictionary<string, DaggerfallSpellDefinition>(), new Dictionary<string, DaggerfallMagicItemDefinition>(), [], [],
            new Dictionary<(int Type, int SubType), DaggerfallMagicEffectCostDefinition> { [(effect.Type, effect.SubType)] = row });

    private static DaggerfallDefinitions Load()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
        return TestPayload.Definitions;
    }
}
