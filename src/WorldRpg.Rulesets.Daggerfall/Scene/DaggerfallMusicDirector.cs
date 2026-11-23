using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What the music answers to: the donor's song contexts in product vocabulary.</summary>
public enum DaggerfallMusicContext
{
    Dungeon,
    Night,
    Tavern,
    Shop,
    MagesGuild,
    Temple,
    Knight,
    Sunny,
    Cloudy,
    Rain,
    Snow,
    Combat,
    Menu,
    Interior,
    Palace,
    Overcast,
    TempleNeutral,
    TempleBad,
    Castle,
    Title,
}

/// <summary>
/// Contextual music selection with explicit loop lifetime: the donor's song lists, in the names of Patch's
/// arrangement of the score, choose the track identity for each context, and one loop plays at a time through safe Engine audio. A
/// repeated update for the playing context allocates nothing; a context change retires the old
/// loop before the new one starts; a quest cue overrides and then resumes what it interrupted.
/// Track identities resolve against admitted audio: MIDI playback is excluded, so a context
/// whose track has no admitted clip stays silent rather than synthesizing music. The session
/// owns the one director; unload and disposal retire the loop with it.
/// </summary>
/// <remarks>
/// A cue is a retained Engine voice, not an emitted one-shot. An emitted signal is keyed by its
/// signal id and can be neither stopped nor replaced, and its clip may not be disposed until the
/// one-shot completes — which a looping cue never does. A voice is the Engine's own owner for a
/// sound with a lifetime: it loops until this director disposes it, so a context change or a
/// session teardown really ends the previous loop instead of leaving it playing under the next.
/// </remarks>
public sealed class DaggerfallMusicDirector : IDisposable
{
    /// <summary>Every dungeon's songs: the donor's Dungeon playlist, and three of the dungeon songs the donor left unused.</summary>
    public static readonly IReadOnlyList<string> DungeonSongs = Numbered("dungeon", 17);

    /// <summary>The donor's Sunny playlist, with the FM set's own swimming song (outside-day-sunny-08).</summary>
    public static readonly IReadOnlyList<string> SunnySongs = Numbered("outside-day-sunny", 8);

    /// <summary>The donor's Night playlist.</summary>
    public static readonly IReadOnlyList<string> NightSongs = Numbered("outside-night", 7);

    /// <summary>The donor's Tavern playlist.</summary>
    public static readonly IReadOnlyList<string> TavernSongs = Numbered("tavern", 5);

    /// <summary>The donor's Shop playlist.</summary>
    public static readonly IReadOnlyList<string> ShopSongs = ["shop-01"];

    /// <summary>The donor's MagesGuild playlist, with the FM set's own guild song (mages-guild-03).</summary>
    public static readonly IReadOnlyList<string> MagesGuildSongs = Numbered("mages-guild", 3);

    /// <summary>The donor's Temple playlist: Arkay, Mara and Dibella.</summary>
    public static readonly IReadOnlyList<string> TempleSongs = ["temple-arkay-mara-dibella-01"];

    /// <summary>The donor's TempleNeutral playlist: Akatosh and Kynareth.</summary>
    public static readonly IReadOnlyList<string> TempleNeutralSongs = ["temple-akatosh-kynareth-01"];

    /// <summary>The donor's TempleBad playlist: Zen, Julianos and Stendarr.</summary>
    public static readonly IReadOnlyList<string> TempleBadSongs = ["temple-zen-julianos-stendarr-01"];

    /// <summary>The donor's Knight playlist: the fighters' trainers in temple buildings.</summary>
    public static readonly IReadOnlyList<string> KnightSongs = ["fighters-guild-temple-01"];

    /// <summary>The donor's Cloudy playlist: the sunny songs and two of the overcast ones.</summary>
    public static readonly IReadOnlyList<string> CloudySongs = [.. SunnySongs, "outside-day-overcast-01", "outside-day-overcast-03"];

    /// <summary>The donor's Overcast playlist, which the castle's song joins.</summary>
    public static readonly IReadOnlyList<string> OvercastSongs = [.. Numbered("outside-day-overcast", 4), "castle-dungeon-01"];

