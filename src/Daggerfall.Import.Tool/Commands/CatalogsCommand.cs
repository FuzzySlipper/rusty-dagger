using System.Text;
using System.Text.Json.Nodes;
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
        [Options.Arena2, Options.Inventory, Options.Authored, Options.Pack, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        IReadOnlyList<SourceInventoryRow> inventory = Options.ReadInventory(args);
        // The vocabulary, actors and items are authored; the catalogs built from them are imported.
        JsonObject authored = PayloadFiles.ReadAuthored(args["--authored"]);
        JsonNode vocabulary = authored["vocabulary"]!;
        List<string> Ids(string property) => [.. authored[property]!.AsArray().Select(value => value!["id"]!.GetValue<string>())];
        List<(string FileName, byte[] Bytes)> careers = [.. Directory
            .EnumerateFiles(args["--arena2"], "CLASS*.CFG")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), File.ReadAllBytes(path)))];
        DaggerfallCatalogs catalogs = DaggerfallCatalogBuilder.Build(
            inventory,
            [.. vocabulary["attributes"]!.AsArray().Select(value => value!.GetValue<string>())],
            [.. vocabulary["skills"]!.AsArray().Select(value => value!.GetValue<string>())],
            careers,
            // The player is an actor identity but not an enemy a catalog references.
            [.. Ids("actors").Where(id => id != "player")],
            Ids("items"));
        byte[] section = DaggerfallCatalogSerializer.Serialize(catalogs, inventory.Select(row => row.PathOrPattern).ToHashSet(StringComparer.Ordinal));

        Console.WriteLine($"catalogs: {catalogs.Races.Count} races, {catalogs.Careers.Count} careers, {catalogs.Attributes.Count} attributes, {catalogs.Skills.Count} skills, {catalogs.Resistances.Count} elements, {catalogs.Enemies.Count} enemy references, {catalogs.ItemTemplates.Count} item-template references");
        foreach (DaggerfallCareerRecord career in catalogs.Careers)
        {
            Console.WriteLine($"  {career.Id} '{career.Name}' hp/level {career.HitPointsPerLevel} primary {string.Join('/', career.PrimarySkills)} source {career.Source.Path}");
        }

        if (!args.Switch("--update")) return Options.ReportOnly("these catalogs");
        PayloadFiles.WriteSections(args["--pack"], new Dictionary<string, string>(StringComparer.Ordinal) { ["catalogs"] = Encoding.UTF8.GetString(section) });
        return 0;
    }
}
