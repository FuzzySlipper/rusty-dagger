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

    private static DaggerfallSiteProfile WithZones(DaggerfallSiteProfile source, IReadOnlyList<DaggerfallAmbientZone> zones) =>
        new(source.Project, source.SpatialArtifact, source.StaticMesh, source.WorldAppearance, source.InitialLook,
            source.Materials, source.ActorSprites, source.MobileSprites, source.Audio, source.ClassicPresentation,
            source.Site, source.Doors, source.ProfileKind, source.ProfileKey.LogicalId, source.Portals,
            source.Anchors.Values.ToArray(), source.Lights, source.GroundContainerSprite, source.DungeonMap,
            source.DungeonActions, source.DungeonActionModels, source.InteriorBuilding, source.Music,
            source.QuestMarkers, source.BillboardSprites) {AmbientZones = zones};

    [Fact]
    public void Real_exterior_shelter_indoor_return_pause_and_restore_keep_weather_and_retire_resources()
    {
        string root = TestData.RepositoryRoot;
        var definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        var exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        var interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0.json");
        var mediaFixture = DaggerfallSkyMediaTests.Fixture();
        var media = DaggerfallSkyMedia.Read(mediaFixture.Content);
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
            Assert.Single(fixture.Engine.Particles.Created);
            Assert.Single(fixture.Engine.Particles.Updated);
            Assert.True(fixture.Engine.SkyBackgrounds.Count + fixture.Engine.SkyBlends.Count > 0);
            Assert.Equal(tuning.Ambient.RainFogDensity, fixture.Engine.Fogs.Last().Density);
            fixture.Spatial.FloorHit = request => request.Direction.Y > .9f
                ? default(SpatialHit) with {Present=true, Kind=SpatialHitKind.StaticMesh, Point=request.Origin+Vector3.UnitY, Normal=-Vector3.UnitY, Distance=1}
                : default;
            session.Update(Update(3));
            Assert.Equal(1, fixture.Engine.Particles.Released);
            Assert.True(session.TryTransitionTo(interior.ProfileKey));
            Assert.Equal(tuning.Ambient.InteriorFogDensity, fixture.Engine.Fogs.Last().Density);
            Assert.True(session.IsRaining);
            fixture.Spatial.FloorHit = _ => default;
            Assert.True(session.TryTransitionTo(exterior.ProfileKey));
            session.Update(Update(4));
            Assert.Equal(2, fixture.Engine.Particles.Created.Count);
            session.ApplyProductMode(ProductMode.Paused);
            session.Update(Update(5));
            Assert.Equal(2, fixture.Engine.Particles.Released);
            session.ApplyProductMode(ProductMode.Playing);
            var before = DaggerfallSavePayload.Read(session.CaptureSave()).Weather;
            session.AdvanceElapsedTime(DaggerfallCalendar.SecondsPerDay*10L);
            var after = DaggerfallSavePayload.Read(session.CaptureSave()).Weather;
            Assert.Equal(before.Climates,after.Climates); Assert.True(after.NextDay > before.NextDay);
            save = session.CaptureSave();
        }
        Assert.Equal(fixture.Engine.Particles.Created.Count,fixture.Engine.Particles.Released);
        var resumed = Engine(exterior,interior);
        using(var restored = DaggerfallSession.Restore(resumed.Engine.Context,composition,save))
        {
            restored.PublishInitial();
            Assert.True(restored.IsRaining);
            Assert.Equal(DaggerfallSavePayload.Read(save).Weather.Climates,DaggerfallSavePayload.Read(restored.CaptureSave()).Weather.Climates);
            Assert.Equal(DaggerfallSavePayload.Read(save).Weather.SkyVariants,DaggerfallSavePayload.Read(restored.CaptureSave()).Weather.SkyVariants);
            restored.ApplyProductMode(ProductMode.Playing); restored.Update(Update(6));
            Assert.Single(resumed.Engine.Particles.Created);
        }
        Assert.Equal(resumed.Engine.Particles.Created.Count,resumed.Engine.Particles.Released);
    }

    private static (EngineContextFake Engine, SpatialFake Spatial, AppearanceFake Appearance) Engine(params DaggerfallSiteProfile[] sites)
    {
        List<string> releases=[]; var content = new ContentFake(releases);
        foreach(var site in sites) PopulateContent(content,site);
        var spatial = SpatialFake.Create(sites[0].SpatialArtifact.Sha256,releases);
        var appearance = new AppearanceFake(releases);
        return (EngineContextFake.Create(content,spatial.Service,appearance),spatial,appearance);
    }
    private static ProductUpdate Update(ulong step) => new(new(ProductLifecycleState.Running,
        step,1,step,step,60,1,0,1d/60d),[]);
}
