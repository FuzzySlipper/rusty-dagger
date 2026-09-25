using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// One enchantment an item maker can put on an item: the donor's classic type and param, its enchant
/// cost, and what that param means for that type.
/// </summary>
internal readonly record struct DaggerfallEnchantmentSetting(string Key, int Type, int Param, int Cost, string Meaning);

/// <summary>
/// The enchantment settings this project's held and condition work needs. The donor enumerates its full
/// item-maker offering from each effect class's own table rather than publishing it alongside MAGIC.DEF's
/// pre-generated items, so an item can hold a payload that no published magic item carries — the held
/// stat, weight, talent, regeneration, armor-strength and condition payloads among them. They are classic
/// fixed data recorded by the donor classes, not authored pack values, which is the same disposition the
/// disease matrix has: the ruleset owns the table and a worn item names one of its keys exactly as it
/// names a magic item.
/// </summary>
/// <remarks>
/// This is not the donor's complete offering: its cast-when-*, potent-versus, low-damage-versus,
/// health-leech, soul-bound, feather-weight, extra-weight and reputation tables are not enumerated here,
/// because no task has needed them yet. Their costs are still quotable where the cost policy retains
/// them (see <c>DaggerfallMagicCostPolicy.TryGetNonSpellEnchantmentCost</c>), and a further family
/// belongs here only when a consumer asks for it.
/// </remarks>
internal static class DaggerfallEnchantmentSettings
{
    internal const int RegeneratesHealthType = 5;
    internal const int ExtraSpellPointsType = 3;
    internal const int IncreasedWeightAllowanceType = 7;
    internal const int RepairsObjectsType = 8;
    internal const int EnhancesSkillType = 10;
    internal const int StrengthensArmorType = 12;
    internal const int ItemDeterioratesType = 16;
    internal const int UserTakesDamageType = 17;
    internal const int WeakensArmorType = 24;
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

    // The four condition payloads: what a worn item costs its owner. The donor prices a detriment
    // negatively — it makes the item cheaper — and an advantage positively.
    private static readonly (string Meaning, int Cost)[] DeteriorationParams =
    [
        ("all-the-time", -3000), ("in-sunlight", -1500), ("in-holy-places", -500),
    ];

    private static readonly (string Meaning, int Cost)[] DamageParams =
    [
        ("in-sunlight", -6000), ("in-holy-places", -1000),
    ];

    private static readonly (string Meaning, int Cost)[] TalentParams =
    [
        ("improved-acute-hearing", 500), ("improved-athleticism", 600), ("improved-adrenaline-rush", 600),
    ];

    private static readonly Dictionary<string, DaggerfallEnchantmentSetting> ByKey = Build();

    private static readonly Dictionary<(int Type, int Param), int> CostsByTypeParam =
        ByKey.Values.ToDictionary(setting => (setting.Type, setting.Param), setting => setting.Cost);

    /// <summary>Every setting the item maker offers, in a stable order.</summary>
    internal static IReadOnlyList<DaggerfallEnchantmentSetting> All { get; } = [.. ByKey.Values];

    /// <summary>
    /// The problems with a settings table, empty when it is sound. The catalog is compiled fixed data, so
    /// this guards a transcription: a key that does not name its own type and param, a type the classic
    /// set does not define, a param below the donor's single-setting sentinel, an empty or duplicated
    /// type-and-param, or a cost of nothing at all.
    /// </summary>
    internal static IReadOnlyList<string> Validate(IEnumerable<DaggerfallEnchantmentSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        List<string> problems = [];
        HashSet<(int Type, int Param)> seen = [];
        foreach (DaggerfallEnchantmentSetting setting in settings)
        {
            if (setting.Key != $"enchantment.{setting.Type}.{setting.Param}")
                problems.Add($"Enchantment setting '{setting.Key}' does not name type {setting.Type} and param {setting.Param}.");
            if (!KnownTypes.Contains(setting.Type))
                problems.Add($"Enchantment setting '{setting.Key}' names classic type {setting.Type}, which this table does not define.");
            if (setting.Param < -1)
                problems.Add($"Enchantment setting '{setting.Key}' names param {setting.Param}, below the donor's single-setting sentinel.");
            if (string.IsNullOrWhiteSpace(setting.Meaning))
                problems.Add($"Enchantment setting '{setting.Key}' has no param meaning.");
            if (setting.Cost == 0)
                problems.Add($"Enchantment setting '{setting.Key}' costs nothing.");
            if (!seen.Add((setting.Type, setting.Param)))
                problems.Add($"Enchantment setting '{setting.Key}' repeats type {setting.Type} and param {setting.Param}.");
        }
        return problems;
    }

    /// <summary>The classic enchantment types this table carries.</summary>
    private static readonly HashSet<int> KnownTypes =
    [
        RegeneratesHealthType, ExtraSpellPointsType, IncreasedWeightAllowanceType, RepairsObjectsType,
        EnhancesSkillType, StrengthensArmorType, ImprovesTalentsType, ItemDeterioratesType,
        UserTakesDamageType, WeakensArmorType,
    ];

    /// <summary>The donor's enchant cost for a classic payload the item maker offers.</summary>
    internal static bool TryCost(int type, int param, out int cost) =>
        CostsByTypeParam.TryGetValue((type, param), out cost);

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
        Add(RepairsObjectsType, -1, 900, "repairs-objects");
        Add(WeakensArmorType, -1, -700, "weakened-armor");
        for (int param = 0; param < DeteriorationParams.Length; param++)
            Add(ItemDeterioratesType, param, DeteriorationParams[param].Cost, DeteriorationParams[param].Meaning);
        for (int param = 0; param < DamageParams.Length; param++)
            Add(UserTakesDamageType, param, DamageParams[param].Cost, DamageParams[param].Meaning);
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
