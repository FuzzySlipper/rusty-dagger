using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Pass 1 interior &amp; dungeon look overrides (<see cref="DaggerfallInteriorLook"/>,
/// <see cref="DaggerfallSiteLighting.ApplyLookOverride"/>, and <see cref="DaggerfallViewLight.FloorOverride"/>).
/// </summary>
public sealed class DaggerfallInteriorLookTests
{
    private static readonly DaggerfallSiteLightingTuning Tuning = DaggerfallTuning.Defaults.SiteLighting;
    private static readonly DaggerfallCalendar Night = DaggerfallCalendar.Start with { Hour = 22 };
    private static readonly DaggerfallSiteLight Torch = new("light-flat/block/0/0/1", new WorldPoint(2, 2.2F, 0), 12, 1.5F, Vector3.One);
    private static readonly DaggerfallSiteLight Hearth = new("light-hearth/block/0/0/2", new WorldPoint(-2, 1.0F, 0), 14, 2.0F, new Vector3(1, .8F, .5F));
    private static readonly DaggerfallSiteLight Crystal = new("light-flat/block/0/0/3", new WorldPoint(0, 2.2F, 4), 10, 1.2F, new Vector3(.2F, .6F, 1F));

    [Fact]
    public void Preset_selection_accepts_known_presets_resets_to_baseline_and_refuses_unknown_names()
    {
        List<string> releases = [];
        EngineContextFake engine = EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(Hash, releases).Service, new AppearanceFake(releases));
        using DaggerfallInteriorLook look = new(engine.Context, null, Tuning);

        Assert.Equal("baseline", look.Preset);
        Assert.Null(look.Select("dungeon-cobalt"));
        Assert.Equal("dungeon-cobalt", look.Preset);
        Assert.Null(look.Select("RESET"));
        Assert.Equal("baseline", look.Preset);
        Assert.Contains("Unknown interior look", look.Select("neon-lasers"));
        Assert.Equal("baseline", look.Preset);
    }

    [Fact]
    public void Applying_a_look_override_remaps_lamp_colors_decay_soft_shadows_and_ambient_and_null_restores_baseline()
    {
        List<string> releases = [];
        AppearanceFake graphics = new(releases);
        EngineContextFake engine = EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(Hash, releases).Service, graphics);
        DaggerfallSiteProfile profile = new(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            FixtureGeometry("spatial/site.json", Hash, 1, "mesh/site.json", Hash),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
            site: new DaggerfallSiteId(17, 179), profileKind: DaggerfallWorldProfileKind.Dungeon, logicalProfileId: "worldrpg/test/dungeon",
            lights: [Torch, Hearth, Crystal]);
        using DaggerfallSiteLighting lighting = new(graphics, engine.Context.CameraView, profile, Tuning, Night.Sky);

        Vector3 baselineSky = lighting.SkyLight;
        DaggerfallInteriorLightOverride pulp = new(
            AmbientColor: new(0.22f, 0.40f, 0.92f),
            AmbientIntensity: 0.28f,
            HemisphereSky: new(0.18f, 0.36f, 0.84f),
            HemisphereGround: new(0.06f, 0.12f, 0.30f),
            HemisphereIntensity: 0f,
            LampWarmColor: new(1.0f, 0.36f, 0.06f),
            HearthColor: new(1.0f, 0.26f, 0.04f),
            ArcaneColor: new(0.14f, 0.78f, 1.0f),
            LampIntensityMultiplier: 2.0f,
            LampDecay: 1.42f,
            LampShadowSoft: true,
            LampGlowLevel: 2.3f);

        lighting.ApplyLookOverride(pulp, Night.Sky);

        LightDescriptor[] updates = [.. graphics.LightUpdates.Select(u => u.Replacement.Descriptor)];
        LightDescriptor ambient = Assert.Single(updates, d => d.Kind == LightKind.Ambient);
        Assert.Equal(pulp.AmbientColor, ambient.Color);
        Assert.Equal(pulp.AmbientIntensity, ambient.Intensity, 4);
        Assert.True(lighting.SkyLight.Z > lighting.SkyLight.X, "Chromatic cobalt dungeon ambient cools SkyLight.");

        LightDescriptor[] points = [.. updates.Where(d => d.Kind == LightKind.Point)];
        Assert.Equal(3, points.Length);
        Assert.All(points, point =>
        {
            Assert.Equal(1.42f, point.Decay, 4);
            Assert.True(point.ShadowSoft);
        });
        Assert.Contains(points, point => point.Color == pulp.LampWarmColor);
        Assert.Contains(points, point => point.Color == pulp.HearthColor);
        Assert.Contains(points, point => point.Color == pulp.ArcaneColor);

        graphics.LightUpdates.Clear();
        lighting.ApplyLookOverride(null, Night.Sky);

        Assert.Equal(baselineSky, lighting.SkyLight);
        LightDescriptor[] restored = [.. graphics.LightUpdates.Where(u => u.Replacement.Descriptor.Kind == LightKind.Point).Select(u => u.Replacement.Descriptor)];
        Assert.Equal(3, restored.Length);
        Assert.All(restored, point =>
        {
            Assert.Equal(2.0f, point.Decay, 4);
            Assert.False(point.ShadowSoft);
        });
        Assert.Contains(restored, point => point.Color == Vector3.One);
    }

    [Fact]
    public void View_light_floor_override_grounds_the_viewmodel_and_clears_back_to_tuning()
    {
        DaggerfallViewLight view = new(Tuning);
        view.Update(() => Vector3.Zero);
        Assert.Equal(Tuning.ViewFloor, view.Level.R, 4);

        view.FloorOverride = 0.07f;
        view.Update(() => Vector3.Zero);
        Assert.Equal(0.07f, view.Level.R, 4);

        view.FloorOverride = null;
        view.Update(() => Vector3.Zero);
        Assert.Equal(Tuning.ViewFloor, view.Level.R, 4);
    }
}
