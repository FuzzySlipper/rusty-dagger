using System.Text;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Builds the normalized reference catalogs from the supplied careers and the documented inventory, reports
/// what it found, and writes them into the imported payload when asked.
/// </summary>
internal static class CatalogsCommand
{
    public static ToolCommand Command { get; } = new("catalogs",
        [Options.Arena2, Options.Inventory, Options.Pack, CommandOption.Required("--donor-races", "RaceTemplate.cs"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        IReadOnlyList<SourceInventoryRow> inventory = Options.ReadInventory(args);
        List<(string FileName, byte[] Bytes)> careers = [.. Directory
            .EnumerateFiles(args["--arena2"], "CLASS*.CFG")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), File.ReadAllBytes(path)))];
        DaggerfallCatalogs catalogs = DaggerfallCatalogBuilder.Build(
            inventory,
            careers,
            File.ReadAllText(args["--donor-races"]),
            File.ReadAllBytes(Path.Combine(args["--arena2"], "CLASSES.DAT")),
            DaggerfallTextBuilder.Build(File.ReadAllBytes(Path.Combine(args["--arena2"], "TEXT.RSC")), "arena2/TEXT.RSC", inventory, "en"));
        byte[] section = DaggerfallCatalogSerializer.Serialize(catalogs, inventory.Select(row => row.PathOrPattern).ToHashSet(StringComparer.Ordinal));

        Console.WriteLine($"catalogs: {catalogs.Races.Count} races, {catalogs.Careers.Count} careers, {catalogs.Attributes.Count} attributes, {catalogs.Skills.Count} skills, {catalogs.Resistances.Count} elements");
        foreach (DaggerfallCareerRecord career in catalogs.Careers)
        {
            Console.WriteLine($"  {career.Id} '{career.Name}' hp/level {career.HitPointsPerLevel} primary skills {string.Join('/', career.PrimarySkills)} source {career.Source.Path}");
        }

        if (!args.Switch("--update")) return Options.ReportOnly("these catalogs");
        PayloadFiles.WriteSections(args["--pack"], new Dictionary<string, string>(StringComparer.Ordinal) { ["catalogs"] = Encoding.UTF8.GetString(section) });
        return 0;
    }
}
