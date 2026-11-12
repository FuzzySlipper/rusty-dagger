using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reads our quest documents and tables (taken from the donor's quest scripts) into the imported payload when asked,
/// so a quest source resolves to its messages and finite top-level QBN blocks rather than to a binary blob. Action bodies remain ordered source
/// lines for later action compilation; an unclaimed top-level line diagnoses the source. The original-source
/// selections feed the quest corpus payloads, which carry what a session reads; nothing at runtime reads the
/// selections themselves, so they go to the import records.
/// </summary>
internal static class QuestsCommand
{
    public static ToolCommand Command { get; } = new("quests",
        [Options.Arena2, CommandOption.Required("--quest-documents", "QUESTS_DIR"), CommandOption.Required("--quest-tables", "QUEST_TABLES.json"),
         CommandOption.Required("--quest-list", "QUEST_LIST.json"), Options.Pack, Options.Records, Options.Inventory, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string tablesFile = args["--quest-tables"], listFile = args["--quest-list"];
        DaggerfallQuestTables tables = TakenTable.Read<QuestTablesInput>(File.ReadAllBytes(tablesFile), tablesFile).Tables;
        DaggerfallQuestCatalog catalog = TakenTable.Read<QuestListInput>(File.ReadAllBytes(listFile), listFile).Catalog;
        List<QuestSourceDocument> documents = [];
        List<(string FileName, string QuestName, int Line, string Reason)> failures = [];
        long totalBytes = 0;
        foreach (QuestDocumentInput quest in Directory.EnumerateFiles(args["--quest-documents"], "*.json")
                     .Select(path => TakenTable.Read<QuestDocumentInput>(File.ReadAllBytes(path), path))
                     .OrderBy(quest => quest.Source.Path, StringComparer.Ordinal))
        {
            totalBytes += quest.Source.ByteLength;
            string fileName = Path.GetFileName(quest.Source.Path);
            if (quest.Document is { } document) documents.Add(document);
            else if (quest.Refusal is { } refusal) failures.Add((fileName, Path.GetFileNameWithoutExtension(fileName), refusal.Line, refusal.Reason));
            else throw new InvalidDataException($"Quest document '{fileName}' carries neither a document nor a refusal.");
        }

        // The pack cites the donor's quest directory by its total length, as it did when the scripts were read there.
        DaggerfallQuestPack pack = DaggerfallQuestPackBuilder.Build(documents, failures, PublishedSourcePath.Donor("Assets/StreamingAssets/Quests"), new byte[totalBytes], Options.ReadInventory(args));
        DaggerfallQuestOriginalSourceSet originals = DaggerfallQuestOriginalSourceBuilder.Build(args["--arena2"], QuestSources(args["--arena2"]), pack);
        Console.WriteLine($"classic catalog: {catalog.Rows.Count(row => row.Active)} active, {catalog.Rows.Count(row => !row.Active)} disabled, {catalog.Rows.Count(row => row.SourceDisposition == "missing")} missing sources");
        Console.WriteLine($"quests: {pack.Quests.Count} sources, {pack.Quests.Count(quest => quest.Disposition == DaggerfallQuestDisposition.Compiled)} compiled");
        Console.WriteLine($"original quest sources: {originals.Quests.Count} stems, {originals.Quests.Count(quest => quest.Selection == DaggerfallQuestOriginalSourceSelection.RewrittenText)} rewritten text selections, {originals.Quests.Count(quest => quest.Selection != DaggerfallQuestOriginalSourceSelection.RewrittenText)} not enabled");
        if (!args.Switch("--update")) return Options.ReportOnly("these sources");
        PayloadFiles.WriteSections(args["--pack"], new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["questCatalog"] = JsonSerializer.Serialize(catalog, PublishedJson.Section),
            ["questTables"] = JsonSerializer.Serialize(tables, PublishedJson.Section),
            ["questSources"] = JsonSerializer.Serialize(pack, PublishedJson.Section),
        });
        PayloadFiles.WriteSection(args["--records"], "questOriginalSources", originals);
        return 0;
    }

    /// <summary>Every QBN and QRC file the corpus directory supplies.</summary>
    internal static QuestSourceInventory QuestSources(string arena2)
    {
        string[] paths = [.. Directory.EnumerateFiles(arena2)
            .Where(path => path.EndsWith(QuestSourceInventory.BinaryExtension, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(QuestSourceInventory.ResourcesExtension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OfType<string>()];
        return QuestSourceInventory.Enumerate(paths, Options.CorpusLabel(arena2));
    }
}
