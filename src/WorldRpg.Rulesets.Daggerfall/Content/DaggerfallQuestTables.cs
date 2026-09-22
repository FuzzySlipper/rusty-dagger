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

internal sealed record DaggerfallQuestTables(DaggerfallQuestTable Globals, DaggerfallQuestTable StaticMessages);
