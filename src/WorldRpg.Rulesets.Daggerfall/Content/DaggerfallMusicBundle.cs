using System.Collections.ObjectModel;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Opens the music cues a site admits through the one product-wide music bundle.
/// </summary>
/// <remarks>
/// The score is the same donor songs wherever the player stands, so its artifacts are staged once and each
/// site's classic sidecar states which of them play there. This owner joins the two: it reads the published
/// music manifest, checks that every cue the site names is a cue the publication actually carries at the
/// stated length and digest, and answers a donor track with the artifact the manifest names. A site that
/// names no cue has no music rather than a substitute, which is why the join can answer nothing.
/// </remarks>
internal sealed class DaggerfallMusicBundle
{
    /// <summary>The bundle the Engine stages the published music into.</summary>
    internal const string BundleId = "daggerfall.music";

    /// <summary>The bundle's root inside the content store, which holds the clips and not the manifest.</summary>
    internal const string LogicalRoot = "worldrpg/media/music/clips";

    /// <summary>The bundle root as a path prefix, for the audio owner's own check.</summary>
    internal const string ContentRoot = LogicalRoot + "/";

    /// <summary>The published cue set: the artifact ledger both the sites and the product read.</summary>
    internal const string ManifestPath = "worldrpg/media/music/manifest.json";

    /// <summary>The tool invocation the manifest must name as its generator.</summary>
    internal const string ManifestGenerator = "daggerfall-import-tool music-media";

    /// <summary>The one container the music publication admits, and the Engine opens.</summary>
    internal const string MimeType = "audio/ogg";

    /// <summary>The container extension that goes with it.</summary>
    internal const string Extension = ".ogg";

    private readonly DaggerfallAudioBundle _clips;
    private readonly IReadOnlyDictionary<string, string> _mediaIdByTrack;

    private DaggerfallMusicBundle(DaggerfallAudioBundle clips, IReadOnlyDictionary<string, string> mediaIdByTrack)
    {
        _clips = clips;
        _mediaIdByTrack = mediaIdByTrack;
    }

    /// <summary>The donor tracks this site can play, in stable order.</summary>
    internal IReadOnlyList<string> Tracks => [.. _mediaIdByTrack.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Whether the given donor track is one this site admits.</summary>
    internal bool CanPlay(string track) => _mediaIdByTrack.ContainsKey(track);

    /// <summary>Opens the clip one admitted donor track plays, or nothing when the site does not admit it.</summary>
    internal AudioClip? OpenClip(IAudioService audio, string track)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrWhiteSpace(track);
        return _mediaIdByTrack.TryGetValue(track, out string? mediaId) ? _clips.OpenClip(audio, mediaId) : null;
    }

    /// <summary>
    /// Admits the cues a site names against the published music manifest, or nothing when it names none.
    /// </summary>
    internal static DaggerfallMusicBundle? Admit(ProductContent content, IReadOnlyList<NormalizedMusicCue> cues)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(cues);
        if (cues.Count == 0)
        {
            // A site published without music composes without it. Nothing is substituted, and the absence
            // is visible as an empty track list rather than as a cue that silently fails to open.
            return null;
        }

        IReadOnlyDictionary<string, ManifestCue> published = ReadManifest(content);
        Dictionary<string, string> mediaIdByTrack = new(StringComparer.Ordinal);
        foreach (NormalizedMusicCue cue in cues)
        {
            if (!published.TryGetValue(cue.MediaId, out ManifestCue? record))
            {
                throw new InvalidOperationException($"Site music cue '{cue.MediaId}' is not carried by the published music manifest '{ManifestPath}'.");
            }

            // The site's cue and the publication's record are two statements about the same artifact, and
            // the product opens the bytes the publication measured. Disagreement means the site was
            // published against a different manifest, so the cue it names is not the cue it would play.
            if (!StringComparer.Ordinal.Equals(record.Track, cue.Track)
                || !StringComparer.Ordinal.Equals(record.Context, cue.Context)
                || !StringComparer.Ordinal.Equals(record.File, cue.File)
                || !StringComparer.Ordinal.Equals(record.MimeType, cue.MimeType)
                || record.ByteLength != cue.ByteLength
                || record.ContentHash != cue.Sha256)
            {
                throw new InvalidOperationException($"Site music cue '{cue.MediaId}' does not agree with the published music manifest '{ManifestPath}'.");
            }

            if (!mediaIdByTrack.TryAdd(cue.Track, cue.MediaId))
            {
                throw new InvalidOperationException($"Site music repeats donor track '{cue.Track}'.");
            }
        }

