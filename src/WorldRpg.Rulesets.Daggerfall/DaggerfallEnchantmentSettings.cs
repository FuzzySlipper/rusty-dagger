using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>A loaded item-maker setting with its donor meaning, display and source.</summary>
internal readonly record struct DaggerfallEnchantmentSetting(
    string Key, int Type, int Param, int Cost, string Meaning, string DisplayName, string TextKey,
    string? ParameterTextKey, string SourceClass, IReadOnlyList<int> ParameterVariants, string? SpellKey = null, bool SpellIdentityShared = false);

/// <summary>Retained payload vocabulary and content admission; settings themselves come from the pack.</summary>
internal static class DaggerfallEnchantmentSettings
{
    internal const int AbsorbsSpellsType = 9;
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
    internal const int GoodRepWithType = 14;
    internal const int BadReactionsFromType = 22;
    internal const int BadRepWithType = 25;


    internal static IReadOnlyList<string> Validate(IEnumerable<DaggerfallEnchantmentSetting> settings)
    {
        List<string> problems = [];
        HashSet<(int Type, int Param)> seen = [];
        foreach (DaggerfallEnchantmentSetting setting in settings)
        {
            if (setting.Type is 0 or 1 or 2)
            {
                int[] variants = TriggerVariants(setting.Type);
                string meaning = setting.Type == 0 ? "cast-when-used" : setting.Type == 1 ? "cast-when-held" : "cast-when-strikes";
                if (!variants.Contains(setting.Param) || !setting.ParameterVariants.SequenceEqual(variants)
                    || setting.Meaning != meaning || setting.Cost <= 0 || setting.Key != $"enchantment.{setting.Type}.{setting.Param}"
                    || string.IsNullOrWhiteSpace(setting.DisplayName) || string.IsNullOrWhiteSpace(setting.SourceClass)
                    || string.IsNullOrWhiteSpace(setting.TextKey) || string.IsNullOrWhiteSpace(setting.ParameterTextKey))
                    problems.Add($"Enchantment setting '{setting.Key}' has invalid classic spell trigger metadata.");
                if (!seen.Add((setting.Type, setting.Param))) problems.Add($"Enchantment setting '{setting.Key}' is duplicated.");
                continue;
            }
            string[] meanings = Meanings(setting.Type);
            int firstParam = setting.Type is 8 or 9 or 12 or 24 ? -1 : 0;
            int index = setting.Param - firstParam;
            if (setting.Key != $"enchantment.{setting.Type}.{setting.Param}")
                problems.Add($"Enchantment setting '{setting.Key}' does not name type {setting.Type} and param {setting.Param}.");
            if (meanings.Length == 0)
                problems.Add($"Enchantment setting '{setting.Key}' names classic type {setting.Type}, which this table does not define.");
            else if (index < 0 || index >= meanings.Length)
                problems.Add($"Enchantment setting '{setting.Key}' names invalid param {setting.Param} for classic type {setting.Type}.");
            else if (setting.Meaning != meanings[index])
                problems.Add($"Enchantment setting '{setting.Key}' has no resolved param meaning: expected '{meanings[index]}', observed '{setting.Meaning}'.");
            if (setting.Param < -1)
                problems.Add($"Enchantment setting '{setting.Key}' is below the donor's single-setting sentinel.");
            if (string.IsNullOrWhiteSpace(setting.Meaning))
                problems.Add($"Enchantment setting '{setting.Key}' has no param meaning.");
            if (setting.Cost == 0)
                problems.Add($"Enchantment setting '{setting.Key}' costs nothing.");
            if (!seen.Add((setting.Type, setting.Param)))
                problems.Add($"Enchantment setting '{setting.Key}' repeats type {setting.Type} and param {setting.Param}.");
            if (!setting.ParameterVariants.SequenceEqual(Enumerable.Range(firstParam, meanings.Length)))
                problems.Add($"Enchantment setting '{setting.Key}' has an unresolved parameter variant list.");
            if (string.IsNullOrWhiteSpace(setting.DisplayName) || string.IsNullOrWhiteSpace(setting.TextKey)
                || (firstParam == 0 && string.IsNullOrWhiteSpace(setting.ParameterTextKey))
                || string.IsNullOrWhiteSpace(setting.SourceClass))
                problems.Add($"Enchantment setting '{setting.Key}' has no display text, text key or donor source class.");
        }
        return problems;
    }

    // These meanings describe the payloads the held/condition owners implement. They validate loaded
    // type/param pairs independently of the publication's own parameterVariants list.
    private static string[] Meanings(int type) => type switch
    {
        10 => ["medical", "etiquette", "streetwise", "jumping", "orcish", "harpy", "giantish", "dragonish", "nymph", "daedric", "spriggan", "centaurian", "impish", "lockpicking", "mercantile", "pickpocket", "stealth", "swimming", "climbing", "backstabbing", "dodging", "running", "destruction", "restoration", "illusion", "alteration", "thaumaturgy", "mysticism", "short-blade", "long-blade", "hand-to-hand", "axe", "blunt-weapon", "archery", "critical-strike"],
        3 => ["during-winter", "during-spring", "during-summer", "during-fall", "during-full-moon", "during-half-moon", "during-new-moon", "near-undead", "near-daedra", "near-humanoids", "near-animals"],
        5 => ["all-the-time", "in-sunlight", "in-darkness"],
        9 => ["spell-absorption"],
        12 => ["strengthened-armor"],
        8 => ["repairs-objects"],
        24 => ["weakened-armor"],
        16 => ["all-the-time", "in-sunlight", "in-holy-places"],
        17 => ["in-sunlight", "in-holy-places"],
        7 => ["one-quarter-more", "one-half-more"],
        13 => ["improved-acute-hearing", "improved-athleticism", "improved-adrenaline-rush"],
        14 or 25 => ["commoners", "merchants", "scholars", "nobility", "underworld", "all"],
        22 => ["from-humanoids", "from-animals", "from-daedra"],
        _ => [],
    };

    private static int[] TriggerVariants(int type) => type switch
    {
        0 => [4, 5, 6, 7, 8, 9, 10, 18, 11, 12, 13, 19, 14, 15, 16, 17, 22, 23, 24, 20, 25, 26, 33, 27, 28, 29, 34, 30, 31, 35, 36, 32, 40, 64, 60, 94],
        1 => [37, 39, 41, 10, 42, 11, 12, 26, 13, 6, 44, 45, 46, 24, 47, 4, 49, 82, 83, 84, 85, 86, 87, 88, 89],
        2 => [50, 53, 52, 54, 56, 33, 20, 25, 16, 7, 55, 67],
        _ => [],
    };

    internal static DaggerfallMagicEnchantmentDefinition ToEffect(DaggerfallEnchantmentSetting setting) => new(
        $"{setting.Key}.enchantment.1",
        setting.Type,
        setting.Param,
        setting.Meaning,
        SpellKey: setting.SpellKey,
        SpellIdentityShared: setting.SpellIdentityShared);

}
