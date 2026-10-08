using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The ordinary session's music: which published cue plays where the player stands, that an ordinary
/// update does not restart it, and that a site change and a session teardown each end the loop once.
/// </summary>
public sealed class DaggerfallMusicSessionTests
{
    [Fact]
    public void The_ordinary_session_plays_the_published_dungeon_cue_and_keeps_one_loop()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile inputs = ReadInputs(root);
        // The site's own classic sidecar names the cues it admits; the bytes live in the product-wide
        // music bundle. A site that named none would compose with no score, which the last fact covers.
        Assert.NotEmpty(inputs.Music);
        DaggerfallMusicBundle bundle = DaggerfallMusicBundle.Admit(content, inputs.Music)
            ?? throw new InvalidOperationException("The regenerated Privateer's Hold sidecar carries no admitted music cue.");
        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, inputs);
        EngineContextFake engine = EngineContextFake.Create(contentService, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity) { Music = bundle });

        session.Update(Update());
        // The first listed dungeon song is the published one, and it plays through the real resolver.
        Assert.Equal("song_dungeon", session.MusicTrack);
        Assert.Single(engine.StartedAudioVoices);
        // The voice is a clip the resolver opened from content, which is the path a substituted or
        // pre-baked clip could not take.
        Assert.Equal(1, engine.OpenedContentClips);
        // A cue change is the one completed change a score has to report: nothing on screen names the track.
        Assert.Equal(["daggerfall.music/cue.started: Music cue 'song_dungeon' started for context 'Dungeon'."], engine.PublishedDiagnostics);
        // The same world in the next admitted update keeps that one loop: no second voice, no release.
        session.Update(Update());
        Assert.Equal("song_dungeon", session.MusicTrack);
        Assert.Single(engine.StartedAudioVoices);
        Assert.Equal(0, engine.ReleasedAudioVoices);
        Assert.Single(engine.PublishedDiagnostics);

        session.Dispose();
        // Disposal retires the loop exactly once, and the session holds no cue afterwards.
        Assert.Equal(1, engine.ReleasedAudioVoices);
        Assert.Equal(
            ["daggerfall.music/cue.started: Music cue 'song_dungeon' started for context 'Dungeon'.",
             "daggerfall.music/cue.retired: Music cue 'song_dungeon' retired."],
            engine.PublishedDiagnostics);
    }

    [Fact]
    public void A_site_change_ends_the_previous_worlds_loop_before_the_new_one_starts()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallSiteProfile castle = ReadProfile(root, content, definitions, "daggerfall.castle-necromoghan.json");
        DaggerfallMusicBundle bundle = DaggerfallMusicBundle.Admit(content, inputs.Music)
            ?? throw new InvalidOperationException("The regenerated Privateer's Hold sidecar carries no admitted music cue.");
        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, inputs);
        // A transition resolves the destination's own spatial content, so the fake carries both sites.
        PopulateContent(contentService, castle);
        EngineContextFake engine = EngineContextFake.Create(contentService, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity) { Music = bundle });
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([inputs, castle]));

        session.Update(Update());
        Assert.Equal("song_dungeon", session.MusicTrack);
        Assert.Single(engine.StartedAudioVoices);

        Assert.True(session.TryTransitionTo(castle.ProfileKey));
        // The transition itself ends the loop: the world the song belonged to is gone.
        Assert.Null(session.MusicTrack);
        Assert.Equal(1, engine.ReleasedAudioVoices);
        Assert.Equal(
            ["daggerfall.music/cue.started: Music cue 'song_dungeon' started for context 'Dungeon'.",
             "daggerfall.music/cue.retired: Music cue 'song_dungeon' retired."],
            engine.PublishedDiagnostics);

        // The destination's own cue starts on the update that settles the new world, as one voice.
        session.Update(Update());
        Assert.Equal("song_dungeon", session.MusicTrack);
        Assert.Equal(2, engine.StartedAudioVoices.Count);
        Assert.Equal(
            ["daggerfall.music/cue.started: Music cue 'song_dungeon' started for context 'Dungeon'.",
             "daggerfall.music/cue.retired: Music cue 'song_dungeon' retired.",
             "daggerfall.music/cue.started: Music cue 'song_dungeon' started for context 'Dungeon'."],
            engine.PublishedDiagnostics);
        session.Dispose();
        Assert.Equal(2, engine.ReleasedAudioVoices);
    }

    [Fact]
    public void A_session_whose_site_publishes_no_music_composes_and_plays_nothing()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile inputs = ReadInputs(root);
        // A publication without music is a supported state, not a failure: the join answers nothing and
        // the ordinary session runs with no score rather than a substituted track.
        Assert.Null(DaggerfallMusicBundle.Admit(content, []));
        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, inputs);
        EngineContextFake engine = EngineContextFake.Create(contentService, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity));

        session.Update(Update());
        Assert.Null(session.MusicTrack);
        Assert.Empty(engine.StartedAudioVoices);
        // No cue, no change to report: a silent score is silent in the diagnostics too.
        Assert.Empty(engine.PublishedDiagnostics);
    }

    [Fact]
    public void A_context_with_no_published_cue_ends_the_playing_loop_and_reports_it()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile outside = ReadProfile(root, content, definitions, "daggerfall.charing-interior-1-1-0.json");
        DaggerfallMusicBundle bundle = DaggerfallMusicBundle.Admit(content, outside.Music)
            ?? throw new InvalidOperationException("The regenerated Charing sidecar carries no admitted music cue.");
        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, outside);
        EngineContextFake engine = EngineContextFake.Create(contentService, SpatialFake.Create(outside.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, outside, DaggerfallTuning.Defaults, identity) { Music = bundle });

        // A game starts at midnight, and the catalogue publishes no night cue, so the first update is
        // silent and reports why no cue started, once for this context.
        session.Update(Update());
        Assert.Null(session.MusicTrack);
        Assert.Empty(engine.StartedAudioVoices);
        Assert.Contains(engine.PublishedDiagnostics, value => value.Contains("cue.unavailable", StringComparison.Ordinal));
        session.Update(Update());
        Assert.Single(engine.PublishedDiagnostics);

        // Daylight above ground plays the sunny list's first published song.
        session.AdvanceElapsedTime(12 * 60 * 60);
        session.Update(Update());
        Assert.Equal("song_gday___d", session.MusicTrack);
        Assert.Single(engine.StartedAudioVoices);

        // Night ends the loop rather than keeping the day's song or falling back to a track the donor
        // played somewhere else.
        session.AdvanceElapsedTime(12 * 60 * 60);
        session.Update(Update());
        Assert.Null(session.MusicTrack);
        Assert.Equal(1, engine.ReleasedAudioVoices);
        Assert.Contains("daggerfall.music/cue.retired: Music cue 'song_gday___d' retired.", engine.PublishedDiagnostics);

        // The next day starts a cue again, as one voice, without a second release.
        session.AdvanceElapsedTime(12 * 60 * 60);
        session.Update(Update());
        Assert.Equal("song_gday___d", session.MusicTrack);
        Assert.Equal(2, engine.StartedAudioVoices.Count);
        Assert.Equal(1, engine.ReleasedAudioVoices);
    }

    [Theory]
    [InlineData(false, "song_gday___d", "song_gsunny2")]
    [InlineData(true, "song_fday___d", "song_02fm")]
    public void The_loaded_playlist_choice_reaches_both_published_sunny_cues(bool alternate, string first, string second)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile outside = ReadProfile(root, content, definitions, "daggerfall.charing-interior-1-1-0.json");
        // The destination is the source-published Charing exterior. The interior publication has
        // no terrain media, while the exterior publication carries the normalized climate terrain
        // set (including the 102/1 tile selected by the admitted climate cell).
        DaggerfallSiteProfile destination = ReadProfile(root, content, definitions, "daggerfall.charing-exterior.json");
        DaggerfallMusicBundle bundle = DaggerfallMusicBundle.Admit(content, outside.Music)!;
        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, outside);
        PopulateContent(contentService, destination);
        PopulateTerrainContent(contentService, outside);
        PopulateTerrainContent(contentService, destination);
        EngineContextFake engine = EngineContextFake.Create(contentService,
            SpatialFake.Create(outside.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new(definitions, outside, MusicTuning(root, alternate)) { Music = bundle });
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([outside, destination]));
        session.AdvanceElapsedTime(12 * 60 * 60);
        session.Update(Update());
        Assert.Equal(first, session.MusicTrack);
        Assert.Equal(1, engine.OpenedContentClips);
        Assert.True(session.TryTransitionTo(destination.ProfileKey));
        session.Update(Update());
        Assert.Equal(second, session.MusicTrack);
        Assert.Equal(2, engine.OpenedContentClips);
        Assert.Equal(2, engine.StartedAudioVoices.Count);
        Assert.Equal(1, engine.ReleasedAudioVoices);
    }

    [Fact]
    public void An_alternate_context_without_published_cues_stays_silent_and_reports_the_reason_once()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile dungeon = ReadInputs(root);
        DaggerfallMusicBundle bundle = DaggerfallMusicBundle.Admit(content, dungeon.Music)!;
        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, dungeon);
        EngineContextFake engine = EngineContextFake.Create(contentService,
            SpatialFake.Create(dungeon.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new(definitions, dungeon, MusicTuning(root, true)) { Music = bundle });
        session.Update(Update());
        session.Update(Update());
        Assert.Null(session.MusicTrack);
        Assert.Empty(engine.StartedAudioVoices);
        Assert.Equal(0, engine.OpenedContentClips);
        Assert.Equal(["daggerfall.music/cue.unavailable: No published music cue for context 'Dungeon' in the alternate playlist."],
            engine.PublishedDiagnostics);
    }

    private static DaggerfallTuning MusicTuning(string root, bool alternate)
    {
        System.Text.Json.Nodes.JsonNode payload = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(
            Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json")))!;
        payload["music"]!["alternatePlaylists"] = alternate;
        return DaggerfallTuning.Read(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()));
    }

    private static void PopulateTerrainContent(ContentFake content, DaggerfallSiteProfile profile)
    {
        foreach (NormalizedTerrainTexture texture in profile.TerrainTextures.Values)
            content.Add(texture.TexturePath, texture.TextureSha256);
    }

    /// <summary>
    /// An ordinary admitted update: one fixed step with no input, which is what a standing player sees.
    /// </summary>
    private static ProductUpdate Update() => new(
        new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 1, 1, 60, 3, 0, 1d / 60d),
        []);
}
