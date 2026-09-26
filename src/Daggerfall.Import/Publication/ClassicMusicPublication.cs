using Daggerfall.Import.Audio;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>One published music cue: the identity, the donor track and the artifact that carries it.</summary>
/// <param name="MediaId">The published media identity the product opens the cue by.</param>
/// <param name="Track">The donor song identity the cue answers, as the donor's song manager names it.</param>
/// <param name="Context">The donor playlist the cue belongs to.</param>
/// <param name="File">The published artifact's name inside the music bundle, which is the donor file's own.</param>
/// <param name="MimeType">The container the Engine admits for the cue.</param>
/// <param name="ByteLength">The published artifact's measured length in bytes.</param>
/// <param name="ContentDigest">The published artifact's measured SHA-256 digest.</param>
public sealed record ClassicMusicRecord(
    string MediaId,
    string Track,
    string Context,
    string File,
    string MimeType,
    long ByteLength,
    string ContentDigest)
{
    /// <summary>The only container the music publication admits.</summary>
    public const string OggMimeType = "audio/ogg";

    /// <summary>The container extension the admitted container carries.</summary>
    public const string OggExtension = ".ogg";

    internal void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(MediaId, nameof(MediaId));
        if (string.IsNullOrWhiteSpace(Track) || Track.Contains('/') || Track.Contains('\\') || Track.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Music cue '{MediaId}' does not name a plain donor track.", nameof(Track));
        }

        if (string.IsNullOrWhiteSpace(Context) || Context.Contains('/') || Context.Contains('\\'))
        {
            throw new ArgumentException($"Music cue '{MediaId}' does not name a donor playlist.", nameof(Context));
        }

        if (string.IsNullOrWhiteSpace(File) || File.Contains('/') || File.Contains('\\') || File.Contains("..", StringComparison.Ordinal)
            || !File.EndsWith(OggExtension, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Music cue '{MediaId}' does not name one '{OggExtension}' artifact.", nameof(File));
        }

        // The publication admits one container. A record that named another one would be published
        // as bytes the Engine may refuse to open, which is a broken cue rather than a wider catalogue.
        if (!StringComparer.Ordinal.Equals(MimeType, OggMimeType))
        {
            throw new ArgumentException($"Music cue '{MediaId}' names container '{MimeType}', which the music publication does not admit.", nameof(MimeType));
        }

        if (ByteLength <= 0)
        {
            throw new ArgumentException($"Music cue '{MediaId}' has no published bytes.", nameof(ByteLength));
        }

        new ContentDigest(ContentDigest);
    }
}

/// <summary>The published cue set, as the music manifest carries it.</summary>
/// <param name="Generator">The tool invocation that produced the manifest.</param>
/// <param name="Cues">The published cues, ordered by media identity.</param>
public sealed record ClassicMusicManifest(string Generator, IReadOnlyList<ClassicMusicRecord> Cues);

/// <summary>Caller-supplied donor bytes for one catalogue cue.</summary>
/// <param name="Cue">The catalogue entry the bytes answer.</param>
/// <param name="Bytes">The donor file's own bytes.</param>
public sealed record ClassicMusicSource(ClassicMusicCue Cue, byte[] Bytes);

