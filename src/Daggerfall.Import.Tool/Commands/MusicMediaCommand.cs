using Daggerfall.Import.Audio;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the catalogue's music cues from a folder of donor song files, with the group's generated index.
/// </summary>
/// <remarks>
/// <para>
/// The donor's own installation keeps its songs outside the Arena2 archives, and a music pack that replaces
/// them is a local input rather than a published one, so the folder is named explicitly and every cue it
/// does not carry is reported. A folder that does not exist publishes no music and says so: the product then
/// plays no score, which is a state the publication supports, and inventing a substitute track would play a
/// song the donor never played there. <c>--require-all</c> refuses a folder that lacks a cue, by file name.
/// </para>
/// <para>
/// This publication is transitional: the sites name its cues until the product reads <c>music.rpak</c> by its
/// track naming scheme. The import feeds it the tracks <c>music-tracks</c> staged, already named
/// <c>&lt;track&gt;.&lt;ext&gt;</c> in lower case, and it publishes the Ogg ones; a supplied folder with none
/// still gets a manifest that names no cue.
/// </para>
/// </remarks>
internal static class MusicMediaCommand
{
    public static ToolCommand Command { get; } = new("music-media",
        [CommandOption.Staged("--out", "CONTENT_ROOT", ImportLayout.MusicPublication), CommandOption.Optional("--sound", "SOURCE_DIR"), CommandOption.Switch("--require-all"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        bool requireAll = args.Switch("--require-all");
        string? sound = args.Optional("--sound");
        if (requireAll && sound is null) throw args.Invalid("--require-all needs --sound.");
        // Without a folder there is nothing to publish; an import without music leaves this command out.
        if (sound is null) throw args.Invalid("--sound names the folder the cues are published from.");
        List<ClassicMusicSource> sources = [];
        if (File.Exists(sound))
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

        if (sources.Count == 0 && !Directory.Exists(sound))
        {
            Console.WriteLine($"music: 0 of {ClassicMusicCatalogue.All.Count} cues published; nothing to write.");
            return 0;
        }

        // A folder that carries no cue's Ogg file (its recordings are in another format the music container
        // admits) still publishes a manifest, naming no cue, so the commands that read it run and the sites
        // compose without a score here rather than failing over a missing file.

        ClassicMusicPublicationResult publication = ClassicMusicPublication.Create(ClassicMusicCatalogue.All, sources);
        long bytes = publication.Manifest.Cues.Sum(cue => cue.ByteLength);
        Console.WriteLine($"music: {publication.Manifest.Cues.Count} of {ClassicMusicCatalogue.All.Count} cues published, {bytes} storage bytes; one playing music clip is retained at a time");
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
