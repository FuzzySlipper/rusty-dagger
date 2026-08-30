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
        new("weapons", LevelScaled: false, PoolKind.Weapons),
        new("armor", LevelScaled: false, PoolKind.ArmorAndShields),
        new("creature1", LevelScaled: true, PoolKind.Unsupported),
        new("creature2", LevelScaled: true, PoolKind.Unsupported),
        new("creature3", LevelScaled: false, PoolKind.Unsupported),
        new("plant1", LevelScaled: true, PoolKind.Unsupported),
        new("plant2", LevelScaled: true, PoolKind.Unsupported),
        new("misc1", LevelScaled: false, PoolKind.Unsupported),
        new("misc2", LevelScaled: false, PoolKind.Unsupported),
        new("magic", LevelScaled: false, PoolKind.Unsupported),
        new("clothing", LevelScaled: false, PoolKind.Unsupported),
        new("books", LevelScaled: false, PoolKind.Unsupported),
        new("religious", LevelScaled: false, PoolKind.Unsupported),
    ];

    private const int CategorySlots = 3;

    internal static DaggerfallLootResult Generate(
        DaggerfallDefinitions definitions,
        string tableKey,
        int playerLevel,
        Func<string, int, int, int> draw)
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
            IReadOnlyList<DaggerfallItemDefinition> pool = Pool(definitions, spec.Pool);
            bool supported = pool.Count > 0;
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
                    pick = Draw(draw, PickRollId(tableKey, spec.Name, slot), 0, pool.Count - 1);
                    item = pool[pick.Value].Id.Value;
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

    private static IReadOnlyList<DaggerfallItemDefinition> Pool(DaggerfallDefinitions definitions, PoolKind kind) =>
        kind switch
        {
            PoolKind.Weapons => definitions.Items.Values.Where(item => item.Weapon is not null).OrderBy(item => item.Id.Value).ToArray(),
            PoolKind.ArmorAndShields => definitions.Items.Values.Where(item => item.Armor is not null || item.Shield is not null).OrderBy(item => item.Id.Value).ToArray(),
            _ => [],
        };

    private readonly record struct CategorySpec(string Name, bool LevelScaled, PoolKind Pool);
    private enum PoolKind { Unsupported, Weapons, ArmorAndShields }
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
