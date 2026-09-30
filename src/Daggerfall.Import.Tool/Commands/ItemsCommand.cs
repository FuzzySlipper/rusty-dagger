using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reads the donor's exported template tables into the imported payload when asked, so a native template
/// index resolves to the substitute record the donor states rather than to a placeholder. Every record
/// carries substitute provenance; the ledger targets resolve to it.
/// </summary>
internal static class ItemsCommand
{
    public static ToolCommand Command { get; } = new("items",
        [Options.Arena2, Options.Pack, Options.Inventory, CommandOption.Required("--item-templates", "FILE"), CommandOption.Required("--magic-templates", "FILE"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        // The substitute tables are read by file name, not by directory: the provenance label names the
        // donor export, so a file that is not that export is refused rather than published under its name.
        string templates = args["--item-templates"];
        string magicTemplates = args["--magic-templates"];
        if (Path.GetFileName(templates) != "ItemTemplates.txt" || Path.GetFileName(magicTemplates) != "MagicItemTemplates.txt")
        {
            throw new ArgumentException("The item substitute tables must be the donor's ItemTemplates.txt and MagicItemTemplates.txt exports.");
        }

        const string label = "donor/Assets/Resources/ItemTemplates.txt";
        IReadOnlyList<SubstituteItemTemplate> substitutes = ItemTemplateReader.ReadTemplates(File.ReadAllText(templates), label);
        IReadOnlyList<SubstituteMagicTemplate> magic = ItemTemplateReader.ReadMagic(File.ReadAllText(magicTemplates), "donor/Assets/Resources/MagicItemTemplates.txt");
        DaggerfallItemTemplates catalog = DaggerfallItemTemplatesBuilder.Build(substitutes, magic, label, File.ReadAllBytes(templates), Options.ReadInventory(args));
        Console.WriteLine($"items: {catalog.Templates.Count} templates, {catalog.Magic.Count} magic templates");
        if (!args.Switch("--update")) return Options.ReportOnly("these templates");

        Dictionary<string, string> sections = new(StringComparer.Ordinal) { ["itemTemplates"] = JsonSerializer.Serialize(catalog, PublishedJson.Section) };
        if (PayloadFiles.ReadGenerated(args["--pack"])[ItemTemplateLedgerBuilder.SectionName] is JsonObject ledger)
        {
            sections[ItemTemplateLedgerBuilder.SectionName] = ItemTemplateLedgerBuilder.WithSubstituteTargets(ledger, catalog).ToJsonString(PublishedJson.Section);
        }

        PayloadFiles.WriteSections(args["--pack"], sections);
        return 0;
    }
}