    /// <summary>The donor's Rain playlist.</summary>
    public static readonly IReadOnlyList<string> RainSongs = Numbered("outside-day-rain", 3);

    /// <summary>The donor's Snow playlist.</summary>
    public static readonly IReadOnlyList<string> SnowSongs = Numbered("outside-day-snow", 4);

    /// <summary>The donor's Interior playlist.</summary>
    public static readonly IReadOnlyList<string> InteriorSongs = ["building-interior-01"];

    /// <summary>The donor's Palace playlist.</summary>
    public static readonly IReadOnlyList<string> PalaceSongs = ["palace-interior-01"];

    /// <summary>The donor's Castle playlist: a dungeon's castle block.</summary>
    public static readonly IReadOnlyList<string> CastleSongs = ["castle-dungeon-01"];

    /// <summary>The entry screen's song, until a game starts and the world's own context replaces it.</summary>
    public static readonly IReadOnlyList<string> TitleSongs = ["title-menu-01"];

    /// <summary>A numbered series of Patch's tracks: <c>dungeon-01</c> to <c>dungeon-17</c>.</summary>
    private static string[] Numbered(string name, int count) =>
        [.. Enumerable.Range(1, count).Select(number => $"{name}-{number:00}")];

    private readonly IAudioService? _audio;
    private readonly Func<string, AudioClip?> _resolve;
    private readonly Action<string>? _retire;
    private bool _disposed;
    private string? _playing;
    private string? _interrupted;
    private DaggerfallMusicContext? _context;
    private bool _questCue;
    private AudioVoice? _voice;

    /// <summary>Creates a director over Engine audio with a track resolver.</summary>
    public DaggerfallMusicDirector(IAudioService? audio, Func<string, AudioClip?> resolve, Action<string>? retire = null)
    {
        _audio = audio;
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _retire = retire;
    }

    /// <summary>The track identity playing now, if any.</summary>
    public string? Playing => _playing;

    /// <summary>
    /// Updates the context: the same context keeps its loop, a new context retires the old loop
    /// and starts the donor's first listed song for the context, and a context with no admitted
    /// clip stays silent. Answers the track identity selected, if any.
    /// </summary>
    public string? Update(DaggerfallMusicContext context, int pick = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(context))
        {
            throw new ArgumentOutOfRangeException(nameof(context), context, "Music answers to no context the contract declares.");
        }

        if (_questCue && _context == context) return _playing;
        _questCue = false;
        _context = context;
        string? track = TrackFor(context, pick);
        if (track is null || string.Equals(_playing, track, StringComparison.Ordinal))
        {
            return _playing;
        }

        Retire();
        AudioClip? clip = _resolve(track);
        if (Start(clip))
        {
            _playing = track;
        }

