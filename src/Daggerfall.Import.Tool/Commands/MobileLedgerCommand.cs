using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reconciles the donor's static mobile table with the published pack and reports every entry nothing
/// published carries. Coverage of the donor's mobiles is reported, not assumed: an entry the pack does not
/// publish is named here rather than staying invisible.
/// </summary>
internal static class MobileLedgerCommand
{
    public static ToolCommand Command { get; } = new("mobile-ledger",
        [CommandOption.Required("--donor", "ENEMY_BASICS.cs"), Options.Authored, Options.Pack], Run);

    private static int Run(CommandArguments args)
    {
        // The ledger joins the authored actors to the imported catalogs' enemy references.
        MobileLedger ledger = MobileLedgerBuilder.Build(File.ReadAllText(args["--donor"]), PayloadFiles.CombinedText(args["--authored"], args["--pack"]));
        Console.WriteLine($"donor mobiles: {ledger.DonorEntries}, published actors: {ledger.PublishedActors}, catalog enemies: {ledger.CatalogEntries}");
        Console.WriteLine($"published {ledger.Entries.Count(entry => entry.Disposition == MobileLedgerDisposition.Published)}, variants {ledger.Entries.Count(entry => entry.Disposition == MobileLedgerDisposition.PublishedVariant)}, human mobiles {ledger.Entries.Count(entry => entry.Disposition == MobileLedgerDisposition.HumanClass)}, unpublished {ledger.Unpublished.Count}");
        foreach (MobileLedgerEntry entry in ledger.Unpublished)
        {
            Console.WriteLine($"unpublished: id {entry.Id} '{entry.Name}' - {entry.Note}");
        }

        foreach (MobileLedgerEntry entry in ledger.Entries.Where(entry => entry.Disposition == MobileLedgerDisposition.PublishedVariant))
        {
            Console.WriteLine($"variant: id {entry.Id} '{entry.Name}' - {entry.Note}");
        }

        return 0;
    }
}
