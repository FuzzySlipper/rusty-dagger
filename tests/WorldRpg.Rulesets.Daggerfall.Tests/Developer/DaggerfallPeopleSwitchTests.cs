using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallPeopleSwitch"/> built without the session: the art set's people rig and gsplat townspeople over
/// recording Engine parts-rig, animation and content services and an empty actor directory. What the figures draw
/// walking through a town is <c>DaggerfallPopulationSessionTests</c>' end-to-end case.
/// </summary>
public sealed class DaggerfallPeopleSwitchTests
{
    private const float WalkerSpeed = 1.3F;

    [Fact]
    public void A_view_set_loads_the_figure_and_off_returns_people_to_their_own_art()
    {
        PartsRigsRecorder rigs = PartsRigsRecorder.Create();
        DaggerfallArtReplacements art = Art();
        DaggerfallPeopleRig rig = art.PeopleRig!;
        using DaggerfallPeopleSwitch people = Switch(art, rigs, AnimationRecorder.Create());

        // Off before any use answers, and makes no crowd.
        Assert.Equal("Walking people show their own art again.", people.Rig("off").Message);
        Assert.Null(people.Crowd);

        var unknown = people.Rig("6");
        Assert.False(unknown.Succeeded);
        Assert.Equal($"People rig {rig.Name} has no view set '6'; it has {string.Join(", ", rig.Directions.Keys.Order(StringComparer.Ordinal))}, or off.", unknown.Message);
        Assert.Null(people.Crowd);

        var four = people.Rig("4");
        Assert.True(four.Succeeded);
        Assert.Equal($"Walking people draw as {rig.Name} seen from 4 directions; 0 walk here now.", four.Message);
        PartsRigCrowd crowd = Assert.IsType<PartsRigCrowd>(people.Crowd);
        Assert.NotNull(crowd.Figure);
        Assert.Equal(["front", "side", "back"], rigs.Loaded.Select(path => Path.GetFileName(Path.GetDirectoryName(path))));

        Assert.True(people.Rig("off").Succeeded);
        Assert.Null(people.Crowd);
        Assert.Null(crowd.Figure);
    }

    [Fact]
    public void A_gsplat_character_opens_once_with_its_clips_bound_and_fitted_and_draws_lit()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = [new("walk", "walk", 1.03F, true, 1), new("idle", "idle", 5.03F, true, 2)];
        DaggerfallArtReplacements art = Art();
        DaggerfallSplatPeople splats = art.SplatPeople!;
        using DaggerfallPeopleSwitch people = Switch(art, PartsRigsRecorder.Create(), animation);

        var unknown = people.Splat("999");
        Assert.False(unknown.Succeeded);
        Assert.Equal($"The art set has no gsplat character '999'; it has {string.Join(", ", splats.Characters.Keys.Order(StringComparer.Ordinal))}, or off.", unknown.Message);
        Assert.Null(people.Crowd);

        DaggerfallSplatPerson townsman = splats.Characters["385"];
        var drawn = people.Splat("385");
        Assert.True(drawn.Succeeded);
        Assert.Equal($"Walking people draw as gsplat 385 ({townsman.Name}), lit; 0 walk here now.", drawn.Message);
        SplatFigure figure = Assert.IsType<SplatCrowd>(people.Crowd).Figure!;
        Assert.Equal(("walk", "idle", .25F), (figure.Walk, figure.Idle, figure.CrossfadeSeconds));
        // Its stride keeps pace with a townsperson: the fitted walk plays at their speed over its own.
        Assert.Equal(WalkerSpeed / townsman.WalkSpeed, figure.WalkRate, 5);

        // The clip packs open once, and the body binds both by joint names under their producer and licence.
        Assert.Equal([splats.Walk.Path, splats.Idle.Path], animation.OpenedPackPaths);
        Assert.Equal([townsman.Body.Path], animation.OpenedPaths);
        Assert.Equal([(9001UL, 9501UL, splats.Producer, splats.License), (9001UL, 9502UL, splats.Producer, splats.License)], animation.JointNameBindings);
        // Each clip's pelvis is fitted to his legs; the walk keeps none of its travel forward, since the walker moves himself.
        Assert.Collection(animation.Fits,
            walk => Assert.Equal(("walk", splats.Joint, new System.Numerics.Vector3(townsman.Walk.Scale, townsman.Walk.Scale, 0), new System.Numerics.Vector3(0, townsman.Walk.Lift, 0)),
                (walk.Clip, walk.Joint, walk.Scale, walk.Offset)),
            idle => Assert.Equal(("idle", splats.Joint, new System.Numerics.Vector3(townsman.Idle.Scale), new System.Numerics.Vector3(0, townsman.Idle.Lift, 0)),
                (idle.Clip, idle.Joint, idle.Scale, idle.Offset)));
        // He is lit by the world's lights, as baked otherwise, and casts an ellipsoid a joint.
        SplatAppearanceRequest look = Assert.Single(animation.Splats);
        Assert.Same(figure.Appearance, look.Appearance);
        // ... in the tuning's look: 3 px blocks.
        Assert.Equal((System.Numerics.Vector3.One, 3U, .5F, 0F, SplatLighting.Lit, .15F, SplatShadowCaster.Ellipsoids),
            (look.Multiplier, look.PixelGrid, look.PixelCoverage, look.PoseRate, look.Lighting, look.LightFloor, look.ShadowCaster));
        Assert.True(look.Palette.IsEmpty);