        return new DaggerfallMusicBundle(DaggerfallAudioBundle.ForMusic(content, cues), new ReadOnlyDictionary<string, string>(mediaIdByTrack));
    }

    private static IReadOnlyDictionary<string, ManifestCue> ReadManifest(ProductContent content)
    {
        byte[] bytes;
        try
        {
            bytes = content.ReadBytes(ManifestPath).ToArray();
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidOperationException($"A site admits music cues but '{ManifestPath}' is not published; run the music publication before publishing a site.", exception);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Published music manifest '{ManifestPath}' is not valid JSON.", exception);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                HashSet<string> members = new(StringComparer.Ordinal);
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name is not ("generator" or "cues") || !members.Add(property.Name))
                    {
                        throw new InvalidOperationException($"Published music manifest '{ManifestPath}' states an undeclared member '{property.Name}'.");
                    }
                }
            }

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("generator", out JsonElement generator) || generator.ValueKind != JsonValueKind.String
                || !StringComparer.Ordinal.Equals(generator.GetString(), ManifestGenerator)
                || !root.TryGetProperty("cues", out JsonElement cues) || cues.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"Published music manifest '{ManifestPath}' is not the generated shape.");
            }

            Dictionary<string, ManifestCue> published = new(StringComparer.Ordinal);
            foreach (JsonElement cue in cues.EnumerateArray())
            {
                if (cue.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException($"Published music manifest '{ManifestPath}' states a cue that is not a record.");
                }

                RejectUnknownMembers(cue);
                ManifestCue record = ReadCue(cue);
                if (!published.TryAdd(record.MediaId, record))
                {
                    throw new InvalidOperationException($"Published music manifest repeats cue '{record.MediaId}'.");
                }
            }

            return new ReadOnlyDictionary<string, ManifestCue>(published);
        }
    }

    private static ManifestCue ReadCue(JsonElement cue)
    {
        string mediaId = RequiredText(cue, "mediaId");
        string file = RequiredText(cue, "file");
        string mimeType = RequiredText(cue, "mimeType");
        long byteLength = RequiredLength(cue, "byteLength", mediaId);
        string digest = RequiredText(cue, "contentDigest");
        _ = DaggerfallContentHash.Parse(digest, $"Published music cue '{mediaId}'");
        if (!StringComparer.Ordinal.Equals(mimeType, MimeType) || !file.EndsWith(Extension, StringComparison.Ordinal)
            || file.Contains('/') || file.Contains('\\') || file.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Published music cue '{mediaId}' names container '{mimeType}' and artifact '{file}', which the music bundle does not carry.");
        }

        return new ManifestCue(mediaId, RequiredText(cue, "track"), RequiredText(cue, "context"), file, mimeType, byteLength, DaggerfallContentHash.Parse(digest, $"Published music cue '{mediaId}'"));
    }

    /// <summary>
    /// Refuses a member this reader does not declare, and a member stated twice.
    /// </summary>
    /// <remarks>
    /// A hand-written content pack is the only way a cue reaches this reader without passing the import
    /// tool, so it is read the way the import reader reads one: a renamed member is a mistake rather than
    /// an ignored field, and a repeated member would otherwise be answered by whichever copy the parser
    /// kept. The generated manifest always states each member once.
    /// </remarks>
    private static void RejectUnknownMembers(JsonElement cue)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in cue.EnumerateObject())
        {
            if (property.Name is not ("mediaId" or "track" or "context" or "file" or "mimeType" or "byteLength" or "contentDigest"))
            {
                throw new InvalidOperationException($"Published music manifest '{ManifestPath}' states unknown member '{property.Name}' on a cue.");
            }

            if (!seen.Add(property.Name))
            {
                throw new InvalidOperationException($"Published music manifest '{ManifestPath}' states '{property.Name}' twice on a cue.");
            }
        }
    }

    private static string RequiredText(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out JsonElement member) || member.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(member.GetString()))
        {
            throw new InvalidOperationException($"A published music cue does not state '{property}'.");
        }

        return member.GetString()!;
    }

    private static long RequiredLength(JsonElement value, string property, string mediaId)
    {
        if (!value.TryGetProperty(property, out JsonElement member) || member.ValueKind != JsonValueKind.Number
            || !member.TryGetInt64(out long length) || length <= 0)
        {
            throw new InvalidOperationException($"Published music cue '{mediaId}' does not state its byte length.");
        }

        return length;
    }

    private sealed record ManifestCue(string MediaId, string Track, string Context, string File, string MimeType, long ByteLength, ContentSha256 ContentHash);
}
