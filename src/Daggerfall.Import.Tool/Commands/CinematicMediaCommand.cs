using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Converts the bound cinematics of one kind into the product-wide cinematic media and records each
/// artifact in the imported payload's cinematics section. The section is replaced only after every
/// selected source converted.
/// </summary>
internal static class CinematicMediaCommand
{
    private const string LogicalRoot = "worldrpg/media/cinematics";

    public static ToolCommand Command { get; } = new("cinematic-media",
        [Options.Arena2, Options.Pack, Options.ContentRoot, CommandOption.Optional("--kind", "vid|flc"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        DaggerfallCinematicKind selection = args.Optional("--kind") switch
        {
            null or "vid" => DaggerfallCinematicKind.Vid,
            "flc" => DaggerfallCinematicKind.Flc,
            _ => throw args.Invalid("--kind must be vid or flc."),
        };
        bool update = args.Switch("--update");
        DaggerfallCinematicPack pack = PayloadFiles.ReadSection<DaggerfallCinematicPack>(args["--pack"], "cinematics");
        pack.Validate();
        DaggerfallCinematicRecord[] records = [.. pack.Cinematics];
        for (int index = 0; index < records.Length; index++)
        {
            DaggerfallCinematicRecord record = records[index];
            if (record.Kind != selection) continue;
            Console.WriteLine($"cinematic: {record.FileName} -> {CinematicMediaPublisher.ArtifactName(record.FileName)}{(update ? "" : " (plan only)")}");
            if (!update) continue;
            CinematicMediaArtifact artifact = CinematicMediaPublisher.Publish(Path.Combine(args["--arena2"], record.FileName), record,
                Path.Combine(args["--out"], LogicalRoot), LogicalRoot);
            records[index] = record with { Artifact = artifact };
            Console.WriteLine($"  {artifact.Width}x{artifact.Height}, {artifact.FrameCount} frames, {artifact.DurationSeconds:F3}s, {artifact.ByteLength} bytes, audio={artifact.HasAudio}");
        }

        if (update) PayloadFiles.WriteSection(args["--pack"], "cinematics", pack with { Cinematics = records });
        return 0;
    }
}
