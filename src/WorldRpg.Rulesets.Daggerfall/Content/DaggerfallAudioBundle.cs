using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Resolves one publication's audio identities through the Engine-owned bundle
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
    private readonly ProductContent _content;
    private readonly string _bundleId;
    private readonly IReadOnlyDictionary<string, string> _paths;

    internal DaggerfallAudioBundle(ProductContent content, string bundleId, string contentRoot, IEnumerable<NormalizedAudioClip> clips)
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

    /// <summary>
    /// Constructs a site's audio through the one product-wide world media bundle: every site's sidecar names
    /// clips the world media publication carries once, so the site contributes the cue mapping and the
    /// bodies stay in that bundle.
    /// </summary>
    internal static DaggerfallAudioBundle ForProfile(ProductContent content, DaggerfallSiteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(profile);
        return new DaggerfallAudioBundle(content, DaggerfallWorldMedia.AudioBundleId, $"{DaggerfallWorldMedia.AudioRoot}/", profile.Audio);
    }

    /// <summary>Constructs the one music bundle every site's cues are opened through.</summary>
    /// <remarks>
    /// The score is the same donor songs in every world, so it is staged once rather than copied into each
    /// site's bundle. A site contributes the cue list it admits; the artifacts stay here.
    /// </remarks>
    internal static DaggerfallAudioBundle ForMusic(ProductContent content, IReadOnlyList<NormalizedMusicCue> cues)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(cues);
        return new DaggerfallAudioBundle(
            content,
            DaggerfallMusicBundle.BundleId,
            DaggerfallMusicBundle.ContentRoot,
            cues.Select(cue => new NormalizedAudioClip(cue.MediaId, $"{DaggerfallMusicBundle.LogicalRoot}/{cue.File}", cue.Sha256)));
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

/// <summary>
/// Profile-keyed audio ownership for one product composition. A profile's cue mapping is built the first
/// time a session opens a clip there or projects it, from the profile the catalog resolves.
/// </summary>
internal sealed class DaggerfallSiteAudioBundles
{
    private readonly ProductContent _content;
    private readonly Func<DaggerfallWorldProfileKey, DaggerfallSiteProfile> _profiles;
    private readonly Dictionary<DaggerfallWorldProfileKey, DaggerfallAudioBundle> _bundles = [];

    internal DaggerfallSiteAudioBundles(ProductContent content, DaggerfallSiteProfiles profiles)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles.Require;
    }

    internal DaggerfallAudioBundle Require(DaggerfallWorldProfileKey key) => Require(key, () => _profiles(key));

    internal DaggerfallAudioBundle Require(DaggerfallSiteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Require(profile.ProfileKey, () => profile);
    }

    private DaggerfallAudioBundle Require(DaggerfallWorldProfileKey key, Func<DaggerfallSiteProfile> profile)
    {
        lock (_bundles)
        {
            if (!_bundles.TryGetValue(key, out DaggerfallAudioBundle? bundle))
                _bundles.Add(key, bundle = DaggerfallAudioBundle.ForProfile(_content, profile()));
            return bundle;
        }
    }
}
