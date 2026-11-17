using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reconciles the documented inventory with every producer citation in the content the product sees and in
/// the import records.
/// </summary>
/// <remarks>
/// The product sees the repository's tracked <c>content/</c> with the import's generated content over it, so the
/// generated root (<c>--content</c>, by default the import's staging tree) is read over <c>content/</c>
/// (<see cref="SourceCoverageArtifact.Collect"/>). This writes only the report; the inventory's disposition column
/// is written from the same join by <c>Daggerfall.Source.Tool</c>'s <c>source-inventory --update-inventory</c>.
/// </remarks>
internal static class SourceCoverageCommand
{
    internal static ToolCommand Command { get; } = new("source-coverage", [Options.Arena2, Options.Inventory,
        CommandOption.Staged("--repository", "DIR", "."), CommandOption.Staged("--output", "FILE", $"{ImportLayout.Records}/source-coverage.json"),
        CommandOption.Staged("--content", "GENERATED_CONTENT_ROOT", ImportLayout.Staging), CommandOption.Staged("--import-records", "DIR", ImportLayout.Records)], Run);
    private static int Run(CommandArguments args)
    {
        string output = Path.GetFullPath(args["--output"]);
        var artifacts = SourceCoverageArtifact.Collect(Path.Combine(Path.GetFullPath(args["--repository"]), "content"),
            Path.GetFullPath(args["--content"]), Path.GetFullPath(args["--import-records"]));
        var report = SourceCoverageReconciler.Reconcile(artifacts.Where(artifact => Path.GetFullPath(artifact.File) != output),
            args["--arena2"], ImportLayout.Inventory, File.ReadAllBytes(args["--inventory"]));
        var reconciliation = SourceInventoryReconciler.Reconcile(args["--inventory"], report, update: false);
        foreach (string line in reconciliation.Drift) Console.Error.WriteLine($"inventory drift: {line}");
        foreach (string line in reconciliation.Unreconciled) Console.Error.WriteLine($"inventory unresolved: {line}");
        foreach (string line in reconciliation.Refused) Console.Error.WriteLine($"inventory refused: {line}");
        PayloadFiles.WriteFile(args["--output"], SourceCoverageReconciler.Serialize(report));
        Console.WriteLine($"source coverage: {report.Records.Count} records, {report.Families.Count} families, {report.Unsupported.Count} unsupported media families");
        foreach (var family in report.Families) Console.WriteLine($"  {family.FamilyId}: {family.Imported} imported, {family.RequiredPending} pending, {family.Unused} unused, {family.Duplicate} duplicate, {family.Excluded} excluded, {family.Malformed} malformed, {family.SourceGap} source-gap, {family.Unresolved} unresolved");
        foreach (string error in report.Errors) Console.Error.WriteLine(error);
        Console.WriteLine("Runtime parity is not certified; archive citations do not establish individual record normalization.");
        return report.IsReconciled ? 0 : 1;
    }
}
