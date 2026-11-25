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

        Assert.Equal("auto", look.Preset);
        Assert.Null(look.Select("baseline"));
        Assert.Equal("baseline", look.Preset);
        Assert.Null(look.Select("dungeon-cobalt"));
        Assert.Equal("dungeon-cobalt", look.Preset);
        Assert.Null(look.Select("RESET"));
        Assert.Equal("baseline", look.Preset);
        Assert.Contains("Unknown interior look", look.Select("neon-lasers"));
        Assert.Equal("baseline", look.Preset);

        Assert.Equal("voxel-pulp", look.ReliefMode);
        Assert.Null(look.SelectRelief("voxel-wet"));
        Assert.Equal("voxel-wet", look.ReliefMode);
        Assert.Null(look.SelectRelief("reset"));
        Assert.Equal("off", look.ReliefMode);
        Assert.Contains("Unknown interior relief mode", look.SelectRelief("bumps"));
        Assert.Equal("off", look.ReliefMode);

        Assert.Equal("full", look.SurfaceMode);
        Assert.Null(look.SelectSurfaces("materials"));
        Assert.Equal("materials", look.SurfaceMode);
        Assert.Null(look.SelectSurfaces("shells"));
        Assert.Equal("shells", look.SurfaceMode);
        Assert.Null(look.SelectSurfaces("reset"));
        Assert.Equal("off", look.SurfaceMode);
        Assert.Contains("Unknown interior surfaces mode", look.SelectSurfaces("chrome"));
        Assert.Equal("off", look.SurfaceMode);
    }

    [Fact]
    public void Pass3_surface_materials_classify_arena2_textures_and_shell_eligibility()
    {
        Assert.Equal((123, 0), DaggerfallInteriorSurfaceMaterials.TryParseTextureRecord("worldrpg/packs/daggerfall.art-replacements/media/surfaces/texture-123-0.png"));
        Assert.Equal((49, 6), DaggerfallInteriorSurfaceMaterials.TryParseTextureRecord("textures/texture-049-6.png"));
        Assert.Null(DaggerfallInteriorSurfaceMaterials.TryParseTextureRecord("custom/stone.png"));

        Assert.Equal(DaggerfallInteriorSurfaceClass.MossyStone, DaggerfallInteriorSurfaceMaterials.Classify(23, 0));
        Assert.Equal(DaggerfallInteriorShellKind.MossOlive, DaggerfallInteriorSurfaceMaterials.ShellKind(23, 0));
        Assert.Equal(DaggerfallInteriorSurfaceClass.MossyStone, DaggerfallInteriorSurfaceMaterials.Classify(123, 1));
        Assert.Equal(DaggerfallInteriorShellKind.MossGreen, DaggerfallInteriorSurfaceMaterials.ShellKind(123, 1));
        Assert.Equal(DaggerfallInteriorSurfaceClass.PlushCarpet, DaggerfallInteriorSurfaceMaterials.Classify(49, 6));
        Assert.Equal(DaggerfallInteriorShellKind.PlushCarpet, DaggerfallInteriorSurfaceMaterials.ShellKind(49, 6));
        Assert.Equal(DaggerfallInteriorSurfaceClass.WovenTapestry, DaggerfallInteriorSurfaceMaterials.Classify(57, 0));
        Assert.Equal(DaggerfallInteriorShellKind.WovenTapestry, DaggerfallInteriorSurfaceMaterials.ShellKind(57, 0));
        Assert.Equal(DaggerfallInteriorSurfaceClass.PolishedMarble, DaggerfallInteriorSurfaceMaterials.Classify(63, 0));
        Assert.Equal(DaggerfallInteriorSurfaceClass.PolishedMarble, DaggerfallInteriorSurfaceMaterials.Classify(363, 3)); // Temperate Temple
        Assert.Equal(DaggerfallInteriorSurfaceClass.PolishedMarble, DaggerfallInteriorSurfaceMaterials.Classify(41, 2));
        Assert.Equal(DaggerfallInteriorSurfaceClass.PolishedMarble, DaggerfallInteriorSurfaceMaterials.Classify(341, 1));
        Assert.Equal(DaggerfallInteriorSurfaceClass.ArcaneStone, DaggerfallInteriorSurfaceMaterials.Classify(337, 0));    // Temperate Mages Guild
        Assert.Equal(DaggerfallInteriorSurfaceClass.PlushCarpet, DaggerfallInteriorSurfaceMaterials.Classify(349, 6));    // Temperate Carpet
        Assert.Equal(DaggerfallInteriorShellKind.PlushCarpet, DaggerfallInteriorSurfaceMaterials.ShellKind(349, 6));
        Assert.Equal(DaggerfallInteriorSurfaceClass.PaintingOrScroll, DaggerfallInteriorSurfaceMaterials.Classify(48, 3));
        Assert.Equal(DaggerfallInteriorSurfaceClass.PaintingOrScroll, DaggerfallInteriorSurfaceMaterials.Classify(348, 1));
        Assert.Equal(DaggerfallInteriorSurfaceClass.BloodAndGore, DaggerfallInteriorSurfaceMaterials.Classify(93, 0));
        Assert.Equal(DaggerfallInteriorSurfaceClass.IronAndDoors, DaggerfallInteriorSurfaceMaterials.Classify(74, 0));
        Assert.Equal(DaggerfallInteriorSurfaceClass.WetSewerCave, DaggerfallInteriorSurfaceMaterials.Classify(47, 1));
        Assert.Equal(DaggerfallInteriorSurfaceClass.FlatSolid, DaggerfallInteriorSurfaceMaterials.Classify(0, 40));

        Assert.True(DaggerfallInteriorSurfaceMaterials.DungeonDampnessFor(6) > DaggerfallInteriorSurfaceMaterials.DungeonDampnessFor(2));
    }

    [Fact]
    public void Auto_mood_mapping_assigns_distinct_palettes_by_dungeon_and_building_type()
    {
        Assert.Equal("dungeon-cobalt", DaggerfallInteriorLook.DungeonPresetFor(2));   // HumanStronghold (Privateer's Hold)
        Assert.Equal("dungeon-cobalt", DaggerfallInteriorLook.DungeonPresetFor(11));  // RuinedCastle (Castle Necromoghan)
        Assert.Equal("dungeon-violet", DaggerfallInteriorLook.DungeonPresetFor(0));   // Crypt
        Assert.Equal("dungeon-violet", DaggerfallInteriorLook.DungeonPresetFor(8));   // VampireHaunt
        Assert.Equal("dungeon-teal", DaggerfallInteriorLook.DungeonPresetFor(6));     // NaturalCave
        Assert.Equal("dungeon-teal", DaggerfallInteriorLook.DungeonPresetFor(5));     // Mine
        Assert.Equal("dungeon-emerald", DaggerfallInteriorLook.DungeonPresetFor(1));  // OrcStronghold
        Assert.Equal("dungeon-emerald", DaggerfallInteriorLook.DungeonPresetFor(12)); // SpiderNest

        Assert.Equal("interior-cozy", DaggerfallInteriorLook.InteriorPresetFor(15));     // Tavern
        Assert.Equal("interior-dramatic", DaggerfallInteriorLook.InteriorPresetFor(18)); // Residence
        Assert.Equal("interior-dramatic", DaggerfallInteriorLook.InteriorPresetFor(14)); // Temple
        Assert.Equal("interior-dramatic", DaggerfallInteriorLook.InteriorPresetFor(null));
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

    [Fact]
    public void Interior_extrude_modes_validate_and_scale_surface_classes()
    {
        List<string> releases = [];
        EngineContextFake engine = EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(Hash, releases).Service, new AppearanceFake(releases));
        using DaggerfallInteriorLook look = new(engine.Context, data: null, Tuning);
        Assert.Equal("off", look.ExtrudeMode);
        Assert.Null(look.SelectExtrude("deep-pom"));
        Assert.Equal("deep-pom", look.ExtrudeMode);
        Assert.Null(look.SelectExtrude("silhouette-pom"));
        Assert.Equal("silhouette-pom", look.ExtrudeMode);
        Assert.Null(look.SelectExtrude("wall-shells"));
        Assert.Equal("wall-shells", look.ExtrudeMode);
        Assert.Null(look.SelectExtrude("wall-shells-shadow"));
        Assert.Equal("wall-shells-shadow", look.ExtrudeMode);
        Assert.NotNull(look.SelectExtrude("not-a-mode"));
        Assert.Null(look.SelectExtrude("reset"));
        Assert.Equal("off", look.ExtrudeMode);

        Assert.Equal(0f, DaggerfallInteriorSurfaceMaterials.ExtrudeScale(DaggerfallInteriorSurfaceClass.FlatSolid));
        Assert.Equal(0f, DaggerfallInteriorSurfaceMaterials.ExtrudeScale(DaggerfallInteriorSurfaceClass.PaintingOrScroll));
        Assert.Equal(0f, DaggerfallInteriorSurfaceMaterials.ExtrudeScale(DaggerfallInteriorSurfaceClass.PlushCarpet));
        Assert.True(DaggerfallInteriorSurfaceMaterials.ExtrudeScale(DaggerfallInteriorSurfaceClass.ArcaneStone) > 1.0f);
        Assert.True(DaggerfallInteriorSurfaceMaterials.ExtrudeScale(DaggerfallInteriorSurfaceClass.DefaultStone) == 1.0f);
    }
}
