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
            try
            {
                _music.Update(context, pick);
            }
            catch
            {
                // The director retires the previous loop before it resolves the next one, so a cue that
                // cannot open has still ended the old one. The failure stays terminal for the update, but
                // the change it already made is reported rather than left as an unreported silence.
                ReportMusicChange(playing, context);
                throw;
            }

            ReportMusicChange(playing, context);
            return;
        }

        // Nothing published answers this context. The director retires whatever played rather than
        // holding a loop the new context does not own.
        _music.Stop();
        ReportMusicChange(playing, context);
    }

    /// <summary>
    /// Records a cue change in the Engine's diagnostics: which donor track plays, and where it stopped.
    /// </summary>
    /// <remarks>
    /// A score is the one part of the product with no visible projection — nothing on screen says which
    /// track plays — so a change is published where the product publishes every other completed change.
    /// Only a change is reported, never a heartbeat: an ordinary update must stay allocation- and
    /// work-free, and a silent product run is still evidence that the loop never restarted.
    /// </remarks>
    private void ReportMusicChange(string? before, DaggerfallMusicContext context)
    {
        string? after = _music?.Playing;
        if (string.Equals(before, after, StringComparison.Ordinal)) return;
        if (after is not null)
        {
            _engine.Diagnostics.Publish(new DiagnosticsPublishRequest(
                DiagnosticsSeverity.Info,
                DiagnosticsDisposition.Accepted,
                "daggerfall.music",
                "cue.started",
                $"Music cue '{after}' started for context '{context}'.",
                string.Empty));
        }
        else if (before is not null)
        {
            _engine.Diagnostics.Publish(new DiagnosticsPublishRequest(
                DiagnosticsSeverity.Info,
                DiagnosticsDisposition.Accepted,
                "daggerfall.music",
                "cue.retired",
                $"Music cue '{before}' retired.",
                string.Empty));
        }
    }

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
        string? playing = _music.Playing;
        _musicRotation++;
        _music.Stop();
        ReportMusicChange(playing, MusicContext());
    }

    private void DisposeMusic(ref Exception? failure)
    {
        try
        {
            string? playing = _music?.Playing;
            _music?.Dispose();
            ReportMusicChange(playing, MusicContext());
        }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { DisposeAll([.. _musicClips.Values]); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        _musicClips.Clear();
    }
}
