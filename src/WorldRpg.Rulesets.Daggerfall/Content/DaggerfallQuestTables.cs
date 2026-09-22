using System.Collections.Frozen;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallQuestTableRow(int Id, string Name, int SourceLine);

/// <summary>Admitted table rows preserve source aliases; numeric IDs are the canonical identity.</summary>
internal sealed class DaggerfallQuestTable(string sourcePath, IReadOnlyList<DaggerfallQuestTableRow> rows)
{
    internal string SourcePath { get; } = sourcePath;
    internal IReadOnlyList<DaggerfallQuestTableRow> Rows { get; } = rows;
    internal IReadOnlyDictionary<string, int> Lookup { get; } = rows
        .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        .ToFrozenDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
}

internal sealed record DaggerfallQuestTables(DaggerfallQuestTable Globals, DaggerfallQuestTable StaticMessages,
    DaggerfallQuestPlaces Places, DaggerfallQuestTable Sounds,
    DaggerfallQuestTable Diseases, DaggerfallQuestTable Spells, DaggerfallQuestActorItemTables ActorItemTables);

internal sealed record DaggerfallQuestPlace(string Name, string CanonicalName, int P1, int P2, int P3,
    uint? LocationKey, byte? TeleportTransfer, int SourceLine);

internal sealed class DaggerfallQuestPlaces(string sourcePath, IReadOnlyList<DaggerfallQuestPlace> rows)
{
    internal string SourcePath { get; } = sourcePath;
    internal IReadOnlyList<DaggerfallQuestPlace> Rows { get; } = rows;
    private readonly IReadOnlyDictionary<string, DaggerfallQuestPlace> _byName = rows.ToFrozenDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);
    internal DaggerfallQuestPlace Resolve(string name) => _byName[_byName[name].CanonicalName];
}
