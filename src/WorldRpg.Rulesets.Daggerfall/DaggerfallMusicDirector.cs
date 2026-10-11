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
    Interior,
    Palace,
    Overcast,
    TempleNeutral,
    TempleBad,
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
    /// <summary>Donor Dungeon playlist in source order.</summary>
    public static readonly IReadOnlyList<string> DungeonSongs =
        ["song_dungeon", "song_dungeon5", "song_dungeon6", "song_dungeon7", "song_dungeon8", "song_dungeon9", "song_gdngn10", "song_gdngn11", "song_gdungn4", "song_gdungn9", "song_04", "song_05", "song_07", "song_15", "song_28"];

    /// <summary>Donor DungeonFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> DungeonSongsFM =
        ["song_fm_dngn1", "song_fm_dngn1", "song_fm_dngn2", "song_fm_dngn3", "song_fm_dngn4", "song_fm_dngn5", "song_fdngn10", "song_fdngn11", "song_fdungn4", "song_fdungn9", "song_04fm", "song_05fm", "song_07fm", "song_15fm", "song_15fm"];

    /// <summary>Donor Sunny playlist in source order.</summary>
    public static readonly IReadOnlyList<string> SunnySongs =
        ["song_gday___d", "song_swimming", "song_gsunny2", "song_sunnyday", "song_02", "song_03", "song_22"];

    /// <summary>Donor SunnyFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> SunnySongsFM =
        ["song_fday___d", "song_fm_swim2", "song_fm_sunny", "song_02fm", "song_03fm", "song_22fm"];

    /// <summary>Donor Night playlist in source order.</summary>
    public static readonly IReadOnlyList<string> NightSongs =
        ["song_10", "song_11", "song_gcurse", "song_geerie", "song_gruins", "song_18", "song_21"];

    /// <summary>Donor NightFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> NightSongsFM =
        ["song_11fm", "song_fcurse", "song_feerie", "song_fruins", "song_18fm", "song_21fm"];

    /// <summary>Donor Tavern playlist in source order.</summary>
    public static readonly IReadOnlyList<string> TavernSongs =
        ["song_square_2", "song_tavern", "song_folk1", "song_folk2", "song_folk3"];

    /// <summary>Donor TavernFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> TavernSongsFM =
        ["song_fm_sqr_2"];

    /// <summary>Donor Shop playlist in source order.</summary>
    public static readonly IReadOnlyList<string> ShopSongs =
        ["song_gshop"];

    /// <summary>Donor ShopFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> ShopSongsFM =
        ["song_fm_sqr_2"];

    /// <summary>Donor MagesGuild playlist in source order.</summary>
    public static readonly IReadOnlyList<string> MagesGuildSongs =
        ["song_gmage_3", "song_magic_2"];

    /// <summary>Donor MagesGuildFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> MagesGuildSongsFM =
        ["song_fm_nite3"];

    /// <summary>Donor Temple playlist in source order.</summary>
    public static readonly IReadOnlyList<string> TempleSongs =
        ["song_ggood"];

    /// <summary>Donor TempleFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> TempleSongsFM =
        ["song_fgood"];

    /// <summary>Donor TempleNeutral playlist in source order.</summary>
    public static readonly IReadOnlyList<string> TempleNeutralSongs =
        ["song_gneut"];

    /// <summary>Donor TempleNeutralFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> TempleNeutralSongsFM =
        ["song_fneut"];

    /// <summary>Donor TempleBad playlist in source order.</summary>
    public static readonly IReadOnlyList<string> TempleBadSongs =
        ["song_gbad"];

    /// <summary>Donor TempleBadFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> TempleBadSongsFM =
        ["song_fbad"];

    /// <summary>Donor Knight playlist in source order.</summary>
    public static readonly IReadOnlyList<string> KnightSongs =
        ["song_17"];

    /// <summary>Donor KnightFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> KnightSongsFM =
        ["song_17fm"];

    /// <summary>Donor Cloudy playlist in source order.</summary>
    public static readonly IReadOnlyList<string> CloudySongs =
        ["song_gday___d", "song_swimming", "song_gsunny2", "song_sunnyday", "song_02", "song_03", "song_22", "song_29", "song_12"];

    /// <summary>Donor CloudyFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> CloudySongsFM =
        ["song_fday___d", "song_fm_swim2", "song_fm_sunny", "song_02fm", "song_03fm", "song_22fm", "song_29fm", "song_12fm"];

    /// <summary>Donor Overcast playlist in source order.</summary>
    public static readonly IReadOnlyList<string> OvercastSongs =
        ["song_29", "song_12", "song_13", "song_gpalac", "song_overcast"];

    /// <summary>Donor OvercastFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> OvercastSongsFM =
        ["song_29fm", "song_12fm", "song_13fm", "song_fpalac", "song_fmover_c"];

    /// <summary>Donor Rain playlist in source order.</summary>
    public static readonly IReadOnlyList<string> RainSongs =
        ["song_overlong", "song_raining", "song_08"];

    /// <summary>Donor RainFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> RainSongsFM =
        ["song_fmover_c", "song_fm_rain", "song_08fm"];

    /// <summary>Donor Snow playlist in source order.</summary>
    public static readonly IReadOnlyList<string> SnowSongs =
        ["song_20", "song_gsnow__b", "song_oversnow", "song_snowing"];

    /// <summary>Donor SnowFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> SnowSongsFM =
        ["song_20fm", "song_fsnow__b", "song_fmover_s"];

    /// <summary>Donor Interior playlist in source order.</summary>
    public static readonly IReadOnlyList<string> InteriorSongs =
        ["song_23"];

    /// <summary>Donor InteriorFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> InteriorSongsFM =
        ["song_23fm"];

    /// <summary>Donor Palace playlist in source order.</summary>
    public static readonly IReadOnlyList<string> PalaceSongs =
        ["song_06"];

    /// <summary>Donor PalaceFM playlist in source order.</summary>
    public static readonly IReadOnlyList<string> PalaceSongsFM =
        ["song_06fm"];

    private readonly bool _alternatePlaylists;
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
    public DaggerfallMusicDirector(IAudioService? audio, Func<string, AudioClip?> resolve, Action<string>? retire = null, bool alternatePlaylists = false)
    {
        _alternatePlaylists = alternatePlaylists;
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
        // The score uses a global 2D emitter. Keep a valid positive distance even
        // though the Engine applies spatial falloff only to spatial sources.
        _voice = _audio.CreateVoice(new AudioSourceDescriptor(
            clip,
            AudioBus.Ambient,
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

    /// <summary>
    /// The donor tracks a context answers, in the donor's own list order.
    /// </summary>
    /// <remarks>
    /// A caller that has to choose among published cues needs the donor's list rather than a copy of it:
    /// the session asks here, then names one of these tracks through <see cref="Update"/>. A context the
    /// donor leaves to whatever plays — combat and the menu — answers the empty list.
    /// </remarks>
    public static IReadOnlyList<string> PlaylistFor(DaggerfallMusicContext context, bool alternatePlaylists = false)
    {
        if (!Enum.IsDefined(context))
        {
            throw new ArgumentOutOfRangeException(nameof(context), context, "Music answers to no context the contract declares.");
        }

        return context switch
        {
            DaggerfallMusicContext.Dungeon => alternatePlaylists ? DungeonSongsFM : DungeonSongs,
            DaggerfallMusicContext.Sunny => alternatePlaylists ? SunnySongsFM : SunnySongs,
            DaggerfallMusicContext.Night => alternatePlaylists ? NightSongsFM : NightSongs,
            DaggerfallMusicContext.Tavern => alternatePlaylists ? TavernSongsFM : TavernSongs,
            DaggerfallMusicContext.Shop => alternatePlaylists ? ShopSongsFM : ShopSongs,
            DaggerfallMusicContext.MagesGuild => alternatePlaylists ? MagesGuildSongsFM : MagesGuildSongs,
            DaggerfallMusicContext.Temple => alternatePlaylists ? TempleSongsFM : TempleSongs,
            DaggerfallMusicContext.Knight => alternatePlaylists ? KnightSongsFM : KnightSongs,
            DaggerfallMusicContext.Cloudy => alternatePlaylists ? CloudySongsFM : CloudySongs,
            DaggerfallMusicContext.Rain => alternatePlaylists ? RainSongsFM : RainSongs,
            DaggerfallMusicContext.Snow => alternatePlaylists ? SnowSongsFM : SnowSongs,
            DaggerfallMusicContext.Interior => alternatePlaylists ? InteriorSongsFM : InteriorSongs,
            DaggerfallMusicContext.Palace => alternatePlaylists ? PalaceSongsFM : PalaceSongs,
            DaggerfallMusicContext.Overcast => alternatePlaylists ? OvercastSongsFM : OvercastSongs,
            DaggerfallMusicContext.TempleNeutral => alternatePlaylists ? TempleNeutralSongsFM : TempleNeutralSongs,
            DaggerfallMusicContext.TempleBad => alternatePlaylists ? TempleBadSongsFM : TempleBadSongs,
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
        IReadOnlyList<string> playlist = PlaylistFor(context, _alternatePlaylists);
        return playlist[pick % playlist.Count];
    }
}
