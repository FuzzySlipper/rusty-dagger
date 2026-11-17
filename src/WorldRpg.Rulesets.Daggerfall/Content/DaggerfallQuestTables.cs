using System.Collections.Frozen;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallQuestTableRow(int Id, string Name);

/// <summary>Table rows with their aliases; numeric IDs are the canonical identity.</summary>
internal sealed class DaggerfallQuestTable(IReadOnlyList<DaggerfallQuestTableRow> rows)
{
    internal IReadOnlyList<DaggerfallQuestTableRow> Rows { get; } = rows;
    internal IReadOnlyDictionary<string, int> Lookup { get; } = rows
        .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        .ToFrozenDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
}

internal sealed record DaggerfallQuestTables(DaggerfallQuestTable Globals, DaggerfallQuestTable StaticMessages,
    DaggerfallQuestPlaces Places, DaggerfallQuestTable Sounds,
    DaggerfallQuestTable Diseases, DaggerfallQuestTable Spells, DaggerfallQuestActorItemTables ActorItemTables)
{
    internal static DaggerfallQuestTables None { get; } = new(new([]), new([]), new([]), new([]), new([]), new([]), new(new([]), new([]), new([])));
}

internal sealed record DaggerfallQuestPlace(string Name, string CanonicalName, int P1, int P2, int P3,
    uint? LocationKey, byte? TeleportTransfer);

internal sealed class DaggerfallQuestPlaces(IReadOnlyList<DaggerfallQuestPlace> rows)
{
    internal IReadOnlyList<DaggerfallQuestPlace> Rows { get; } = rows;
    private readonly IReadOnlyDictionary<string, DaggerfallQuestPlace> _byName = rows.ToFrozenDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);
    internal DaggerfallQuestPlace Resolve(string name) => _byName[_byName[name].CanonicalName];
}
