using System.Diagnostics;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Bakes the wilderness into its eroded relief from WOODS.WLD and the climate and locations the import has published.
/// The relief and its rivers are raw files under the content root, beside the cell samples, which the walked and distant ground read.
/// </summary>
internal static class ReliefCommand
{
    public static ToolCommand Command { get; } = new("relief", [Options.Arena2, Options.Pack, Options.ContentRoot, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        WoodsFile woods = WoodsReader.Read(File.ReadAllBytes(Path.Combine(args["--arena2"], "WOODS.WLD")), Options.Arena2Label("WOODS.WLD"));
        ReliefInput input = ReliefBake.Input(woods, PayloadFiles.ReadSection<DaggerfallClimateGrid>(args["--pack"], "climate"),
            PayloadFiles.ReadSection<DaggerfallLocations>(args["--pack"], "locations"));
        Stopwatch clock = Stopwatch.StartNew();
        DaggerfallRelief relief = ReliefBake.Bake(input, ReliefBake.DefaultStages, report: Console.WriteLine);
        Console.WriteLine($"relief: {relief.Width} by {relief.Height} nodes, {input.Places.Count} locations, in {clock.Elapsed.TotalSeconds:F0} s");
        if (!args.Switch("--update")) return Options.ReportOnly("this relief");
        string root = Path.GetFullPath(args[Options.ContentRoot.Name]);
        PayloadFiles.WriteFile(Path.Combine(root, DaggerfallRelief.RelativePath), relief.Encode());
        PayloadFiles.WriteFile(Path.Combine(root, DaggerfallRivers.RelativePath), relief.Rivers.Encode());
        return 0;
    }
}
