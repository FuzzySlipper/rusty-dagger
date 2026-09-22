using System.Collections.Frozen;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>A non-row source comment retained with its original one-based source line.</summary>
internal sealed record DaggerfallQuestTableComment(string Text, int SourceLine);

/// <summary>An item row whose source name is its lookup identity, with the cataloguer's source parameters.</summary>
internal sealed record DaggerfallQuestItemTableRow(string Name, int P1, int P2, bool Active, int SourceLine);

/// <summary>A faction/person row. <see cref="P2Text"/> retains an unresolved donor value such as <c>?</c>.</summary>
internal sealed record DaggerfallQuestFactionTableRow(string Name, int P1, string P2Text, int? P2, int P3, bool Active, int SourceLine);

/// <summary>A foe row whose source aliases may share the donor numeric identity.</summary>
internal sealed record DaggerfallQuestFoeTableRow(int Id, string Name, bool Active, int SourceLine);

/// <summary>The admitted item lookup surface; table source names are the donor's primary keys.</summary>
internal sealed class DaggerfallQuestItemTable(string sourcePath, IReadOnlyList<DaggerfallQuestItemTableRow> rows, IReadOnlyList<DaggerfallQuestTableComment> comments)
{
    internal string SourcePath { get; } = sourcePath;
    internal IReadOnlyList<DaggerfallQuestItemTableRow> Rows { get; } = rows;
    internal IReadOnlyList<DaggerfallQuestTableComment> Comments { get; } = comments;
    private readonly IReadOnlyDictionary<string, DaggerfallQuestItemTableRow> _byName = rows.Where(row => row.Active).ToFrozenDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);
    internal DaggerfallQuestItemTableRow Resolve(string name) => _byName[name];
}

/// <summary>The admitted faction/person lookup surface; uncertainty remains source data.</summary>
internal sealed class DaggerfallQuestFactionTable(string sourcePath, IReadOnlyList<DaggerfallQuestFactionTableRow> rows, IReadOnlyList<DaggerfallQuestTableComment> comments)
{
    internal string SourcePath { get; } = sourcePath;
    internal IReadOnlyList<DaggerfallQuestFactionTableRow> Rows { get; } = rows;
    internal IReadOnlyList<DaggerfallQuestTableComment> Comments { get; } = comments;
    private readonly IReadOnlyDictionary<string, DaggerfallQuestFactionTableRow> _byName = rows.Where(row => row.Active).ToFrozenDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);
    internal DaggerfallQuestFactionTableRow Resolve(string name) => _byName[name];
}

/// <summary>The admitted foe lookup surface; aliases resolve by their source names.</summary>
internal sealed class DaggerfallQuestFoeTable(string sourcePath, IReadOnlyList<DaggerfallQuestFoeTableRow> rows, IReadOnlyList<DaggerfallQuestTableComment> comments)
{
    internal string SourcePath { get; } = sourcePath;
    internal IReadOnlyList<DaggerfallQuestFoeTableRow> Rows { get; } = rows;
    internal IReadOnlyList<DaggerfallQuestTableComment> Comments { get; } = comments;
    private readonly IReadOnlyDictionary<string, DaggerfallQuestFoeTableRow> _byName = rows.Where(row => row.Active).ToFrozenDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);
    internal DaggerfallQuestFoeTableRow Resolve(string name) => _byName[name];
}

/// <summary>The three admitted quest actor/item tables; they publish source facts and introduce no gameplay policy.</summary>
internal sealed record DaggerfallQuestActorItemTables(
    DaggerfallQuestItemTable Items,
    DaggerfallQuestFactionTable Factions,
    DaggerfallQuestFoeTable Foes);