        return _playing;
    }

    /// <summary>Overrides with a quest cue, remembering what it interrupted.</summary>
    public string? Cue(string track)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(track);
        if (string.Equals(_playing, track, StringComparison.Ordinal))
        {
            return _playing;
        }

        _interrupted = _playing;
        _questCue = true;
        Retire();
        AudioClip? clip = _resolve(track);
        if (Start(clip))
        {
            _playing = track;
        }

        return _playing;
    }

    /// <summary>Resumes what the cue interrupted, if anything still claims it.</summary>
    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _questCue = false;
        string? interrupted = _interrupted;
        _interrupted = null;
        if (interrupted is null) return;
        Retire();
        AudioClip? clip = _resolve(interrupted);
        if (Start(clip))
        {
            _playing = interrupted;
        }
    }

    /// <summary>Stops the loop: pause, load and quit all retire the track.</summary>
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _questCue = false;
        _interrupted = null;
        Retire();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Retire();
    }

    /// <summary>
    /// Starts the one loop for a resolved clip, answering whether anything plays now.
    /// </summary>
    /// <remarks>
    /// The voice is retained here and released in <see cref="Retire"/>. A clip the caller answers but
    /// cannot play — no audio service at all — leaves the director silent rather than tracking a cue
    /// the Engine was never asked to start.
    /// </remarks>
    private bool Start(AudioClip? clip)
    {
        if (_audio is null || clip is null) return false;
        // The score uses a global 2D emitter on the Music bus, whose level the player's Music control sets. Keep a
        // valid positive distance even though the Engine applies spatial falloff only to spatial sources.
        _voice = _audio.CreateVoice(new AudioSourceDescriptor(
            clip,
            AudioBus.Music,
            0.8f,
            1.0f,
            true,
            0.0f,
            1.0f,
            AudioRolloff.LinearDecibels,
            0.0f,
            AudioEmitterKind.Global2d,
            System.Numerics.Vector3.Zero,
            0,
            System.Numerics.Vector3.Zero));
        return true;
    }

    private void Retire()
    {
        if (_playing is null) return;
        string retired = _playing;
        _playing = null;
        AudioVoice? voice = _voice;
        _voice = null;
        voice?.Dispose();
        _retire?.Invoke(retired);
    }

    /// <summary>Every track any playlist names, ordered.</summary>
    internal static IReadOnlyList<string> PlaylistTracks { get; } =
        [.. Enum.GetValues<DaggerfallMusicContext>()
            .SelectMany(PlaylistFor)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>The tracks the playlists name that the music container carries no recording of, ordered.</summary>
    internal static IReadOnlyList<string> MissingTracks(DaggerfallMusicBundle music) => [.. PlaylistTracks.Where(track => !music.CanPlay(track))];

    /// <summary>
    /// The donor tracks a context answers, in the donor's own list order.
    /// </summary>
    /// <remarks>
    /// A caller that has to choose among published cues needs the donor's list rather than a copy of it:
    /// the session asks here, then names one of these tracks through <see cref="Update"/>. A context the
    /// donor leaves to whatever plays — combat and the menu — answers the empty list.
    /// </remarks>
    public static IReadOnlyList<string> PlaylistFor(DaggerfallMusicContext context)
    {
        if (!Enum.IsDefined(context))
        {
            throw new ArgumentOutOfRangeException(nameof(context), context, "Music answers to no context the contract declares.");
        }

        return context switch
        {
            DaggerfallMusicContext.Dungeon => DungeonSongs,
            DaggerfallMusicContext.Sunny => SunnySongs,
            DaggerfallMusicContext.Night => NightSongs,
            DaggerfallMusicContext.Tavern => TavernSongs,
            DaggerfallMusicContext.Shop => ShopSongs,
            DaggerfallMusicContext.MagesGuild => MagesGuildSongs,
            DaggerfallMusicContext.Temple => TempleSongs,
            DaggerfallMusicContext.Knight => KnightSongs,
            DaggerfallMusicContext.Cloudy => CloudySongs,
            DaggerfallMusicContext.Rain => RainSongs,
            DaggerfallMusicContext.Snow => SnowSongs,
            DaggerfallMusicContext.Interior => InteriorSongs,
            DaggerfallMusicContext.Palace => PalaceSongs,
            DaggerfallMusicContext.Castle => CastleSongs,
            DaggerfallMusicContext.Overcast => OvercastSongs,
            DaggerfallMusicContext.TempleNeutral => TempleNeutralSongs,
            DaggerfallMusicContext.TempleBad => TempleBadSongs,
            DaggerfallMusicContext.Title => TitleSongs,
            DaggerfallMusicContext.Combat or DaggerfallMusicContext.Menu => [],
            _ => throw new ArgumentOutOfRangeException(nameof(context), context, "Music answers to no context the contract declares."),
        };
    }

    // Combat and the menu name no donor song: battle never interrupts the song and the menu
    // never starts one, so both keep whatever plays. Pause, load and quit retire through Stop and
    // resume through Resume rather than through a context.
    private string? TrackFor(DaggerfallMusicContext context, int pick)
    {
        if (context is DaggerfallMusicContext.Combat or DaggerfallMusicContext.Menu) return _playing;
        IReadOnlyList<string> playlist = PlaylistFor(context);
        return playlist[pick % playlist.Count];
    }
}
