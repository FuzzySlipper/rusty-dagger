using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Entities;
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
             "placement": {"size": {"width": 0.8, "height": 0.8}, "pivot": {"x": 0.5, "y": 0.5}, "sequence": [0, 1], "framesPerSecond": 5, "loops": true}},
            {"replaces": "effect.impact.fire", "path": "refreshed/fire-impact.png", "byteLength": 1, "sha256": "HASH", "width": 64, "height": 32,
             "frames": [{"x": 0, "y": 0, "width": 32, "height": 32}, {"x": 32, "y": 0, "width": 32, "height": 32}],
             "placement": {"size": {"width": 0.8, "height": 0.8}, "pivot": {"x": 0.5, "y": 0.5}, "sequence": [0, 1], "framesPerSecond": 15, "loops": false}}],
          "effectGlow": {"path": "refreshed/glow.png", "byteLength": 1, "sha256": "HASH"},
          "meshes": [
            {"replaces": {"archive": 380, "record": 1}, "path": "refreshed/corpse-bones.glb", "byteLength": 1, "sha256": "HASH", "footprint": 2.15},
            {"replaces": {"archive": 380, "record": 4}, "path": "refreshed/corpse-bones.glb", "byteLength": 1, "sha256": "HASH", "footprint": 2.15}],
          "flats": [
            {"replaces": {"archive": 212, "record": 0}, "path": "refreshed/well.png", "byteLength": 1, "sha256": "HASH", "width": 32, "height": 32,
             "frames": [{"x": 0, "y": 0, "width": 32, "height": 32}], "classic": {"x": 0.25, "y": 0.5, "width": 0.5, "height": 0.5}},
            {"replaces": {"archive": 210, "record": 14}, "path": "refreshed/lamp.png", "byteLength": 1, "sha256": "HASH", "width": 32, "height": 32,
             "emission": {"path": "refreshed/lamp-glow.png", "byteLength": 1, "sha256": "HASH"},
             "frames": [{"x": 0, "y": 0, "width": 32, "height": 32}], "classic": {"x": 0.25, "y": 0.5, "width": 0.5, "height": 0.5}},
            {"replaces": {"archive": 210, "record": 12}, "path": "refreshed/orb.png", "byteLength": 1, "sha256": "HASH", "width": 32, "height": 32,
             "frames": [{"x": 0, "y": 0, "width": 32, "height": 32}], "classic": {"x": 0.25, "y": 0.5, "width": 0.5, "height": 0.5}}]}}
        """)), "test.art");

    private static (DaggerfallSiteAppearance Presentation, AppearanceFake Graphics, PresentationRecorder Particles) Compose(Func<bool> refreshed,
        NormalizedActorSprite? corpse = null, AnimationRecorder? animation = null, IReadOnlyList<DaggerfallSiteDecoration>? decorations = null)
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("refreshed/fire-missile.png", Hash);
        content.Add("refreshed/fire-impact.png", Hash);
        content.Add("refreshed/glow.png", Hash);
        content.Add("refreshed/corpse-bones.glb", Hash);
        content.Add("sprite/corpse.png", Hash);
        content.Add("sprite/well.png", Hash);
        content.Add("refreshed/well.png", Hash);
        content.Add("refreshed/lamp.png", Hash);
        content.Add("refreshed/lamp-glow.png", Hash);
        content.Add("refreshed/orb.png", Hash);
        AppearanceFake graphics = new(releases);
        DaggerfallSiteAppearance presentation = new(content, graphics, MediaInputs(classic: ClassicEffects(fireSpell: true), corpse: corpse, decorations: decorations));
        PresentationRecorder particles = PresentationRecorder.Create();
        presentation.UseEffectArt(particles.Service, (animation ?? AnimationRecorder.Create()).Service, Art(), refreshed);
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
        Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.SpellGlow, missile.Material.EmissionIntensity);
        Assert.NotEqual(default, missile.Material.EmissionMap);
        // The refreshed missile draws at its own size: the classic frame fills half of it, so twice the classic size about the same centre.
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
        Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.SpellGlow, missile.Material.EmissionIntensity);
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
    public void A_refreshed_effect_plays_its_own_frames_size_anchor_and_rate_whatever_its_classic_effect_states()
    {
        // Three frames played last first, against a classic effect of two frames played in order: the classic effect's
        // size, anchor, frames, rate and loop are withheld from the refreshed one, which reads them from its own entry.
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("refreshed/fire-impact.png", Hash);
        AppearanceFake graphics = new(releases);
        NormalizedClassicPresentation classic = ClassicEffects(fireSpell: true);
        classic = classic with { Effects = [.. classic.Effects.Select(effect => effect.Name == "fireImpact"
            ? effect with { DisplaySize = new Vector2(7F, 9F), Pivot = Vector2.Zero, Sequence = [1, 1, 0], FramesPerSecond = 99F, Loops = true } : effect)] };
        using DaggerfallSiteAppearance presentation = new(content, graphics, MediaInputs(classic: classic));
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(WithHash("""
            {"artReplacements": {"images": [], "effects": [
              {"replaces": "effect.impact.fire", "path": "refreshed/fire-impact.png", "byteLength": 1, "sha256": "HASH", "width": 96, "height": 32,
               "frames": [{"x": 0, "y": 0, "width": 32, "height": 32}, {"x": 32, "y": 0, "width": 32, "height": 32}, {"x": 64, "y": 0, "width": 32, "height": 32}],
               "placement": {"size": {"width": 1.2, "height": 0.6}, "pivot": {"x": 0.5, "y": 0.1}, "sequence": [2, 0, 1], "framesPerSecond": 8, "loops": false,
                 "from": {"size": "original", "pivot": "original", "sequence": "hand", "framesPerSecond": "hand", "loops": "original"}}}]}}
            """)), "test.art");
        presentation.UseEffectArt(PresentationRecorder.Create().Service, AnimationRecorder.Create().Service, art, () => true);
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new(2, 0, 3));

        presentation.PlaySpellImpact(7, 0, new Vector3(1, 1, 4));
        Publish(presentation, actors);
        SpriteFromAtlasRequest impact = graphics.SpriteRequests[^1];
        Assert.Equal(new Vector2(1.2F, .6F), impact.Size);
        Assert.Equal(new Vector2(.5F, .1F), impact.Pivot);
        SpritePlaybackCreateRequest playback = graphics.PlaybackRequests[^1];
        Assert.Equal([2U, 0U, 1U], playback.Frames.Span.ToArray().Select(frame => frame.FrameId));
        Assert.All(playback.Frames.Span.ToArray(), frame => Assert.Equal(1D / 8, frame.DurationSeconds, 6));
        Assert.Equal(SpritePlaybackLoopMode.OneShot, playback.LoopMode);
    }

    [Theory]
    [InlineData("size")]
    [InlineData("pivot")]
    [InlineData("sequence")]
    [InlineData("framesPerSecond")]
    [InlineData("loops")]
    public void A_refreshed_effect_missing_a_placement_value_is_refused_by_name(string value)
    {
        JsonObject placement = new() { ["size"] = new JsonObject { ["width"] = 1, ["height"] = 1 }, ["pivot"] = new JsonObject { ["x"] = .5, ["y"] = .5 },
            ["sequence"] = new JsonArray(0), ["framesPerSecond"] = 5, ["loops"] = true };
        placement.Remove(value);
        JsonObject effect = new()
        {
            ["replaces"] = "effect.missile.fire", ["path"] = "refreshed/fire-missile.png", ["byteLength"] = 1, ["sha256"] = HashHex, ["width"] = 32, ["height"] = 32,
            ["frames"] = new JsonArray(new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = 32, ["height"] = 32 }), ["placement"] = placement,
        };
        JsonObject payload = new() { ["artReplacements"] = new JsonObject { ["images"] = new JsonArray(), ["effects"] = new JsonArray(effect) } };
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(payload.ToJsonString()), "test.art"));
        Assert.Equal($"Content pack 'test.art' states no {value} in the placement of refreshed effect 'effect.missile.fire'.", refused.Message);
    }

    private static NormalizedActorSprite Corpse((int, int) source) =>
        new("sprite/corpse.png", Hash, 8, 8, [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, 0F), new Vector2(2.15F, .9F)) { SourceTexture = source };

    private static ActorsState DefeatedActor()
    {
        Rusty.Engine.Mechanics.Stat maximum = new(100);
        Rusty.Engine.Mechanics.StatsComponent stats = new();
        stats.AddStat(Rusty.Engine.Mechanics.StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(Rusty.Engine.Mechanics.TrackId.Parse("health"), new Rusty.Engine.Mechanics.Track(maximum, 0));
        ActorsState actors = new();
        actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), new Rusty.Engine.Mechanics.StatsComponent(), "health", DaggerActorFactory.PlayerCapabilities);
        actors.CreateActor(11, new EntityTypeId("test"), stats, new ActorPose(new WorldPoint(2, 0, 3), MathF.PI / 2), "health", DaggerActorFactory.NonPlayerCapabilities);
        return actors;
    }

    [Fact]
    public void A_creature_whose_corpse_is_the_shared_sprite_lies_as_the_bone_pile_mesh_in_refreshed_art()
    {
        bool refreshed = true;
        AnimationRecorder animation = AnimationRecorder.Create();
        var (presentation, graphics, _) = Compose(() => refreshed, Corpse((380, 1)), animation);
        using DaggerfallSiteAppearance owned = presentation;
        using ActorsState actors = DefeatedActor();
        Assert.Equal(["refreshed/corpse-bones.glb"], animation.OpenedPaths);
        Appearance mesh = Assert.Single(animation.Created);

        Publish(presentation, actors);
        AppearanceFact lying = Assert.Single(graphics.Snapshots[^1], fact => fact.Appearance == mesh);
        // The mesh lies along the creature's heading, where it fell.
        Assert.Equal(new Vector3(2, 0, 3), lying.Transform.Translation);
        Assert.Equal(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2), lying.Transform.Rotation);

        refreshed = false;
        Publish(presentation, actors);
        Assert.DoesNotContain(graphics.Snapshots[^1], fact => fact.Appearance == mesh);

        presentation.RetireActor(11);
        presentation.BeginAdmittedUpdate(); presentation.CompleteAdmittedUpdate(); presentation.BeginAdmittedUpdate();
        Assert.Equal(1, animation.ReleasedAppearances);
    }

    [Fact]
    public void A_placed_bone_pile_draws_as_the_mesh_at_its_sprite_width_on_its_floor_in_refreshed_art()
    {
        bool refreshed = true;
        AnimationRecorder animation = AnimationRecorder.Create();
        NormalizedBillboardSprite bones = new("sprite/corpse.png", Hash, 8, 8, [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, .5F), new Vector2(1.075F, .725F));
        DaggerfallSiteDecoration pile = new("flat-1", new WorldPoint(4, .5F, 6), false, bones, bones, (380, 4), (380, 4));
        var (presentation, graphics, _) = Compose(() => refreshed, animation: animation, decorations: [pile]);
        using DaggerfallSiteAppearance owned = presentation;
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new(2, 0, 3));
        // One model serves both records it replaces; this site gives the one placed pile its appearance.
        Assert.Equal(["refreshed/corpse-bones.glb"], animation.OpenedPaths);
        Appearance mesh = Assert.Single(animation.Created);

        Publish(presentation, actors);
        AppearanceFact lying = Assert.Single(graphics.Snapshots[^1], fact => fact.Appearance == mesh);
        // The sprite stands centred on its position, so the mesh rests half its height lower, at half the footprint's width.
        Assert.Equal(new Vector3(4, .5F - .3625F, 6), lying.Transform.Translation);
        Assert.Equal(new Vector3(.5F), lying.Transform.Scale);

        refreshed = false;
        Publish(presentation, actors);
        Assert.DoesNotContain(graphics.Snapshots[^1], fact => fact.Appearance == mesh);
    }

    [Fact]
    public void A_placed_flat_draws_its_refreshed_sprite_in_refreshed_art_and_its_classic_one_otherwise()
    {
        bool refreshed = true;
        NormalizedBillboardSprite well = new("sprite/well.png", Hash, 8, 8, [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, .5F), new Vector2(2F, 3F));
        DaggerfallSiteDecoration placed = new("flat-1", new WorldPoint(4, .5F, 6), true, well, well, (212, 0), (212, 0));
        var (presentation, graphics, _) = Compose(() => refreshed, decorations: [placed]);
        using DaggerfallSiteAppearance owned = presentation;
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new(2, 0, 3));

        Publish(presentation, actors);
        Appearance drawn = Assert.Single(graphics.Snapshots[^1], fact => fact.Transform.Translation == new Vector3(4, .5F, 6)).Appearance;

        refreshed = false;
        Publish(presentation, actors);
        Appearance classic = Assert.Single(graphics.Snapshots[^1], fact => fact.Transform.Translation == new Vector3(4, .5F, 6)).Appearance;
        Assert.NotEqual(drawn, classic);
    }

    [Fact]
    public void A_creature_with_its_own_corpse_keeps_its_sprite_in_refreshed_art()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        var (presentation, _, _) = Compose(() => true, Corpse((96, 2)), animation);
        using DaggerfallSiteAppearance owned = presentation;
        Assert.Empty(animation.Created);
    }

    private static List<DaggerfallSiteAppearance.EffectVisual> EffectsOf(DaggerfallSiteAppearance presentation) =>
        (List<DaggerfallSiteAppearance.EffectVisual>)typeof(DaggerfallSiteAppearance).GetField("effects",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(presentation)!;
}
