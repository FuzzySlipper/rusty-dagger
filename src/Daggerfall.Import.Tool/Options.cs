using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool;

/// <summary>The options several commands share, spelled once.</summary>
internal static class Options
{
    public static readonly CommandOption Arena2 = CommandOption.Required("--arena2", "SOURCE_DIR");
    // Each staged option defaults to what `import` gives it, so a command rerun alone updates the staging tree.
    public static readonly CommandOption Pack = CommandOption.Staged("--pack", "IMPORTED.json", ImportLayout.ImportedPayload);
    public static readonly CommandOption ContentRoot = CommandOption.Staged("--out", "CONTENT_ROOT", ImportLayout.Staging);
    public static readonly CommandOption Group = CommandOption.Staged("--group", "NAME", "worldrpg");
    public static readonly CommandOption Update = CommandOption.Switch("--update");

    /// <summary>
    /// The logical label of an Arena2 source: the documented corpus path, whatever directory the caller
    /// supplied the bytes from, so a published section does not change with where the operator keeps the corpus.
    /// </summary>
    public static string Arena2Label(string relativePath) => PublishedSourcePath.Arena2(relativePath);

    /// <summary>The report a command prints when it was run without <c>--update</c>.</summary>
    public static int ReportOnly(string what)
    {
        Console.WriteLine($"not written (rerun with --update to publish {what})");
        return 0;
    }

    /// <summary>The corpus directory's own name, the label an enumeration's errors cite.</summary>
    public static string CorpusLabel(string arena2Directory)
    {
        string label = Path.GetFileName(Path.TrimEndingDirectorySeparator(arena2Directory));
        return string.IsNullOrEmpty(label) ? arena2Directory : label;
    }
}
