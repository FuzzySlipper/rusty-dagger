using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Source.Tool.Donor;

/// <summary>
/// Offline reader for the donor's quest item, faction/person and foe tables. The donor's primary
/// key is its source name, so numeric parameter values are source data rather than a new enum.
/// </summary>
internal static class DonorQuestActorItemTables
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
