using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>An item row whose source name is the table's primary key and whose parameters describe its class and subclass.</summary>
public sealed record DaggerfallQuestItemTableRow(string Name, int P1, int P2, bool Active, int SourceLine);

/// <summary>A faction/person row. <see cref="P2Text"/> retains the donor's unresolved question-mark values.</summary>
public sealed record DaggerfallQuestFactionTableRow(string Name, int P1, string P2Text, int? P2, int P3, bool Active, int SourceLine);

/// <summary>A foe row whose numeric identity can be shared by source aliases.</summary>
public sealed record DaggerfallQuestFoeTableRow(int Id, string Name, bool Active, int SourceLine);

/// <summary>Quest item rows and every non-row source comment from <c>Quests-Items.txt</c>.</summary>
public sealed record DaggerfallQuestItemTable(
    ImportPublicationSource Source,
    IReadOnlyList<DaggerfallQuestItemTableRow> Rows,
    IReadOnlyList<DaggerfallQuestTableComment> Comments);

/// <summary>Quest faction/person rows and every non-row source comment from <c>Quests-Factions.txt</c>.</summary>
public sealed record DaggerfallQuestFactionTable(
    ImportPublicationSource Source,
    IReadOnlyList<DaggerfallQuestFactionTableRow> Rows,
    IReadOnlyList<DaggerfallQuestTableComment> Comments);

/// <summary>Quest foe rows and every non-row source comment from <c>Quests-Foes.txt</c>.</summary>
public sealed record DaggerfallQuestFoeTable(
    ImportPublicationSource Source,
    IReadOnlyList<DaggerfallQuestFoeTableRow> Rows,
    IReadOnlyList<DaggerfallQuestTableComment> Comments);

/// <summary>The three actor/item quest tables loaded together by the donor quest machine.</summary>
public sealed record DaggerfallQuestActorItemTables(
    DaggerfallQuestItemTable Items,
    DaggerfallQuestFactionTable Factions,
    DaggerfallQuestFoeTable Foes);

/// <summary>
/// Offline reader for the donor's quest item, faction/person and foe tables. The donor's primary
/// key is its source name, so numeric parameter values are source data rather than a new enum.
/// </summary>
public static class DaggerfallQuestActorItemTableReader
{
    public static DaggerfallQuestActorItemTables Read(byte[] itemBytes, string itemSourcePath,
        byte[] factionBytes, string factionSourcePath, byte[] foeBytes, string foeSourcePath) => new(
        ReadItems(itemBytes, itemSourcePath), ReadFactions(factionBytes, factionSourcePath), ReadFoes(foeBytes, foeSourcePath));

    public static DaggerfallQuestItemTable ReadItems(byte[] bytes, string sourcePath)
    {
        QuestParameterTable table = ReadTable(bytes, sourcePath, ["*name", "p1", "p2"]);
        List<DaggerfallQuestItemTableRow> rows = [];
        foreach (QuestParameterTableRow row in table.Rows)
        {
            rows.Add(new(row.Name, QuestParameterTableReader.Number(row.Parameters[0], sourcePath, row.SourceLine),
                QuestParameterTableReader.Number(row.Parameters[1], sourcePath, row.SourceLine), row.Active, row.SourceLine));
        }

        return new(table.Source, rows, table.Comments);
    }

    public static DaggerfallQuestFactionTable ReadFactions(byte[] bytes, string sourcePath)
    {
        QuestParameterTable table = ReadTable(bytes, sourcePath, ["*name", "p1", "p2", "p3"]);
        List<DaggerfallQuestFactionTableRow> rows = [];
        foreach (QuestParameterTableRow row in table.Rows)
        {
            string p2Text = row.Parameters[1];
            if (p2Text == "?" && row.Active)
            {
                throw new Arena2FormatException(sourcePath, row.SourceLine, "Only a disabled faction row may retain an unresolved p2 parameter.");
            }

            int? p2 = p2Text == "?" ? null : QuestParameterTableReader.Number(p2Text, sourcePath, row.SourceLine);
            rows.Add(new(row.Name, QuestParameterTableReader.Number(row.Parameters[0], sourcePath, row.SourceLine), p2Text, p2,
                QuestParameterTableReader.Number(row.Parameters[2], sourcePath, row.SourceLine), row.Active, row.SourceLine));
        }

        return new(table.Source, rows, table.Comments);
    }

    public static DaggerfallQuestFoeTable ReadFoes(byte[] bytes, string sourcePath)
    {
        QuestParameterTable table = ReadTable(bytes, sourcePath, ["id", "*name"]);
        List<DaggerfallQuestFoeTableRow> rows = [];
        foreach (QuestParameterTableRow row in table.Rows)
        {
            rows.Add(new(QuestParameterTableReader.Number(row.Parameters[0], sourcePath, row.SourceLine), row.Name, row.Active, row.SourceLine));
        }

        return new(table.Source, rows, table.Comments);
    }

    private static QuestParameterTable ReadTable(byte[] bytes, string sourcePath, IReadOnlyList<string> expectedSchema)
    {
        QuestParameterTable table = QuestParameterTableReader.Read(bytes, sourcePath, expectedSchema);
        Dictionary<string, int> activeNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (QuestParameterTableRow row in table.Rows.Where(row => row.Active))
        {
            if (activeNames.TryGetValue(row.Name, out int priorLine))
            {
                throw new Arena2FormatException(sourcePath, row.SourceLine,
                    $"Canonical source name '{row.Name}' duplicates line {priorLine}; numeric parameter aliases remain legal.");
            }

            activeNames.Add(row.Name, row.SourceLine);
        }

        return table;
    }

}
