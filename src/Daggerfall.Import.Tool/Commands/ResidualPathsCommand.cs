using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Classifies every residual (CNT-027) source path against the documented inventory: which bounded family
/// it belongs to, what reads it, and what happened to it.
/// </summary>
internal static class ResidualPathsCommand
{
    public static ToolCommand Command { get; } = new("residual-paths", [Options.Arena2, Options.Inventory], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        Dictionary<string, string> documented = [];
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [];
        foreach (SourceInventoryRow row in Options.ReadInventory(args)
            .Where(row => row.RowType == "file" && row.FamilyId == "CNT-027")
            .OrderBy(row => row.PathOrPattern, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(row.PathOrPattern);
            string path = Path.Combine(arena2, name);
            if (!File.Exists(path)) throw new InvalidOperationException($"The documented residual path '{name}' is not supplied by '{arena2}'.");

            // The documented disposition travels with the path: a file the inventory already imports has a
            // consumer this classification cannot see for itself.
            documented[name] = row.Disposition;
            sources.Add((name, File.ReadAllBytes(path)));
        }

        ResidualSourceInventory inventory = ResidualSourceInventory.Enumerate(sources, Options.CorpusLabel(arena2), documented);
        Console.WriteLine($"residual paths: {inventory.Files.Count} documented and classified, {inventory.Imported.Count()} already imported, {inventory.Unused.Count()} readable with no consumer, {inventory.Malformed.Count()} refused by their family's reader, {inventory.Unresolved.Count()} with no reader in this repository");
        foreach (IGrouping<string, ResidualSourceRecord> family in inventory.Files.GroupBy(file => file.Family, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            ResidualSourceRecord first = family.First();
            Console.WriteLine($"  {family.Key,-4} {family.Count(),3}  reader=[{(first.Reader.Length == 0 ? "none" : first.Reader)}] donor=[{(first.DonorReader.Length == 0 ? "none" : first.DonorReader)}]{(first.Documented ? string.Empty : "  (not in the documented family list)")}");
        }

        foreach (ResidualSourceRecord refused in inventory.Malformed)
        {
            Console.WriteLine($"  refused {refused.Path}: {refused.Note}");
        }

        Console.WriteLine($"families: {inventory.UndocumentedFamilies.Count} supplied but undocumented [{string.Join(", ", inventory.UndocumentedFamilies)}], {inventory.MissingDocumentedFamilies.Count} documented but not supplied [{string.Join(", ", inventory.MissingDocumentedFamilies)}]");
        return 0;
    }
}
