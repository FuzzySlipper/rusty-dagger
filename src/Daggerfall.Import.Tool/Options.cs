using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool;

/// <summary>The options several commands share, spelled once.</summary>
internal static class Options
{
    public static readonly CommandOption Arena2 = CommandOption.Required("--arena2", "SOURCE_DIR");
    public static readonly CommandOption Pack = CommandOption.Required("--pack", "IMPORTED.json");
    public static readonly CommandOption Authored = CommandOption.Required("--authored", "BASE.json");
    public static readonly CommandOption Inventory = CommandOption.Required("--inventory", "CSV");
    public static readonly CommandOption Records = CommandOption.Required("--records", "RECORDS.json");
    public static readonly CommandOption ContentRoot = CommandOption.Required("--out", "CONTENT_ROOT");
    public static readonly CommandOption Group = CommandOption.Required("--group", "NAME");
    public static readonly CommandOption Update = CommandOption.Switch("--update");

    /// <summary>
    /// The logical label of an Arena2 source: the documented corpus path, whatever directory the caller
    /// supplied the bytes from, so a published section does not change with where the operator keeps the corpus.
    /// </summary>
    public static string Arena2Label(string relativePath) => $"{SourceManifestPublication.Arena2LogicalRoot}/{relativePath}";

    /// <summary>The documented inventory's rows.</summary>
    public static IReadOnlyList<SourceInventoryRow> ReadInventory(CommandArguments args) =>
        SourceManifestBuilder.ReadInventory(File.ReadAllBytes(args["--inventory"]));

    /// <summary>The report a command prints when it was run without <c>--update</c>.</summary>
    public static int ReportOnly(string what)
    {
        Console.WriteLine($"not written (rerun with --update to publish {what})");
        return 0;
    }

    /// <summary>
    /// Checks that a family's documented file rows and the files the corpus supplies name the same set,
    /// refusing a disagreement in either direction.
    /// </summary>
    public static int CheckDocumentedFamily(IReadOnlyCollection<string> documented, IReadOnlyCollection<string> supplied, string noun)
    {
        string[] missing = [.. supplied.Where(path => !documented.Contains(path, StringComparer.Ordinal))];
        string[] extra = [.. documented.Where(path => !supplied.Contains(path, StringComparer.Ordinal))];
        if (missing.Length != 0 || extra.Length != 0)
        {
            throw new InvalidOperationException($"The documented inventory and the supplied corpus disagree; supplied but undocumented: [{string.Join(", ", missing)}], documented but not supplied: [{string.Join(", ", extra)}].");
        }

        Console.WriteLine($"inventory: {documented.Count} documented {noun} match the corpus");
        return 0;
    }

    /// <summary>The file leaves of a family's documented file rows.</summary>
    public static HashSet<string> DocumentedFiles(IReadOnlyList<SourceInventoryRow> rows, Func<SourceInventoryRow, bool> family) =>
        [.. rows.Where(row => row.RowType == "file" && family(row)).Select(row => Path.GetFileName(row.PathOrPattern))];

    /// <summary>The corpus directory's own name, the label an inventory's records cite.</summary>
    public static string CorpusLabel(string arena2Directory)
    {
        string label = Path.GetFileName(Path.TrimEndingDirectorySeparator(arena2Directory));
        return string.IsNullOrEmpty(label) ? arena2Directory : label;
    }
}
