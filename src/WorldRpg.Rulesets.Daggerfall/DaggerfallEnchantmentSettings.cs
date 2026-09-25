using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// One enchantment an item maker can put on an item: the donor's classic type and param, its enchant
/// cost, and what that param means for that type.
/// </summary>
internal readonly record struct DaggerfallEnchantmentSetting(string Key, int Type, int Param, int Cost, string Meaning);

/// <summary>
/// The enchantment settings the classic item maker offers. The donor enumerates them from each effect
/// class's own table rather than publishing them alongside MAGIC.DEF's pre-generated items, so an item
/// can hold a payload that no published magic item carries — the held stat, weight, talent and
/// condition payloads among them. They are classic fixed data recorded by the donor classes, not
/// authored pack values, which is the same disposition the disease matrix has: the ruleset owns the
/// table and a worn item names one of its keys exactly as it names a magic item.
/// </summary>
internal static class DaggerfallEnchantmentSettings
{
    internal const int RegeneratesHealthType = 5;
    internal const int ExtraSpellPointsType = 3;
    internal const int IncreasedWeightAllowanceType = 7;
    internal const int EnhancesSkillType = 10;
    internal const int StrengthensArmorType = 12;
    internal const int ImprovesTalentsType = 13;

    /// <summary>Keys a worn item can name, in the donor's own enumeration order.</summary>
    internal const string KeyPrefix = "enchantment";

    private static readonly string[] ClassicSkillIds =
    [
        "medical", "etiquette", "streetwise", "jumping", "orcish", "harpy", "giantish", "dragonish",
        "nymph", "daedric", "spriggan", "centaurian", "impish", "lockpicking", "mercantile",
        "pickpocket", "stealth", "swimming", "climbing", "backstabbing", "dodging", "running",
        "destruction", "restoration", "illusion", "alteration", "thaumaturgy", "mysticism",
        "short-blade", "long-blade", "hand-to-hand", "axe", "blunt-weapon", "archery", "critical-strike",
    ];

    // ExtraSpellPts params in the donor's order: the four seasons, the three moon phases, then the
    // four creature groups, each with the donor's own cost.
    private static readonly (string Meaning, int Cost)[] SpellPointParams =
    [
        ("during-winter", 500), ("during-spring", 500), ("during-summer", 500), ("during-fall", 500),
        ("during-full-moon", 200), ("during-half-moon", 200), ("during-new-moon", 200),
        ("near-undead", 700), ("near-daedra", 800), ("near-humanoids", 900), ("near-animals", 1000),
    ];

    private static readonly (string Meaning, int Cost)[] WeightParams =
    [
        ("one-quarter-more", 400), ("one-half-more", 600),
    ];

    // RegensHealth names when it regenerates, and StrengthensArmor has a single unnamed setting — the
    // donor publishes it with classic param -1.
    private static readonly (string Meaning, int Cost)[] RegenerationParams =
    [
        ("all-the-time", 4000), ("in-sunlight", 3000), ("in-darkness", 3000),
    ];

    private static readonly (string Meaning, int Cost)[] TalentParams =
    [
        ("improved-acute-hearing", 500), ("improved-athleticism", 600), ("improved-adrenaline-rush", 600),
    ];

    private static readonly Dictionary<string, DaggerfallEnchantmentSetting> ByKey = Build();

    /// <summary>Every setting the item maker offers, in a stable order.</summary>
    internal static IReadOnlyList<DaggerfallEnchantmentSetting> All { get; } = [.. ByKey.Values];

    /// <summary>The setting a worn item's enchantment key names, when it names one of these.</summary>
    internal static bool TryResolve(string key, out DaggerfallEnchantmentSetting setting) =>
        ByKey.TryGetValue(key, out setting);

    /// <summary>
    /// The effect payload a setting applies while its item is worn, shaped exactly like the enchantment
    /// a published magic item carries so the held owner needs no second path.
    /// </summary>
    internal static DaggerfallMagicEnchantmentDefinition ToEffect(DaggerfallEnchantmentSetting setting) => new(
        $"{setting.Key}.enchantment.1",
        setting.Type,
        setting.Param,
        setting.Meaning,
        SpellKey: null,
        SpellIdentityShared: false);

    private static Dictionary<string, DaggerfallEnchantmentSetting> Build()
    {
        Dictionary<string, DaggerfallEnchantmentSetting> settings = new(StringComparer.Ordinal);
        // EnhancesSkill names a skill by its classic index; the donor prices every one of them alike.
        for (int param = 0; param < ClassicSkillIds.Length; param++)
            Add(EnhancesSkillType, param, 900, ClassicSkillIds[param]);
        for (int param = 0; param < SpellPointParams.Length; param++)
            Add(ExtraSpellPointsType, param, SpellPointParams[param].Cost, SpellPointParams[param].Meaning);
        for (int param = 0; param < RegenerationParams.Length; param++)
            Add(RegeneratesHealthType, param, RegenerationParams[param].Cost, RegenerationParams[param].Meaning);
        Add(StrengthensArmorType, -1, 700, "strengthened-armor");
        for (int param = 0; param < WeightParams.Length; param++)
            Add(IncreasedWeightAllowanceType, param, WeightParams[param].Cost, WeightParams[param].Meaning);
        for (int param = 0; param < TalentParams.Length; param++)
            Add(ImprovesTalentsType, param, TalentParams[param].Cost, TalentParams[param].Meaning);
        return settings;

        void Add(int type, int param, int cost, string meaning)
        {
            DaggerfallEnchantmentSetting setting = new($"{KeyPrefix}.{type}.{param}", type, param, cost, meaning);
            settings.Add(setting.Key, setting);
        }
    }
}
