using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using WorldRpg.Rulesets.Daggerfall.World;
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
    public void A_dungeon_castle_zone_plays_the_castle_song_and_leaving_it_returns_to_the_dungeon()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile original = ReadInputs(root);
        System.Numerics.Vector3 start = original.Project.PlayerPosition!.Value.ToVector();
        DaggerfallSiteProfile inputs = DaggerfallWeatherSessionTests.WithZones(original,
            [new DaggerfallAmbientZone("fixture-castle", DaggerfallAmbientZoneKind.Castle, start - System.Numerics.Vector3.One * 4, start + System.Numerics.Vector3.One * 4)]);
        var fixture = MusicEngine(inputs);
        using var session = DaggerfallSession.StartNew(fixture.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults) {
            Music = DaggerfallMusicBundle.Admit(FullContent(root), inputs.Music) });

        session.Update(Update());
        // Donor SongManager: IsPlayerInsideDungeonCastle selects CastleSongs over the dungeon list.
        Assert.Equal(DaggerfallMusicContext.Castle, session.MusicContext());
        Assert.Equal("song_gpalac", session.MusicTrack);
        session.State.PlayerControl.MoveTo(start + System.Numerics.Vector3.UnitX * 30);
        session.Update(Update());
        Assert.Equal(DaggerfallMusicContext.Dungeon, session.MusicContext());
        Assert.Equal("song_dungeon", session.MusicTrack);
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
        Assert.Equal("song_dungeon6", session.MusicTrack);
        Assert.Equal(2, engine.StartedAudioVoices.Count);
        Assert.Equal(
            ["daggerfall.music/cue.started: Music cue 'song_dungeon' started for context 'Dungeon'.",
             "daggerfall.music/cue.retired: Music cue 'song_dungeon' retired.",
             "daggerfall.music/cue.started: Music cue 'song_dungeon6' started for context 'Dungeon'."],
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

    [Theory]
    [InlineData("tavern", DaggerfallMusicContext.Tavern, "song_square_2")]
    [InlineData("daggerfall.charing-interior-1-1-0.json", DaggerfallMusicContext.Interior, "song_23")]
    [InlineData("daggerfall.charing-interior-3-1-13.json", DaggerfallMusicContext.TempleNeutral, "song_gneut")]
    [InlineData("daggerfall.charing-interior-3-4-0.json", DaggerfallMusicContext.MagesGuild, "song_magic_2")]
    [InlineData("daggerfall.charing-interior-2-1-0.json", DaggerfallMusicContext.Shop, "song_gshop")]
    public void Entering_a_source_building_selects_its_music(string descriptor, DaggerfallMusicContext context, string track)
    {
        string root = TestData.RepositoryRoot;
        var definitions = TestPayload.Definitions;
        var content = FullContent(root);
        var exterior = ReadProfile(root, content, definitions, "daggerfall.charing-exterior.json");
        var interior = descriptor == "tavern" ? AssembleTavern(content, definitions)
            : ReadProfile(root, content, definitions, descriptor);
        var fixture = MusicEngine(exterior, interior);
        using var session = DaggerfallSession.StartNew(fixture.Context,
            new(definitions, exterior, DaggerfallTuning.Defaults) {
                Music = DaggerfallMusicBundle.Admit(content, exterior.Music), Profiles = new([exterior, interior]),
                Blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.blocks.json"))) });
        session.Update(Update());
        Assert.Equal(DaggerfallMusicContext.Night, session.MusicContext());
        Assert.True(session.TryTransitionTo(interior.ProfileKey));
        session.Update(Update());
        Assert.Equal(context, session.MusicContext());
        Assert.Equal(track, session.MusicTrack);
        Assert.Equal(2, fixture.OpenedContentClips);
        Assert.Equal(1, fixture.ReleasedContentClips);
        Assert.Equal(1, fixture.ReleasedAudioVoices);
        session.Dispose();
        Assert.Equal(fixture.OpenedContentClips, fixture.ReleasedContentClips);
    }

    [Theory]
    [InlineData(false, "song_overlong", "song_10")]
    [InlineData(true, "song_fmover_c", "song_11fm")]
    public void Outdoor_rain_answers_daylight_and_night_overrides_weather(bool alternate, string rain, string night)
    {
        string root = TestData.RepositoryRoot;
        var content = FullContent(root);
        var inputs = ReadProfile(root, content, TestPayload.Definitions, "daggerfall.charing-exterior.json");
        var fixture = MusicEngine(inputs);
        var tuning = MusicTuning(root, alternate) with { Weather = DaggerfallWeatherTuning.Classic with {
            Odds = Enumerable.Range(0,24).Select(_ => new[]{0,0,0,0,100,0,0}).ToArray() } };
        using var session = DaggerfallSession.StartNew(fixture.Context,
            new(TestPayload.Definitions, inputs, tuning) { Music = DaggerfallMusicBundle.Admit(content, inputs.Music),
                Sky = DaggerfallSkyMedia.Read(DaggerfallSkyMediaTests.Fixture().Content) });
        session.Update(Update());
        Assert.Equal(night, session.MusicTrack);
        session.AdvanceElapsedTime(12 * 60 * 60);
        session.Update(Update());
        Assert.Equal(DaggerfallMusicContext.Rain, session.MusicContext());
        Assert.Equal(rain, session.MusicTrack);
        Assert.Equal(1, fixture.ReleasedContentClips);
        session.AdvanceElapsedTime(12 * 60 * 60);
        session.Update(Update());
        Assert.Equal(night, session.MusicTrack);
        Assert.Equal(2, fixture.ReleasedContentClips);
    }

    [Fact]
    public void Every_environment_playlist_has_published_music_in_both_variants()
    {
        var content = FullContent(TestData.RepositoryRoot);
        var bundle = DaggerfallMusicBundle.Admit(content, ReadInputs(TestData.RepositoryRoot).Music)!;
        foreach (var context in Enum.GetValues<DaggerfallMusicContext>().Except([DaggerfallMusicContext.Combat, DaggerfallMusicContext.Menu]))
            foreach (bool alternate in new[]{false,true})
                Assert.Contains(DaggerfallMusicDirector.PlaylistFor(context, alternate), bundle.CanPlay);
    }

    [Fact]
    public void An_admitted_combat_hit_keeps_the_environmental_song()
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        var bundle = DaggerfallMusicBundle.Admit(FullContent(TestData.RepositoryRoot), inputs.Music);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        var perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, 1d, PerceptionPairKind.Visible, 1d));
        var appearance = new AppearanceFake(releases);
        var engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using var session = DaggerfallSession.StartNew(engine.Context,
            new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults) { Music = bundle });
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d),
            [Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "attack")]));
        appearance.AdvanceReceiptForAll = WeaponHitReading(2, 2);
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 1, 2, 60, 1, 0, 1d / 60d), []));
        Assert.Equal(1, session.State.Progression.SkillUses["long-blade"]);
        Assert.Equal("song_dungeon", session.MusicTrack);
        Assert.Equal(1, engine.OpenedContentClips);
        Assert.Single(engine.PublishedDiagnostics, value => value.Contains("cue.started"));
        Assert.Equal(0, engine.ReleasedContentClips);
    }

    private static DaggerfallSiteProfile AssembleTavern(ProductContent content, DaggerfallDefinitions definitions)
    {
        var media = DaggerfallWorldMedia.Read(content);
        var product = DaggerfallSiteContent.ReadProductMedia(content, media, definitions);
        var meshes = DaggerfallLocationAssembly.ReadMeshIndex(content, media);
        var assembly = new DaggerfallLocationAssembly(definitions, new DaggerfallWorldBlocks(content), () => product, () => meshes);
        var city = definitions.Locations.Records.Single(site => site.Id == new DaggerfallSiteId(17, 4));
        var tavern = city.Exterior!.Buildings.First(pair => pair.Value.Source.BuildingType == 15).Key;
        return assembly.Assemble(DaggerfallWorldProfileIds.Interior(city.Id, tavern));
    }

    private static EngineContextFake MusicEngine(params DaggerfallSiteProfile[] profiles)
    {
        List<string> releases = [];
        ContentFake content = new(releases);
        foreach (var profile in profiles) { PopulateContent(content, profile); PopulateTerrainContent(content, profile); }
        return EngineContextFake.Create(content, SpatialFake.Create(profiles[0].SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
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
