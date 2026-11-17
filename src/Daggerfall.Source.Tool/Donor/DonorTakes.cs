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
        Input("npc-captions.json", donor =>
        {
            TakenFrom source = Taken(donor, $"{TextDirectory}/Internal_Flats.csv", out _);
            return new NpcCaptionTable(source, DonorNpcCaptions.Read(File.ReadAllBytes(Path.Combine(donor, TextDirectory, "Internal_Flats.csv")), source.Path));
        }),
        Section("buildingNames", donor => DonorBuildingNames.Build(File.ReadAllBytes(Path.Combine(donor, "Assets/Scripts/API/MapsFile.cs")),
            PublishedSourcePath.Donor("Assets/Scripts/API/MapsFile.cs"))),
        Section("internalText", donor => DonorInternalText.Build(File.ReadAllBytes(Path.Combine(donor, $"{TextDirectory}/Internal_Strings.csv")),
            PublishedSourcePath.Donor($"{TextDirectory}/Internal_Strings.csv"), "en")),
    ];

    /// <summary>The base pack's payload, where the tables the import never needs are sections the ruleset reads.</summary>
    internal const string BasePayload = "content/worldrpg/packs/daggerfall.base/payload.json";

    private const string TextDirectory = "Assets/StreamingAssets/Text/Master Localization CSV Files";
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
