using Rusty.Engine;
using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Opens the score's tracks by name from the music container installed beside the product.
/// </summary>
/// <remarks>
/// <para>
/// The music container (<see cref="ContainerName"/>) holds one recording per classic track at its root, named
/// <c>&lt;track&gt;.&lt;ext&gt;</c>: the track identifier the playlists name (<c>song_dungeon</c>, <c>song_04</c>)
/// and the extension of any format the Engine's audio plays. Any source of recordings named that way works,
/// and nothing pins the bytes of one; the playlists, which say which track plays where, stay in
/// <see cref="DaggerfallMusicDirector"/>.
/// </para>
/// <para>
/// A track the container does not carry is skipped by its playlist, and the tracks no recording answers are
/// named once, in <see cref="MissingTracks"/>. A container that carries two recordings of one track is refused
/// by name: playing either would silently drop the other.
/// </para>
/// </remarks>
internal sealed class DaggerfallMusicBundle
{
    /// <summary>The music container's file name, installed beside the product.</summary>
    internal const string ContainerName = "music.rpak";

    /// <summary>The formats the Engine's audio plays, by file extension.</summary>
    internal static IReadOnlyList<string> Extensions { get; } = [".ogg", ".opus", ".mp3", ".flac", ".wav"];

    /// <summary>Every track any playlist names, in either playlist set, ordered.</summary>
    internal static IReadOnlyList<string> PlaylistTracks { get; } =
        [.. Enum.GetValues<DaggerfallMusicContext>()
            .SelectMany(context => DaggerfallMusicDirector.PlaylistFor(context, alternatePlaylists: false).Concat(DaggerfallMusicDirector.PlaylistFor(context, alternatePlaylists: true)))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private readonly ProductContentBundle _container;
    private readonly IReadOnlyDictionary<string, string> _pathByTrack;

    private DaggerfallMusicBundle(ProductContentBundle container, IReadOnlyDictionary<string, string> pathByTrack)
    {
        _container = container;
        _pathByTrack = pathByTrack;
        MissingTracks = [.. PlaylistTracks.Where(track => !pathByTrack.ContainsKey(track))];
    }

    /// <summary>The tracks the container carries, in stable order.</summary>
    internal IReadOnlyList<string> Tracks => [.. _pathByTrack.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The tracks the playlists name that the container carries no recording of, ordered.</summary>
    internal IReadOnlyList<string> MissingTracks { get; }

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
