using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>
/// Compiled Daggerfall loot-table policy.  The caller supplies the Engine
/// Random keyed-roll function; this policy owns only table interpretation,
/// category ordering, and the donor's bounded geometric generation.
/// </summary>
internal static class DaggerfallLootPolicy
{
    private static readonly IReadOnlyList<CategorySpec> Categories =
    [
        new("weapons", LevelScaled: false, "Weapons"),
        new("armor", LevelScaled: false, "Armor"),
        new("creature1", LevelScaled: true, "CreatureIngredients1"),
        new("creature2", LevelScaled: true, "CreatureIngredients2"),
        new("creature3", LevelScaled: false, "CreatureIngredients3"),
        new("plant1", LevelScaled: true, "PlantIngredients1"),
        new("plant2", LevelScaled: true, "PlantIngredients2"),
        new("misc1", LevelScaled: false, "MiscellaneousIngredients1"),
        new("misc2", LevelScaled: false, "MiscellaneousIngredients2"),
        new("magic", LevelScaled: false, Mode: PoolMode.Magic),
        new("clothing", LevelScaled: false, Mode: PoolMode.PlayerClothing),
        new("books", LevelScaled: false, "Books"),
        new("religious", LevelScaled: false, "ReligiousItems"),
    ];

    private const int CategorySlots = 3;

    internal static DaggerfallLootResult Generate(
        DaggerfallDefinitions definitions,
        string tableKey,
        int playerLevel,
        Func<string, int, int, int> draw,
        string? clothingGroup = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableKey);
        ArgumentNullException.ThrowIfNull(draw);
        if (playerLevel < 1) throw new ArgumentOutOfRangeException(nameof(playerLevel));
        if (!definitions.LootTables.TryGetValue(tableKey, out DaggerfallLootTableDefinition? table))
            throw new ArgumentException($"Unknown Daggerfall loot table '{tableKey}'.", nameof(tableKey));

        List<DaggerfallLootDrop> drops = [];
        int? goldRoll = null;
        if (table.MinimumGold != 0 || table.MaximumGold != 0)
        {
            goldRoll = Draw(draw, GoldRollId(tableKey), table.MinimumGold, table.MaximumGold);
            int gold = checked(goldRoll.Value * playerLevel);
            if (gold > 0) drops.Add(new("gold-piece", gold, "gold"));
        }

        List<DaggerfallLootCategoryResult> categoryResults = [];
        foreach (CategorySpec spec in Categories)
        {
            int chance = table.Categories.TryGetValue(spec.Name, out int value) ? value : 0;
            if (chance == 0) continue;
            int effectiveChance = spec.LevelScaled ? checked(chance * playerLevel) : chance;
            IReadOnlyList<DaggerfallItemDefinition> pool = Pool(definitions, spec.Pool, clothingGroup);
            IReadOnlyList<string> magic = spec.Mode == PoolMode.Magic
                ? definitions.Magic.MagicItems.Values.Where(item => item.Type == 0).OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key).ToArray()
                : [];
            int poolCount = spec.Mode == PoolMode.Magic ? magic.Count : pool.Count;
            bool supported = poolCount > 0;
            List<DaggerfallLootRoll> rolls = [];
            int slotChance = effectiveChance;
            for (int slot = 0; slot < CategorySlots; slot++)
            {
                int roll = Draw(draw, SuccessRollId(tableKey, spec.Name, slot), 0, 99);
                bool success = roll < slotChance;
                string? item = null;
                int? pick = null;
                if (success && supported)
                {
                    pick = Draw(draw, PickRollId(tableKey, spec.Name, slot), 0, poolCount - 1);
                    item = spec.Mode == PoolMode.Magic ? magic[pick.Value] : pool[pick.Value].Id.Value;
                    drops.Add(new(item, 1, spec.Name));
                }
                rolls.Add(new(slot, slotChance, roll, success, pick, item));
                if (!success) break;
                slotChance /= 2;
            }
            categoryResults.Add(new(spec.Name, chance, effectiveChance, supported, rolls));
        }

        return new(tableKey, playerLevel, goldRoll, categoryResults, drops);
    }

    internal static string GoldRollId(string tableKey) => $"loot.{tableKey}.gold";
    internal static string SuccessRollId(string tableKey, string category, int slot) => $"loot.{tableKey}.{category}.{slot}";
    internal static string PickRollId(string tableKey, string category, int slot) => $"{SuccessRollId(tableKey, category, slot)}.pick";

    private static int Draw(Func<string, int, int, int> draw, string id, int minimum, int maximum)
    {
        int value = draw(id, minimum, maximum);
        if (value < minimum || value > maximum) throw new InvalidOperationException($"Random roll '{id}' returned {value}, outside [{minimum}, {maximum}].");
        return value;
    }

    private static IReadOnlyList<DaggerfallItemDefinition> Pool(DaggerfallDefinitions definitions, PoolKind kind, string? clothingGroup)
    {
        if (kind.Mode == PoolMode.TemplateGroup) return TemplateGroup(definitions, kind.Group!);
        if (kind.Mode == PoolMode.PlayerClothing && clothingGroup is "MensClothing" or "WomensClothing")
            return TemplateGroup(definitions, clothingGroup);
        return [];
    }

    private static IReadOnlyList<DaggerfallItemDefinition> TemplateGroup(DaggerfallDefinitions definitions, string group) =>
        definitions.TemplateItems.Values
            // The catalog includes material and magic Engine definitions for save resolution;
            // loot selects a donor template once, then the factory chooses its material/meaning.
            .Where(item => item.Template is { } template
                && item.Id.Value == $"template-{template.Index}"
                && template.Groups.Contains(group, StringComparer.Ordinal))
            .OrderBy(item => item.Template!.Index)
            .ToArray();

    private readonly record struct CategorySpec(string Name, bool LevelScaled, string? TemplateGroup = null, PoolMode Mode = PoolMode.TemplateGroup)
    {
        internal PoolKind Pool => Mode == PoolMode.PlayerClothing ? new(PoolMode.PlayerClothing, null) : TemplateGroup is null ? new(PoolMode.Unsupported, null) : new(PoolMode.TemplateGroup, TemplateGroup);
    }

    private readonly record struct PoolKind(PoolMode Mode, string? Group);

    private enum PoolMode { Unsupported, TemplateGroup, PlayerClothing, Magic }
}

internal sealed record DaggerfallLootResult(
    string TableKey,
    int PlayerLevel,
    int? GoldRoll,
    IReadOnlyList<DaggerfallLootCategoryResult> Categories,
    IReadOnlyList<DaggerfallLootDrop> Drops);

internal sealed record DaggerfallLootCategoryResult(
    string Category,
    int Chance,
    int EffectiveChance,
    bool Supported,
    IReadOnlyList<DaggerfallLootRoll> Rolls);

internal sealed record DaggerfallLootRoll(
    int Slot,
    int Chance,
    int Roll,
    bool Success,
    int? Pick,
    string? Item);

internal sealed record DaggerfallLootDrop(string ItemId, int Quantity, string SourceCategory);
