using System.Text.Json;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reads the climate and politic grids from their PAK files and writes them into the imported payload when
/// asked, so a terrain, weather or social consumer resolves a map pixel by the coordinates the source tiles
/// rather than by a policy inferred from a cell value.
/// </summary>
internal static class ClimateCommand
{
    public static ToolCommand Command { get; } = new("climate", [Options.Arena2, Options.Pack, Options.Inventory, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        IReadOnlyList<SourceInventoryRow> inventory = Options.ReadInventory(args);
        DaggerfallClimateGrid climate = DaggerfallWorldGridsBuilder.BuildClimate(File.ReadAllBytes(Path.Combine(arena2, "CLIMATE.PAK")), Options.Arena2Label("CLIMATE.PAK"), inventory);
        DaggerfallPoliticGrid politic = DaggerfallWorldGridsBuilder.BuildPolitic(File.ReadAllBytes(Path.Combine(arena2, "POLITIC.PAK")), Options.Arena2Label("POLITIC.PAK"), inventory);

        Console.WriteLine($"climate: {climate.Rows.Count} rows, {climate.Values.Count} distinct values ({climate.Values.Count(value => value.Disposition == DaggerfallClimateDisposition.Named)} named)");
        Console.WriteLine($"politic: {politic.Rows.Count} rows, {politic.Values.Count} distinct values ({politic.Values.Count(value => value.Disposition == DaggerfallPoliticDisposition.Region)} regions, {politic.Values.Count(value => value.Disposition == DaggerfallPoliticDisposition.Ocean)} ocean, {politic.Values.Count(value => value.Disposition == DaggerfallPoliticDisposition.Unresolved)} unresolved)");
        if (!args.Switch("--update")) return Options.ReportOnly("these grids");
        PayloadFiles.WriteSections(args["--pack"], new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["climate"] = JsonSerializer.Serialize(climate, PublishedJson.Section),
            ["politic"] = JsonSerializer.Serialize(politic, PublishedJson.Section),
        });
        return 0;
    }
}
