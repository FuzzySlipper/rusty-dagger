using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reads donor-shaped quest text into the imported payload when asked, so a quest source resolves to its
/// messages and finite top-level QBN blocks rather than to a binary blob. Action bodies remain ordered source
/// lines for later action compilation; an unclaimed top-level line diagnoses the source. The original-source
/// selections feed the quest corpus payloads, which carry what a session reads; nothing at runtime reads the
/// selections themselves, so they go to the import records.
/// </summary>
internal static class QuestsCommand
{
    public static ToolCommand Command { get; } = new("quests",
        [Options.Arena2, CommandOption.Required("--quest-text", "SOURCE_DIR"), CommandOption.Required("--tables", "TABLE_DIR"), Options.Pack, Options.Records, Options.Inventory, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string tablesDirectory = args["--tables"];
        byte[] Table(string name) => File.ReadAllBytes(Path.Combine(tablesDirectory, name));
        DaggerfallQuestTables tables = new(
            DaggerfallQuestTableReader.Read(Table("Quests-GlobalVars.txt"), "Tables/Quests-GlobalVars.txt", globals: true),
            DaggerfallQuestTableReader.Read(Table("Quests-StaticMessages.txt"), "Tables/Quests-StaticMessages.txt"),
            DaggerfallQuestPlaceReader.Read(Table("Quests-Places.txt"), "Tables/Quests-Places.txt"),
            DaggerfallQuestTableReader.Read(Table("Quests-Sounds.txt"), "Tables/Quests-Sounds.txt"),
            DaggerfallQuestTableReader.Read(Table("Quests-Diseases.txt"), "Tables/Quests-Diseases.txt"),
            DaggerfallQuestTableReader.Read(Table("Quests-Spells.txt"), "Tables/Quests-Spells.txt"),
            DaggerfallQuestActorItemTableReader.Read(
                Table("Quests-Items.txt"), "Tables/Quests-Items.txt",
                Table("Quests-Factions.txt"), "Tables/Quests-Factions.txt",
                Table("Quests-Foes.txt"), "Tables/Quests-Foes.txt"));
        List<QuestSourceDocument> documents = [];
        List<(string FileName, string QuestName, int Line, string Reason)> failures = [];
        long totalBytes = 0;
        foreach (string path in Directory.EnumerateFiles(args["--quest-text"], "*.txt").Order(StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(path);
            totalBytes += new FileInfo(path).Length;
            try
            {
                documents.Add(QuestSourceReader.Read(File.ReadAllText(path), fileName, tables.StaticMessages.Lookup, tables.Globals.Lookup));
            }
            catch (Arena2FormatException exception)
            {
                failures.Add((fileName, Path.GetFileNameWithoutExtension(path), exception.Offset, exception.Message));
            }
        }

        DaggerfallQuestPack pack = DaggerfallQuestPackBuilder.Build(documents, failures, "donor/StreamingAssets/Quests", new byte[totalBytes], Options.ReadInventory(args));
        DaggerfallQuestCatalog catalog = DaggerfallQuestCatalogReader.Read(
            Table("QuestList-Classic.txt"), "Tables/QuestList-Classic.txt",
            Directory.EnumerateFiles(args["--quest-text"], "*.txt"));
        DaggerfallQuestOriginalSourceSet originals = DaggerfallQuestOriginalSourceBuilder.Build(args["--arena2"], QuestSourcesCommand.Enumerate(args["--arena2"]), pack);
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
}
