using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The session's ordinary music: what plays where the player stands, and when it retires.
/// </summary>
/// <remarks>
/// The director owns cue selection and loop lifetime; the session owns the decision the director cannot
/// make, which is what the admitted world is. A dungeon plays its own songs, an outdoor or interior site
/// plays the day's or the night's, and changing site retires the loop so the next one starts the song the
/// new place plays rather than continuing the previous world's. A context whose published cues are absent
/// stays silent: the catalogue is deliberately smaller than the donor's playlists, and the alternative
/// would be playing a track the original game played somewhere else.
/// </remarks>
internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallMusicBundle? _musicBundle;
    private readonly DaggerfallMusicDirector? _music;
    private readonly Dictionary<string, AudioClip> _musicClips = new(StringComparer.Ordinal);
    private int _musicRotation;

    /// <summary>The donor track playing now, or nothing when no admitted track plays.</summary>
    internal string? MusicTrack => _music?.Playing;

    /// <summary>Opens the clip for one donor track, keeping the Engine resource for the session's life.</summary>
    private AudioClip? ResolveMusicClip(string track)
    {
        if (_musicClips.TryGetValue(track, out AudioClip? cached)) return cached;
        AudioClip? clip = _musicBundle?.OpenClip(_engine.Audio, track);
        if (clip is not null) _musicClips.Add(track, clip);
        return clip;
    }

    /// <summary>
    /// Advances the score to the context the world now has. The director keeps a playing cue rather than
    /// restarting it, so an ordinary update allocates nothing and emits nothing.
    /// </summary>
    private void AdvanceMusic()
    {
        if (_music is null || _musicBundle is null) return;
        string? playing = _music.Playing;
        DaggerfallMusicContext context = MusicContext();
        IReadOnlyList<string> playlist = DaggerfallMusicDirector.PlaylistFor(context);
        for (int offset = 0; offset < playlist.Count; offset++)
        {
            // The rotation is per site change, so a place with more than one published song starts a
            // different one than the place before it, exactly as the donor's own lists rotate.
            int pick = (_musicRotation + offset) % playlist.Count;
            if (!_musicBundle.CanPlay(playlist[pick])) continue;
            // A cue that cannot open throws out of the update and stays terminal for it. The loop it
            // replaced was already reported by the director, so nothing ends here unreported.
            _music.Update(context, pick);
            if (!string.Equals(playing, _music.Playing, StringComparison.Ordinal) && _music.Playing is { } started)
            {
                ReportMusicStarted(started, context);
            }

            return;
        }

        // Nothing published answers this context. The director retires whatever played rather than
        // holding a loop the new context does not own, and reports the retirement itself.
        _music.Stop();
    }

    /// <summary>Records that a cue started, and for which admitted context the session chose it.</summary>
    private void ReportMusicStarted(string track, DaggerfallMusicContext context) =>
        Report("cue.started", $"Music cue '{track}' started for context '{context}'.");

    /// <summary>
    /// Records that a cue ended, wherever it ended: a site change, a context with nothing published, a
    /// failure to open the next cue, or session disposal.
    /// </summary>
    /// <remarks>
    /// The director owns the loop and therefore owns this report: it retires the voice before it asks
    /// for the next clip, so a retirement the director decides is a retirement the product knows about
    /// even when resolving the replacement throws. Only a change is reported, never a heartbeat: an
    /// ordinary update must stay allocation- and work-free, and a silent run is still evidence that the
    /// loop never restarted.
    /// </remarks>
    private void ReportMusicRetired(string track) => Report("cue.retired", $"Music cue '{track}' retired.");

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

    /// <summary>The donor context the admitted world answers.</summary>
    private DaggerfallMusicContext MusicContext() => _activeProfileKey.Kind switch
    {
        DaggerfallWorldProfileKind.Dungeon => DaggerfallMusicContext.Dungeon,
        _ => _time.Calendar.IsDay ? DaggerfallMusicContext.Sunny : DaggerfallMusicContext.Night,
    };

    /// <summary>Retires the loop for a site change and moves the rotation on to the new site's song.</summary>
    private void ChangeMusicSite()
    {
        if (_music is null) return;
        _musicRotation++;
        // The director reports the retirement, so this only has to move the rotation on.
        _music.Stop();
    }

    private void DisposeMusic(ref Exception? failure)
    {
        try { _music?.Dispose(); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { DisposeAll([.. _musicClips.Values]); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        _musicClips.Clear();
    }
}
