namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>Which loot table each classic dungeon type's treasure is drawn from.</summary>
internal static class DaggerfallDungeonLootTables
{
    /// <summary>The donor's <c>DFRegion.DungeonTypes</c> ordinal to loot-letter table.</summary>
    internal static string DungeonTableKey(int dungeonType) => dungeonType >= 0 && dungeonType < DungeonTableKeys.Length
        ? DungeonTableKeys[dungeonType]
        : throw new ArgumentOutOfRangeException(nameof(dungeonType), "Daggerfall publishes nineteen dungeon loot table types (0 through 18).");

    /// <summary>The loot tables the dungeon-type map selects, which an admitted base pack must therefore carry.</summary>
    internal static IEnumerable<string> DungeonTableKeysInUse => DungeonTableKeys.Distinct(StringComparer.Ordinal);

    private static readonly string[] DungeonTableKeys =
    [
        "K", "N", "N", "N", "K", "M", "M", "Q", "K", "U", "D", "N", "L", "F", "S", "N", "M", "L", "N",
    ];
}
