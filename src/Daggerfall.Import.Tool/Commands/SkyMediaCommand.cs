using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the source-backed daytime and night sky media closure. SKY##.DAT remains a source input;
/// this command emits ordinary PNG resources plus a typed manifest that retains the source frame and
/// palette/hemisphere facts needed by the runtime selector. Night images are paired with the
/// donor's explicit NIGHTSKY.COL source and deterministic star policy.
/// </summary>
internal static class SkyMediaCommand
{
    public static ToolCommand Command { get; } = new("sky-media", [Options.Arena2, Options.ContentRoot, Options.Group, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        SkyMediaSource[] day = Enumerable.Range(0, SkyFileDecoder.FileCount)
            .Select(index =>
            {
                string file = $"SKY{index:00}.DAT";
                return new SkyMediaSource(index, Options.Arena2Label(file), PayloadFiles.ReadBounded(Path.Combine(arena2, file), $"sky source {file}"));
            })
            .ToArray();

        string paletteName = NightSkyPaletteSource.FileName;
        NightSkyPaletteSource nightPalette = new(
            Options.Arena2Label(paletteName),
            PayloadFiles.ReadBounded(Path.Combine(arena2, paletteName), $"night sky palette {paletteName}"));
        NightSkyMediaSource[] night = Enumerable.Range(0, 4)
            .Select(index =>
            {
                string file = $"NITE{index:00}I0.IMG";
                return new NightSkyMediaSource(index, Options.Arena2Label(file),
                    PayloadFiles.ReadBounded(Path.Combine(arena2, file), $"night sky source {file}"));
            })
            .ToArray();

        SkyMediaPublication publication = SkyMediaPublication.Create(day, night, nightPalette);
        Console.WriteLine($"sky media: {publication.Manifest.Sources.Count} day sources, {publication.Manifest.Resources.Count} day panoramas, {publication.Manifest.NightResources.Count} night backgrounds, {publication.Artifacts.Count} artifacts");
        Console.WriteLine($"sky curve: {string.Join(", ", SkyMediaPublication.DaylightFrameCurve.Select(knot => $"{knot.Time:0.###}->{knot.Value:0.###}"))}");
        if (!args.Switch("--update")) return Options.ReportOnly("these sky artifacts");

        string outRoot = Path.Combine(args["--out"], args["--group"]);
        PayloadFiles.WriteArtifacts(outRoot, publication.Artifacts);
        Console.WriteLine($"content: wrote {publication.Artifacts.Count} sky artifacts under {outRoot}");
        return 0;
    }
}
