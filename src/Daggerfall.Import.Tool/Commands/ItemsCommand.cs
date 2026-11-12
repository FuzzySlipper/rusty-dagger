using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reads our item template tables (taken from the donor's exports) into the imported payload when asked, so a
/// native template index resolves to the substitute record the donor states rather than to a placeholder. Every record
/// carries substitute provenance; the ledger targets resolve to it.
/// </summary>
internal static class ItemsCommand
{
    public static ToolCommand Command { get; } = new("items",
        [Options.Arena2, Options.Pack, Options.Inventory, CommandOption.Required("--item-templates", "ITEM_TEMPLATES.json"),
         CommandOption.Required("--magic-item-templates", "MAGIC_ITEM_TEMPLATES.json"), CommandOption.Required("--item-groups", "ITEM_GROUPS.json"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string templates = args["--item-templates"], magicTemplates = args["--magic-item-templates"], groups = args["--item-groups"];
        ItemTemplateTable substitutes = TakenTable.Read<ItemTemplateTable>(File.ReadAllBytes(templates), templates);
        MagicItemTemplateTable magic = TakenTable.Read<MagicItemTemplateTable>(File.ReadAllBytes(magicTemplates), magicTemplates);
        ItemGroupTable groupTable = TakenTable.Read<ItemGroupTable>(File.ReadAllBytes(groups), groups);
        DaggerfallItemTemplates catalog = DaggerfallItemTemplatesBuilder.Build(substitutes.Templates, magic.Templates, substitutes.Source.Path,
            substitutes.Source.ByteLength, Options.ReadInventory(args));
        catalog = catalog with { GroupTable = new(groupTable.ItemEnums.Cited(), groupTable.ItemHelper.Cited(), [.. groupTable.Groups.OrderBy(group => group.Id)]) };
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
