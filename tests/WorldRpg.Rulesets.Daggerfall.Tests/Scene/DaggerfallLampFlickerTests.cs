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
/// A lit lamp near the eye flickers about its level, on a phase of its own from its authored id, twenty times a second at
/// most; farther lamps and lamps that are out keep their light (#10906).
/// </summary>
public sealed class DaggerfallLampFlickerTests
{
    private static readonly DaggerfallSiteLightingTuning Tuning = DaggerfallTuning.Defaults.SiteLighting;
    private static readonly DaggerfallSiteLight Door = new("light-flat/block/0/0/1", new WorldPoint(4, 2.6F, 6), 18, 1, Vector3.One) { CityLight = true };
    private static readonly DaggerfallSiteLight Corner = new("light-flat/block/0/0/2", new WorldPoint(-6, 2.6F, 2), 18, 1, Vector3.One) { CityLight = true };
    private static readonly DaggerfallSiteLight Harbour = new("light-flat/block/3/1/7", new WorldPoint(400, 2.6F, 6), 18, 1, Vector3.One) { CityLight = true };

    private static (DaggerfallSiteLighting Lighting, AppearanceFake Graphics) Lighting(DaggerfallCalendar calendar)
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(Hash, releases).Service, appearance);
        DaggerfallSiteProfile profile = new(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            FixtureGeometry("spatial/site.json", Hash, 1, "mesh/site.json", Hash),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
            site: new DaggerfallSiteId(17, 4), profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: "worldrpg/test/street",
            lights: [Door, Corner, Harbour]);
        return (new(appearance, engine.Context.CameraView, profile, Tuning, calendar.Sky), appearance);
    }

    [Fact]
    public void Lit_lamps_near_the_eye_flicker_about_their_level_each_on_its_own_phase_and_far_ones_burn_steady()
    {
        var (lighting, graphics) = Lighting(DaggerfallCalendar.Start with { Hour = 22 });
        using DaggerfallSiteLighting owned = lighting;
        Vector3 eye = new(0, 1.6F, 4);
        float level = Tuning.ExteriorLampScale;

        lighting.Flicker(eye, .3);
        LightDescriptor[] first = [.. graphics.LightUpdates.Select(update => update.Replacement.Descriptor)];
        Assert.Equal(2, first.Length);
        Assert.DoesNotContain(first, lamp => lamp.Position == Harbour.Position.ToVector());
        Assert.All(first, lamp => Assert.InRange(lamp.Intensity, level * (1 - Tuning.LampFlicker), level * (1 + Tuning.LampFlicker)));
        Assert.NotEqual(first[0].Intensity, first[1].Intensity);

        // Within a twentieth of a second nothing changes; after it, the flames have moved on.
        lighting.Flicker(eye, .01);
        Assert.Equal(2, graphics.LightUpdates.Count);
        lighting.Flicker(eye, .05);
        Assert.Equal(4, graphics.LightUpdates.Count);
        Assert.NotEqual(first[0].Intensity, graphics.LightUpdates[2].Replacement.Descriptor.Intensity);
    }

    [Fact]
    public void Lamps_that_are_out_do_not_flicker_and_a_lamp_flickers_alike_in_every_session()
    {
        var (day, dayGraphics) = Lighting(DaggerfallCalendar.Start with { Hour = 12 });
        using (day)
        {
            day.Flicker(new Vector3(0, 1.6F, 4), .3);
            Assert.Empty(dayGraphics.LightUpdates);
        }

        var (one, oneGraphics) = Lighting(DaggerfallCalendar.Start with { Hour = 22 });
        var (other, otherGraphics) = Lighting(DaggerfallCalendar.Start with { Hour = 22 });
        using (one)
        using (other)
        {
            one.Flicker(Door.Position.ToVector(), .3);
            other.Flicker(Door.Position.ToVector(), .3);
            Assert.Equal(oneGraphics.LightUpdates.Select(update => update.Replacement.Descriptor.Intensity),
                otherGraphics.LightUpdates.Select(update => update.Replacement.Descriptor.Intensity));
        }
    }
}
