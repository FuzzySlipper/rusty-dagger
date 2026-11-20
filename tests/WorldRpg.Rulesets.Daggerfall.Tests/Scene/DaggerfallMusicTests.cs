using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The music owner alone, with no session: the donor context a place answers, the song it plays for a context, the
/// rotation a site change moves on, a dungeon's own song, the tavern's song by the day, a silent context reported once, and the clips it
/// releases.
/// </summary>
public sealed class DaggerfallMusicTests
{
    private const int Mages = 40;
    private const int FightersTrainer = 849;

    [Fact]
    public void The_donor_context_follows_the_place_the_building_and_the_weather()
    {
        DaggerfallMusic music = new(Engine().Context, null, Mages, FightersTrainer);

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
    public void The_context_asks_each_reading_only_where_it_decides()
    {
        DaggerfallMusic music = new(Engine().Context, null, Mages, FightersTrainer);
        Place dungeon = new(DaggerfallWorldProfileKind.Dungeon) { InsideCastle = true };
        Assert.Equal(DaggerfallMusicContext.Castle, music.Context(dungeon));
        Assert.Equal(["kind", "castle"], dungeon.Asked);

        Place tavern = new(DaggerfallWorldProfileKind.Interior) { Building = Building(15, 0) };
        Assert.Equal(DaggerfallMusicContext.Tavern, music.Context(tavern));
        Assert.Equal(["kind", "building"], tavern.Asked);

        Place graveyard = new(DaggerfallWorldProfileKind.Exterior) { Site = DaggerfallSiteKind.Graveyard, IsDay = true };
        Assert.Equal(DaggerfallMusicContext.Night, music.Context(graveyard));
        Assert.Equal(["kind", "site"], graveyard.Asked);

        Place night = new(DaggerfallWorldProfileKind.Exterior) { IsDay = false, Weather = DaggerfallWeatherKind.Rain };
        Assert.Equal(DaggerfallMusicContext.Night, music.Context(night));
        Assert.Equal(["kind", "site", "day"], night.Asked);

        Place rain = new(DaggerfallWorldProfileKind.Exterior) { IsDay = true, Weather = DaggerfallWeatherKind.Rain };
        Assert.Equal(DaggerfallMusicContext.Rain, music.Context(rain));
        Assert.Equal(["kind", "site", "day", "weather"], rain.Asked);
    }

    private sealed class Place(DaggerfallWorldProfileKind kind) : IDaggerfallMusicPlace
    {
        internal List<string> Asked { get; } = [];
        internal bool InsideCastle { get; init; }
        internal DaggerfallInteriorBuilding? Building { get; init; }
        internal DaggerfallSiteKind? Site { get; init; }
        internal bool IsDay { get; init; }
        internal DaggerfallWeatherKind Weather { get; init; }
        DaggerfallWorldProfileKind IDaggerfallMusicPlace.Kind { get { Asked.Add("kind"); return kind; } }
        bool IDaggerfallMusicPlace.InsideCastle { get { Asked.Add("castle"); return InsideCastle; } }
        DaggerfallInteriorBuilding? IDaggerfallMusicPlace.Building { get { Asked.Add("building"); return Building; } }
        DaggerfallSiteKind? IDaggerfallMusicPlace.Site { get { Asked.Add("site"); return Site; } }
        bool IDaggerfallMusicPlace.IsDay { get { Asked.Add("day"); return IsDay; } }
        DaggerfallWeatherKind IDaggerfallMusicPlace.Weather { get { Asked.Add("weather"); return Weather; } }
    }

    [Fact]
    public void Without_a_score_nothing_plays_and_nothing_is_said()
    {
        EngineContextFake engine = Engine();
        DaggerfallMusic music = new(engine.Context, null, Mages, FightersTrainer);

        Assert.False(music.Scored);
        music.Advance(DaggerfallMusicContext.Dungeon, 0, Site);
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
        DaggerfallMusic music = new(engine.Context, Score("outside-day-sunny-01", "outside-day-sunny-02"), Mages, FightersTrainer);

        music.Advance(DaggerfallMusicContext.Sunny, 0, Site);
        music.Advance(DaggerfallMusicContext.Sunny, 0, Site);

        Assert.True(music.Scored);
        Assert.Equal("outside-day-sunny-01", music.Track);
        Assert.Single(engine.StartedAudioVoices);
        Assert.Equal(1, engine.OpenedMusicClips);

        music.ChangeSite();
        // The retirement releases the voice and the clip it played.
        Assert.Null(music.Track);
        Assert.Equal(1, engine.ReleasedAudioVoices);
        music.Advance(DaggerfallMusicContext.Sunny, 0, Site);
        Assert.Equal("outside-day-sunny-02", music.Track);

        Exception? failure = null;
        music.Dispose(ref failure);
        Assert.Null(failure);
        Assert.Equal(2, engine.ReleasedAudioVoices);
        Assert.Equal(
            ["daggerfall.music/cue.started: Music cue 'outside-day-sunny-01' started for context 'Sunny'.",
             "daggerfall.music/cue.retired: Music cue 'outside-day-sunny-01' retired.",
             "daggerfall.music/cue.started: Music cue 'outside-day-sunny-02' started for context 'Sunny'.",
             "daggerfall.music/cue.retired: Music cue 'outside-day-sunny-02' retired."],
            Cues(engine));
    }

    [Fact]
    public void A_dungeon_plays_its_own_song_on_every_visit_whatever_was_visited_before()
    {
        DaggerfallSiteId first = new(17, 216), second = new(17, 214);
        IReadOnlyList<string> dungeon = DaggerfallMusicDirector.DungeonSongs;
        string own = dungeon[DaggerfallMusic.SiteSong(first, dungeon.Count)];
        Assert.NotEqual(own, dungeon[DaggerfallMusic.SiteSong(second, dungeon.Count)]);
        DaggerfallMusic music = new(Engine().Context, Score([.. dungeon]), Mages, FightersTrainer);

        music.Advance(DaggerfallMusicContext.Dungeon, 0, first);
        Assert.Equal(own, music.Track);
        music.ChangeSite();
        music.Advance(DaggerfallMusicContext.Dungeon, 0, second);
        music.ChangeSite();
        music.ChangeSite();
        music.Advance(DaggerfallMusicContext.Dungeon, 0, first);
        Assert.Equal(own, music.Track);
        // A fresh owner, as a later session or another save, picks the same song for the same dungeon.
        DaggerfallMusic later = new(Engine().Context, Score([.. dungeon]), Mages, FightersTrainer);
        later.Advance(DaggerfallMusicContext.Dungeon, 0, first);
        Assert.Equal(own, later.Track);
        // The pick is a fixed hash, pinned so a change to it is a deliberate one.
        Assert.Equal(14, DaggerfallMusic.SiteSong(first, dungeon.Count));
    }

    [Fact]
    public void A_tavern_plays_its_song_by_the_day_whatever_the_rotation()
    {
        DaggerfallMusic music = new(Engine().Context, Score([.. DaggerfallMusicDirector.TavernSongs]), Mages, FightersTrainer);
        music.ChangeSite();

        music.Advance(DaggerfallMusicContext.Tavern, 7, Site);

        Assert.Equal(DaggerfallMusicDirector.TavernSongs[7 % DaggerfallMusicDirector.TavernSongs.Count], music.Track);
    }

    [Fact]
    public void A_context_with_no_published_song_is_silent_and_said_once()
    {
        EngineContextFake engine = Engine();
        DaggerfallMusic music = new(engine.Context, Score("dungeon-01"), Mages, FightersTrainer);
        music.Advance(DaggerfallMusicContext.Dungeon, 0, Site);

        music.Advance(DaggerfallMusicContext.Sunny, 0, Site);
        music.Advance(DaggerfallMusicContext.Sunny, 0, Site);

        Assert.Null(music.Track);
        Assert.Equal(1, engine.ReleasedAudioVoices);
        Assert.Single(Cues(engine), cue => cue.StartsWith("daggerfall.music/cue.unavailable:", StringComparison.Ordinal));
        // The tracks the container has no recording of are named once, as the owner is built.
        Assert.Single(engine.PublishedDiagnostics, value => value.StartsWith("daggerfall.music/tracks.missing:", StringComparison.Ordinal));
    }

    private static readonly DaggerfallSiteId Site = new(0, 0);

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
