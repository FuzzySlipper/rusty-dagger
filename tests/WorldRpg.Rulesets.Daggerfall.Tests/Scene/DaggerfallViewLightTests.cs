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

/// <summary>The tint the first-person view takes from the light the eye stands in.</summary>
public sealed class DaggerfallViewLightTests
{
    private static readonly DaggerfallSiteLightingTuning Tuning = DaggerfallTuning.Defaults.SiteLighting;
    private static readonly DaggerfallCalendar Noon = DaggerfallCalendar.Start with { Hour = 12 };
    private static readonly DaggerfallCalendar Midnight = DaggerfallCalendar.Start with { Hour = 0 };

    private static DaggerfallSiteLighting Lighting(DaggerfallWorldProfileKind kind, DaggerfallCalendar calendar, params DaggerfallSiteLight[] lights)
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(Hash, releases).Service, appearance);
        DaggerfallSiteProfile profile = new(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            DaggerfallSiteGeometry.Closure(new SpatialContentArtifact("spatial/site.json", Hash, 1), new ContentArtifact("mesh/site.json", Hash)),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
            site: new DaggerfallSiteId(17, 4), profileKind: kind, logicalProfileId: "worldrpg/test/site", lights: lights);
        return new(appearance, engine.Context.CameraView, profile, Tuning, calendar.Sky);
    }

    private static Vector3 Level(DaggerfallSiteLighting lighting, Vector3 eye = default) =>
        DaggerfallViewLight.Tint(lighting.SkyLight + lighting.LampLight(eye), Tuning);

    private static DaggerfallSiteLighting Sky(DaggerfallCalendar calendar, float daylight)
    {
        DaggerfallSiteLighting lighting = Lighting(DaggerfallWorldProfileKind.Exterior, calendar);
        lighting.UpdateAmbient(calendar.Sky, daylight);
        return lighting;
    }

    [Fact]
    public void The_clear_noon_sky_draws_the_view_at_its_own_value()
    {
        using DaggerfallSiteLighting noon = Sky(Noon, DaggerfallTuning.Defaults.Weather.Daylight(Noon, DaggerfallWeatherKind.Sunny));

        Assert.InRange(Level(noon).X, .97F, 1F);
        Assert.Equal(1F, Level(noon).Z, 2);
    }

    [Fact]
    public void Night_dims_the_view_and_cools_it()
    {
        using DaggerfallSiteLighting night = Sky(Midnight, 0);

        Vector3 level = Level(night);
        Assert.InRange(level.Z, Tuning.ViewFloor, .3F);
        Assert.True(level.X < level.Z, "The night's ambient is cool, as it is on the ground.");
    }

    [Fact]
    public void An_overcast_noon_falls_short_of_a_clear_one()
    {
        using DaggerfallSiteLighting clear = Sky(Noon, .9F);
        using DaggerfallSiteLighting grey = Sky(Noon, .5F);

        Assert.True(Level(grey).X < Level(clear).X);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_room_with_no_lamp_near_stands_the_view_at_its_floor_or_above_and_well_under_noon(bool dungeon)
    {
        using DaggerfallSiteLighting room = Lighting(dungeon ? DaggerfallWorldProfileKind.Dungeon : DaggerfallWorldProfileKind.Interior, Noon);

        Assert.InRange(Level(room).X, Tuning.ViewFloor, .5F);
        Assert.Equal(Level(room).X, Level(room).Z, 4);
    }

    [Fact]
    public void A_lamp_lifts_the_view_in_its_colour_within_its_reach_alone()
    {
        DaggerfallSiteLight lamp = new("lamp", new WorldPoint(0, 2, 0), 8, 3, new Vector3(1, .6F, .2F));
        using DaggerfallSiteLighting night = Lighting(DaggerfallWorldProfileKind.Dungeon, Midnight, lamp);

        Vector3 beside = Level(night, new Vector3(0, 1, 0)), near = Level(night, new Vector3(3, 2, 0)), beyond = Level(night, new Vector3(8.5F, 2, 0));

        Assert.True(beside.X > near.X && near.X > beyond.X, $"{beside} {near} {beyond}");
        Assert.True(beside.X > beside.Z, "A warm lamp warms the hand.");
        Assert.Equal(Level(Lighting(DaggerfallWorldProfileKind.Dungeon, Midnight)), beyond);
    }

    [Fact]
    public void A_light_past_full_keeps_its_hue_and_no_light_stands_at_the_floor()
    {
        Assert.Equal(new Vector3(1, .5F, .25F), DaggerfallViewLight.Tint(new Vector3(8, 4, 2) * Tuning.ViewFull, Tuning));
        Assert.Equal(new Vector3(Tuning.ViewFloor), DaggerfallViewLight.Tint(Vector3.Zero, Tuning));
    }

    [Fact]
    public void The_level_is_sampled_every_few_updates_and_moves_only_in_steps()
    {
        DaggerfallViewLight view = new(Tuning);
        int reads = 0;
        Vector3 light = new(Tuning.ViewFull / 2);
        Vector3 Read() { reads++; return light; }

        for (int update = 0; update < 12; update++) view.Update(Read);
        Assert.Equal(2, reads);
        Assert.Equal(.5F, view.Level.R, 4);

        light *= 1.01F;
        for (int update = 0; update < 6; update++) view.Update(Read);
        Assert.Equal(.5F, view.Level.R, 4);

        light *= 1.1F;
        for (int update = 0; update < 6; update++) view.Update(Read);
        Assert.True(view.Level.R > .55F);
    }

    [Fact]
    public void A_slot_keeps_its_metal_under_the_view_light()
    {
        Color metal = new(.5F, .25F, 1, 1), night = new(.5F, .5F, 1, 1);

        Assert.Equal(new Color(.25F, .125F, 1, 1), DaggerfallHeldLitLook.Under(metal, night));
    }
}
