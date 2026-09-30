using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Builds the item-template ledger from the donor's own group enumerations and either reports the drift
/// against the pack's ledger or publishes the rebuilt section.
/// </summary>
internal static class ItemTemplateLedgerCommand
{
    private const string ItemTemplateFamily = "CNT-011";

    public static ToolCommand Command { get; } = new("item-template-ledger",
        [CommandOption.Required("--donor", "ITEMS_DIR"), Options.Inventory, Options.Authored, Options.Pack, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string donor = args["--donor"];
        string packFile = args["--pack"];
        // The target's provenance comes from the documented inventory rather than from a literal here, so a
        // manifest-row change flows into the ledger instead of leaving it silently stale.
        SourceInventoryRow family = Options.ReadInventory(args)
            .FirstOrDefault(row => row.RowType == "family" && row.Id == ItemTemplateFamily)
            ?? throw new InvalidOperationException($"The documented inventory does not carry family '{ItemTemplateFamily}'.");
        string targetStatus = ItemTemplateLedgerBuilder.StatusFor(family.Disposition);
        ItemTemplateBaseline baseline = ItemTemplateBaseline.FromDonorSources(
            File.ReadAllText(Path.Combine(donor, "ItemEnums.cs")),
            File.ReadAllText(Path.Combine(donor, "ItemHelper.cs")),
            File.ReadAllText(Path.Combine(donor, "..", "..", "API", "ItemsFile.cs")),
            PublishedSourcePath.DonorRoot);
        // The published items are authored; the ledger records how many there are.
        int publishedItems = PayloadFiles.ReadAuthored(args["--authored"])["items"]?.AsArray().Count ?? 0;
        JsonObject ledger = ItemTemplateLedgerBuilder.Build(baseline, family.Id, family.PathOrPattern, targetStatus, publishedItems);
        Console.WriteLine($"target: {family.Id} | {family.PathOrPattern} | documented '{family.Disposition}' -> status '{targetStatus}'");
        Console.WriteLine($"item template ledger: {baseline.Targets.Count} targets, {baseline.Targets.Count(target => target.IsReferenced)} referenced by donor groups, {baseline.Unreferenced.Count()} referenced by none, {baseline.OutOfRangeIndices.Count} outside the classic space");
        Console.WriteLine($"unreferenced indices: [{string.Join(", ", baseline.Unreferenced.Select(target => target.Index))}]");
        Console.WriteLine($"published items: {publishedItems}, value provenance 'catalog-migration', native decoding false");

        if (!args.Switch("--update"))
        {
            // Report only: the pack's ledger is the published artifact, and a difference is printed for a
            // human to publish rather than failed, since this command needs the donor checkout that a
            // routine build does not carry.
            JsonNode? existing = PayloadFiles.ReadGenerated(packFile)[ItemTemplateLedgerBuilder.SectionName];
            Console.WriteLine(existing is null
                ? $"pack: no {ItemTemplateLedgerBuilder.SectionName} section (rerun with --update to publish it)"
                : JsonNode.DeepEquals(existing, ledger)
                    ? $"pack: {ItemTemplateLedgerBuilder.SectionName} matches the donor baseline"
                    : $"pack: {ItemTemplateLedgerBuilder.SectionName} differs from the donor baseline (rerun with --update to publish it)");
            return 0;
        }

        PayloadFiles.WriteSections(packFile, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ItemTemplateLedgerBuilder.SectionName] = ledger.ToJsonString(Daggerfall.Import.Publication.PublishedJson.Section),
        });
        return 0;
    }
}
