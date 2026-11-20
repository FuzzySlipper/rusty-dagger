using Rusty.Engine;
using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Opens the score's tracks by name from the music container installed beside the product.
/// </summary>
/// <remarks>
/// <para>
/// The music container (<see cref="ContainerName"/>) ships with the game: Patch's arrangement of the score, one
/// recording per track at its root, named <c>&lt;track&gt;.&lt;ext&gt;</c>: the track the playlists name
/// (<c>dungeon-01</c>, <c>outside-night-03</c>) and the extension of any format the Engine's audio plays
/// (authoring/tools/music builds it). The playlists, which say which track plays where, stay in Scene's music
/// director, which also names the tracks no recording answers.
/// </para>
/// <para>
/// A track the container does not carry is skipped by its playlist. A container that carries two recordings of
/// one track is refused by name: playing either would silently drop the other.
/// </para>
/// </remarks>
internal sealed class DaggerfallMusicBundle
{
    /// <summary>The music container's file name, installed beside the product.</summary>
    internal const string ContainerName = "music.rpak";

    /// <summary>The formats the Engine's audio plays, by file extension.</summary>
    internal static IReadOnlyList<string> Extensions { get; } = [".ogg", ".opus", ".mp3", ".flac", ".wav"];

    private readonly ProductContentBundle _container;
    private readonly IReadOnlyDictionary<string, string> _pathByTrack;

    private DaggerfallMusicBundle(ProductContentBundle container, IReadOnlyDictionary<string, string> pathByTrack)
    {
        _container = container;
        _pathByTrack = pathByTrack;
    }

    /// <summary>The tracks the container carries, in stable order.</summary>
    internal IReadOnlyList<string> Tracks => [.. _pathByTrack.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Whether the given track has a recording.</summary>
    internal bool CanPlay(string track) => _pathByTrack.ContainsKey(track);

    /// <summary>Opens the clip one track plays, or nothing when the container carries no recording of it.</summary>
    internal AudioClip? OpenClip(IAudioService audio, string track)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrWhiteSpace(track);
        if (!_pathByTrack.TryGetValue(track, out string? path)) return null;
        using ContentReference reference = _container.OpenReference(path);
        return audio.OpenClipFromContent(new AudioClipFromContentRequest(reference));
    }

    /// <summary>The score a composition resolved over, or nothing when no music container was opened.</summary>
    internal static DaggerfallMusicBundle? For(ResolvedGameComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return composition.Installed.FirstOrDefault(installed => installed.Name == ContainerName)?.Container is { } container ? Read(container) : null;
    }

    /// <summary>Reads which tracks a music container carries.</summary>
    /// <exception cref="InvalidOperationException">The container carries two recordings of one track.</exception>
    internal static DaggerfallMusicBundle Read(ProductContentBundle container)
    {
        ArgumentNullException.ThrowIfNull(container);
        Dictionary<string, string> pathByTrack = new(StringComparer.Ordinal);
        foreach (ContentReferenceInfo entry in container.Entries.Span)
        {
            // Tracks sit at the root; the container's own description and anything in a folder are not tracks.
            if (entry.Path.Contains('/', StringComparison.Ordinal)) continue;
            string extension = Path.GetExtension(entry.Path).ToLowerInvariant();
            if (!Extensions.Contains(extension, StringComparer.Ordinal)) continue;
            string track = Path.GetFileNameWithoutExtension(entry.Path).ToLowerInvariant();
            if (!pathByTrack.TryAdd(track, entry.Path))
                throw new InvalidOperationException($"{container.Id} carries two recordings of track '{track}': '{pathByTrack[track]}' and '{entry.Path}'.");
        }

        return new DaggerfallMusicBundle(container, pathByTrack);
    }
}
