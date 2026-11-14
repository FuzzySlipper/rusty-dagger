using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The session's ordinary music: the donor context a place answers, what plays for it, and when it retires. The session
/// tells it the context each admitted update; it holds the score, the director, the clips it opened and the rotation.
/// </summary>
internal sealed class DaggerfallMusic
{
    private readonly IEngineContext _engine;
    private readonly bool _alternatePlaylists;
    private readonly int _magesFactionId;
    private readonly int _fightersTrainerFactionId;
    private readonly Dictionary<string, AudioClip> _clips = new(StringComparer.Ordinal);
    private int _rotation;
    private DaggerfallMusicContext? _silentContext;

    /// <param name="bundle">The music container's tracks; without one there is no score and nothing plays.</param>
    /// <param name="magesFactionId">The Mages Guild's faction, whose hall plays the guild's songs.</param>
    /// <param name="fightersTrainerFactionId">The fighters' trainer's faction, whose temple building plays the knight's songs.</param>
    internal DaggerfallMusic(IEngineContext engine, DaggerfallMusicBundle? bundle, bool alternatePlaylists, int magesFactionId, int fightersTrainerFactionId)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _alternatePlaylists = alternatePlaylists;
        _magesFactionId = magesFactionId;
        _fightersTrainerFactionId = fightersTrainerFactionId;
        Bundle = bundle;
        // The score's tracks are read by name from the music container, so the resolver the director asks is this
        // owner's own: it keeps the Engine resource alive for as long as that track plays and releases it on disposal.
        if (bundle is not null)
        {
            Director = new DaggerfallMusicDirector(engine.Audio, ResolveClip, ReportRetired, alternatePlaylists);
            ReportMissingTracks(bundle);
        }
    }

    /// <summary>The music container's tracks, or nothing when the composition has none.</summary>
    internal DaggerfallMusicBundle? Bundle { get; }

    /// <summary>The director that keeps the one loop, or nothing without a score.</summary>
    internal DaggerfallMusicDirector? Director { get; }

    /// <summary>Whether there is a score to play; without one the session need not read the world's context.</summary>
    internal bool Scored => Director is not null;

    /// <summary>The donor track playing now, or nothing when no admitted track plays.</summary>
    internal string? Track => Director?.Playing;

    /// <summary>The donor context under ground: a dungeon's castle block plays the castle's songs.</summary>
    internal static DaggerfallMusicContext Underground(bool insideCastle) =>
        insideCastle ? DaggerfallMusicContext.Castle : DaggerfallMusicContext.Dungeon;

    /// <summary>The donor context of a building's interior, by its type and faction; one with no building plays the plain interior's.</summary>
    internal DaggerfallMusicContext Inside(DaggerfallInteriorBuilding? building) => building?.BuildingType switch
    {
        0 or 2 or 3 or 5 or 6 or 7 or 8 or 9 or 10 or 12 or 13 => DaggerfallMusicContext.Shop,
        15 => DaggerfallMusicContext.Tavern,
        11 when building.FactionId == _magesFactionId => DaggerfallMusicContext.MagesGuild,
        16 => DaggerfallMusicContext.Palace,
        14 when building.FactionId == _fightersTrainerFactionId => DaggerfallMusicContext.Knight,
        14 => building.FactionId switch
        {
            0x54 or 0x16 or 0x5e or 0x1b or 0x6a or 0x21 => DaggerfallMusicContext.TempleBad,
            0x5c or 0x1a or 0x24 or 0x23 => DaggerfallMusicContext.TempleNeutral,
            _ => DaggerfallMusicContext.Temple,
        },
        _ => DaggerfallMusicContext.Interior,
    };

    /// <summary>Whether an exterior site plays the night's songs whatever the hour: a dungeon's grounds, a coven, a poor home, a graveyard.</summary>
    /// <remarks>Battle does not interrupt the donor's environmental score: no combat track is invented.</remarks>
    internal static bool NightPlace(DaggerfallSiteKind? site) => site is DaggerfallSiteKind.DungeonKeep or DaggerfallSiteKind.DungeonLabyrinth
        or DaggerfallSiteKind.DungeonRuin or DaggerfallSiteKind.Coven or DaggerfallSiteKind.HomePoor or DaggerfallSiteKind.Graveyard;

    /// <summary>The donor context of the open air by day, by the weather.</summary>
    internal static DaggerfallMusicContext Outside(DaggerfallWeatherKind weather) => weather switch
    {
        DaggerfallWeatherKind.Cloudy => DaggerfallMusicContext.Cloudy,
        DaggerfallWeatherKind.Overcast or DaggerfallWeatherKind.Fog => DaggerfallMusicContext.Overcast,
        DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder => DaggerfallMusicContext.Rain,
        DaggerfallWeatherKind.Snow => DaggerfallMusicContext.Snow,
        _ => DaggerfallMusicContext.Sunny,
    };

    /// <summary>
    /// Advances the score to <paramref name="context"/>. The director keeps a playing cue rather than restarting it, so an
    /// ordinary update allocates nothing and emits nothing.
    /// </summary>
    /// <param name="dayNumber">The calendar's day, by which a tavern picks its song.</param>
    internal void Advance(DaggerfallMusicContext context, long dayNumber)
    {
        if (Director is null || Bundle is null) return;
        string? playing = Director.Playing;
        IReadOnlyList<string> playlist = DaggerfallMusicDirector.PlaylistFor(context, _alternatePlaylists);
        for (int offset = 0; offset < playlist.Count; offset++)
        {
            // The rotation is per site change, so a place with more than one published song starts a
            // different one than the place before it, exactly as the donor's own lists rotate.
            int rotation = context == DaggerfallMusicContext.Tavern ? (int)(dayNumber % playlist.Count) : _rotation;
            int pick = (rotation + offset) % playlist.Count;
            if (!Bundle.CanPlay(playlist[pick])) continue;
            // A cue that cannot open throws out of the update and stays terminal for it. The loop it
            // replaced was already reported by the director, so nothing ends here unreported.
            _silentContext = null;
            Director.Update(context, pick);
            if (!string.Equals(playing, Director.Playing, StringComparison.Ordinal) && Director.Playing is { } started)
            {
                Report("cue.started", $"Music cue '{started}' started for context '{context}'.");
            }

            return;
        }

        // Nothing published answers this context. The director retires whatever played rather than
        // holding a loop the new context does not own, and reports the retirement itself.
        Director.Stop();
        if (_silentContext != context)
        {
            _silentContext = context;
            Report("cue.unavailable", $"No published music cue for context '{context}' in the {(_alternatePlaylists ? "alternate" : "standard")} playlist.");
        }
    }

    /// <summary>Retires the loop for a site change and moves the rotation on to the new site's song.</summary>
    internal void ChangeSite()
    {
        if (Director is null) return;
        _rotation++;
        // The director reports the retirement, so this only has to move the rotation on.
        Director.Stop();
    }

    /// <summary>Ends the loop and releases every clip it opened, adding what failed to <paramref name="failure"/>.</summary>
    internal void Dispose(ref Exception? failure)
    {
        try { Director?.Dispose(); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { DisposeClips([.. _clips.Values]); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        _clips.Clear();
    }

    private static void DisposeClips(IReadOnlyList<AudioClip> clips)
    {
        List<Exception>? failures = null;
        for (int index = clips.Count - 1; index >= 0; index--)
        {
            try { clips[index].Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    /// <summary>Opens the clip for one donor track, retaining its Engine resource until the voice retires.</summary>
    private AudioClip? ResolveClip(string track)
    {
        if (_clips.TryGetValue(track, out AudioClip? cached)) return cached;
        AudioClip? clip = Bundle?.OpenClip(_engine.Audio, track);
        if (clip is not null) _clips.Add(track, clip);
        return clip;
    }

    /// <summary>
    /// Warns once that the music container carries no recording of some tracks the playlists name; their
    /// contexts play their other tracks, and a context with none stays silent.
    /// </summary>
    private void ReportMissingTracks(DaggerfallMusicBundle music)
    {
        if (music.MissingTracks.Count == 0) return;
        _engine.Diagnostics.Publish(new DiagnosticsPublishRequest(
            DiagnosticsSeverity.Warning,
            DiagnosticsDisposition.Accepted,
            "daggerfall.music",
            "tracks.missing",
            $"{DaggerfallMusicBundle.ContainerName} carries no recording of {music.MissingTracks.Count} of the {DaggerfallMusicBundle.PlaylistTracks.Count} tracks the playlists name: {string.Join(", ", music.MissingTracks)}.",
            string.Empty));
    }

    /// <summary>
    /// Records that a cue ended, wherever it ended: a site change, a context with nothing published, a
    /// failure to open the next cue, or disposal.
    /// </summary>
    /// <remarks>
    /// The director owns the loop and therefore owns this report: it retires the voice before it asks
    /// for the next clip, so a retirement the director decides is a retirement the product knows about
    /// even when resolving the replacement throws. Only a change is reported, never a heartbeat: an
    /// ordinary update must stay allocation- and work-free, and a silent run is still evidence that the
    /// loop never restarted.
    /// </remarks>
    private void ReportRetired(string track)
    {
        // The director has already disposed the voice; the clip can now leave the live audio budget.
        if (_clips.Remove(track, out AudioClip? clip)) clip.Dispose();
        Report("cue.retired", $"Music cue '{track}' retired.");
    }

    /// <summary>
    /// Publishes one music change. A score is the one part of the product with no visible projection —
    /// nothing on screen says which track plays — so a change is published where the product publishes
    /// every other completed change.
    /// </summary>
    private void Report(string code, string message) =>
        _engine.Diagnostics.Publish(new DiagnosticsPublishRequest(
            DiagnosticsSeverity.Info,
            DiagnosticsDisposition.Accepted,
            "daggerfall.music",
            code,
            message,
            string.Empty));
}
