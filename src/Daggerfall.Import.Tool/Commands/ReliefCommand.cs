using System.Diagnostics;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Relief;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Bakes the wilderness into its eroded relief from WOODS.WLD and the climate and locations the import has published.
/// The relief and its rivers are raw files under the content root, beside the cell samples, which the walked and distant ground read.
/// </summary>
/// <remarks>
/// With <c>--keep</c> (the import passes <see cref="ImportLayout.KeptRelief"/>) a bake is kept there with the record of
/// what it was baked from, and a later run whose record matches publishes the kept bake instead of baking for minutes;
/// otherwise it bakes and says which input changed. Without it, as when run by hand, it always bakes.
/// </remarks>
internal static class ReliefCommand
{
    public static ToolCommand Command { get; } = new("relief",
        [Options.Arena2, Options.Pack, Options.ContentRoot, CommandOption.Optional("--keep", "DIR"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        WoodsFile woods = WoodsReader.Read(File.ReadAllBytes(Path.Combine(args["--arena2"], "WOODS.WLD")), Options.Arena2Label("WOODS.WLD"));
        ReliefInput input = ReliefInputs.Read(woods, PayloadFiles.ReadSection<DaggerfallClimateGrid>(args["--pack"], "climate"),
            PayloadFiles.ReadSection<DaggerfallLocations>(args["--pack"], "locations"));
        string root = Path.GetFullPath(args[Options.ContentRoot.Name]);
        string? keep = args.Switch("--update") ? args.Optional("--keep") : null;
        ReliefRecord record = ReliefRecord.Of(input, ReliefBake.DefaultStages, ReliefBake.DefaultSourceSquareKilometres);
        if (keep is not null)
        {
            if (KeptRelief.Take(keep, record, out string why) is { } kept)
            {
                Publish(root, kept.Relief, kept.Rivers);
                Console.WriteLine($"relief: reused the bake kept in {keep}: same bake code ({record.Code[..12]}), settings, runtime, WOODS.WLD heights, climate rain and locations");
                return 0;
            }

            Console.WriteLine($"relief: baking, since {why}");
        }

        Stopwatch clock = Stopwatch.StartNew();
        DaggerfallRelief relief = ReliefBake.Bake(input, ReliefBake.DefaultStages, ReliefBake.DefaultSourceSquareKilometres, report: Console.WriteLine);
        Console.WriteLine($"relief: {relief.Width} by {relief.Height} nodes, {input.Places.Count} locations, in {clock.Elapsed.TotalSeconds:F0} s");
        if (!args.Switch("--update")) return Options.ReportOnly("this relief");
        byte[] reliefBytes = relief.Encode(), riversBytes = relief.Rivers.Encode();
        Publish(root, reliefBytes, riversBytes);
        if (keep is not null)
        {
            KeptRelief.Keep(keep, record, reliefBytes, riversBytes);
            Console.WriteLine($"relief: kept the bake in {keep} for the next import");
        }

        return 0;
    }

    private static void Publish(string root, byte[] relief, byte[] rivers)
    {
        PayloadFiles.WriteFile(Path.Combine(root, DaggerfallRelief.RelativePath), relief);
        PayloadFiles.WriteFile(Path.Combine(root, DaggerfallRivers.RelativePath), rivers);
    }
}
