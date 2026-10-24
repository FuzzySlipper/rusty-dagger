using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reconciles the documented inventory with every producer citation in the content the product sees and in
/// the import records.
/// </summary>
/// <remarks>
/// The product sees the repository's tracked <c>content/</c> with the import's generated content over it, so a
/// generated root (<c>--content</c>, the import's staging tree) is read over <c>content/</c>: a file it
/// holds replaces the repository's file at the same path. Without one the repository's <c>content/</c> is read
/// alone, as a loose generated tree leaves it. Every artifact is reported by its repository path
/// (<c>content/worldrpg/...</c>, <c>import-records/...</c>) wherever it was read from.
/// </remarks>
internal static class SourceCoverageCommand
{
    internal static ToolCommand Command { get; } = new("source-coverage", [Options.Arena2, Options.Inventory,
        CommandOption.Required("--repository", "DIR"), CommandOption.Required("--output", "FILE"),
        CommandOption.Optional("--content", "GENERATED_CONTENT_ROOT"), CommandOption.Optional("--import-records", "DIR")], Run);
    private static int Run(CommandArguments args)
    {
        string repository = Path.GetFullPath(args["--repository"]);
        byte[] csv = File.ReadAllBytes(args["--inventory"]);
        var inventory = SourceManifestBuilder.ReadInventory(csv);
        foreach (int number in Enumerable.Range(1, 28))
            if (!inventory.Any(row => row.RowType == "family" && row.FamilyId == $"CNT-{number:D3}"))
                throw new InvalidOperationException($"The source inventory omits required family CNT-{number:D3}.");
        string output = Path.GetFullPath(args["--output"]);
        Dictionary<string, string> content = Artifacts(Path.Combine(repository, "content"), "worldrpg");
        if (args.Optional("--content") is { } generated)
            foreach ((string path, string file) in Artifacts(Path.GetFullPath(generated), "worldrpg")) content[path] = file;
        string records = Path.GetFullPath(args.Optional("--import-records") ?? Path.Combine(repository, "import-records"));
        SourceCoverageArtifact[] artifacts = [
            .. content.Select(pair => new SourceCoverageArtifact("content/" + pair.Key, pair.Value)),
            .. Artifacts(records, "").Select(pair => new SourceCoverageArtifact("import-records/" + pair.Key, pair.Value))];
        var inputs = SourceCoverageInputReader.Read(artifacts.Where(artifact => Path.GetFullPath(artifact.File) != output), inventory);
        string[] imported = [.. inputs.Consumers.Where(value => !value.ContextOnly && value.SourcePath.StartsWith("arena2/", StringComparison.Ordinal)).Select(value => value.SourcePath["arena2/".Length..]).Distinct()];
        var request = new SourceManifestRequest("arena2", "data/content-source-manifest.csv", args["--arena2"], imported, [],
            [.. inventory.Where(row => row.Disposition == "excluded").Select(row => row.PathOrPattern.StartsWith("arena2/", StringComparison.Ordinal) ? row.PathOrPattern["arena2/".Length..] : row.PathOrPattern)]);
        var manifest = SourceManifestBuilder.Scan(request, csv);
        var report = SourceCoverageReconciler.Build(manifest, inputs.Consumers, inputs.Quests, inputs.Unsupported, inventory);
        var reconciliation = SourceInventoryReconciler.Reconcile(args["--inventory"],
            report.Records.Select(record => record.Source with { Disposition = record.Disposition }).ToArray(), update: false);
        foreach (string line in reconciliation.Drift) Console.Error.WriteLine($"inventory drift: {line}");
        foreach (string line in reconciliation.Unreconciled) Console.Error.WriteLine($"inventory unresolved: {line}");
        PayloadFiles.WriteFile(args["--output"], SourceCoverageReconciler.Serialize(report));
        Console.WriteLine($"source coverage: {report.Records.Count} records, {report.Families.Count} families, {report.QuestHandoffs.Count} original quest handoffs, {report.Unsupported.Count} unsupported media families");
        foreach (var family in report.Families) Console.WriteLine($"  {family.FamilyId}: {family.Imported} imported, {family.RequiredPending} pending, {family.Unused} unused, {family.Duplicate} duplicate, {family.Excluded} excluded, {family.Malformed} malformed, {family.SourceGap} source-gap, {family.Unresolved} unresolved");
        foreach (string error in report.Errors) Console.Error.WriteLine(error);
        Console.WriteLine("Runtime parity is not certified; archive citations do not establish individual record normalization.");
        return report.IsReconciled ? 0 : 1;
    }

    /// <summary>Every JSON file below one directory of a root, by its root-relative path.</summary>
    private static Dictionary<string, string> Artifacts(string root, string directory)
    {
        string start = Path.Combine(root, directory);
        return Directory.Exists(start)
            ? Directory.EnumerateFiles(start, "*.json", SearchOption.AllDirectories)
                .ToDictionary(file => Path.GetRelativePath(root, file).Replace('\\', '/'), file => file, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
