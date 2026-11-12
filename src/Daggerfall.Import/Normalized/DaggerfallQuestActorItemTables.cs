using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>An item row whose source name is the table's primary key and whose parameters describe its class and subclass.</summary>
/// <summary>A non-row comment preserved from a quest table with its original one-based line.</summary>
public sealed record DaggerfallQuestTableComment(string Text, int SourceLine);

public sealed record DaggerfallQuestItemTableRow(string Name, int P1, int P2, bool Active, int SourceLine);

/// <summary>A faction/person row. <see cref="P2Text"/> retains the donor's unresolved question-mark values.</summary>
public sealed record DaggerfallQuestFactionTableRow(string Name, int P1, string P2Text, int? P2, int P3, bool Active, int SourceLine);

/// <summary>A foe row whose numeric identity can be shared by source aliases.</summary>
public sealed record DaggerfallQuestFoeTableRow(int Id, string Name, bool Active, int SourceLine);

/// <summary>Quest item rows and every non-row source comment from <c>Quests-Items.txt</c>.</summary>
public sealed record DaggerfallQuestItemTable(
    PublishedSource Source,
    IReadOnlyList<DaggerfallQuestItemTableRow> Rows,
    IReadOnlyList<DaggerfallQuestTableComment> Comments);

/// <summary>Quest faction/person rows and every non-row source comment from <c>Quests-Factions.txt</c>.</summary>
public sealed record DaggerfallQuestFactionTable(
    PublishedSource Source,
    IReadOnlyList<DaggerfallQuestFactionTableRow> Rows,
    IReadOnlyList<DaggerfallQuestTableComment> Comments);

/// <summary>Quest foe rows and every non-row source comment from <c>Quests-Foes.txt</c>.</summary>
public sealed record DaggerfallQuestFoeTable(
    PublishedSource Source,
    IReadOnlyList<DaggerfallQuestFoeTableRow> Rows,
    IReadOnlyList<DaggerfallQuestTableComment> Comments);

/// <summary>The three actor/item quest tables loaded together by the donor quest machine.</summary>
public sealed record DaggerfallQuestActorItemTables(
    DaggerfallQuestItemTable Items,
    DaggerfallQuestFactionTable Factions,
    DaggerfallQuestFoeTable Foes);
