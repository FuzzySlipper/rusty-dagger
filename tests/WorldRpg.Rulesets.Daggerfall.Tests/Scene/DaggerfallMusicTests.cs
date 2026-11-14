using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The music owner alone, with no session: the donor context a place answers, the song it plays for a context, the
/// rotation a site change moves on, the tavern's song by the day, a silent context reported once, and the clips it
/// releases.
/// </summary>
public sealed class DaggerfallMusicTests
{
    private const int Mages = 40;
    private const int FightersTrainer = 849;

    [Fact]
    public void The_donor_context_follows_the_place_the_building_and_the_weather()
    {
        DaggerfallMusic music = new(Engine().Context, null, false, Mages, FightersTrainer);

        Assert.Equal(DaggerfallMusicContext.Castle, DaggerfallMusic.Underground(insideCastle: true));
        Assert.Equal(DaggerfallMusicContext.Dungeon, DaggerfallMusic.Underground(insideCastle: false));
        Assert.Equal(DaggerfallMusicContext.Interior, music.Inside(null));
        Assert.Equal(DaggerfallMusicContext.Shop, music.Inside(Building(0, 0)));
        Assert.Equal(DaggerfallMusicContext.Tavern, music.Inside(Building(15, 0)));
        Assert.Equal(DaggerfallMusicContext.MagesGuild, music.Inside(Building(11, Mages)));
        Assert.Equal(DaggerfallMusicContext.Interior, music.Inside(Building(11, 0)));
        Assert.Equal(DaggerfallMusicContext.Palace, music.Inside(Building(16, 0)));
        Assert.Equal(DaggerfallMusicContext.Knight, music.Inside(Building(14, FightersTrainer)));
        Assert.Equal(DaggerfallMusicContext.TempleBad, music.Inside(Building(14, 0x54)));
        Assert.Equal(DaggerfallMusicContext.TempleNeutral, music.Inside(Building(14, 0x5c)));
        Assert.Equal(DaggerfallMusicContext.Temple, music.Inside(Building(14, 0)));
        Assert.True(DaggerfallMusic.NightPlace(DaggerfallSiteKind.Graveyard));
        Assert.False(DaggerfallMusic.NightPlace(null));
        Assert.Equal(DaggerfallMusicContext.Overcast, DaggerfallMusic.Outside(DaggerfallWeatherKind.Fog));
        Assert.Equal(DaggerfallMusicContext.Rain, DaggerfallMusic.Outside(DaggerfallWeatherKind.Thunder));
        Assert.Equal(DaggerfallMusicContext.Sunny, DaggerfallMusic.Outside(DaggerfallWeatherKind.Sunny));
    }

    [Fact]
    public void Without_a_score_nothing_plays_and_nothing_is_said()
    {
        EngineContextFake engine = Engine();
        DaggerfallMusic music = new(engine.Context, null, false, Mages, FightersTrainer);

        Assert.False(music.Scored);
        music.Advance(DaggerfallMusicContext.Dungeon, 0);
        music.ChangeSite();
        Exception? failure = null;
        music.Dispose(ref failure);

        Assert.Null(music.Track);
        Assert.Null(failure);
        Assert.Empty(engine.StartedAudioVoices);
        Assert.Empty(engine.PublishedDiagnostics);
    }

    [Fact]
    public void A_context_keeps_one_loop_and_a_site_change_moves_on_to_its_next_song()
    {
        EngineContextFake engine = Engine();
        DaggerfallMusic music = new(engine.Context, Score("song_dungeon", "song_dungeon5"), false, Mages, FightersTrainer);

        music.Advance(DaggerfallMusicContext.Dungeon, 0);
        music.Advance(DaggerfallMusicContext.Dungeon, 0);

        Assert.True(music.Scored);
        Assert.Equal("song_dungeon", music.Track);
        Assert.Single(engine.StartedAudioVoices);
        Assert.Equal(1, engine.OpenedMusicClips);

        music.ChangeSite();
        // The retirement releases the voice and the clip it played.
        Assert.Null(music.Track);
        Assert.Equal(1, engine.ReleasedAudioVoices);
        music.Advance(DaggerfallMusicContext.Dungeon, 0);
        Assert.Equal("song_dungeon5", music.Track);

        Exception? failure = null;
        music.Dispose(ref failure);
        Assert.Null(failure);
        Assert.Equal(2, engine.ReleasedAudioVoices);
        Assert.Equal(
            ["daggerfall.music/cue.started: Music cue 'song_dungeon' started for context 'Dungeon'.",
             "daggerfall.music/cue.retired: Music cue 'song_dungeon' retired.",
             "daggerfall.music/cue.started: Music cue 'song_dungeon5' started for context 'Dungeon'.",
             "daggerfall.music/cue.retired: Music cue 'song_dungeon5' retired."],
            Cues(engine));
    }

    [Fact]
    public void A_tavern_plays_its_song_by_the_day_whatever_the_rotation()
    {
        DaggerfallMusic music = new(Engine().Context, Score([.. DaggerfallMusicDirector.TavernSongs]), false, Mages, FightersTrainer);
        music.ChangeSite();

        music.Advance(DaggerfallMusicContext.Tavern, 7);

        Assert.Equal(DaggerfallMusicDirector.TavernSongs[7 % DaggerfallMusicDirector.TavernSongs.Count], music.Track);
    }

    [Fact]
    public void A_context_with_no_published_song_is_silent_and_said_once()
    {
        EngineContextFake engine = Engine();
        DaggerfallMusic music = new(engine.Context, Score("song_dungeon"), false, Mages, FightersTrainer);
        music.Advance(DaggerfallMusicContext.Dungeon, 0);

        music.Advance(DaggerfallMusicContext.Sunny, 0);
        music.Advance(DaggerfallMusicContext.Sunny, 0);

        Assert.Null(music.Track);
        Assert.Equal(1, engine.ReleasedAudioVoices);
        Assert.Single(Cues(engine), cue => cue.StartsWith("daggerfall.music/cue.unavailable:", StringComparison.Ordinal));
        // The tracks the container has no recording of are named once, as the owner is built.
        Assert.Single(engine.PublishedDiagnostics, value => value.StartsWith("daggerfall.music/tracks.missing:", StringComparison.Ordinal));
    }

    private static EngineContextFake Engine()
    {
        List<string> releases = [];
        return EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(TestSessions.Hash, releases).Service, new AppearanceFake(releases));
    }

    private static DaggerfallInteriorBuilding Building(int type, int faction) => new(0, 0, new("TEST.RMB", 0), type, faction);

    private static DaggerfallMusicBundle Score(params string[] tracks)
    {
        BundleContentFake service = new();
        service.Container(DaggerfallMusicBundle.ContainerName);
        foreach (string track in tracks) service.Add(DaggerfallMusicBundle.ContainerName, $"{track}.ogg", [7]);
        return DaggerfallMusicBundle.Read(new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallMusicBundle.ContainerName));
    }

    private static string[] Cues(EngineContextFake engine) =>
        [.. engine.PublishedDiagnostics.Where(value => value.StartsWith("daggerfall.music/cue.", StringComparison.Ordinal))];
}
