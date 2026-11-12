using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Source.Tool.Donor;

/// <summary>One of our tables and what a Daggerfall Unity checkout says it should hold, as JSON to compare.</summary>
/// <param name="Label">Where our table lives.</param>
/// <param name="Ours">Our table, read from a repository root.</param>
/// <param name="Donor">The same table read from a Daggerfall Unity checkout's root.</param>
internal sealed record DonorTake(string Label, Func<string, JsonNode?> Ours, Func<string, JsonNode?> Donor);

/// <summary>
/// Every table we took from Daggerfall Unity, each with the donor files and parser it was taken with. The drift check
/// (<c>donor-drift</c>) is the only reader of the donor.
/// </summary>
internal static class DonorTakes
{
    public static IReadOnlyList<DonorTake> All { get; } =
    [
        Input("races.json", donor => new DaggerfallRaceTable(
            Taken(donor, "Assets/Scripts/Game/Entities/RaceTemplate.cs", out string text), DonorRaceTable.Read(text))),
        Input("item-groups.json", donor =>
        {
            TakenFrom enums = Taken(donor, "Assets/Scripts/Game/Items/ItemEnums.cs", out string enumsText);
            TakenFrom helper = Taken(donor, "Assets/Scripts/Game/Items/ItemHelper.cs", out string helperText);
            TakenFrom itemsFile = Taken(donor, "Assets/Scripts/API/ItemsFile.cs", out string itemsFileText);
            (int count, IReadOnlyList<ItemGroupEnumeration> groups) = DonorItemGroups.Read(enumsText, helperText, itemsFileText, "daggerfall-unity item groups");
            return new ItemGroupTable(enums, helper, itemsFile, count, groups);
        }),
        Input("item-templates.json", donor =>
        {
            TakenFrom source = Taken(donor, "Assets/Resources/ItemTemplates.txt", out string text);
            return new ItemTemplateTable(source, DonorItemTemplates.ReadTemplates(text, source.Path));
        }),
        Input("magic-item-templates.json", donor =>
        {
            TakenFrom source = Taken(donor, "Assets/Resources/MagicItemTemplates.txt", out string text);
            return new MagicItemTemplateTable(source, DonorItemTemplates.ReadMagic(text, source.Path));
        }),
        Input("spell-costs.json", donor =>
        {
            TakenFrom source = Taken(donor, "Assets/Scripts/Game/Formulas/FormulaHelper.cs", out string text);
            (IReadOnlyList<SpellCostEffect> effects, IReadOnlyList<IReadOnlyList<int>> rows) = DonorSpellCosts.Read(text);
            return new SpellCostTable(source, effects, rows);
        }),
        Input("mobiles.json", donor => new MobileTable(
            Taken(donor, "Assets/Scripts/Utility/EnemyBasics.cs", out string text), DonorMobileTable.Read(text))),
        Input("quest-tables.json", donor => new QuestTablesInput("", QuestTables(donor))),
        Input("quest-list.json", donor => new QuestListInput("", DonorQuestList.Read(File.ReadAllBytes(Path.Combine(donor, QuestTableDirectory, "QuestList-Classic.txt")),
            PublishedSourcePath.Donor($"{QuestTableDirectory}/QuestList-Classic.txt"), Directory.EnumerateFiles(Path.Combine(donor, QuestDirectory), "*.txt")))),
        new($"{ImportLayout.Inputs}/quests/", QuestDocuments, donor =>
        {
            DaggerfallQuestTables tables = QuestTables(donor);
            JsonObject documents = [];
            foreach (string path in Directory.EnumerateFiles(Path.Combine(donor, QuestDirectory), "*.txt").Order(StringComparer.Ordinal))
            {
                string fileName = Path.GetFileName(path);
                TakenFrom source = Taken(donor, $"{QuestDirectory}/{fileName}", out string text);
                QuestDocumentInput document;
                try
                {
                    document = new(source, DonorQuestScript.Read(text, fileName, tables.StaticMessages.Lookup, tables.Globals.Lookup), null);
                }
                catch (Arena2FormatException exception)
                {
                    document = new(source, null, new QuestRefusal(exception.Offset, exception.Message));
                }

                documents[Path.GetFileNameWithoutExtension(fileName)] = JsonSerializer.SerializeToNode(document, PublishedJson.Section);
            }

            return documents;
        }),
        Section("buildingNames", donor => DonorBuildingNames.Build(File.ReadAllBytes(Path.Combine(donor, "Assets/Scripts/API/MapsFile.cs")),
            PublishedSourcePath.Donor("Assets/Scripts/API/MapsFile.cs"))),
        Section("internalText", donor => DonorInternalText.Build(File.ReadAllBytes(Path.Combine(donor, $"{TextDirectory}/Internal_Strings.csv")),
            PublishedSourcePath.Donor($"{TextDirectory}/Internal_Strings.csv"), "en")),
        Section("npcCaptions", donor => DonorNpcCaptions.Build(File.ReadAllBytes(Path.Combine(donor, $"{TextDirectory}/Internal_Flats.csv")),
            PublishedSourcePath.Donor($"{TextDirectory}/Internal_Flats.csv"))),
    ];

