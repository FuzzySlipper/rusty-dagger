using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Audio;
using Daggerfall.Import.Publication;
using Daggerfall.Import;

namespace Daggerfall.Import.Tool;

internal static partial class Program
{
    /// <summary>
    /// Reads the cue list a site publication records, from the manifest the music publication wrote.
    /// </summary>
    /// <remarks>
    /// A site names the cues the product may open there, and the bytes live in the product-wide music
    /// publication. A caller that supplied no manifest gets no cues and is told so: the site then composes
    /// without a score, which is a supported state, and quietly naming cues nobody published would move
    /// the failure to the product's admission of a site it cannot play.
    /// </remarks>
    private static IReadOnlyList<ClassicMusicRecord> LoadMusicRecords(string? manifestPath)
    {
        if (manifestPath is null)
        {
            Console.WriteLine("music: no --music-manifest was supplied, so this publication names no cue and the product plays no score here.");
            return [];
        }

        byte[] bytes = File.ReadAllBytes(manifestPath);
        ClassicMusicManifest manifest = ClassicMusicPublication.Read(bytes);
        long bytesTotal = manifest.Cues.Sum(cue => cue.ByteLength);
        Console.WriteLine($"music: {manifest.Cues.Count} published cues admitted from {manifestPath}, {bytesTotal} bytes carried by '{ClassicMusicPublication.BundleId}'");
        return manifest.Cues;
    }

    /// <summary>
    /// Publishes the catalogue's music cues from a folder of donor song files.
    /// </summary>
    /// <remarks>
    /// The donor's own installation keeps its songs outside the Arena2 archives, and a music pack that
    /// replaces them is a local input rather than a published one, so the folder is named explicitly and
    /// every cue it does not carry is reported. An absent folder publishes no music and says so: the
    /// product then plays no score, which is a state the publication supports, and inventing a
    /// substitute track would play a song the donor never played there.
    /// </remarks>
    private static int RunMusicMediaCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool music-media --out CONTENT_ROOT [--sound SOURCE_DIR] [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        if (!values.ContainsKey("--out") || values.Keys.Any(key => key is not ("--out" or "--sound")))
        {
            throw new ArgumentException(Usage);
        }

        string outRoot = Path.GetFullPath(values["--out"]);
        List<ClassicMusicSource> sources = [];
        if (!values.TryGetValue("--sound", out string? sound))
        {
            Console.WriteLine("music: no --sound folder was supplied, so no cue is published and the product plays no score.");
        }
        else if (File.Exists(sound))
        {
            throw new ArgumentException($"--sound names the file '{sound}' rather than the folder holding the donor songs. {Usage}");
        }
        else if (!Directory.Exists(sound))
        {
            Console.WriteLine($"music: '{sound}' does not exist, so no cue is published and the product plays no score.");
        }
        else
        {
            foreach (ClassicMusicCue cue in ClassicMusicCatalogue.All)
            {
                string path = Path.Combine(sound, cue.SourceFile);
                if (!File.Exists(path))
                {
                    Console.WriteLine($"music: '{cue.SourceFile}' is not in '{sound}'; cue '{cue.MediaId}' is not published.");
                    continue;
                }

                sources.Add(new ClassicMusicSource(cue, File.ReadAllBytes(path)));
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

        if (!update)
        {
            Console.WriteLine("content: not written (rerun with --update to publish these artifacts)");
            return 0;
        }

        foreach (ImportPublicationArtifact artifact in publication.Artifacts)
        {
            string path = Path.Combine(outRoot, ClassicMusicPublication.Group, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, artifact.Bytes.ToArray());
        }

        // The group states the bytes it published, the way the classic and character groups do: a
        // consumer reads one generated index instead of walking the tree, and an index that drifts from
        // its content fails the check that reads both.
        JsonObject inventory = new()
        {
            ["schemaVersion"] = 1,
            ["generator"] = ClassicMusicPublication.Generator,
            ["artifacts"] = new JsonArray([.. publication.Artifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(artifact => (JsonNode)InventoryEntry(artifact))]),
        };
        File.WriteAllText(
            Path.Combine(outRoot, ClassicMusicPublication.Group, "media", "music", "music-inventory.json"),
            inventory.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"music: wrote {publication.Artifacts.Count} artifacts and their inventory under {Path.Combine(outRoot, ClassicMusicPublication.LogicalRoot)}");
        return 0;
    }

    private static JsonObject InventoryEntry(ImportPublicationArtifact artifact)
    {
        JsonObject entry = new()
        {
            ["path"] = $"{ClassicMusicPublication.Group}/{artifact.RelativePath}",
            ["byteLength"] = artifact.Bytes.Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(artifact.Bytes.Span)),
        };
        if (artifact.MediaId is { } mediaId) entry["mediaId"] = mediaId;
        return entry;
    }
}
