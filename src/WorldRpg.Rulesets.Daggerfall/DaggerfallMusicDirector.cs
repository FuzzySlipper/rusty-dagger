using Rusty.Engine;

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
}

/// <summary>
/// Contextual music selection with explicit loop lifetime: the donor's song lists choose the
/// track identity for each context, and one loop plays at a time through safe Engine audio. A
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
    /// <summary>The donor's dungeon songs, in list order.</summary>
    public static readonly IReadOnlyList<string> DungeonSongs =
    [
        "song_dungeon", "song_dungeon5", "song_dungeon6", "song_dungeon7", "song_dungeon8",
        "song_dungeon9", "song_gdngn10", "song_gdngn11", "song_gdungn4", "song_gdungn9",
        "song_04", "song_05", "song_07", "song_15", "song_28",
    ];

    /// <summary>The donor's sunny songs, in list order.</summary>
    public static readonly IReadOnlyList<string> SunnySongs =
    [
        "song_gday___d", "song_swimming", "song_gsunny2", "song_sunnyday",
        "song_02", "song_03", "song_22",
    ];

    /// <summary>The donor's night songs, in list order.</summary>
    public static readonly IReadOnlyList<string> NightSongs =
    [
        "song_10", "song_11", "song_gcurse", "song_geerie", "song_gruins", "song_18", "song_21",
    ];

    /// <summary>The donor's tavern songs, in list order.</summary>
    public static readonly IReadOnlyList<string> TavernSongs =
    [
        "song_square_2", "song_tavern", "song_folk1", "song_folk2", "song_folk3",
    ];

    /// <summary>The donor's shop songs, in list order.</summary>
    public static readonly IReadOnlyList<string> ShopSongs = ["song_gshop"];

    /// <summary>The donor's Mages Guild songs, in list order.</summary>
    public static readonly IReadOnlyList<string> MagesGuildSongs = ["song_gmage_3", "song_magic_2"];

    /// <summary>The donor's temple songs, in list order.</summary>
    public static readonly IReadOnlyList<string> TempleSongs = ["song_ggood", "song_gneut", "song_gbad"];

    /// <summary>The donor's knightly-order song.</summary>
    public static readonly IReadOnlyList<string> KnightSongs = ["song_17"];

    /// <summary>The donor's cloudy songs, in list order.</summary>
    public static readonly IReadOnlyList<string> CloudySongs =
    [
        "song_gday___d", "song_swimming", "song_gsunny2", "song_sunnyday",
        "song_02", "song_03", "song_22", "song_29", "song_12",
    ];

    /// <summary>The donor's rain songs, in list order.</summary>
    public static readonly IReadOnlyList<string> RainSongs = ["song_overlong", "song_raining", "song_08"];

    /// <summary>The donor's snow songs, in list order.</summary>
    public static readonly IReadOnlyList<string> SnowSongs = ["song_20", "song_gsnow__b", "song_oversnow"];

    private readonly IAudioService? _audio;
    private readonly Func<string, AudioClip?> _resolve;
    private readonly Action<string>? _retire;
    private bool _disposed;
    private string? _playing;
    private string? _interrupted;
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
        // A retained voice over a 2D emitter: the score is heard everywhere, so its distance attenuation
        // is neutral rather than zero. The Engine refuses a descriptor whose attenuation is not finite and
        // positive — the refusal arrives as a failed CreateVoice — and the other fields stay in the ranges
        // it validates: volume 0..1, pitch 0.25..4, spatial blend 0..1, pan -1..1.
        _voice = _audio.CreateVoice(new AudioSourceDescriptor(
            clip,
            AudioBus.Ambient,
            0.8f,
            1.0f,
            true,
            0.0f,
            1.0f,
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
            DaggerfallMusicContext.Combat or DaggerfallMusicContext.Menu => [],
            _ => throw new ArgumentOutOfRangeException(nameof(context), context, "Music answers to no context the contract declares."),
        };
    }

    // Combat and the menu name no donor song: battle never interrupts the song and the menu
    // never starts one, so both keep whatever plays. Pause, load and quit retire through Stop and
    // resume through Resume rather than through a context.
    private string? TrackFor(DaggerfallMusicContext context, int pick) => context switch
    {
        DaggerfallMusicContext.Dungeon => DungeonSongs[pick % DungeonSongs.Count],
        DaggerfallMusicContext.Sunny => SunnySongs[pick % SunnySongs.Count],
        DaggerfallMusicContext.Night => NightSongs[pick % NightSongs.Count],
        DaggerfallMusicContext.Combat => _playing,
        DaggerfallMusicContext.Menu => _playing,
        DaggerfallMusicContext.Tavern => TavernSongs[pick % TavernSongs.Count],
        DaggerfallMusicContext.Shop => ShopSongs[pick % ShopSongs.Count],
        DaggerfallMusicContext.MagesGuild => MagesGuildSongs[pick % MagesGuildSongs.Count],
        DaggerfallMusicContext.Temple => TempleSongs[pick % TempleSongs.Count],
        DaggerfallMusicContext.Knight => KnightSongs[pick % KnightSongs.Count],
        DaggerfallMusicContext.Cloudy => CloudySongs[pick % CloudySongs.Count],
        DaggerfallMusicContext.Rain => RainSongs[pick % RainSongs.Count],
        DaggerfallMusicContext.Snow => SnowSongs[pick % SnowSongs.Count],
        _ => throw new ArgumentOutOfRangeException(nameof(context), context, "Music answers to no context the contract declares."),
    };
}
