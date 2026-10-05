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
            for (int slot = 0; ; slot++)
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

    /// <summary>
    /// Selects and generates the classic table for one of the nineteen donor
    /// dungeon types. The returned extras preserve the donor's map/potion/
    /// recipe rolls beside the ordinary letter-table receipt.
    /// </summary>
    internal static DaggerfallDungeonLootResult GenerateDungeon(
        DaggerfallDefinitions definitions,
        int dungeonType,
        int playerLevel,
        Func<string, int, int, int> draw,
        string? clothingGroup = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(draw);
        string tableKey = DungeonTableKey(dungeonType);
        DaggerfallLootResult ordinary = Generate(definitions, tableKey, playerLevel, draw, clothingGroup);
        List<DaggerfallLootDrop> drops = ordinary.Drops.ToList();
        List<DaggerfallLootExtraRoll> extras = [];
        if (tableKey[0] is >= 'J' and <= 'O')
        {
            string prefix = $"loot.dungeon.{dungeonType}";
            int mapChance = DungeonMapChances[tableKey[0] - 'J'];
            RollExtra(draw, prefix, "map", MapItem, mapChance, null, extras, drops);
            RollExtra(draw, prefix, "potion", PotionItem, 4, definitions.Magic.ClassicPotionRecipeKeys, extras, drops);
            RollExtra(draw, prefix, "potion-recipe", PotionRecipeItem, 2, definitions.Magic.ClassicPotionRecipeKeys, extras, drops);
        }
        return new(dungeonType, tableKey, ordinary with { Drops = drops }, extras);
    }

    /// <summary>
    /// The chances EnemyEntity applies after its table loot and equipment: every enemy rolls its
    /// mobile's map chance, and only an enemy whose mobile names a loot table then rolls the donor's
    /// 3% potion and 2% potion recipe. A keyless enemy (the City Watch) gets the map roll alone.
    /// </summary>
    internal static DaggerfallEnemyLootExtrasResult GenerateEnemyExtras(
        DaggerfallMagicCatalogSet magic, int mapChance,
        bool hasLootTable,
        Func<string, int, int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (mapChance is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(mapChance), "A mobile's map chance is a percentage.");
        List<DaggerfallLootDrop> drops = [];
        List<DaggerfallLootExtraRoll> extras = [];
        RollExtra(draw, EnemyExtraPrefix, "map", MapItem, mapChance, null, extras, drops);
        if (hasLootTable)
        {
            // CreateRandomPotion picks among the effect broker's registered recipes, which without
            // mods are exactly the twenty classic ones the recipe sheet also picks from.
            RollExtra(draw, EnemyExtraPrefix, "potion", PotionItem, EnemyPotionChance, magic.ClassicPotionRecipeKeys, extras, drops);
            RollExtra(draw, EnemyExtraPrefix, "potion-recipe", PotionRecipeItem, EnemyPotionRecipeChance, magic.ClassicPotionRecipeKeys, extras, drops);
        }
        return new(extras, drops);
    }

    /// <summary>
    /// One donor <c>Dice100.SuccessRoll</c> (a 0-99 roll below the chance) and, for a potion or a
    /// recipe sheet, a uniform pick of its classic recipe.
    /// </summary>
    private static void RollExtra(
        Func<string, int, int, int> draw, string prefix, string kind, string itemId, int chance, IReadOnlyList<int>? recipes,
        List<DaggerfallLootExtraRoll> extras, List<DaggerfallLootDrop> drops)
    {
        int roll = Draw(draw, $"{prefix}.{kind}", 0, 99);
        bool success = roll < chance;
        int? recipe = success && recipes is not null
            ? recipes[Draw(draw, $"{prefix}.{kind}.recipe", 0, recipes.Count - 1)]
            : null;
        extras.Add(new(kind, chance, roll, success, recipe));
        if (success) drops.Add(new DaggerfallLootDrop(itemId, 1, kind, recipe));
    }

    /// <summary>The donor's <c>DFRegion.DungeonTypes</c> ordinal to loot-letter table.</summary>
    internal static string DungeonTableKey(int dungeonType) => dungeonType >= 0 && dungeonType < DungeonTableKeys.Length
        ? DungeonTableKeys[dungeonType]
        : throw new ArgumentOutOfRangeException(nameof(dungeonType), "Daggerfall publishes nineteen dungeon loot table types (0 through 18).");

    /// <summary>The loot tables the dungeon-type map selects, which an admitted base pack must therefore carry.</summary>
    internal static IEnumerable<string> DungeonTableKeysInUse => DungeonTableKeys.Distinct(StringComparer.Ordinal);

    internal static string GoldRollId(string tableKey) => $"loot.{tableKey}.gold";
    internal static string SuccessRollId(string tableKey, string category, int slot) => $"loot.{tableKey}.{category}.{slot}";
    internal static string PickRollId(string tableKey, string category, int slot) => $"{SuccessRollId(tableKey, category, slot)}.pick";

    private static readonly string[] DungeonTableKeys =
    [
        "K", "N", "N", "N", "K", "M", "M", "Q", "K", "U", "D", "N", "L", "F", "S", "N", "M", "L", "N",
    ];

    private static readonly int[] DungeonMapChances = [2, 1, 1, 2, 2, 15];

    // DaggerfallLoot's item templates: MiscItems.Map, UselessItems1.Potion and MiscItems.Potion_recipe.
    private const string MapItem = "template-287";
    private const string PotionItem = "template-83";
    private const string PotionRecipeItem = "template-278";

    // EnemyEntity's fixed RandomlyAddPotion(3) and RandomlyAddPotionRecipe(2) chances.
    private const int EnemyPotionChance = 3;
    private const int EnemyPotionRecipeChance = 2;
    private const string EnemyExtraPrefix = "loot.enemy";

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
        internal PoolKind Pool => Mode switch
        {
            PoolMode.PlayerClothing => new(PoolMode.PlayerClothing, null),
            PoolMode.TemplateGroup => new(PoolMode.TemplateGroup, TemplateGroup ?? throw new InvalidOperationException($"Loot category '{Name}' is missing its template group.")),
            PoolMode.Magic => new(PoolMode.Magic, null),
            _ => throw new InvalidOperationException($"Loot category '{Name}' has an unknown pool mode."),
        };
    }

    private readonly record struct PoolKind(PoolMode Mode, string? Group);

    private enum PoolMode { TemplateGroup, PlayerClothing, Magic }
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

internal sealed record DaggerfallLootDrop(string ItemId, int Quantity, string SourceCategory, int? PotionRecipeKey = null);

internal sealed record DaggerfallDungeonLootResult(
    int DungeonType,
    string TableKey,
    DaggerfallLootResult Loot,
    IReadOnlyList<DaggerfallLootExtraRoll> Extras);

/// <summary>An enemy's map/potion/recipe rolls and the drops they added after its table loot.</summary>
internal sealed record DaggerfallEnemyLootExtrasResult(
    IReadOnlyList<DaggerfallLootExtraRoll> Extras,
    IReadOnlyList<DaggerfallLootDrop> Drops);

internal sealed record DaggerfallLootExtraRoll(string Kind, int Chance, int Roll, bool Success, int? PotionRecipeKey);
