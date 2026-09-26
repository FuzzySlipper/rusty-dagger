using System.Reflection;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Music selection and loop lifetime: donor song lists, idempotent same-context updates, cue
/// override with resume, and silent contexts without admitted clips.
/// </summary>
public sealed class DaggerfallMusicDirectorTests
{
    [Fact]
    public void Selects_donor_songs_and_keeps_one_loop()
    {
        IAudioService service = AudioFake.Create(out AudioFake audio);
        Dictionary<string, AudioClip> clips = new(StringComparer.Ordinal)
        {
            ["song_dungeon"] = new AudioClip(new AudioClipHandle(1), static () => { }),
            ["song_square_2"] = new AudioClip(new AudioClipHandle(2), static () => { }),
        };
        using DaggerfallMusicDirector music = new(service, track => clips.GetValueOrDefault(track), audio.Retire);

        Assert.Equal("song_dungeon", music.Update(DaggerfallMusicContext.Dungeon));
        Assert.Single(audio.Started);
        // The same context again allocates no new loop.
        Assert.Equal("song_dungeon", music.Update(DaggerfallMusicContext.Dungeon));
        Assert.Single(audio.Started);
        // A new context retires the old loop before starting its own.
        Assert.Equal("song_square_2", music.Update(DaggerfallMusicContext.Tavern));
        Assert.Equal(2, audio.Started.Count);
        Assert.Equal(["song_dungeon"], audio.Retired);
        // Retiring really ends the loop: the voice is released, not just forgotten.
        Assert.Equal(1, audio.ReleasedVoices);

        // Combat and menu name no song: whatever plays keeps playing.
        Assert.Equal("song_square_2", music.Update(DaggerfallMusicContext.Combat));
        Assert.Equal("song_square_2", music.Update(DaggerfallMusicContext.Menu));
        Assert.Equal(2, audio.Started.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => music.Update((DaggerfallMusicContext)99));
    }

    [Fact]
    public void Unadmitted_tracks_stay_silent_with_cue_and_resume()
    {
        IAudioService silent = AudioFake.Create(out AudioFake audio);
        using DaggerfallMusicDirector music = new(silent, _ => null, audio.Retire);

        Assert.Null(music.Update(DaggerfallMusicContext.Dungeon));
        Assert.Null(music.Playing);
        Assert.Empty(audio.Started);
        Assert.Null(music.Cue("song_quest"));
        music.Resume();
        music.Stop();
        music.Dispose();
        Assert.Throws<ObjectDisposedException>(() => music.Update(DaggerfallMusicContext.Dungeon));
    }

    [Fact]
    public void Cue_overrides_and_resumes_the_interrupted_track()
    {
        IAudioService service = AudioFake.Create(out AudioFake audio);
        Dictionary<string, AudioClip> clips = new(StringComparer.Ordinal)
        {
            ["song_square_2"] = new AudioClip(new AudioClipHandle(2), static () => { }),
            ["song_quest"] = new AudioClip(new AudioClipHandle(3), static () => { }),
        };
        using DaggerfallMusicDirector music = new(service, track => clips.GetValueOrDefault(track), audio.Retire);

        music.Update(DaggerfallMusicContext.Tavern);
        Assert.Equal("song_quest", music.Cue("song_quest"));
        Assert.Equal(["song_square_2"], audio.Retired);
        music.Resume();
        Assert.Equal("song_square_2", music.Playing);
        Assert.Equal(3, audio.Started.Count);
    }

    /// <summary>
    /// Records the retained voices a director starts and releases, and every track it retires.
    /// </summary>
    /// <remarks>
    /// The fake answers voices rather than emitted signals because that is the Engine contract a loop
    /// needs: an emitted one-shot cannot be stopped, and the director's own facts assert that a context
    /// change releases the previous loop before the next one starts.
    /// </remarks>
    [Fact]
    public void Stopping_and_disposing_a_playing_loop_releases_its_voice_once()
    {
        IAudioService service = AudioFake.Create(out AudioFake audio);
        Dictionary<string, AudioClip> clips = new(StringComparer.Ordinal)
        {
            ["song_dungeon"] = new AudioClip(new AudioClipHandle(1), static () => { }),
            ["song_quest"] = new AudioClip(new AudioClipHandle(2), static () => { }),
        };
        DaggerfallMusicDirector music = new(service, track => clips.GetValueOrDefault(track), audio.Retire);
        Assert.Equal("song_dungeon", music.Update(DaggerfallMusicContext.Dungeon));
        Assert.Equal(0, audio.ReleasedVoices);

        // The cue overrides the loop, so the dungeon's voice is released as the quest voice starts.
        Assert.Equal("song_quest", music.Cue("song_quest"));
        Assert.Equal(1, audio.ReleasedVoices);
        music.Stop();
        Assert.Null(music.Playing);
        Assert.Equal(2, audio.ReleasedVoices);
        // A cue that a stop ended is not resumed afterwards: the interruption belonged to the cue.
        music.Resume();
        Assert.Null(music.Playing);
        Assert.Equal(2, audio.ReleasedVoices);

        // Disposing a stopped director releases nothing a second time.
        Assert.Equal("song_dungeon", music.Update(DaggerfallMusicContext.Dungeon));
        Assert.Equal(3, audio.Started.Count);
        music.Dispose();
        Assert.Equal(3, audio.ReleasedVoices);
        music.Dispose();
        Assert.Equal(3, audio.ReleasedVoices);
    }

    private class AudioFake : DispatchProxy
    {
        internal readonly List<string> Started = [];
        internal readonly List<string> Retired = [];
        internal int ReleasedVoices { get; private set; }

        internal static IAudioService Create(out AudioFake fake)
        {
            IAudioService service = DispatchProxy.Create<IAudioService, AudioFake>();
            fake = (AudioFake)(object)service;
            return service;
        }

        internal void Retire(string track) => Retired.Add(track);

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IAudioService.OpenClip) => new AudioClip(new AudioClipHandle(1), static () => { }),
            nameof(IAudioService.CreateVoice) => CreateVoice(),
            _ => throw new NotSupportedException(method?.Name),
        };

        private AudioVoice CreateVoice()
        {
            Started.Add($"voice-{Started.Count + 1}");
            int handle = Started.Count;
            return new AudioVoice(new AudioVoiceHandle(checked((ulong)handle)), () => ReleasedVoices++);
        }
    }
}
