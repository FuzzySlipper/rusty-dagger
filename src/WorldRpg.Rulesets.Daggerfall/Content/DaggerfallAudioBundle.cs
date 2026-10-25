using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Resolves one publication's audio identities through the generated root
/// that carries their imported bodies.
/// </summary>
/// <remarks>
/// The generated classic manifest supplies the identity-to-content path mapping.
/// This object retains only that metadata; each request opens and closes the root
/// around its retained content reference, leaving the returned audio clip as
/// the Engine-owned resource for the caller to dispose.
/// </remarks>
internal sealed class DaggerfallAudioBundle
{
    private readonly DaggerfallData _content;
    private readonly string _root;
    private readonly IReadOnlyDictionary<string, string> _paths;

    internal DaggerfallAudioBundle(DaggerfallData content, string root, IEnumerable<NormalizedAudioClip> clips)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(clips);
        _root = root;
        string contentRoot = root + "/";
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        foreach (NormalizedAudioClip clip in clips)
        {
            if (!clip.Path.StartsWith(contentRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Published audio '{clip.Id}' names '{clip.Path}', which is outside the '{root}' root.");
            }

            string path = clip.Path[contentRoot.Length..];
            if (string.IsNullOrWhiteSpace(path) || !paths.TryAdd(clip.Id, path))
            {
                throw new InvalidOperationException($"Published audio '{clip.Id}' does not have one unique resource below its root.");
            }
        }

        _paths = paths;
    }

    /// <summary>
    /// Constructs a site's audio through the one product-wide world media publication: every site's sidecar names
    /// clips the world media publication carries once, so the site contributes the cue mapping and the
    /// bodies stay in that publication.
    /// </summary>
    internal static DaggerfallAudioBundle ForProfile(DaggerfallData content, DaggerfallSiteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(profile);
        return new DaggerfallAudioBundle(content, DaggerfallWorldMedia.AudioRoot, profile.Audio);
    }

    /// <summary>Opens the exact resource published for a named audio identity.</summary>
    internal AudioClip OpenClip(IAudioService audio, string mediaId)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        if (!_paths.TryGetValue(mediaId, out string? path))
        {
            throw new InvalidOperationException($"Published audio resource '{mediaId}' is not carried by '{_root}'.");
        }

        try
        {
            using DaggerfallDataRoot bundle = _content.Open(_root);
            using ContentReference reference = bundle.OpenReference(path);
            return audio.OpenClipFromContent(new AudioClipFromContentRequest(reference));
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidOperationException($"Published audio resource '{mediaId}' is not available as '{_root}/{path}'.", exception);
        }
    }
}

/// <summary>
/// Profile-keyed audio ownership for one product composition. A profile's cue mapping is built the first
/// time a session opens a clip there or projects it, from the profile the catalog resolves.
/// </summary>
internal sealed class DaggerfallSiteAudioBundles
{
    private readonly DaggerfallData _content;
    private readonly Func<DaggerfallWorldProfileKey, DaggerfallSiteProfile> _profiles;
    private readonly Dictionary<DaggerfallWorldProfileKey, DaggerfallAudioBundle> _bundles = [];

    internal DaggerfallSiteAudioBundles(DaggerfallData content, DaggerfallSiteProfiles profiles)
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
