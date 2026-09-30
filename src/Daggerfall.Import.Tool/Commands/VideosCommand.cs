using System.Security.Cryptography;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Records cinematic source identities: digests tell files apart and donor callers bind the opening three
/// VIDs and the sixteen Daedric FLCs, while the rest stay unresolved rather than assuming an ending mapping.
/// The cinematic-media command converts the bound files afterwards.
/// </summary>
internal static class VideosCommand
{
    public static ToolCommand Command { get; } = new("videos", [Options.Arena2, Options.Pack, Options.Inventory, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        List<(string FileName, DaggerfallCinematicKind Kind, long ByteLength, string Digest)> files = [];
        foreach (string path in Directory.EnumerateFiles(args["--arena2"]).Order(StringComparer.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(path);
            DaggerfallCinematicKind? kind = fileName.EndsWith(".VID", StringComparison.OrdinalIgnoreCase)
                ? DaggerfallCinematicKind.Vid
                : fileName.EndsWith(".FLC", StringComparison.OrdinalIgnoreCase) ? DaggerfallCinematicKind.Flc : null;
            if (kind is null) continue;
            byte[] bytes = File.ReadAllBytes(path);
            files.Add((fileName, kind.Value, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
        }

        DaggerfallCinematicPack pack = DaggerfallCinematicPackBuilder.Build(files, SourceManifestPublication.Arena2LogicalRoot, Options.ReadInventory(args));
        Console.WriteLine($"videos: {pack.Cinematics.Count} cinematics, {pack.Cinematics.Count(record => record.Binding == DaggerfallCinematicBinding.Bound)} bound");
        if (!args.Switch("--update")) return Options.ReportOnly("these identities");
        PayloadFiles.WriteSection(args["--pack"], "cinematics", pack);
        return 0;
    }
}