        // people.splat-look sets the grid, coverage, palette and pose rate for the run, lit as before.
        Assert.True(people.SplatLook(5, .7, "own", 12).Succeeded);
        SplatAppearanceRequest tried = animation.Splats[^1];
        Assert.Equal((5U, .7F, 12F, SplatLighting.Lit, .15F), (tried.PixelGrid, tried.PixelCoverage, tried.PoseRate, tried.Lighting, tried.LightFloor));
        Assert.Equal(townsman.Palette, tried.Palette.ToArray());
        Assert.True(people.SplatLook(0, .5, "none", 0).Succeeded);
        Assert.Equal((0U, 0F), (animation.Splats[^1].PixelGrid, animation.Splats[^1].PoseRate));
        Assert.True(animation.Splats[^1].Palette.IsEmpty);
        Assert.False(people.SplatLook(0, .5, "own", 0).Succeeded);
        Assert.False(people.SplatLook(17, .5, "none", 0).Succeeded);
        Assert.True(people.SplatLook(3, .5, "none", 0).Succeeded);

        // people.splat-shadow switches the caster for the run and keeps the look.
        Assert.True(people.SplatShadow("splats").Succeeded);
        Assert.Equal((SplatShadowCaster.Splats, 3U), (animation.Splats[^1].ShadowCaster, animation.Splats[^1].PixelGrid));
        Assert.True(people.SplatShadow("none").Succeeded);
        Assert.Equal(SplatShadowCaster.None, animation.Splats[^1].ShadowCaster);
        Assert.False(people.SplatShadow("blobs").Succeeded);
        Assert.True(people.SplatShadow("ellipsoids").Succeeded);

        // The rig figure takes over from him, and he from it, without opening him again.
        Assert.True(people.Rig("4").Succeeded);
        Assert.IsType<PartsRigCrowd>(people.Crowd);
        Assert.True(people.Splat("385").Succeeded);
        Assert.Same(figure, Assert.IsType<SplatCrowd>(people.Crowd).Figure);
        Assert.Single(animation.OpenedPaths);
        Assert.Equal(2, animation.OpenedPackPaths.Count);

