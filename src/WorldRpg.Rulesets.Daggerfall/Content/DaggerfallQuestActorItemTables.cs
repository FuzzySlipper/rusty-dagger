using System.Collections.Frozen;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>An item row whose name is its lookup identity, with the cataloguer's parameters.</summary>
internal sealed record DaggerfallQuestItemTableRow(string Name, int P1, int P2, bool Active);

/// <summary>A faction/person row. <see cref="P2Text"/> keeps an unresolved value such as <c>?</c>.</summary>
internal sealed record DaggerfallQuestFactionTableRow(string Name, int P1, string P2Text, int? P2, int P3, bool Active);

/// <summary>A foe row whose aliases may share one numeric identity.</summary>
internal sealed record DaggerfallQuestFoeTableRow(int Id, string Name, bool Active);

/// <summary>The item lookup surface; row names are the primary keys.</summary>
internal sealed class DaggerfallQuestItemTable(IReadOnlyList<DaggerfallQuestItemTableRow> rows)
{
    internal IReadOnlyList<DaggerfallQuestItemTableRow> Rows { get; } = rows;
    private readonly IReadOnlyDictionary<string, DaggerfallQuestItemTableRow> _byName = rows.Where(row => row.Active).ToFrozenDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);
    internal DaggerfallQuestItemTableRow Resolve(string name) => _byName[name];
}

/// <summary>The faction/person lookup surface; uncertainty stays data.</summary>
internal sealed class DaggerfallQuestFactionTable(IReadOnlyList<DaggerfallQuestFactionTableRow> rows)
{
    internal IReadOnlyList<DaggerfallQuestFactionTableRow> Rows { get; } = rows;
    private readonly IReadOnlyDictionary<string, DaggerfallQuestFactionTableRow> _byName = rows.Where(row => row.Active).ToFrozenDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);
    internal DaggerfallQuestFactionTableRow Resolve(string name) => _byName[name];
}

/// <summary>The foe lookup surface; aliases resolve by their names.</summary>
internal sealed class DaggerfallQuestFoeTable(IReadOnlyList<DaggerfallQuestFoeTableRow> rows)
{
    internal IReadOnlyList<DaggerfallQuestFoeTableRow> Rows { get; } = rows;
    private readonly IReadOnlyDictionary<string, DaggerfallQuestFoeTableRow> _byName = rows.Where(row => row.Active).ToFrozenDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);
    internal DaggerfallQuestFoeTableRow Resolve(string name) => _byName[name];
}

/// <summary>The three quest actor/item tables; they hold table facts and introduce no gameplay policy.</summary>
internal sealed record DaggerfallQuestActorItemTables(
    DaggerfallQuestItemTable Items,
    DaggerfallQuestFactionTable Factions,
    DaggerfallQuestFoeTable Foes);
