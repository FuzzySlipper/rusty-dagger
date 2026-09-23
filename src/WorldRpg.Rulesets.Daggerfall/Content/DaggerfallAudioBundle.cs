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
    private readonly string _bundleId;
    private readonly IReadOnlyDictionary<string, string> _paths;

    internal DaggerfallAudioBundle(ProductContent content, IEnumerable<NormalizedAudioClip> clips)
        : this(content, BundleId, ContentRoot, clips) { }

    private DaggerfallAudioBundle(ProductContent content, string bundleId, string contentRoot, IEnumerable<NormalizedAudioClip> clips)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(clips);
        _bundleId = bundleId;
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        foreach (NormalizedAudioClip clip in clips)
        {
            if (!clip.Path.StartsWith(contentRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Published audio '{clip.Id}' names '{clip.Path}', which is outside the '{contentRoot}' bundle root.");
            }

            string path = clip.Path[contentRoot.Length..];
            if (string.IsNullOrWhiteSpace(path) || !paths.TryAdd(clip.Id, path))
            {
                throw new InvalidOperationException($"Published audio '{clip.Id}' does not have one unique bundle resource.");
            }
        }

        _paths = paths;
    }

    /// <summary>Constructs the exact audio bundle for one profile publication root.</summary>
    internal static DaggerfallAudioBundle ForProfile(ProductContent content, PrivateersHoldInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(inputs);
        string root = inputs.ProfileKey.LogicalId;
        const string importPrefix = "worldrpg/imports/";
        if (!root.StartsWith(importPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"World profile '{root}' has no publishable audio bundle root.");
        string profileName = root[importPrefix.Length..].Replace('/', '-');
        return new DaggerfallAudioBundle(content, $"daggerfall.{profileName}-audio", $"{root}/media/audio/clips/", inputs.Audio);
    }

    /// <summary>Opens the exact bundle resource published for a named audio identity.</summary>
    internal AudioClip OpenClip(IAudioService audio, string mediaId)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        if (!_paths.TryGetValue(mediaId, out string? path))
        {
            throw new InvalidOperationException($"Published audio resource '{mediaId}' is not carried by bundle '{_bundleId}'.");
        }

        try
        {
            using ProductContentBundle bundle = _content.OpenBundle(_bundleId);
            using ContentReference reference = bundle.OpenReference(path);
            return audio.OpenClipFromContent(new AudioClipFromContentRequest(reference));
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidOperationException($"Published audio resource '{mediaId}' is not available as '{_bundleId}/{path}'.", exception);
        }
    }
}

/// <summary>Profile-keyed audio ownership for all closures admitted by one product composition.</summary>
internal sealed class DaggerfallSiteAudioBundles
{
    private readonly IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallAudioBundle> _bundles;

    internal DaggerfallSiteAudioBundles(ProductContent content, DaggerfallSiteProfiles profiles)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(profiles);
        Dictionary<DaggerfallWorldProfileKey, DaggerfallAudioBundle> bundles = [];
        foreach (DaggerfallWorldProfileKey key in profiles.Keys)
            bundles.Add(key, DaggerfallAudioBundle.ForProfile(content, profiles.Require(key)));
        _bundles = bundles;
    }

    internal DaggerfallAudioBundle Require(DaggerfallWorldProfileKey key) => _bundles.TryGetValue(key, out DaggerfallAudioBundle? bundle)
        ? bundle
        : throw new InvalidOperationException($"World profile '{key.LogicalId}' has no admitted audio bundle.");
}
