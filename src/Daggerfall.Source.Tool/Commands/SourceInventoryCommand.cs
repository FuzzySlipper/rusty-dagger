using Daggerfall.Import.Publication;

namespace Daggerfall.Source.Tool.Commands;

/// <summary>
/// Reconciles the documented inventory's disposition column with what every publication the product ships read:
/// the same join of each artifact's own citations the import's <c>source-coverage</c> step reports, over the content
/// the product sees and the import records. <c>--update-inventory</c> is the only writer of that column.
/// </summary>
/// <remarks>
/// A source is imported when any publication reads it. One publication's source manifest cannot decide that, since
/// a source only the world blocks or the imported payload reads is outside the world media's; so the update refuses,
/// naming the source and the publication, to write unused a source a published artifact cites.
/// </remarks>
internal static class SourceInventoryCommand
{
    public static ToolCommand Command { get; } = new("source-inventory",
        [Options.Arena2, Options.Inventory, CommandOption.Staged("--repository", "DIR", "."),
            CommandOption.Staged("--content", "GENERATED_CONTENT_ROOT", ImportLayout.Staging),
            CommandOption.Staged("--import-records", "DIR", ImportLayout.Records), CommandOption.Switch("--update-inventory")], Run, Program.Tool);

    private static int Run(CommandArguments args)
    {
        string inventoryFile = args["--inventory"];
        SourceCoverageReport coverage = SourceCoverageReconciler.Reconcile(
            SourceCoverageArtifact.Collect(Path.Combine(args["--repository"], "content"), args["--content"], args["--import-records"]),
            args["--arena2"], ImportLayout.Inventory, File.ReadAllBytes(inventoryFile));

        SourceManifestFamilyCount total = SourceManifestFamilyCount.From("total", "summary",
            coverage.Records.Select(record => record.Source with { Disposition = record.Disposition }));
        Console.WriteLine($"source inventory: {total.Discovered} records across {coverage.Families.Count(family => family.Discovered > 0)} supplied families, every publication joined");
        Console.WriteLine($"  imported {total.Imported}, unused {total.Unused}, source-gap {total.SourceGap}, required-pending {total.RequiredPending}, unresolved {total.Unresolved}, excluded {total.Excluded}, duplicate {total.Duplicate}, malformed {total.Malformed}");
        // Every family is printed, including the ones with no records: a documented source gap or an
        // excluded family is exactly what a reader needs to see, and filtering by record count hides those.
        foreach (SourceManifestFamilyCount family in coverage.Families)
        {
            Console.WriteLine($"  {family.FamilyId} [{family.DocumentedDisposition}]: {family.Discovered} = {family.Imported} imported, {family.Unused} unused, {family.SourceGap} source-gap, {family.RequiredPending} pending, {family.Unresolved} unresolved, {family.Excluded} excluded, {family.Malformed} malformed");
        }

        SourceInventoryReconciliation reconciliation = SourceInventoryReconciler.Reconcile(inventoryFile, coverage, args.Switch("--update-inventory"));
        foreach (string line in reconciliation.Drift) Console.WriteLine($"inventory drift: {line}");
        foreach (string line in reconciliation.Unreconciled.Take(20)) Console.WriteLine($"inventory unresolved: {line}");
        foreach (string line in reconciliation.Refused) Console.WriteLine($"inventory refused: {line}");

        // The status line is chosen from what reconciliation actually did, so it cannot announce an update
        // that was refused. A requested update that did not happen is a failure the caller has to see.
        Console.Error.WriteLine(reconciliation switch
        {
            { IsClean: true } => "inventory: documented dispositions match every publication's sources",
            { Updated: true } => $"inventory: documented dispositions updated: {reconciliation.Drift.Count}",
            { UpdateBlocked: true } => $"inventory: the inventory file was NOT rewritten — unresolved documented rows: {reconciliation.Unreconciled.Count}, refused as unused while a publication reads them: {reconciliation.Refused.Count}, disagreements remaining: {reconciliation.Drift.Count}",
            { Unreconciled.Count: > 0 } or { Refused.Count: > 0 } => $"inventory: unresolved documented rows: {reconciliation.Unreconciled.Count}, refused: {reconciliation.Refused.Count}, disagreements: {reconciliation.Drift.Count}; --update-inventory cannot resolve either",
            _ => $"inventory: disagreements to record: {reconciliation.Drift.Count} (rerun with --update-inventory)",
        });
        return reconciliation.UpdateBlocked ? 1 : 0;
    }
}
