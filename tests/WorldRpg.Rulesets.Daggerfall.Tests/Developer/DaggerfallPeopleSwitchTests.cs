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
        Assert.Equal("Walking people draw their sprites.", people.Rig("off").Message);
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
        animation.Clips = Clips(Art().SplatPeople!);
        DaggerfallArtReplacements art = Art();
        DaggerfallSplatPeople splats = art.SplatPeople!;
        using DaggerfallPeopleSwitch people = Switch(art, PartsRigsRecorder.Create(), animation);
        // His plain splat body, in the plain look (the hybrid body has a test of its own).
        Assert.True(people.SplatHybrid("off").Succeeded);

        var unknown = people.Splat("999");
        Assert.False(unknown.Succeeded);
        Assert.Equal($"The art set has no gsplat character '999'; it has {string.Join(", ", splats.Characters.Keys.Order(StringComparer.Ordinal))}, own or off.", unknown.Message);
        Assert.Null(people.Crowd);

        DaggerfallSplatPerson townsman = splats.Characters["385"];
        var drawn = people.Splat("385");
        Assert.True(drawn.Succeeded);
        Assert.Equal($"Walking people draw as gsplat 385 ({townsman.Name}), lit; 0 walk here now.", drawn.Message);
        SplatFigure figure = Assert.IsType<SplatCrowd>(people.Crowd).Figure!;
        // He walks the art set's walks and stands in its idles, crossfading between them.
        Assert.Equal(splats.Walks, figure.Walks.Select(walk => walk.Clip));
        Assert.Equal(splats.Idles, figure.Idles);
        Assert.Equal(.25F, figure.CrossfadeSeconds);
        // Each walk's stride keeps pace with a townsperson: its fitted clip plays at their speed over its own.
        Assert.All(figure.Walks, walk => Assert.Equal((1F, WalkerSpeed / townsman.Fits[walk.Clip].Speed!.Value), (walk.Pace, walk.Rate)));

        // The clip packs open once each, and the body binds them all by joint names under their producer and licence.
        string[] played = [.. splats.Walks.Concat(splats.Idles)];
        Assert.Equal(played.Select(clip => splats.Clips[clip].Path), animation.OpenedPackPaths);
        Assert.Equal([townsman.Body.Path], animation.OpenedPaths);
        Assert.Equal(played.Select((_, at) => (9001UL, 9501UL + (ulong)at, splats.Producer, splats.License)), animation.JointNameBindings);
        // Each clip's pelvis is fitted to his legs; a walk keeps none of its travel forward, since the walker moves himself.
        Assert.Equal(played.Select(clip => (clip, splats.Joint, townsman.Fits[clip] is { Speed: null } idle ? new System.Numerics.Vector3(idle.Scale)
                : new System.Numerics.Vector3(townsman.Fits[clip].Scale, townsman.Fits[clip].Scale, 0), new System.Numerics.Vector3(0, townsman.Fits[clip].Lift, 0))),
            animation.Fits.Select(fit => (fit.Clip, fit.Joint, fit.Scale, fit.Offset)));
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
        Assert.True(people.SplatShadow("core").Succeeded);
        Assert.Equal((SplatShadowCaster.Core, 3U), (animation.Splats[^1].ShadowCaster, animation.Splats[^1].PixelGrid));
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
        Assert.Equal(splats.Walks.Count + splats.Idles.Count, animation.OpenedPackPaths.Count);

        Assert.True(people.Splat("off").Succeeded);
        Assert.Null(people.Crowd);
        people.Dispose();
        Assert.Equal(1, animation.ReleasedAppearances);
    }

    [Fact]
    public void A_character_with_a_hybrid_body_draws_it_in_the_hybrid_look_until_people_splat_hybrid_off()
    {
        const string HybridPath = "worldrpg/packs/daggerfall.art-replacements/media/splat-people/hybrid/385.glb";
        AnimationRecorder animation = AnimationRecorder.Create();
        DaggerfallArtReplacements art = Art(characters => characters["385"]!["hybrid"] = new System.Text.Json.Nodes.JsonObject
        {
            ["path"] = HybridPath, ["byteLength"] = 1, ["sha256"] = new string('a', 64),
        });
        animation.Clips = Clips(art.SplatPeople!);
        DaggerfallSplatPerson townsman = art.SplatPeople!.Characters["385"];
        Assert.Equal(HybridPath, townsman.Hybrid!.Path);
        using DaggerfallPeopleSwitch people = Switch(art, PartsRigsRecorder.Create(), animation);

        // The tuning draws hybrid bodies: his splats over the base mesh that fills their holes and casts the shadow, smooth,
        // without a light floor, drawing over the mesh by the surface and close bias, his Gaussians' material read.
        Assert.True(people.Splat("385").Succeeded);
        SplatFigure hybrid = Assert.IsType<SplatCrowd>(people.Crowd).Figure!;
        Assert.Equal($"385 ({townsman.Name}, hybrid)", hybrid.Name);
        Assert.Equal([HybridPath], animation.OpenedPaths);
        SplatAppearanceRequest look = animation.Splats[^1];
        Assert.Same(hybrid.Appearance, look.Appearance);
        Assert.Equal((0U, 0F, SplatShadowCaster.None, .03F, 1.2F, 3F, .08F, true, .6F),
            (look.PixelGrid, look.LightFloor, look.ShadowCaster, look.SurfaceBias, look.CloseNear, look.CloseFar, look.CloseBias, look.Material, look.DiffuseWrap));

        // people.splat-hybrid off draws his plain splat body in the plain look, wrapped as the hybrid is.
        var plain = people.SplatHybrid("off");
        Assert.True(plain.Succeeded);
        Assert.Equal("Gsplat people draw their plain splat bodies.", plain.Message);
        SplatFigure splats = Assert.IsType<SplatCrowd>(people.Crowd).Figure!;
        Assert.NotSame(hybrid, splats);
        Assert.Equal([HybridPath, townsman.Body.Path], animation.OpenedPaths);
        look = animation.Splats[^1];
        Assert.Same(splats.Appearance, look.Appearance);
        Assert.Equal((3U, .15F, SplatShadowCaster.Ellipsoids, 0F, 0F, false, .6F),
            (look.PixelGrid, look.LightFloor, look.ShadowCaster, look.SurfaceBias, look.CloseBias, look.Material, look.DiffuseWrap));

        // The look commands change both bodies' looks for the run: a grid reaches the hybrid too.
        Assert.True(people.SplatLook(5, .5, "none", 0).Succeeded);
        Assert.Equal(5U, animation.Splats[^1].PixelGrid);
        Assert.Equal(5U, animation.Splats.Last(request => request.Appearance == hybrid.Appearance).PixelGrid);
        Assert.True(people.SplatHybrid("on").Succeeded);
        Assert.Same(hybrid, Assert.IsType<SplatCrowd>(people.Crowd).Figure);
        Assert.Equal(2, animation.OpenedPaths.Count);
        Assert.False(people.SplatHybrid("maybe").Succeeded);
    }

    [Fact]
    public void A_character_whose_clips_did_not_bind_is_refused_and_released()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = [new("walk", "walk", 1.03F, true, 1)];
        using DaggerfallPeopleSwitch people = Switch(Art(), PartsRigsRecorder.Create(), animation);
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => people.Splat("385"));
        Assert.Equal("Gsplat character 385 carries no clip 'stroll' after its clip packs were bound; it has walk.", refused.Message);
        Assert.Null(people.Crowd);
        Assert.Empty(animation.Created);
    }

    [Fact]
    public void An_art_set_without_a_people_rig_or_gsplat_people_refuses_them()
    {
        using ActorsState actors = new();
        List<string> warnings = [];
        using DaggerfallPeopleSwitch people = new(null, null, null, () => PartsRigsRecorder.Create().Service, () => AnimationRecorder.Create().Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [385], _ => 385, _ => 0, () => true, warnings.Add);
        // Walkers draw their sprites, and nothing is said: there are no characters to miss.
        Assert.Null(people.Show(100, 10, 1.7F, walking: true));
        Assert.Null(people.Crowd);
        Assert.Empty(warnings);
        var refused = people.Rig("4");
        Assert.False(refused.Succeeded);
        Assert.Equal("The art set carries no people rig.", refused.Message);
        var noSplats = people.Splat("385");
        Assert.False(noSplats.Succeeded);
        Assert.Equal("The art set carries no gsplat townspeople.", noSplats.Message);
        Assert.True(people.Rig("off").Succeeded);
        Assert.True(people.Splat("off").Succeeded);
        Assert.Equal("The art set carries no gsplat townspeople.", people.Splat("own").Message);
        Assert.Equal("The art set carries no gsplat townspeople.", people.Spawn("385", 1).Message);
    }

    [Fact]
    public void Spawn_stands_the_nearest_walkers_ahead_as_their_own_character_whatever_the_rest_draw_as()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(Art().SplatPeople!);
        using ActorsState actors = new();
        actors.CreatePlayer(1, new Rusty.Engine.Entities.EntityTypeId("player"), new Rusty.Engine.Mechanics.StatsComponent(), "health");
        foreach ((long id, float distance) in new[] { (10L, 12F), (11L, 15F), (12L, 20F) })
        {
            ActorState civilian = actors.CreateActor(id, new Rusty.Engine.Entities.EntityTypeId(DaggerfallActorKinds.Civilian),
                new Rusty.Engine.Mechanics.StatsComponent(), new ActorPose(new WorldPoint(distance, 0F, 0F), 0F), "health");
            civilian.Stats.AddTrack(Rusty.Engine.Mechanics.TrackId.Parse("health"), new Rusty.Engine.Mechanics.Track(new Rusty.Engine.Mechanics.Stat(1), 1));
            civilian.Actor.Add(new WorldRpg.Kit.Ai.WanderMemoryComponent());
        }
        // These walkers wear no townsperson art, so the crowd draws them as their sprites.
        using DaggerfallPeopleSwitch people = new(null, Art().SplatPeople, Shipped(), () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [], _ => null, _ => 0, () => true, _ => { });

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

        // They stay their character under people.splat off, which keeps it open for them, until spawn off.
        Assert.True(people.Splat("off").Succeeded);
        Assert.Same(character, people.Show(100, 10, 1.7F, walking: false));
        people.Publishing();
        people.Settle();
        Assert.Equal(0, animation.ReleasedAppearances);
        Assert.Same(character, people.Show(100, 10, 1.7F, walking: false));
        Assert.Equal("3 spawned walkers draw as the rest do again.", people.Spawn("off", 0).Message);
        Assert.Null(people.Show(100, 10, 1.7F, walking: false));
    }

    [Fact]
    public void Each_walker_draws_by_default_as_the_character_of_its_townsperson_art_and_off_draws_their_sprites()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(Art().SplatPeople!);
        DaggerfallSplatPeople splats = Art().SplatPeople!;
        List<string> warnings = [];
        bool refreshed = true;
        // Walkers 10 and 14 wear townsperson art 385, 11 wears 386, 12 wears art without a character, 13 none at all.
        Dictionary<long, int?> worn = new() { [10] = 385, [11] = 386, [12] = 999, [13] = null, [14] = 385 };
        using ActorsState actors = new();
        using DaggerfallPeopleSwitch people = new(null, splats, Shipped(), () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [385, 386, 999, 385], id => worn[id],
            _ => 0, () => refreshed, warnings.Add);

        // Nothing opens before a walker is drawn.
        Assert.Null(people.Crowd);
        Assert.Empty(animation.OpenedPaths);

        Appearance townsman = people.Show(100, 10, 1.7F, walking: true)!;
        Appearance other = people.Show(101, 11, 1.7F, walking: false)!;
        Assert.Same(townsman, people.Show(104, 14, 1.7F, walking: true));
        Assert.NotSame(townsman, other);
        // Each character opens once, the one of the walker's art: 386 is drawn by 386-C.
        // Each draws its hybrid body, which the art set gives every townsperson.
        Assert.Equal([splats.Characters["385"].Hybrid!.Path, splats.Characters["386-C"].Hybrid!.Path], animation.OpenedPaths);
        Assert.Equal([townsman, other], animation.Splats.Select(look => look.Appearance));
        Assert.IsType<SplatCrowd>(people.Crowd);

        // Art without a character, and a walker wearing none, draw their sprites, each said once.
        Assert.Null(people.Show(102, 12, 1.7F, walking: true));
        Assert.Null(people.Show(103, 13, 1.7F, walking: true));
        Assert.Null(people.Show(103, 13, 1.7F, walking: true));
        Assert.Equal(["Townsperson art 999 has no gsplat character; walkers wearing it draw their sprites.",
            "A walker wears no classic townsperson art (its race has none), so it draws its sprite."], warnings);

        // While the classic art draws, so do the walkers.
        refreshed = false;
        Assert.Null(people.Show(100, 10, 1.7F, walking: true));
        refreshed = true;

        // people.splat ID draws them all as one character, and own as their own again.
        Assert.True(people.Splat("454").Succeeded);
        Appearance one = animation.Splats[^1].Appearance;
        Assert.Same(one, people.Show(101, 11, 1.7F, walking: false));
        Assert.Equal("Walking people draw as the gsplat characters of their townsperson art, lit; 0 walk here now.", people.Splat("own").Message);
        Assert.Same(other, people.Show(101, 11, 1.7F, walking: false));

        // Off draws their sprites, and the rig takes over from them too, until own.
        Assert.True(people.Splat("off").Succeeded);
        Assert.Null(people.Crowd);
        Assert.Null(people.Show(100, 10, 1.7F, walking: true));
        Assert.True(people.Splat("own").Succeeded);
        Assert.Same(townsman, people.Show(100, 10, 1.7F, walking: true));
        Assert.Equal(3, animation.OpenedPaths.Count);
    }

    [Fact]
    public void Staged_walkers_keep_drawing_as_the_character_of_their_own_townsperson_art()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(Art().SplatPeople!);
        DaggerfallSplatPeople splats = Art().SplatPeople!;
        using ActorsState actors = new();
        actors.CreatePlayer(1, new Rusty.Engine.Entities.EntityTypeId("player"), new Rusty.Engine.Mechanics.StatsComponent(), "health");
        foreach ((long id, float distance) in new[] { (10L, 12F), (11L, 15F) })
        {
            ActorState civilian = actors.CreateActor(id, new Rusty.Engine.Entities.EntityTypeId(DaggerfallActorKinds.Civilian),
                new Rusty.Engine.Mechanics.StatsComponent(), new ActorPose(new WorldPoint(distance, 0F, 0F), 0F), "health");
            civilian.Stats.AddTrack(Rusty.Engine.Mechanics.TrackId.Parse("health"), new Rusty.Engine.Mechanics.Track(new Rusty.Engine.Mechanics.Stat(1), 1));
            civilian.Actor.Add(new WorldRpg.Kit.Ai.WanderMemoryComponent());
        }
        Dictionary<long, int?> worn = new() { [10] = 381, [11] = 395 };
        DaggerfallPeopleStage stage = Stage(actors);
        using DaggerfallPeopleSwitch people = new(null, splats, Shipped(), () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, stage, [381, 395], id => worn[id], _ => 0, () => true, _ => { });
        Appearance townsman = people.Show(100, 10, 1.7F, walking: true)!, townswoman = people.Show(101, 11, 1.7F, walking: true)!;

        // Staging moves them in front of the player; it never changes what they wear.
        Assert.True(stage.Stage(2, 4, 2).Succeeded);
        Assert.Same(townsman, people.Show(100, 10, 1.7F, walking: false));
        Assert.Same(townswoman, people.Show(101, 11, 1.7F, walking: false));
        Assert.True(stage.Cross(2, 4, 3).Succeeded);
        Assert.Same(townsman, people.Show(100, 10, 1.7F, walking: true));
        Assert.Equal([splats.Characters[splats.ByArchive[381]].Hybrid!.Path, splats.Characters[splats.ByArchive[395]].Hybrid!.Path], animation.OpenedPaths);
    }

    [Fact]
    public void A_session_that_yields_to_its_replacement_lets_go_of_its_walkers_and_characters_and_draws_sprites()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(Art().SplatPeople!);
        using ActorsState actors = new();
        using DaggerfallPeopleSwitch people = new(null, Art().SplatPeople, Shipped(), () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [385], _ => 385, _ => 0, () => true, _ => { });
        Assert.NotNull(people.Show(100, 10, 1.7F, walking: true));
        people.Publishing();
        people.Settle();
        Assert.Equal(1, animation.Instances);

        // The replacement publishes the same walkers' objects, so the next snapshot draws their sprites and lets their
        // instances go before it is published; the figure, whose body the replacement binds again, goes after it.
        people.Yield();
        Assert.Null(people.Show(100, 10, 1.7F, walking: true));
        people.Publishing();
        Assert.Equal(1, animation.ReleasedInstances);
        Assert.Equal(0, animation.ReleasedAppearances);
        people.Settle();
        people.Dispose();
        Assert.Equal(1, animation.ReleasedAppearances);
        Assert.Null(people.Show(100, 10, 1.7F, walking: true));
        Assert.Equal(1, animation.Instances);
    }

    [Fact]
    public void Off_lets_go_of_the_characters_once_the_snapshot_without_them_is_accepted_so_sprites_cost_what_sprites_cost()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(Art().SplatPeople!);
        Dictionary<long, int?> worn = new() { [10] = 385, [11] = 386 };
        using ActorsState actors = new();
        using DaggerfallPeopleSwitch people = new(null, Art().SplatPeople, Shipped(), () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [385, 386], id => worn[id], _ => 0, () => true, _ => { });
        Assert.NotNull(people.Show(100, 10, 1.7F, walking: true));
        Assert.NotNull(people.Show(101, 11, 1.7F, walking: true));
        people.Publishing();
        people.Settle();

        // The snapshot after off draws their sprites and lets their instances go before it is published; the characters
        // it no longer draws go once it is accepted, since the Engine refuses to destroy an appearance a snapshot draws.
        Assert.True(people.Splat("off").Succeeded);
        Assert.Null(people.Show(100, 10, 1.7F, walking: true));
        Assert.Null(people.Show(101, 11, 1.7F, walking: true));
        people.Publishing();
        Assert.Equal((2, 0), (animation.ReleasedInstances, animation.ReleasedAppearances));
        people.Settle();
        Assert.Equal(2, animation.ReleasedAppearances);

        // Own opens them, and their clips, again.
        Assert.True(people.Splat("own").Succeeded);
        Assert.NotNull(people.Show(100, 10, 1.7F, walking: true));
        Assert.Equal(3, animation.OpenedPaths.Count);
        DaggerfallSplatPeople splats = Art().SplatPeople!;
        Assert.Equal(2 * (splats.Walks.Count + splats.Idles.Count), animation.OpenedPackPaths.Count);
    }

    [Fact]
    public void A_character_that_will_not_open_leaves_its_walkers_their_sprites_said_once()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = [new("walk", "walk", 1.03F, true, 1)];
        List<string> warnings = [];
        using ActorsState actors = new();
        using DaggerfallPeopleSwitch people = new(null, Art().SplatPeople, Shipped(), () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [385], _ => 385, _ => 0, () => true, warnings.Add);
        Assert.Null(people.Show(100, 10, 1.7F, walking: true));
        Assert.Null(people.Show(101, 11, 1.7F, walking: true));
        Assert.Equal(["Gsplat character 385 would not open, so walkers wearing townsperson art 385 draw their sprites: " +
            "Gsplat character 385 carries no clip 'stroll' after its clip packs were bound; it has walk."], warnings);
        Assert.Single(animation.OpenedPaths);
        Assert.Empty(animation.Created);
    }

    [Fact]
    public void Without_the_animations_container_walkers_draw_their_sprites_and_the_gsplat_switches_say_why()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        List<string> warnings = [];
        using ActorsState actors = new();
        using DaggerfallPeopleSwitch people = new(null, Art().SplatPeople, null, () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [385], _ => 385, _ => 0, () => true, warnings.Add);
        // The session says once that the container is missing; the walkers draw their sprites, and nothing is opened.
        Assert.Null(people.Show(100, 10, 1.7F, walking: true));
        Assert.Null(people.Crowd);
        Assert.Empty(animation.OpenedPaths);
        Assert.Empty(warnings);
        const string missing = "animations.rpak is not installed, so the gsplat townspeople have no clips.";
        Assert.Equal(missing, people.Splat("385").Message);
        Assert.Equal(missing, people.Splat("own").Message);
        Assert.Equal(missing, people.Spawn("385", 1).Message);
        Assert.True(people.Splat("off").Succeeded);
    }

    [Fact]
    public void A_container_holding_another_build_of_a_clip_leaves_walkers_their_sprites_said_once()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(Art().SplatPeople!);
        List<string> warnings = [];
        using ActorsState actors = new();
        using DaggerfallPeopleSwitch people = new(null, Art().SplatPeople, Shipped(other: "stroll"), () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [385], _ => 385, _ => 0, () => true, warnings.Add);
        Assert.Null(people.Show(100, 10, 1.7F, walking: true));
        Assert.Null(people.Show(101, 11, 1.7F, walking: true));
        Assert.Equal(["Gsplat character 385 would not open, so walkers wearing townsperson art 385 draw their sprites: animations.rpak holds another build of " +
            "splat-people/stroll.glb than the art pack names; pull the container this checkout's payload was written with (scripts/data-sync.sh pull)."], warnings);
        Assert.Empty(animation.Created);
    }

    [Fact]
    public void Every_classic_townsperson_has_one_character_in_the_art_set()
    {
        DaggerfallSplatPeople splats = Art().SplatPeople!;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        // Classic Daggerfall dresses a Breton, Redguard or Nord walker in one of four archives a gender.
        DaggerfallRaceDefinition[] towns = [.. definitions.Catalogs.Races.Where(race => race.Townspeople.Count != 0)];
        Assert.Equal(["breton", "redguard", "nord"], towns.Select(race => race.Id));
        int[] archives = [.. towns.SelectMany(race => race.Townspeople.Values.SelectMany(worn => worn))];
        Assert.All(towns.SelectMany(race => race.Townspeople.Values), worn => Assert.Equal(4, worn.Count));
        Assert.Equal(24, archives.Distinct().Count());
        // Each has its character, and no character draws art nobody wears.
        Assert.Equal(archives.Order(), splats.ByArchive.Keys.Order());
        Assert.Equal("386-C", splats.ByArchive[386]);
        Assert.Equal("397-B", splats.ByArchive[397]);
        // A walker wears the one its seed picks.
        DaggerfallRaceDefinition breton = towns[0];
        Assert.Equal([385, 386, 391, 394, 385], new[] { 0, 1, 2, 3, 4 }.Select(seed => breton.Townsperson("Male", seed)));
        Assert.Equal(456, breton.Townsperson("Female", 65535));
        Assert.Null(definitions.Catalogs.Races.Single(race => race.Id == "khajiit").Townsperson("Male", 0));
    }

    [Fact]
    public void Every_character_in_the_art_set_has_its_eyes_marked_so_every_walker_blinks()
    {
        DaggerfallSplatPeople splats = Art().SplatPeople!;
        Assert.All(splats.Characters.Where(character => character.Value.Archive is not null),
            character => Assert.True(character.Value.Eyes is { Group: 1, Lid.A: 1F }, $"{character.Key} has eyes {character.Value.Eyes}."));
    }

    [Fact]
    public void The_art_set_states_the_six_long_skirts_on_their_joints_each_walking_the_skirt_walks()
    {
        DaggerfallSplatPeople splats = Art().SplatPeople!;
        Assert.Equal(["gentle", "stroll-gentle"], splats.SkirtWalks);
        Assert.Equal(["395", "397-B", "398", "452-B", "453", "456"], splats.Characters.Where(person => person.Value.Skirt is not null).Select(person => person.Key).Order(StringComparer.Ordinal));
        // 395's wrap hangs on one tier, a ring of 8; the rest on two, a 16 x 7 sheet with a column of joints on every other.
        SplatRingSkirt ring = Assert.IsType<SplatRingSkirt>(splats.Characters["395"].Skirt);
        Assert.Equal((8, 16), (ring.Joints.Count, ring.Heights.Count));
        foreach (string id in new[] { "397-B", "398", "452-B", "453", "456" })
        {
            SplatClothSkirt cloth = Assert.IsType<SplatClothSkirt>(splats.Characters[id].Skirt);
            Assert.Equal((16, 7, 112, false), (cloth.Columns, cloth.Rows, cloth.Grid.Count, cloth.Slit));
            Assert.Equal([0, 2, 4, 6, 8, 10, 12, 14], cloth.BoneColumns);
            Assert.Equal(("Skirt0_U", "Skirt7_L", "mixamorig:Hips", "mixamorig:LeftUpLeg"), (cloth.Upper[0], cloth.Lower[^1], cloth.Hips, cloth.Legs.LeftUpLeg));
        }
        // Their walks are softer, shorter in the stride and slower than the walkers' pace.
        Assert.All(splats.Characters.Values.Where(person => person.Skirt is not null), person => Assert.All(splats.SkirtWalks, clip =>
            Assert.True(person.Fits[clip] is { Scale: < .7F, Speed: < WalkerSpeed }, $"{person.Name} walks {clip} at {person.Fits[clip]}.")));
    }

    [Fact]
    public void Every_walker_picks_from_a_few_mild_walks_and_idles_each_fitted_to_every_character_that_plays_it()
    {
        DaggerfallSplatPeople splats = Art().SplatPeople!;
        Assert.Equal(["walk", "stroll", "stroll-easy"], splats.Walks);
        Assert.Equal(["idle", "idle-relaxed", "idle-crossed", "idle-hips"], splats.Idles);
        foreach ((string id, DaggerfallSplatPerson person) in splats.Characters)
        {
            // Each walk it walks has its stride's fit and travels near the walkers' pace, so its rate stays mild; each idle
            // is fitted by the leg and travels nowhere.
            Assert.All(splats.WalksOf(person), clip => Assert.True(person.Fits[clip] is { Scale: > .5F and < 1.2F, Speed: > 1F and < 1.6F },
                $"{id} walks {clip} at {person.Fits[clip]}."));
            Assert.All(splats.Idles, clip => Assert.True(person.Fits[clip] is { Scale: > .5F and < 1.2F, Speed: null }, $"{id} stands {clip} at {person.Fits[clip]}."));
            // The easy stroll is the stroll softened: shorter in the stride and slower.
            if (person.Skirt is null)
                Assert.True(person.Fits["stroll-easy"].Speed < person.Fits["stroll"].Speed, id);
        }
    }

    [Fact]
    public void Each_walker_drawn_as_a_character_walks_and_stands_as_its_seed_picks_and_goes_at_its_gait_s_pace()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        DaggerfallSplatPeople splats = Art().SplatPeople!;
        animation.Clips = Clips(splats);
        using ActorsState actors = new();
        actors.CreatePlayer(1, new Rusty.Engine.Entities.EntityTypeId("player"), new Rusty.Engine.Mechanics.StatsComponent(), "health");
        Dictionary<long, int> seeds = new() { [10] = 101, [11] = 202, [12] = 303, [13] = 404 };
        foreach (long id in seeds.Keys)
        {
            ActorState civilian = actors.CreateActor(id, new Rusty.Engine.Entities.EntityTypeId(DaggerfallActorKinds.Civilian),
                new Rusty.Engine.Mechanics.StatsComponent(), new ActorPose(new WorldPoint(12F + id, 0F, 0F), 0F), "health");
            civilian.Stats.AddTrack(Rusty.Engine.Mechanics.TrackId.Parse("health"), new Rusty.Engine.Mechanics.Track(new Rusty.Engine.Mechanics.Stat(1), 1));
            civilian.Actor.Add(new WorldRpg.Kit.Ai.WanderMemoryComponent());
        }
        using DaggerfallPeopleSwitch people = new(null, splats, Shipped(), () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [385], _ => 385, id => seeds[id], () => true, _ => { });

        foreach (long id in seeds.Keys) Assert.NotNull(people.Show((ulong)(100 + id), id, 1.7F, walking: true));
        SplatCrowd crowd = Assert.IsType<SplatCrowd>(people.Crowd);
        SplatFigure figure = crowd.Own!(10)!;
        foreach ((long id, int seed) in seeds)
        {
            // The gait its name seed draws, by the tuning's jitter; it walks at the pace that gait's stride keeps.
            SplatGait gait = crowd.Gait((ulong)(100 + id))!.Value;
            Assert.Equal(figure.Gait(seed, DaggerfallSplatPeopleTuning.Default.GaitJitter), gait);
            Assert.Equal(gait.Pace, actors.Get(id).Wander.Pace, 5);
            Assert.NotEqual(1F, gait.Pace);
        }
        Assert.True(seeds.Keys.Select(id => crowd.Gait((ulong)(100 + id))!.Value.Walk.Clip).Distinct().Count() > 1);
    }

    [Fact]
    public void A_character_in_a_long_skirt_walks_the_gentle_clip_with_its_skirt_on_its_rig_at_the_pace_the_tuning_gives()
    {
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(Art().SplatPeople!);
        DaggerfallSplatPeople splats = Art().SplatPeople!;
        DaggerfallSplatPerson person = splats.Characters["456"];
        SplatClothSkirt skirt = Assert.IsType<SplatClothSkirt>(person.Skirt);
        // Its rig: the hips, the legs and each column's two skirt joints, standing where the sheet's rest puts them.
        (string Name, System.Numerics.Vector3 At)[] rig = [(skirt.Hips, new(0, 1, 0)), .. skirt.Legs.Names.Select(name => (name, System.Numerics.Vector3.Zero)),
            .. skirt.BoneColumns.SelectMany((column, i) => new[] { (skirt.Upper[i], skirt.Grid[column * skirt.Rows]), (skirt.Lower[i], skirt.Grid[column * skirt.Rows + skirt.KneeRow]) })];
        animation.Joints = [.. rig.Select(joint => new AnimationJointInfo(joint.Name, 0))];
        animation.Rest = [.. rig.Select(joint => joint.At)];
        using ActorsState actors = new();
        actors.CreatePlayer(1, new Rusty.Engine.Entities.EntityTypeId("player"), new Rusty.Engine.Mechanics.StatsComponent(), "health");
        ActorState walker = actors.CreateActor(10, new Rusty.Engine.Entities.EntityTypeId(DaggerfallActorKinds.Civilian),
            new Rusty.Engine.Mechanics.StatsComponent(), new ActorPose(new WorldPoint(12F, 0F, 0F), 0F), "health");
        walker.Stats.AddTrack(Rusty.Engine.Mechanics.TrackId.Parse("health"), new Rusty.Engine.Mechanics.Track(new Rusty.Engine.Mechanics.Stat(1), 1));
        walker.Actor.Add(new WorldRpg.Kit.Ai.WanderMemoryComponent());
        using DaggerfallPeopleSwitch people = new(null, splats, Shipped(), () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [], _ => null, _ => 0, () => true, _ => { });

        Assert.True(people.Splat("456").Succeeded);
        SplatFigure figure = Assert.IsType<SplatCrowd>(people.Crowd).Figure!;
        // She binds the skirt walks in place of the walks, beside the idles, fitted to her legs, and by the tuning walks each as authored.
        Assert.Equal(splats.SkirtWalks.Concat(splats.Idles).Select(clip => splats.Clips[clip].Path), animation.OpenedPackPaths);
        Assert.Equal(6, animation.JointNameBindings.Count);
        AnimationClipTranslationFitRequest fit = Assert.Single(animation.Fits, fit => fit.Clip == "gentle");
        Assert.Equal((new System.Numerics.Vector3(person.Fits["gentle"].Scale, person.Fits["gentle"].Scale, 0), new System.Numerics.Vector3(0, person.Fits["gentle"].Lift, 0)),
            (fit.Scale, fit.Offset));
        Assert.Equal(splats.SkirtWalks, figure.Walks.Select(walk => walk.Clip));
        Assert.All(figure.Walks, walk =>
        {
            Assert.Equal(person.Fits[walk.Clip].Speed!.Value / WalkerSpeed, walk.Pace, 5);
            Assert.Equal(1F, walk.Rate, 5);
        });
        Assert.Same(skirt, figure.Skirt!.Skirt);
        // A skirt drives from where it was bound: the first step at rest places each joint where it stands.
        JointOverride[] placed = figure.Skirt.Sheet().Step(0, animation.ModelPose(new(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.One)).Span);
        Assert.Equal(16, placed.Length);
        Assert.All(placed, joint => Assert.True(System.Numerics.Vector3.Distance(animation.Rest[(int)joint.Joint], joint.Translation) < 1e-4F));

        // A walker drawn as her goes at her walk's own pace, slower than the rest, by its gait's rate.
        Assert.Same(figure.Appearance, people.Show(100, walker.DurableId, 1.7F, walking: true));
        SplatGait gait = Assert.IsType<SplatCrowd>(people.Crowd).Gait(100)!.Value;
        float own = person.Fits[gait.Walk.Clip].Speed!.Value / WalkerSpeed;
        Assert.Equal(own * gait.Rate, walker.Wander.Pace, 5);

        // people.splat-pace clip plays her walk faster to keep the walkers' pace, for the run, wherever she is drawn.
        Assert.Equal("people.splat-pace takes slow or clip.", people.SplatPace("fast").Message);
        Assert.True(people.Spawn("456", 1).Succeeded);
        Assert.True(people.SplatPace("clip").Succeeded);
        SplatCrowd crowd = Assert.IsType<SplatCrowd>(people.Crowd);
        Assert.All(crowd.Figure!.Walks, walk =>
        {
            Assert.Equal(1F, walk.Pace);
            Assert.Equal(WalkerSpeed / person.Fits[walk.Clip].Speed!.Value, walk.Rate, 5);
        });
        Assert.Same(crowd.Figure, crowd.Assigned[walker.DurableId]);
        people.Show(100, walker.DurableId, 1.7F, walking: true);
        Assert.Equal(gait.Rate, walker.Wander.Pace, 5);
        Assert.True(people.SplatPace("slow").Succeeded);
        people.Show(100, walker.DurableId, 1.7F, walking: true);
        Assert.Equal(own * gait.Rate, walker.Wander.Pace, 5);

        // Drawn as her sprite again, she walks at the walkers' pace; her character stays open for the spawned walker.
        Assert.True(people.Splat("off").Succeeded);
        Assert.True(people.Spawn("off", 0).Succeeded);
        Assert.Null(people.Show(100, walker.DurableId, 1.7F, walking: true));
        Assert.Equal(1F, walker.Wander.Pace);
    }

    [Fact]
    public void Walkers_drawn_as_a_character_with_eyes_blink_on_their_own_clocks_until_splat_blinks_off_and_one_without_is_said_once()
    {
        // 385 has eyes marked; every other character none, whatever the shipped art carries.
        DaggerfallSplatPeople splats = Art(characters =>
        {
            foreach ((string _, System.Text.Json.Nodes.JsonNode? character) in characters) character!.AsObject().Remove("eyes");
            characters["385"]!["eyes"] = new System.Text.Json.Nodes.JsonObject { ["group"] = 1, ["lid"] = "#C08070" };
        }).SplatPeople!;
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(splats);
        List<string> warnings = [];
        // Walkers 10 and 14 wear 385's art, 11 wears 386's (drawn by 386-C, which has no eyes).
        Dictionary<long, int?> worn = new() { [10] = 385, [11] = 386, [14] = 385 };
        using ActorsState actors = new();
        using DaggerfallPeopleSwitch people = new(null, splats, Shipped(), () => PartsRigsRecorder.Create().Service, () => animation.Service,
            () => new ContentFake([]), WalkerSpeed, DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [385, 386], id => worn[id],
            actor => (int)actor, () => true, warnings.Add);

        // The lids are the marked sRGB colour as it is, which the Engine's group tint takes, not converted to linear light.
        Assert.Equal(new SplatEyes(1, new Color(0xC0 / 255F, 0x80 / 255F, 0x70 / 255F, 1F)), splats.Characters["385"].Eyes);
        Assert.Null(splats.Characters["386-C"].Eyes);

        // 20 seconds of 20 ms updates: each 385 walker blinks on its own clock, shutting fully, toward its lids; 386-C never.
        Run(1000);
        Assert.Equal([$"Gsplat characters {string.Join(", ", splats.Characters.Where(character => character.Value.Archive is not null && character.Key != "385").Select(character => character.Key).Order(StringComparer.Ordinal))} have no eyes marked, so walkers drawn as them don't blink."],
            warnings);
        ulong[] seeing = [.. animation.Tints.Select(tint => tint.Instance.Handle.Value).Distinct().Order()];
        Assert.Equal(2, seeing.Length);
        Assert.All(animation.Tints, tint => Assert.Equal((1, splats.Characters["385"].Eyes!.Lid), (tint.Group, tint.Target)));
        Assert.All(animation.Tints, tint => Assert.InRange(tint.Weight, 0F, 1F));
        foreach (ulong instance in seeing)
            Assert.InRange(animation.Tints.Count(tint => tint.Instance.Handle.Value == instance && tint.Weight == 1F), 3, 10);
        Assert.NotEqual(Weights(seeing[0]), Weights(seeing[1]));

        // people.splat-blinks off opens any shut lid and blinks no more; on blinks again; anything else is refused.
        Assert.Equal("people.splat-blinks takes on or off.", people.SplatBlinks("shut").Message);
        Assert.Equal("Gsplat people keep their eyes open.", people.SplatBlinks("off").Message);
        Run(1);
        Assert.All(seeing, instance => Assert.Equal(0F, animation.Tints.Last(tint => tint.Instance.Handle.Value == instance).Weight));
        int count = animation.Tints.Count;
        Run(500);
        Assert.Equal(count, animation.Tints.Count);
        Assert.Equal("Gsplat people with eyes blink every 2 to 6 seconds, each on its own clock.", people.SplatBlinks("on").Message);
        Run(500);
        Assert.Contains(animation.Tints.Skip(count), tint => tint.Weight == 1F);
        Assert.Single(warnings);

        void Run(int updates)
        {
            for (int update = 0; update < updates; update++)
            {
                people.Show(100, 10, 1.7F, walking: true);
                people.Show(101, 11, 1.7F, walking: true);
                people.Show(104, 14, 1.7F, walking: false);
                people.Advance(.02F);
                people.Publishing();
                people.Settle();
            }
        }

        float[] Weights(ulong instance) => [.. animation.Tints.Where(tint => tint.Instance.Handle.Value == instance).Select(tint => tint.Weight)];
    }

    [Fact]
    public void Splat_fade_updates_active_townspeople_and_refuses_invalid_ranges()
    {
        DaggerfallArtReplacements art = Art();
        PartsRigsRecorder rigs = PartsRigsRecorder.Create();
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(art.SplatPeople!);
        using DaggerfallPeopleSwitch people = Switch(art, rigs, animation);

        Assert.True(people.Splat("381").Succeeded);
        Assert.NotNull(people.Show(100, 10, 1.7F, walking: false));
        people.Publishing();
        people.Settle();

        Assert.True(people.SplatFade(4, 12, .02).Succeeded);
        Assert.Equal((4F, 12F, .02F), (animation.Splats[^1].FadeNear, animation.Splats[^1].FadeFar, animation.Splats[^1].SurfaceBias));
        Assert.True(people.SplatFade(0, 0, .03).Succeeded);
        Assert.Equal((0F, 0F, .03F), (animation.Splats[^1].FadeNear, animation.Splats[^1].FadeFar, animation.Splats[^1].SurfaceBias));
        Assert.False(people.SplatFade(10, 5, 0).Succeeded);
        Assert.False(people.SplatFade(-1, 5, 0).Succeeded);
        Assert.False(people.SplatFade(0, 0, .6).Succeeded);
    }

    [Theory]
    [InlineData(0, "#c08070")]
    [InlineData(256, "#c08070")]
    [InlineData(1, "c08070")]
    [InlineData(1, "#c0807")]
    [InlineData(1, "#c0807g")]
    public void Eyes_outside_a_group_of_1_to_255_or_with_lids_not_rrggbb_are_refused_naming_the_character(int group, string lid)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Art(characters =>
            characters["385"]!["eyes"] = new System.Text.Json.Nodes.JsonObject { ["group"] = group, ["lid"] = lid }));
        Assert.Equal($"Content pack 'daggerfall.art-replacements' gives splat person 385 eyes in group {group} with lids '{lid}'; eyes are a group of 1 to 255 with lids #rrggbb.", refused.Message);
    }

    private static DaggerfallPeopleSwitch Switch(DaggerfallArtReplacements art, PartsRigsRecorder rigs, AnimationRecorder animation)
    {
        ActorsState actors = new();
        return new(art.PeopleRig, art.SplatPeople, Shipped(), () => rigs.Service, () => animation.Service, () => new ContentFake([]), WalkerSpeed,
            DaggerfallSplatPeopleTuning.Default, actors, Stage(actors), [], _ => null, _ => 0, () => true, _ => { });
    }

    /// <summary>Staging on flat ground at the player's feet, where any spot is clear but one a staged walker already stands on.</summary>
    private static DaggerfallPeopleStage Stage(ActorsState actors) => new(actors, _ => true, new WorldRpg.Kit.Controls.PlayerControlState(new WorldPoint(0F, 0F, 0F), 0F, 0F),
        (at, moving) => actors.All.Any(actor => !moving.Contains(actor.DurableId) && actor.Wander.Route is not null
            && System.Numerics.Vector3.Distance(actor.Position.ToVector(), at) < 1F) ? null : WorldPoint.From(at));

    /// <summary>
    /// The animations container as the art set names its clips: each at its path with its digest. The recording animation
    /// service never reads a pack's bytes, so the container serves none.
    /// </summary>
    /// <param name="other">A clip the container holds another build of.</param>
    internal static DaggerfallAnimations Shipped(string? other = null)
    {
        BundleContentFake service = new();
        service.Container(DaggerfallAnimations.ContainerName);
        foreach ((string name, DaggerfallArtFile clip) in Art().SplatPeople!.Clips)
            service.AddEntry(DaggerfallAnimations.ContainerName, clip.Path, name == other ? default : clip.Sha256, 1,
                () => throw new InvalidOperationException("The recording animation service reads no clip pack's bytes."));
        return new(new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallAnimations.ContainerName));
    }

    /// <summary>Every clip the art set's packs carry, as the Engine reads them back from a body that bound them all, each named its own id.</summary>
    internal static AnimationClipInfo[] Clips(DaggerfallSplatPeople splats) => [.. splats.Clips.Keys.Select((clip, at) => new AnimationClipInfo(clip, clip, 3F, true, (uint)at + 1))];

    /// <summary>
    /// The art set's people rig and gsplat townspeople alone: the rest of the refreshed set has no bearing on the switch.
    /// <paramref name="characters"/> may change the gsplat characters as read, by id, first.
    /// </summary>
    internal static DaggerfallArtReplacements Art(Action<System.Text.Json.Nodes.JsonObject>? characters = null)
    {
        System.Text.Json.Nodes.JsonNode section = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot,
            "content/worldrpg/packs/daggerfall.art-replacements/payload.json")))!["artReplacements"]!;
        characters?.Invoke(section["splatPeople"]!["characters"]!.AsObject());
        return DaggerfallArtReplacements.Read(System.Text.Encoding.UTF8.GetBytes(
            $"{{\"artReplacements\": {{\"images\": [], \"peopleRig\": {section["peopleRig"]!.ToJsonString()}, \"splatPeople\": {section["splatPeople"]!.ToJsonString()}}}}}"),
            "daggerfall.art-replacements");
    }
}
