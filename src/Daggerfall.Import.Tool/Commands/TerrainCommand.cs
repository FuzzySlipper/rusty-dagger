using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reads the classic wilderness file into the imported payload when asked, so an exterior consumer resolves a
/// map pixel by the coordinates the source tiles rather than by a second spatial system. The heightmap tiles
/// rows; the cell samples are written as one raw file under the content root, which the section names; the prefix
/// bytes no reader consumes stay documented domains rather than republished bytes.
/// </summary>
internal static class TerrainCommand
{
    public static ToolCommand Command { get; } = new("terrain", [Options.Arena2, Options.Pack, Options.ContentRoot, Options.Inventory, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        DaggerfallTerrain terrain = DaggerfallTerrainBuilder.Build(
            File.ReadAllBytes(Path.Combine(args["--arena2"], "WOODS.WLD")), Options.Arena2Label("WOODS.WLD"), Options.ReadInventory(args));
        Console.WriteLine($"terrain: {terrain.Heightmap.Count} heightmap rows, {terrain.CellCount} cells from {terrain.CellBase} stride {terrain.CellStride}, {terrain.Prefix.Count} prefix domains");
        if (!args.Switch("--update")) return Options.ReportOnly("this terrain");
        PayloadFiles.WriteFile(Path.Combine(Path.GetFullPath(args[Options.ContentRoot.Name]), terrain.Samples), terrain.SampleBytes);
        PayloadFiles.WriteSection(args["--pack"], "terrain", terrain);
        return 0;
    }
}
