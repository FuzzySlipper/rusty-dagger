namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>What kind of cinematic a file is.</summary>
internal enum DaggerfallCinematicKind
{
    Vid,
    Flc,
}

/// <summary>How a cinematic's story hook is accounted for.</summary>
internal enum DaggerfallCinematicBinding
{
    Bound,
    Unresolved,
}

/// <summary>One cinematic source identity: its file, digest and story hook.</summary>
/// <param name="FileName">The source file name.</param>
/// <param name="Kind">Whether it is a VID or FLC cinematic.</param>
/// <param name="ByteLength">The file's bytes.</param>
/// <param name="Digest">The SHA-256 digest of the file.</param>
/// <param name="Binding">Whether a donor caller names it.</param>
/// <param name="Caller">The donor caller, when one names it.</param>
/// <param name="FactionId">The donor faction id, when the caller is a Daedric summons.</param>
/// <param name="Quest">The quest name, when the caller is a Daedric summons.</param>
internal sealed record DaggerfallCinematicDefinition(
    string FileName,
    DaggerfallCinematicKind Kind,
    long ByteLength,
    string Digest,
    DaggerfallCinematicBinding Binding,
    string Caller,
    int? FactionId,
    string Quest)
{
    internal DaggerfallCinematicArtifact? Artifact { get; init; }
}

internal sealed record DaggerfallCinematicArtifact(string Path, string MimeType, long ByteLength,
    string Sha256, int Width, int Height, long FrameCount, double DurationSeconds, bool HasAudio);

/// <summary>The cinematic provenance set, loaded from the pack alone.</summary>
/// <param name="Cinematics">The cinematics by file name.</param>
internal sealed record DaggerfallCinematicSet(IReadOnlyDictionary<string, DaggerfallCinematicDefinition> Cinematics)
{
    /// <summary>
    /// Resolves a cinematic identity by file: its digest and whether a donor caller binds it.
    /// Nothing here plays video; the binding tells a presentation owner what the file is for.
    /// </summary>
    internal DaggerfallCinematicDefinition Resolve(string fileName) => Cinematics[fileName];
}