    /// <summary>The base pack's payload, where the tables the import never needs are sections the ruleset reads.</summary>
    internal const string BasePayload = "content/worldrpg/packs/daggerfall.base/payload.json";

    private const string TextDirectory = "Assets/StreamingAssets/Text/Master Localization CSV Files";
    private const string QuestDirectory = "Assets/StreamingAssets/Quests";
    private const string QuestTableDirectory = "Assets/StreamingAssets/Tables";

    /// <summary>The quest tables as the donor's <c>Quests-*.txt</c> files state them.</summary>
    private static DaggerfallQuestTables QuestTables(string donor)
    {
        (byte[] Bytes, string Label) Table(string name) =>
            (File.ReadAllBytes(Path.Combine(donor, QuestTableDirectory, $"Quests-{name}.txt")), PublishedSourcePath.Donor($"{QuestTableDirectory}/Quests-{name}.txt"));
        DaggerfallQuestTable Read(string name, bool globals = false) => Table(name) is var (bytes, label) ? DonorQuestTable.Read(bytes, label, globals) : null!;
        (byte[] items, string itemsLabel) = Table("Items");
        (byte[] factions, string factionsLabel) = Table("Factions");
        (byte[] foes, string foesLabel) = Table("Foes");
        (byte[] places, string placesLabel) = Table("Places");
        return new(Read("GlobalVars", globals: true), Read("StaticMessages"), DonorQuestPlaces.Read(places, placesLabel), Read("Sounds"),
            Read("Diseases"), Read("Spells"), DonorQuestActorItemTables.Read(items, itemsLabel, factions, factionsLabel, foes, foesLabel));
    }

    /// <summary>Our quest documents, one per script, by the script's name.</summary>
    private static JsonNode QuestDocuments(string repository)
    {
        JsonObject documents = [];
        foreach (string path in Directory.EnumerateFiles(Path.Combine(repository, ImportLayout.Inputs, "quests"), "*.json").Order(StringComparer.Ordinal))
            documents[Path.GetFileNameWithoutExtension(path)] = JsonNode.Parse(File.ReadAllBytes(path));
        return documents;
    }

    /// <summary>A section of the base pack's payload.</summary>
    private static DonorTake Section<T>(string section, Func<string, T> fromDonor) => new(
        $"{BasePayload} section {section}",
        repository => JsonNode.Parse(File.ReadAllBytes(Path.Combine(repository, BasePayload)))?[section],
        donor => JsonSerializer.SerializeToNode(fromDonor(donor), PublishedJson.Section));

    /// <summary>A table of ours beside the importer, under <c>src/Daggerfall.Import.Tool/inputs</c>.</summary>
    private static DonorTake Input<T>(string file, Func<string, T> fromDonor) => new(
        $"{ImportLayout.Inputs}/{file}",
        repository => JsonNode.Parse(File.ReadAllBytes(Path.Combine(repository, ImportLayout.Inputs, file))),
        donor => JsonSerializer.SerializeToNode(fromDonor(donor), PublishedJson.Section));

    /// <summary>Reads one donor file, returning where it was taken from (its commit is not compared) and its text.</summary>
    internal static TakenFrom Taken(string donor, string relative, out string text)
    {
        string path = Path.Combine(donor, relative);
        text = File.ReadAllText(path);
        return TakenFrom.Of(PublishedSourcePath.Donor(relative), "", File.ReadAllBytes(path));
    }
}
