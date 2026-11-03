using System.Numerics;
using System.Text;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class SpellEffectPresentationTests
{
    // TestSessions.Hash, as a publication states it.
    private const string HashHex = "0000000000000001000000000000000200000000000000030000000000000004";

    private static string WithHash(string json) => json.Replace("HASH", HashHex, StringComparison.Ordinal);

    private static DaggerfallArtReplacements Art() => DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(WithHash("""
        {"artReplacements": {"images": [],
          "effects": [
            {"replaces": "effect.missile.fire", "path": "refreshed/fire-missile.png", "byteLength": 1, "sha256": "HASH", "width": 64, "height": 32,
             "frames": [{"x": 0, "y": 0, "width": 32, "height": 32}, {"x": 32, "y": 0, "width": 32, "height": 32}],
             "classic": {"x": 0.25, "y": 0.25, "width": 0.5, "height": 0.5}},
            {"replaces": "effect.impact.fire", "path": "refreshed/fire-impact.png", "byteLength": 1, "sha256": "HASH", "width": 64, "height": 32,
             "frames": [{"x": 0, "y": 0, "width": 32, "height": 32}, {"x": 32, "y": 0, "width": 32, "height": 32}],
             "classic": {"x": 0.25, "y": 0.25, "width": 0.5, "height": 0.5}}],
          "effectGlow": {"path": "refreshed/glow.png", "byteLength": 1, "sha256": "HASH"}}}
        """)), "test.art");

    private static (DaggerfallSiteAppearance Presentation, AppearanceFake Graphics, PresentationRecorder Particles) Compose(Func<bool> refreshed)
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("refreshed/fire-missile.png", Hash);
        content.Add("refreshed/fire-impact.png", Hash);
        content.Add("refreshed/glow.png", Hash);
        AppearanceFake graphics = new(releases);
        DaggerfallSiteAppearance presentation = new(content, graphics, MediaInputs(classic: ClassicEffects(fireSpell: true)));
        PresentationRecorder particles = PresentationRecorder.Create();
        presentation.UseEffectArt(particles.Service, Art(), refreshed);
        return (presentation, graphics, particles);
    }

    private static void Publish(DaggerfallSiteAppearance presentation, ActorsState actors, params DaggerfallDungeonSpellFlightView[] flights) =>
        presentation.Publish(actors, new Dictionary<long, DaggerfallGroundContainer>(), dungeonSpellFlights: flights);

    [Fact]
    public void A_flying_spell_draws_its_element_missile_in_refreshed_art_with_a_trail_and_lands_as_its_impact()
    {
        var (presentation, graphics, particles) = Compose(() => true);
        using DaggerfallSiteAppearance owned = presentation;
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new(2, 0, 3));
        Assert.Contains("refreshed/fire-missile.png", graphics.OpenedResourcePaths);

        Publish(presentation, actors, new DaggerfallDungeonSpellFlightView(7, new WorldPoint(1, 1, 1), Vector3.UnitZ, 0));
        SpriteFromAtlasRequest missile = graphics.SpriteRequests[^1];
        Assert.Equal(BillboardMode.Spherical, missile.Billboard);
        Assert.Equal(SpriteBlendMode.Additive, missile.Material.Blend);
        // The classic frame fills half the refreshed one, so the refreshed one draws twice the classic size about the same centre.
        Assert.Equal(new Vector2(.8F, .8F), missile.Size);
        Assert.Equal(new Vector2(.5F, .5F), missile.Pivot);
        PresentationParticleDescriptor trail = Assert.Single(particles.Created);
        Assert.Equal(new Vector3(1, 1, 1), trail.Anchor.Position);
        Assert.Equal(1, EffectCount(presentation));

        Publish(presentation, actors, new DaggerfallDungeonSpellFlightView(7, new WorldPoint(1, 1, 3), Vector3.UnitZ, 0));
        Assert.Equal(new WorldPoint(1, 1, 3), Effect(presentation).Position);
        Assert.Equal(new Vector3(1, 1, 3), particles.Updated[^1].Anchor.Position);

        presentation.PlaySpellImpact(7, 0, new Vector3(1, 1, 4));
        Publish(presentation, actors);
        DaggerfallSiteAppearance.EffectVisual impact = Effect(presentation);
        Assert.True(impact.Refreshed);
        Assert.Equal(new WorldPoint(1, 1, 4), impact.Position);
        Assert.Equal(24U, Assert.Single(particles.Emitted).BurstCount);
    }

    [Fact]
    public void Classic_art_draws_the_classic_missile_and_impact_without_particles()
    {
        bool refreshed = false;
        var (presentation, graphics, particles) = Compose(() => refreshed);
        using DaggerfallSiteAppearance owned = presentation;
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new(2, 0, 3));

        Publish(presentation, actors, new DaggerfallDungeonSpellFlightView(7, new WorldPoint(1, 1, 1), Vector3.UnitZ, 0));
        SpriteFromAtlasRequest missile = graphics.SpriteRequests[^1];
        Assert.Equal(BillboardMode.Cylindrical, missile.Billboard);
        Assert.Equal(new Vector2(.4F, .4F), missile.Size);
        Assert.Empty(particles.Created);
        presentation.PlaySpellImpact(7, 0, new Vector3(1, 1, 4));
        Assert.False(Assert.Single(EffectsOf(presentation), effect => effect.Position == new WorldPoint(1, 1, 4)).Refreshed);
        Assert.Empty(particles.Emitted);

        // Choosing refreshed art draws the next spell refreshed.
        refreshed = true;
        presentation.PlaySpellImpact(8, 0, new Vector3(2, 1, 4));
        Assert.True(Assert.Single(EffectsOf(presentation), effect => effect.Position == new WorldPoint(2, 1, 4)).Refreshed);
    }

    [Fact]
    public void A_refreshed_effect_with_other_frames_than_its_classic_is_refused()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("refreshed/fire-missile.png", Hash);
        using DaggerfallSiteAppearance presentation = new(content, new AppearanceFake(releases), MediaInputs(classic: ClassicEffects(fireSpell: true)));
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(WithHash("""
            {"artReplacements": {"images": [], "effects": [
              {"replaces": "effect.missile.fire", "path": "refreshed/fire-missile.png", "byteLength": 1, "sha256": "HASH", "width": 32, "height": 32,
               "frames": [{"x": 0, "y": 0, "width": 32, "height": 32}], "classic": {"x": 0, "y": 0, "width": 1, "height": 1}}]}}
            """)), "test.art");
        Assert.Throws<InvalidOperationException>(() => presentation.UseEffectArt(PresentationRecorder.Create().Service, art, () => true));
    }

    private static List<DaggerfallSiteAppearance.EffectVisual> EffectsOf(DaggerfallSiteAppearance presentation) =>
        (List<DaggerfallSiteAppearance.EffectVisual>)typeof(DaggerfallSiteAppearance).GetField("effects",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(presentation)!;
}
