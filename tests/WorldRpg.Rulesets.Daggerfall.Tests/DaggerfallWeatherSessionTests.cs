using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallWeatherSessionTests
{
    [Fact]
    public void Admitted_dungeon_zones_update_the_existing_light_across_exit_rebase_and_restore()
    {
        var original = ReadInputs(TestData.RepositoryRoot);
        Vector3 start = original.Project.PlayerPosition!.Value.ToVector();
        var zones = new[] {
            new DaggerfallAmbientZone("fixture-castle", DaggerfallAmbientZoneKind.Castle,
                start - Vector3.One * 2, start + Vector3.One * 2),
            new DaggerfallAmbientZone("fixture-special", DaggerfallAmbientZoneKind.SpecialArea,
                start + new Vector3(8,-2,-2), start + new Vector3(12,2,2)),
        };
        var inputs = WithZones(original, zones);
        var definitions = TestPayload.Definitions;
        var tuning = DaggerfallTuning.Defaults;
        var composition = new DaggerfallSessionComposition(definitions, inputs, tuning);
        var fixture = Engine(inputs);
        RulesetSavePayload saved;
        using (var session = DaggerfallSession.StartNew(fixture.Engine.Context, composition))
        {
            session.PublishInitial();
            var lighting = session.Sites.Projection.Lighting;
            Assert.Equal(tuning.Ambient.ZoneAmbient, LastAmbient(fixture.Appearance));
            int lights = fixture.Appearance.LightRequests.Count;
            session.State.PlayerControl.MoveTo(start + Vector3.UnitX * 20);
            session.PublishInitial();
            Assert.Equal(tuning.SiteLighting.Dungeon, LastAmbient(fixture.Appearance));
            session.State.PlayerControl.MoveTo(start + Vector3.UnitX * 10);
            session.PublishInitial();
            Assert.Equal(tuning.Ambient.ZoneAmbient, LastAmbient(fixture.Appearance));
            Assert.Same(lighting, session.Sites.Projection.Lighting);
            Assert.Equal(lights, fixture.Appearance.LightRequests.Count);
            saved = session.CaptureSave();
            Vector3 offset = new(128, 0, -128);
            session.Sites.Projection.Rebase(offset);
            session.State.PlayerControl.MoveTo(start + Vector3.UnitX * 10 + offset);
            session.PublishInitial();
            Assert.Equal(tuning.Ambient.ZoneAmbient, LastAmbient(fixture.Appearance));
            Assert.Equal(lights, fixture.Appearance.LightRequests.Count);
        }
        Assert.Equal(fixture.Appearance.LightRequests.Count, fixture.Appearance.DisposedLights);
        var resumed = Engine(inputs);
        using (var restored = DaggerfallSession.Restore(resumed.Engine.Context, composition, saved))
        {
            restored.PublishInitial();
            Assert.Equal(tuning.Ambient.ZoneAmbient, LastAmbient(resumed.Appearance));
            Assert.Single(resumed.Appearance.LightRequests, request => request.Descriptor.Kind == LightKind.Ambient);
        }
        Assert.Equal(resumed.Appearance.LightRequests.Count, resumed.Appearance.DisposedLights);
    }

    private static float LastAmbient(AppearanceFake appearance) => appearance.LightUpdates
        .Last(update => update.Replacement.Descriptor.Kind == LightKind.Ambient).Replacement.Descriptor.Intensity;

    internal static DaggerfallSiteProfile WithZones(DaggerfallSiteProfile source, IReadOnlyList<DaggerfallAmbientZone> zones) =>
        new(source.Project, source.Geometry, source.WorldAppearance, source.InitialLook,
            source.Materials, source.ActorSprites, source.MobileSprites, source.Audio, source.ClassicPresentation,
            source.Site, source.Doors, source.ProfileKind, source.ProfileKey.LogicalId, source.Portals,
            source.Anchors.Values.ToArray(), source.Lights, source.GroundContainerSprites, source.DungeonMap,
            source.DungeonActions, source.DungeonActionModels, source.InteriorBuilding,
            source.QuestMarkers, source.BillboardSprites) {AmbientZones = zones};

    [Fact]
    public void The_horizon_stands_outdoors_comes_down_indoors_and_gives_way_to_the_classic_sky_under_classic_art()
    {
        string root = TestData.RepositoryRoot;
        var definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        var exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        var interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0.json");
        var media = DaggerfallSkyMedia.Read(TestSessions.Data(DaggerfallSkyMediaTests.Fixture().Content));
        var fairOdds = Enumerable.Range(0,24).Select(_ => new[]{100,0,0,0,0,0,0}).ToArray();
        var tuning = DaggerfallTuning.Defaults with {Weather = DaggerfallWeatherTuning.Classic with {Odds = fairOdds}};
        var composition = new DaggerfallSessionComposition(definitions, exterior, tuning)
            {Profiles = new([exterior,interior]),Sky = media};
        var fixture = Engine(exterior, interior);
        using var session = DaggerfallSession.StartNew(fixture.Engine.Context, composition);
        session.ApplyProductMode(ProductMode.Playing);
        session.PublishInitial();
        session.Update(Update(1));

        Assert.True(fixture.Engine.BackdropLinked);
        Assert.True(fixture.Engine.HeightfieldVisible);
        Assert.Equal(FogMode.Exponential, fixture.Engine.Fogs.Last().Mode);
        Assert.Equal(tuning.Horizon.FogDensity, fixture.Engine.Fogs.Last().Density);
        Assert.Equal(tuning.Horizon.CloudCoverage, fixture.Engine.Clouds.Last().Coverage);
        Assert.Equal(tuning.Horizon.CloudPixelMetres, fixture.Engine.Clouds.Last().PixelMetres);
        Assert.All(fixture.Engine.Moons.TakeLast(2), moon => Assert.True(moon.Radiance > 0));

        Assert.True(session.TryTransitionTo(interior.ProfileKey));
        session.Update(Update(2));
        Assert.False(fixture.Engine.BackdropLinked);
        Assert.All(fixture.Engine.Moons.TakeLast(2), moon => Assert.Equal(0, moon.Radiance));
        Assert.Equal(tuning.Ambient.InteriorFogDensity, fixture.Engine.Fogs.Last().Density);
        Assert.Equal(0, fixture.Engine.Clouds.Last().Coverage);

        Assert.True(session.TryTransitionTo(exterior.ProfileKey));
        session.Update(Update(3));
        Assert.True(fixture.Engine.BackdropLinked);

        session.Update(new ProductUpdate(new(ProductLifecycleState.Running, 4,1,4,4,60,1,0,1d/60d), [ClassicArt()]));
        session.Update(Update(5));
        Assert.False(fixture.Engine.BackdropLinked);
        Assert.Equal(FogMode.Linear, fixture.Engine.Fogs.Last().Mode);
        Assert.Equal(tuning.Ambient.ClearFogEnd, fixture.Engine.Fogs.Last().End);
        Assert.Equal(0, fixture.Engine.Clouds.Last().Coverage);
    }

    [Fact]
    public void Thick_weather_closes_the_sky_in_its_own_air_which_the_world_fades_into_and_classic_art_keeps_its_panorama()
    {
        string root = TestData.RepositoryRoot;
        var definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        var exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        var interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0.json");
        var media = DaggerfallSkyMedia.Read(TestSessions.Data(DaggerfallSkyMediaTests.Fixture().Content));
        var fairOdds = Enumerable.Range(0,24).Select(_ => new[]{100,0,0,0,0,0,0}).ToArray();
        var tuning = DaggerfallTuning.Defaults with {Weather = DaggerfallWeatherTuning.Classic with {Odds = fairOdds}};
        var composition = new DaggerfallSessionComposition(definitions, exterior, tuning) {Profiles = new([exterior,interior]),Sky = media};
        var fixture = Engine(exterior, interior);
        using var session = DaggerfallSession.StartNew(fixture.Engine.Context, composition);
        session.ApplyProductMode(ProductMode.Playing);
        session.PublishInitial();
        session.Update(Update(1));
        int panoramas = fixture.Engine.SkyBackgrounds.Count + fixture.Engine.SkyBlends.Count;
        Assert.True(panoramas > 0);
        int cleared = fixture.Engine.ClearedSkyBackgrounds;

        foreach (string kind in new[] {"Rain", "Thunder", "Snow", "Fog"})
        {
            session.Weather(kind);
            session.Update(Update(2));
            Assert.Equal(fixture.Engine.Fogs.Last().Color, fixture.Engine.BackgroundColors.Last());
            Assert.Equal(panoramas, fixture.Engine.SkyBackgrounds.Count + fixture.Engine.SkyBlends.Count);
        }
        Assert.Equal(cleared + 1, fixture.Engine.ClearedSkyBackgrounds);

        // The first update out of closed weather already leaves the panorama up, under either art.
        session.Weather("Sunny");
        session.Update(Update(3));
        Assert.Equal("panorama", fixture.Engine.Background);

        session.Weather("Rain");
        session.Update(Update(4));
        Assert.Equal("colour", fixture.Engine.Background);
        session.Update(new ProductUpdate(new(ProductLifecycleState.Running, 5,1,5,5,60,1,0,1d/60d), [ClassicArt()]));
        Assert.Equal("panorama", fixture.Engine.Background);
        session.Update(Update(6));
        Assert.Equal("panorama", fixture.Engine.Background);
        session.Update(new ProductUpdate(new(ProductLifecycleState.Running, 7,1,7,7,60,1,0,1d/60d), [ClassicArt(false)]));
        session.Update(Update(8));
        Assert.Equal("colour", fixture.Engine.Background);

        // Indoors the closed sky gives way to the dark of a room.
        Assert.True(session.TryTransitionTo(interior.ProfileKey));
        session.Update(Update(9));
        Assert.Equal("colour", fixture.Engine.Background);
        Assert.Equal(new Color(0,0,0,1), fixture.Engine.BackgroundColors.Last());
    }

    [Fact]
    public void Real_exterior_shelter_indoor_return_pause_and_restore_keep_weather_and_retire_resources()
    {
        string root = TestData.RepositoryRoot;
        var definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        var exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        var interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0.json");
        var mediaFixture = DaggerfallSkyMediaTests.Fixture();
        var media = DaggerfallSkyMedia.Read(TestSessions.Data(mediaFixture.Content));
        var rainOdds = Enumerable.Range(0,24).Select(_ => new[]{0,0,0,0,100,0,0}).ToArray();
        var tuning = DaggerfallTuning.Defaults with {Weather = DaggerfallWeatherTuning.Classic with {Odds = rainOdds}};
        var composition = new DaggerfallSessionComposition(definitions, exterior, tuning)
            {Profiles = new([exterior,interior]),Sky = media};
        var fixture = Engine(exterior, interior);
        RulesetSavePayload save;
        using (var session = DaggerfallSession.StartNew(fixture.Engine.Context, composition))
        {
            session.ApplyProductMode(ProductMode.Playing);
            session.PublishInitial();
            Assert.True(session.IsRaining);
            session.Update(Update(1)); session.Update(Update(2));
            PrecipitationRequest rain = fixture.Engine.Precipitation.Last();
            Assert.Equal(PrecipitationShape.Streak, rain.Shape);
            Assert.True(rain.Drops > 0);
            Assert.True(rain.Velocity.Y < 0);
            session.GroundWeather(1, 1);
            session.Update(Update(3));
            Assert.Equal(tuning.Ambient.WetLook, fixture.Engine.Wetness.Last().Wetness);
            Assert.Equal(tuning.Ambient.SnowLook, fixture.Engine.Snow.Last().Amount);
            Assert.Equal(fixture.Engine.Fogs.Last().Color, fixture.Engine.BackgroundColors.Last());
            Assert.Equal(tuning.Ambient.RainFogDensity, fixture.Engine.Fogs.Last().Density);
            Assert.False(fixture.Engine.HeightfieldVisible);
            fixture.Spatial.FloorHit = request => request.Direction.Y > .9f
                ? default(SpatialHit) with {Present=true, Kind=SpatialHitKind.StaticMesh, Point=request.Origin+Vector3.UnitY, Normal=-Vector3.UnitY, Distance=1}
                : default;
            session.Update(Update(4));
            Assert.True(fixture.Engine.Precipitation.Last().Drops < rain.Drops, "Rain thins out over a shelter.");
            Assert.True(session.TryTransitionTo(interior.ProfileKey));
            Assert.Equal(tuning.Ambient.InteriorFogDensity, fixture.Engine.Fogs.Last().Density);
            Assert.True(session.IsRaining);
            session.Update(Update(5));
            Assert.Equal(0, fixture.Engine.Wetness.Last().Wetness);
            Assert.Equal(0, fixture.Engine.Snow.Last().Amount);
            Assert.Equal(0u, fixture.Engine.Precipitation.Last().Drops);
            fixture.Spatial.FloorHit = _ => default;
            Assert.True(session.TryTransitionTo(exterior.ProfileKey));
            session.Update(Update(6));
            Assert.True(fixture.Engine.Precipitation.Last().Drops > 0);
            session.ApplyProductMode(ProductMode.Playing);
            var before = DaggerfallSavePayload.Read(session.CaptureSave()).Weather;
            session.AdvanceElapsedTime(DaggerfallCalendar.SecondsPerDay*10L);
            var after = DaggerfallSavePayload.Read(session.CaptureSave()).Weather;
            Assert.Equal(before.Climates,after.Climates); Assert.True(after.NextDay > before.NextDay);
            save = session.CaptureSave();
        }
        var resumed = Engine(exterior,interior);
        using(var restored = DaggerfallSession.Restore(resumed.Engine.Context,composition,save))
        {
            restored.PublishInitial();
            Assert.True(restored.IsRaining);
            Assert.Equal(DaggerfallSavePayload.Read(save).Weather.Climates,DaggerfallSavePayload.Read(restored.CaptureSave()).Weather.Climates);
            Assert.Equal(DaggerfallSavePayload.Read(save).Weather.SkyVariants,DaggerfallSavePayload.Read(restored.CaptureSave()).Weather.SkyVariants);
            restored.ApplyProductMode(ProductMode.Playing); restored.Update(Update(7));
            Assert.Equal(PrecipitationShape.Streak, resumed.Engine.Precipitation.Last().Shape);
            Assert.True(resumed.Engine.Precipitation.Last().Drops > 0);
        }
    }

    private static (EngineContextFake Engine, SpatialFake Spatial, AppearanceFake Appearance) Engine(params DaggerfallSiteProfile[] sites)
    {
        List<string> releases=[]; var content = new ContentFake(releases);
        foreach(var site in sites) PopulateContent(content,site);
        var spatial = SpatialFake.Create(sites[0].SpatialArtifact.Sha256,releases);
        var appearance = new AppearanceFake(releases);
        return (EngineContextFake.Create(content,spatial.Service,appearance),spatial,appearance);
    }
    private static ProductInputEvent ClassicArt(bool enabled = true) => Input(InputEventKind.DirectDigital) with
    {
        ValueKind = InputValueKind.ProductPayload, PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
        PayloadData = System.Text.Encoding.UTF8.GetBytes($"{{\"action\":\"controls-classic-art\",\"enabled\":{(enabled ? "true" : "false")}}}"),
    };

    private static ProductUpdate Update(ulong step) => new(new(ProductLifecycleState.Running,
        step,1,step,step,60,1,0,1d/60d),[]);
}
