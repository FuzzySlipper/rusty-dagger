using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Source.Tool.Relief;

namespace Daggerfall.Source.Tool.Commands;

/// <summary>
/// Bakes the wilderness into its eroded relief and rivers from WOODS.WLD and the climate and locations the import
/// published, and writes them into the base pack's terrain folder (<see cref="ImportLayout.TerrainBake"/>) for the pack
/// to commit, with the digest of the WOODS.WLD they were baked from: the game warns when the player's differs, since
/// the rivers would then sit on other heights. Run it after changing the bake or its stages; it takes about 7 minutes.
/// The import's atlas is drawn from these files, so a new bake moves the importer revision.
/// </summary>
internal static class ReliefBakeCommand
{
    /// <summary>The bake's record beside the relief and rivers: the WOODS.WLD digest the game compares with the player's.</summary>
    internal const string RecordFile = "relief.json";

    public static ToolCommand Command { get; } = new("relief-bake",
    [
        Options.Arena2,
        CommandOption.Staged("--imported", "IMPORTED.json", ImportLayout.ImportedPayload),
        CommandOption.Staged("--terrain", "TERRAIN_DIR", ImportLayout.TerrainBake),
        Options.Update,
    ], Run, Program.Tool);

    private static int Run(CommandArguments args)
    {
        byte[] woodsBytes = File.ReadAllBytes(Path.Combine(args[Options.Arena2.Name], "WOODS.WLD"));
        WoodsFile woods = WoodsReader.Read(woodsBytes, Options.Arena2Label("WOODS.WLD"));
        ReliefInput input = ReliefInputs.Read(woods, PayloadFiles.ReadSection<DaggerfallClimateGrid>(args["--imported"], "climate"),
            PayloadFiles.ReadSection<DaggerfallLocations>(args["--imported"], "locations"));
        Stopwatch clock = Stopwatch.StartNew();
        DaggerfallRelief relief = ReliefBake.Bake(input, ReliefBake.DefaultStages, ReliefBake.DefaultSourceSquareKilometres, report: Console.WriteLine);
        Console.WriteLine($"relief-bake: {relief.Width} by {relief.Height} nodes, {input.Places.Count} locations, in {clock.Elapsed.TotalSeconds:F0} s");
        if (!args.Switch("--update")) return Options.ReportOnly("this relief");

        string terrain = Path.GetFullPath(args["--terrain"]);
        Directory.CreateDirectory(terrain);
        File.WriteAllBytes(Path.Combine(terrain, "relief.bin"), relief.Encode());
        File.WriteAllBytes(Path.Combine(terrain, "rivers.bin"), relief.Rivers.Encode());
        JsonObject record = new() { ["woods"] = Convert.ToHexStringLower(SHA256.HashData(woodsBytes)) };
        File.WriteAllText(Path.Combine(terrain, RecordFile), record.ToJsonString() + "\n");
        Console.WriteLine($"relief-bake: wrote {terrain}; commit it with the change that moved it, and route the data lane, since the atlas is drawn from it");
        return 0;
    }
}