        Assert.True(people.Splat("off").Succeeded);
        Assert.Null(people.Crowd);
        people.Dispose();
        Assert.Equal(1, animation.ReleasedAppearances);
    }

    [Fact]
    public void A_character_whose_clips_did_not_bind_is_refused_and_released()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = [new("walk", "walk", 1.03F, true, 1)];
        using DaggerfallPeopleSwitch people = Switch(Art(), PartsRigsRecorder.Create(), animation);
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => people.Splat("385"));
        Assert.Equal("Gsplat character 385 carries no clip 'idle' after its clip packs were bound; it has walk.", refused.Message);
        Assert.Null(people.Crowd);
        Assert.Empty(animation.Created);
    }

    [Fact]
    public void An_art_set_without_a_people_rig_or_gsplat_people_refuses_them()
    {
        using ActorsState actors = new();
        using DaggerfallPeopleSwitch people = new(null, null, () => PartsRigsRecorder.Create().Service, () => AnimationRecorder.Create().Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors));
        var refused = people.Rig("4");
        Assert.False(refused.Succeeded);
        Assert.Equal("The art set carries no people rig.", refused.Message);
        var noSplats = people.Splat("385");
        Assert.False(noSplats.Succeeded);
        Assert.Equal("The art set carries no gsplat townspeople.", noSplats.Message);
        Assert.True(people.Rig("off").Succeeded);
        Assert.True(people.Splat("off").Succeeded);
        Assert.Equal("The art set carries no gsplat townspeople.", people.Spawn("385", 1).Message);
    }

    [Fact]
    public void Spawn_stands_the_nearest_walkers_ahead_as_their_own_character_whatever_the_rest_draw_as()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = [new("walk", "walk", 1.03F, true, 1), new("idle", "idle", 5.03F, true, 2)];
        using ActorsState actors = new();
        actors.CreatePlayer(1, new Rusty.Engine.Entities.EntityTypeId("player"), new Rusty.Engine.Mechanics.StatsComponent(), "health");
        foreach ((long id, float distance) in new[] { (10L, 12F), (11L, 15F), (12L, 20F) })
        {
            ActorState civilian = actors.CreateActor(id, new Rusty.Engine.Entities.EntityTypeId(DaggerfallActorKinds.Civilian),
                new Rusty.Engine.Mechanics.StatsComponent(), new ActorPose(new WorldPoint(distance, 0F, 0F), 0F), "health");
            civilian.Stats.AddTrack(Rusty.Engine.Mechanics.TrackId.Parse("health"), new Rusty.Engine.Mechanics.Track(new Rusty.Engine.Mechanics.Stat(1), 1));
            civilian.Actor.Add(new WorldRpg.Kit.Ai.WanderMemoryComponent());
        }
        using DaggerfallPeopleSwitch people = new(null, Art().SplatPeople, () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors));

        var spawned = people.Spawn("454", 2);
        Assert.True(spawned.Succeeded, spawned.Message);
        // The two nearest stand 4 m ahead, 1.5 m apart, drawn as 454 while the crowd draws nobody.
        Appearance character = Assert.Single(animation.Splats).Appearance;
        Assert.Equal([new System.Numerics.Vector3(-.75F, 0, -4), new System.Numerics.Vector3(.75F, 0, -4)],
            new long[] { 10, 11 }.Select(id => actors.Get(id).Position.ToVector()));
        Assert.Null(people.Crowd);
        Assert.Same(character, people.Show(100, 10, 1.7F, walking: false));
        Assert.Null(people.Show(102, 12, 1.7F, walking: false));

        // A second spawn takes the next walker, a row further back: the first row is taken.
        Assert.True(people.Spawn("454", 1).Succeeded);
        Assert.Equal(new System.Numerics.Vector3(0, 0, -5.5F), actors.Get(12).Position.ToVector());
        Assert.Equal("people.spawn takes a COUNT of 1 to 16.", people.Spawn("454", 17).Message);

        // They stay their character under people.splat off, until spawn off.
        Assert.True(people.Splat("off").Succeeded);
        Assert.Same(character, people.Show(100, 10, 1.7F, walking: false));
        Assert.Equal("3 spawned walkers draw as the rest do again.", people.Spawn("off", 0).Message);
        Assert.Null(people.Show(100, 10, 1.7F, walking: false));
    }

    private static DaggerfallPeopleSwitch Switch(DaggerfallArtReplacements art, PartsRigsRecorder rigs, AnimationRecorder animation)
    {
        ActorsState actors = new();
        return new(art.PeopleRig, art.SplatPeople, () => rigs.Service, () => animation.Service, () => new ContentFake([]), WalkerSpeed,
            DaggerfallSplatPeopleTuning.Default, actors, Stage(actors));
    }

    /// <summary>Staging on flat ground at the player's feet, where any spot is clear but one a staged walker already stands on.</summary>
    private static DaggerfallPeopleStage Stage(ActorsState actors) => new(actors, _ => true, new WorldRpg.Kit.Controls.PlayerControlState(new WorldPoint(0F, 0F, 0F), 0F, 0F),
        (at, moving) => actors.All.Any(actor => !moving.Contains(actor.DurableId) && actor.Wander.Route is not null
            && System.Numerics.Vector3.Distance(actor.Position.ToVector(), at) < 1F) ? null : WorldPoint.From(at));

    /// <summary>The art set's people rig and gsplat townspeople alone: the rest of the refreshed set has no bearing on the switch.</summary>
    internal static DaggerfallArtReplacements Art()
    {
        using JsonDocument payload = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/packs/daggerfall.art-replacements/payload.json")));
        JsonElement section = payload.RootElement.GetProperty("artReplacements");
        return DaggerfallArtReplacements.Read(System.Text.Encoding.UTF8.GetBytes(
            $"{{\"artReplacements\": {{\"images\": [], \"peopleRig\": {section.GetProperty("peopleRig").GetRawText()}, \"splatPeople\": {section.GetProperty("splatPeople").GetRawText()}}}}}"),
            "daggerfall.art-replacements");
    }
}
