using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reconciles the documented inventory against a real source tree and writes the machine-readable source
/// manifest. Which sources count as imported comes from a published site closure, so the record states what
/// a consumer actually read rather than a second guess at it. <c>--update-inventory</c> is the only writer
/// of the inventory's disposition column.
/// </summary>
internal static class SourceManifestCommand
{
    public static ToolCommand Command { get; } = new("source-manifest",
        [Options.Arena2, Options.Inventory, CommandOption.Required("--output", "FILE"), CommandOption.Required("--publication", "PUBLISHED_DIR"), CommandOption.Switch("--update-inventory")], Run);

    private static int Run(CommandArguments args)
    {
        string inventoryFile = args["--inventory"];
        CanonicalImportManifest published = ImportPublicationManifestSerializer.Deserialize(
            File.ReadAllBytes(Path.Combine(args["--publication"], ImportPublicationManifestSerializer.ManifestRelativePath)));
        byte[] bytes = SourceManifestSerializer.Serialize(SourceManifestPublication.ForPublication(
            published.Sources.Select(source => source.SourcePath), args["--arena2"], Path.GetFileName(inventoryFile), File.ReadAllBytes(inventoryFile)));
        PayloadFiles.WriteFile(args["--output"], bytes);
        SourceManifest manifest = SourceManifestSerializer.Deserialize(bytes);

        SourceManifestFamilyCount total = SourceManifestFamilyCount.From("total", "summary", manifest.Records);
        Console.WriteLine($"source manifest: {total.Discovered} records across {manifest.Families.Count(family => family.Discovered > 0)} supplied families");
        Console.WriteLine($"  imported {total.Imported}, unused {total.Unused}, source-gap {total.SourceGap}, required-pending {total.RequiredPending}, unresolved {total.Unresolved}, excluded {total.Excluded}, duplicate {total.Duplicate}, malformed {total.Malformed}");
        // Every family is printed, including the ones with no records: a documented source gap or an
        // excluded family is exactly what a reader needs to see, and filtering by record count hides those.
        foreach (SourceManifestFamilyCount family in manifest.Families)
        {
            Console.WriteLine($"  {family.FamilyId} [{family.DocumentedDisposition}]: {family.Discovered} = {family.Imported} imported, {family.Unused} unused, {family.SourceGap} source-gap, {family.RequiredPending} pending, {family.Unresolved} unresolved, {family.Excluded} excluded, {family.Malformed} malformed");
        }

        Console.WriteLine($"manifest: {args["--output"]}");
        Console.WriteLine($"digest: {ContentDigest.Compute(bytes).Value}");
        SourceInventoryReconciliation reconciliation = SourceInventoryReconciler.Reconcile(inventoryFile, manifest.Records, args.Switch("--update-inventory"));
        foreach (string line in reconciliation.Drift) Console.WriteLine($"inventory drift: {line}");
        foreach (string line in reconciliation.Unreconciled.Take(20)) Console.WriteLine($"inventory unresolved: {line}");

        // The status line is chosen from what reconciliation actually did, so it cannot announce an update
        // that was refused. A requested update that did not happen is a failure the caller has to see.
        Console.Error.WriteLine(reconciliation switch
        {
            { IsClean: true } => "inventory: documented dispositions match the supplied tree",
            { Updated: true } => $"inventory: documented dispositions updated: {reconciliation.Drift.Count}",
            { UpdateBlocked: true } => $"inventory: the inventory file was NOT rewritten — unresolved documented rows: {reconciliation.Unreconciled.Count}, disagreements remaining: {reconciliation.Drift.Count}",
            { Unreconciled.Count: > 0 } => $"inventory: unresolved documented rows: {reconciliation.Unreconciled.Count}, disagreements: {reconciliation.Drift.Count}; --update-inventory cannot resolve an unresolved row",
            _ => $"inventory: disagreements to record: {reconciliation.Drift.Count} (rerun with --update-inventory)",
        });
        return reconciliation.UpdateBlocked ? 1 : 0;
    }
}
