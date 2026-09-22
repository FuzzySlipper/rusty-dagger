using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Resolves the Privateer's Hold audio identities through the Engine-owned bundle
/// that carries their imported WAV bodies.
/// </summary>
/// <remarks>
/// The generated classic manifest supplies the identity-to-content path mapping.
/// This object retains only that metadata; each request opens and closes an Engine
/// bundle around its retained content reference, leaving the returned audio clip as
/// the Engine-owned resource for the caller to dispose.
/// </remarks>
internal sealed class DaggerfallAudioBundle
{
    internal const string BundleId = "daggerfall.privateers-hold-audio";
    private const string ContentRoot = "worldrpg/imports/privateers-hold/media/audio/clips/";

    private readonly ProductContent _content;
    private readonly IReadOnlyDictionary<string, string> _paths;

    internal DaggerfallAudioBundle(ProductContent content, IEnumerable<NormalizedAudioClip> clips)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        ArgumentNullException.ThrowIfNull(clips);
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        foreach (NormalizedAudioClip clip in clips)
        {
            if (!clip.Path.StartsWith(ContentRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Published audio '{clip.Id}' names '{clip.Path}', which is outside the '{ContentRoot}' bundle root.");
            }

            string path = clip.Path[ContentRoot.Length..];
            if (string.IsNullOrWhiteSpace(path) || !paths.TryAdd(clip.Id, path))
            {
                throw new InvalidOperationException($"Published audio '{clip.Id}' does not have one unique bundle resource.");
            }
        }

        _paths = paths;
    }

    /// <summary>Opens the exact bundle resource published for a named audio identity.</summary>
    internal AudioClip OpenClip(IAudioService audio, string mediaId)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        if (!_paths.TryGetValue(mediaId, out string? path))
        {
            throw new InvalidOperationException($"Published audio resource '{mediaId}' is not carried by bundle '{BundleId}'.");
        }

        try
        {
            using ProductContentBundle bundle = _content.OpenBundle(BundleId);
            using ContentReference reference = bundle.OpenReference(path);
            return audio.OpenClipFromContent(new AudioClipFromContentRequest(reference));
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidOperationException($"Published audio resource '{mediaId}' is not available as '{BundleId}/{path}'.", exception);
        }
    }
}
