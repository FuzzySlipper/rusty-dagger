using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Source.Tool.Commands;

/// <summary>
/// Reconciles our mobile table, taken from the donor's static table, with the authored actors and reports every entry nothing
/// published carries. Coverage of the donor's mobiles is reported, not assumed: an entry the pack does not
/// publish is named here rather than staying invisible.
/// </summary>
internal static class MobileLedgerCommand
{
    private static readonly CommandOption Authored = CommandOption.Staged("--authored", "BASE.json", "content/worldrpg/packs/daggerfall.base/payload.json");

    public static ToolCommand Command { get; } = new("mobile-ledger",
        [CommandOption.Staged("--mobiles", "MOBILES.json", $"{ImportLayout.Inputs}/mobiles.json"), Authored], Run, Program.Tool);

    private static int Run(CommandArguments args)
    {
        string authored = args["--authored"];
        if (!File.Exists(authored)) throw new FileNotFoundException($"The authored base payload is not at '{authored}'.", authored);
        MobileLedger ledger = MobileLedgerBuilder.Build(TakenTable.Read<MobileTable>(File.ReadAllBytes(args["--mobiles"]), args["--mobiles"]), File.ReadAllText(authored));
        Console.WriteLine($"donor mobiles: {ledger.DonorEntries}, published actors: {ledger.PublishedActors}");
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
