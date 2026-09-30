using Daggerfall.Import.Audio;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the catalogue's music cues from a folder of donor song files, with the group's generated index.
/// </summary>
/// <remarks>
/// The donor's own installation keeps its songs outside the Arena2 archives, and a music pack that replaces
/// them is a local input rather than a published one, so the folder is named explicitly and every cue it
/// does not carry is reported. An absent folder publishes no music and says so: the product then plays no
/// score, which is a state the publication supports, and inventing a substitute track would play a song the
/// donor never played there. A regeneration needs every cue (<c>--require-all</c>): a folder that lacks one
/// would publish a smaller score and name fewer cues in every site, so the absence is refused by file name.
/// </remarks>
internal static class MusicMediaCommand
{
    public static ToolCommand Command { get; } = new("music-media",
        [Options.ContentRoot, CommandOption.Optional("--sound", "SOURCE_DIR"), CommandOption.Switch("--require-all"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        bool requireAll = args.Switch("--require-all");
        string? sound = args.Optional("--sound");
        if (requireAll && sound is null) throw args.Invalid("--require-all needs --sound.");
        List<ClassicMusicSource> sources = [];
        if (sound is null)
        {
            Console.WriteLine("music: no --sound folder was supplied, so no cue is published and the product plays no score.");
        }
        else if (File.Exists(sound))
        {
            throw args.Invalid($"--sound names the file '{sound}' rather than the folder holding the donor songs.");
        }
        else if (!Directory.Exists(sound))
        {
            if (requireAll) throw new DirectoryNotFoundException($"The music folder '{sound}' does not exist, and --require-all publishes every cue or nothing.");
            Console.WriteLine($"music: '{sound}' does not exist, so no cue is published and the product plays no score.");
        }
        else
        {
            foreach (ClassicMusicCue cue in ClassicMusicCatalogue.All)
            {
                string path = Path.Combine(sound, cue.SourceFile);
                if (File.Exists(path))
                {
                    sources.Add(new ClassicMusicSource(cue, File.ReadAllBytes(path)));
                    continue;
                }

                if (requireAll) throw new FileNotFoundException($"'{cue.SourceFile}' is not in '{sound}', and --require-all publishes cue '{cue.MediaId}' or nothing.", path);
                Console.WriteLine($"music: '{cue.SourceFile}' is not in '{sound}'; cue '{cue.MediaId}' is not published.");
            }
        }

        if (sources.Count == 0)
        {
            Console.WriteLine($"music: 0 of {ClassicMusicCatalogue.All.Count} cues published; nothing to write.");
            return 0;
        }

        ClassicMusicPublicationResult publication = ClassicMusicPublication.Create(ClassicMusicCatalogue.All, sources);
        long bytes = publication.Manifest.Cues.Sum(cue => cue.ByteLength);
        long budget = ClassicMusicCatalogue.MaximumTotalBytes - ClassicMusicCatalogue.EffectHeadroomBytes;
        Console.WriteLine($"music: {publication.Manifest.Cues.Count} of {ClassicMusicCatalogue.All.Count} cues published, {bytes} of {budget} bytes shared with the effect clips");
        foreach (string warning in publication.Warnings) Console.WriteLine($"  {warning}");
        foreach (ClassicMusicRecord cue in publication.Manifest.Cues)
        {
            Console.WriteLine($"  {cue.MediaId} <- {cue.File} ({cue.Track}, {cue.Context}), {cue.ByteLength} bytes, {cue.ContentDigest}");
        }

        if (!args.Switch("--update")) return Options.ReportOnly("these artifacts");
        string groupRoot = Path.Combine(Path.GetFullPath(args["--out"]), ClassicMusicPublication.Group);
        PayloadFiles.WriteArtifacts(groupRoot, publication.Artifacts);
        PayloadFiles.WriteFile(
            Path.Combine(groupRoot, "media", "music", "music-inventory.json"),
            ArtifactInventory.Write(ClassicMusicPublication.Generator, publication.Artifacts.Select(artifact =>
                ArtifactInventory.Entry($"{ClassicMusicPublication.Group}/{artifact.RelativePath}", artifact.Bytes.Span, artifact.MediaId))));
        Console.WriteLine($"music: wrote {publication.Artifacts.Count} artifacts and their inventory under {Path.Combine(args["--out"], ClassicMusicPublication.LogicalRoot)}");
        return 0;
    }
}
