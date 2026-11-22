using Rusty.Engine;
using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The animation clips the game plays, which ship with it in their own container installed beside the product
/// (<see cref="ContainerName"/>) and never live in Git: the townspeople's gsplat clip packs, written by
/// <c>authoring/tools/people/splat-people.py</c> from their sources on the NAS. A payload names a clip by its path in the
/// container and its digest; a clip the container lacks, or holds other bytes of, is refused when it is opened.
/// </summary>
internal sealed class DaggerfallAnimations
{
    /// <summary>The container's file name, installed beside the product.</summary>
    internal const string ContainerName = "animations.rpak";

    private readonly ProductContentBundle _container;
    private readonly Dictionary<string, ContentSha256> _digests;

    internal DaggerfallAnimations(ProductContentBundle container)
    {
        _container = container;
        _digests = container.Entries.ToArray().ToDictionary(entry => entry.Path, entry => entry.Sha256, StringComparer.Ordinal);
    }

    /// <summary>The clips a composition resolved over, or nothing when no animations container was opened.</summary>
    internal static DaggerfallAnimations? For(ResolvedGameComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return composition.Installed.FirstOrDefault(installed => installed.Name == ContainerName)?.Container is { } container ? new(container) : null;
    }

    /// <summary>Opens <paramref name="clip"/> from the container.</summary>
    /// <exception cref="InvalidDataException">The container lacks the clip or holds other bytes at its path: an older or newer build.</exception>
    internal ContentReference Open(DaggerfallArtFile clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (!_digests.TryGetValue(clip.Path, out ContentSha256 digest))
            throw new InvalidDataException($"{ContainerName} holds no clip {clip.Path}; build it again (authoring/tools/people/splat-people.py) or pull it (scripts/data-sync.sh pull).");
        if (digest != clip.Sha256)
            throw new InvalidDataException($"{ContainerName} holds another build of {clip.Path} than the art pack names; pull the container this checkout's payload was written with (scripts/data-sync.sh pull).");
        return _container.OpenReference(clip.Path);
    }
}
