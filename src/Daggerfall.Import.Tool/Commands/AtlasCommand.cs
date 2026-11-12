using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Daggerfall.Import.Relief;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Draws the travel atlas from the relief, its rivers and the climate and politic grids, and publishes it as a pyramid
/// of tiles with a manifest of its levels and an inventory of its own, beside the relief it is drawn from.
/// </summary>
internal static class AtlasCommand
{
    public static ToolCommand Command { get; } = new("atlas", [Options.Pack, Options.ContentRoot, Options.Update], Run);

    /// <summary>Where the atlas is published, below the content root; the ruleset reads the manifest and inventory here.</summary>
    internal const string Root = "worldrpg/media/atlas";
    internal const string Manifest = Root + "/atlas.json";
    internal const string InventoryPath = "worldrpg/media/atlas-inventory.json";
    internal const string Generator = "daggerfall-import-tool atlas";

    private static int Run(CommandArguments args)
    {
        string root = Path.GetFullPath(args[Options.ContentRoot.Name]);
        AtlasInput input = AtlasBake.Input(
            File.ReadAllBytes(Path.Combine(root, DaggerfallRelief.RelativePath)), File.ReadAllBytes(Path.Combine(root, DaggerfallRivers.RelativePath)),
            PayloadFiles.ReadSection<DaggerfallClimateGrid>(args["--pack"], "climate"), PayloadFiles.ReadSection<DaggerfallPoliticGrid>(args["--pack"], "politic"));
        Stopwatch clock = Stopwatch.StartNew();
        List<(string Path, byte[] Bytes, string MediaId)> tiles = [.. AtlasBake.Tiles(input, AtlasPalette.Painted).Select(tile =>
            ($"{Root}/{tile.Level}/{tile.Column}-{tile.Row}.png", DeterministicPngEncoder.EncodeRgba8(tile.Size, tile.Size, tile.Rgba), $"atlas.{tile.Level}.{tile.Column}.{tile.Row}"))];
        IReadOnlyList<AtlasLevel> levels = AtlasBake.Levels(input.Width, input.Height);
        Console.WriteLine($"atlas: {levels.Count} levels, {tiles.Count} tiles, {tiles.Sum(tile => tile.Bytes.Length) / 1e6:F1} MB, in {clock.Elapsed.TotalSeconds:F0} s");
        if (!args.Switch("--update")) return Options.ReportOnly("this atlas");

        // A rebake replaces the atlas whole, so a level that shrank leaves no stale tile behind.
        string published = Path.Combine(root, Root.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(published)) Directory.Delete(published, recursive: true);
        foreach ((string path, byte[] bytes, _) in tiles) PayloadFiles.WriteFile(Path.Combine(root, path), bytes);
        JsonObject manifest = new()
        {
            ["tileSize"] = AtlasBake.TileSize,
            ["worldWidth"] = input.Width / input.NodesPerPixel,
            ["worldHeight"] = input.Height / input.NodesPerPixel,
            ["levels"] = new JsonArray([.. levels.Select(level => (JsonNode)new JsonObject
            {
                ["level"] = level.Level, ["width"] = level.Width, ["height"] = level.Height, ["columns"] = level.Columns, ["rows"] = level.Rows,
            })]),
        };
        PayloadFiles.WriteFile(Path.Combine(root, Manifest), Encoding.UTF8.GetBytes(manifest.ToJsonString(PublishedJson.Section) + "\n"));
        PayloadFiles.WriteFile(Path.Combine(root, InventoryPath),
            ArtifactInventory.Write(Generator, tiles.Select(tile => ArtifactInventory.Entry(tile.Path, tile.Bytes, tile.MediaId))));
        return 0;
    }
}