/// <summary>One music publication: the artifacts to write, the manifest, and what could not be published.</summary>
/// <param name="Artifacts">The clip artifacts and the manifest artifact, in the group the tool writes.</param>
/// <param name="Manifest">The published cue set.</param>
/// <param name="Warnings">Cues that were not published, and cues whose source length differs from the catalogue's record.</param>
public sealed record ClassicMusicPublicationResult(
    IReadOnlyList<ImportPublicationArtifact> Artifacts,
    ClassicMusicManifest Manifest,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Publishes the classic music catalogue into one product-wide music bundle.
/// </summary>
/// <remarks>
/// The score is the same seven donor songs wherever the player stands, so it is published once, above the
/// sites, rather than copied into every site publication the way the effect clips are. A site names the
/// cues it admits in its classic sidecar; this publication owns the bytes and the digests both sides read.
/// A cue whose donor file the caller did not supply is reported and skipped rather than substituted: the
/// catalogue says which donor track a context plays, and a different file would play a different song.
/// The manifest sits above the bundle root so a consumer can read it eagerly while the clips stay lazy.
/// </remarks>
public static class ClassicMusicPublication
{
    /// <summary>The bundle the Engine stages the published clips into.</summary>
    public const string BundleId = "daggerfall.music";

    /// <summary>The content-root group the music publication writes into.</summary>
    public const string Group = "worldrpg";

    /// <summary>The content-root path the bundle root sits at.</summary>
    public const string LogicalRoot = $"{Group}/media/music";

    /// <summary>The group-relative directory the clip artifacts are written into.</summary>
    public const string ClipRelativeRoot = "media/music/clips";

    /// <summary>The group-relative path of the published manifest.</summary>
    public const string ManifestRelativePath = "media/music/manifest.json";

    /// <summary>The tool invocation the manifest names as its generator.</summary>
    public const string Generator = "daggerfall-import-tool music-media";

    /// <summary>Publishes every cue the supplied sources answer, and reports the ones they do not.</summary>
    public static ClassicMusicPublicationResult Create(
        IReadOnlyList<ClassicMusicCue> cues,
        IReadOnlyList<ClassicMusicSource> sources)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(sources);
        if (cues.Count == 0)
        {
            throw new ArgumentException("The music catalogue is empty, so there is nothing to publish.", nameof(cues));
        }

        Dictionary<string, byte[]> byFile = new(StringComparer.Ordinal);
        foreach (ClassicMusicSource source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.Bytes is null || source.Bytes.Length == 0)
            {
                throw new ArgumentException($"Music source '{source.Cue.SourceFile}' carries no bytes.", nameof(sources));
            }

            if (!byFile.TryAdd(source.Cue.SourceFile, source.Bytes))
            {
                throw new ArgumentException($"Music source '{source.Cue.SourceFile}' is supplied more than once.", nameof(sources));
            }
        }

        NormalizedImportDocument.ValidateUnique(cues, cue => cue.MediaId, "music cue media identity");
        NormalizedImportDocument.ValidateUnique(cues, cue => cue.SourceFile, "music cue source file");
        NormalizedImportDocument.ValidateUnique(cues, cue => cue.Track, "music cue donor track");

        List<ImportPublicationArtifact> artifacts = [];
        List<ClassicMusicRecord> records = [];
        List<string> warnings = [];
        long publishedBytes = 0;
        foreach (ClassicMusicCue cue in cues.OrderBy(cue => cue.MediaId, StringComparer.Ordinal))
        {
            if (!byFile.TryGetValue(cue.SourceFile, out byte[]? bytes))
            {
                warnings.Add($"music cue '{cue.MediaId}' is not published: the supplied folder does not carry '{cue.SourceFile}'.");
                continue;
            }

            if (bytes.Length != cue.SourceBytes)
            {
                // The catalogue records the donor file's length, and the budget decision was made with it.
                // A folder carrying a different encode of the same song is published as it is, with both
                // values reported, because refusing would stop every later cue over a container difference.
                warnings.Add($"music cue '{cue.MediaId}' source '{cue.SourceFile}' carries {bytes.Length} bytes where the catalogue records {cue.SourceBytes}.");
            }

            ContentDigest digest = ContentDigest.Compute(bytes);
            string relativePath = $"{ClipRelativeRoot}/{cue.SourceFile}";
            artifacts.Add(new ImportPublicationArtifact(relativePath, bytes, mediaId: cue.MediaId));
            records.Add(new ClassicMusicRecord(
                cue.MediaId,
                cue.Track,
                cue.Context,
                cue.SourceFile,
                ClassicMusicRecord.OggMimeType,
                bytes.LongLength,
                digest.Value));
            publishedBytes += bytes.Length;
        }

        // The Engine admits a bounded number of clips and a bounded byte total, and the imported effect
        // clips share that total. Publishing past either bound produces a bundle the product cannot open,
        // so the publication refuses here, where the numbers are still visible, instead of at admission.
        if (records.Count > ClassicMusicCatalogue.MaximumCueCount)
        {
            throw new InvalidOperationException($"The music publication carries {records.Count} cues, which is more than the Engine admits ({ClassicMusicCatalogue.MaximumCueCount}).");
        }

        long admittedTotal = ClassicMusicCatalogue.MaximumTotalBytes - ClassicMusicCatalogue.EffectHeadroomBytes;
        if (publishedBytes > admittedTotal)
        {
            throw new InvalidOperationException($"The music publication carries {publishedBytes} bytes, which leaves the imported effect clips less than {ClassicMusicCatalogue.EffectHeadroomBytes} of the Engine's {ClassicMusicCatalogue.MaximumTotalBytes}.");
        }

        ClassicMusicManifest manifest = new(Generator, records);
        artifacts.Add(new ImportPublicationArtifact(ManifestRelativePath, Serialize(manifest)));
        return new ClassicMusicPublicationResult(artifacts, manifest, warnings);
    }

    /// <summary>Writes one cue set in the published dialect.</summary>
    public static byte[] Serialize(ClassicMusicManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        foreach (ClassicMusicRecord record in manifest.Cues) record.Validate();
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifest, PublishedJson.Section);
    }

    /// <summary>Reads a published cue set, refusing a member the record does not declare.</summary>
    public static ClassicMusicManifest Read(ReadOnlySpan<byte> bytes)
    {
        ClassicMusicManifest manifest = System.Text.Json.JsonSerializer.Deserialize<ClassicMusicManifest>(bytes, PublishedJson.SectionRead)
            ?? throw new FormatException("The published music manifest is empty.");
        if (!StringComparer.Ordinal.Equals(manifest.Generator, Generator))
        {
            throw new FormatException($"The published music manifest was not written by '{Generator}'.");
        }

        if (manifest.Cues is null)
        {
            throw new FormatException("The published music manifest carries no cue list.");
        }

        foreach (ClassicMusicRecord record in manifest.Cues) record.Validate();
        NormalizedImportDocument.ValidateUnique(manifest.Cues, record => record.MediaId, "published music cue");
        NormalizedImportDocument.ValidateUnique(manifest.Cues, record => record.Track, "published music track");
        NormalizedImportDocument.ValidateUnique(manifest.Cues, record => record.File, "published music artifact");
        return manifest;
    }
}
