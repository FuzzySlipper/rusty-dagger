using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

internal static class SourceCoverageCommand
{
    internal static ToolCommand Command { get; } = new("source-coverage", [Options.Arena2, Options.Inventory,
        CommandOption.Required("--repository", "DIR"), CommandOption.Required("--output", "FILE")], Run);
    private static int Run(CommandArguments args)
    {
        string repository = Path.GetFullPath(args["--repository"]);
        byte[] csv = File.ReadAllBytes(args["--inventory"]);
        var inventory = SourceManifestBuilder.ReadInventory(csv);
        foreach (int number in Enumerable.Range(1, 28))
            if (!inventory.Any(row => row.RowType == "family" && row.FamilyId == $"CNT-{number:D3}"))
                throw new InvalidOperationException($"The source inventory omits required family CNT-{number:D3}.");
        string[] files = [.. new[] { "content/worldrpg", "import-records" }.SelectMany(directory =>
            Directory.EnumerateFiles(Path.Combine(repository, directory), "*.json", SearchOption.AllDirectories))
            .Where(file => Path.GetFullPath(file) != Path.GetFullPath(args["--output"]))];
        var inputs = SourceCoverageInputReader.Read(repository, files, inventory);
        string[] imported = [.. inputs.Consumers.Where(value => !value.ContextOnly && value.SourcePath.StartsWith("arena2/", StringComparison.Ordinal)).Select(value => value.SourcePath["arena2/".Length..]).Distinct()];
        var request = new SourceManifestRequest("arena2", "data/content-source-manifest.csv", args["--arena2"], imported, [],
            [.. inventory.Where(row => row.Disposition == "excluded").Select(row => row.PathOrPattern.StartsWith("arena2/", StringComparison.Ordinal) ? row.PathOrPattern["arena2/".Length..] : row.PathOrPattern)]);
        var manifest = SourceManifestBuilder.Scan(request, csv);
        var report = SourceCoverageReconciler.Build(manifest, inputs.Consumers, inputs.Quests, inputs.Unsupported, inventory);
        PayloadFiles.WriteFile(args["--output"], SourceCoverageReconciler.Serialize(report));
        Console.WriteLine($"source coverage: {report.Records.Count} records, {report.Families.Count} families, {report.QuestHandoffs.Count} original quest handoffs, {report.Unsupported.Count} unsupported media families");
        foreach (var family in report.Families) Console.WriteLine($"  {family.FamilyId}: {family.Imported} imported, {family.RequiredPending} pending, {family.Unused} unused, {family.Duplicate} duplicate, {family.Excluded} excluded, {family.Malformed} malformed, {family.SourceGap} source-gap, {family.Unresolved} unresolved");
        foreach (string error in report.Errors) Console.Error.WriteLine(error);
        Console.WriteLine("Runtime parity is not certified; archive citations do not establish individual record normalization.");
        return report.IsReconciled ? 0 : 1;
    }
}
