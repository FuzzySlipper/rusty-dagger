using System.Numerics;
using System.Text;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Hanging roots, chains, the cage and lanterns: the rig a hanging mesh names, how the site draws one from its sprite's top,
/// and how <see cref="DaggerfallHanging"/> wakes a piece when an actor walks near, pushes it with the actor's capsule,
/// releases it once left alone, stops for classic art, leaves one that cannot fit at rest, and carries a lantern's light.
/// </summary>
public sealed class DaggerfallHangingTests
{
    private const uint None = uint.MaxValue;
    private const string HashHex = "0000000000000001000000000000000200000000000000030000000000000004";

    // A root: anchor at the top of a 2 m model, two bodies, then its end at the foot.
    private static readonly AnimationJointInfo[] RootJoints = [new("anchor", None), new("root0", 0), new("root1", 1), new("end", 2)];
    private static readonly Vector3[] RootRest = [new(0, 2, 0), new(0, 1.9F, 0), new(0, 1, 0), new(0, 0, 0)];

    [Fact]
    public void A_rig_hangs_its_bodies_one_below_another_from_its_anchor()
    {
        DaggerfallHangingRig rig = DaggerfallHangingRig.Read(RootJoints, "213-8");

        Assert.Equal([new DaggerfallHangingBone(0, 1, true, false), new DaggerfallHangingBone(1, 2, false, false), new DaggerfallHangingBone(2, 3, false, true)], rig.Bones);
        Assert.Equal([(0, 1), (1, 2)], rig.Links);
        Assert.Null(rig.Light);
    }

    [Fact]
    public void Two_strands_hang_from_one_anchor_and_a_lanterns_light_is_never_a_body()
    {
        AnimationJointInfo[] joints =
        [
            new("anchor", None), new("a00", 0), new("aend", 1), new("end.a", 2), new("b00", 0), new("bend", 4), new("end.b", 5),
        ];
        DaggerfallHangingRig strands = DaggerfallHangingRig.Read(joints, "211-5");
        // The anchor with two strands below it reaches no single joint; each strand hangs from it.
        Assert.Equal(new DaggerfallHangingBone(0, null, true, false), strands.Bones[0]);
        Assert.Equal([(0, 1), (0, 2), (1, 3), (2, 4)], strands.Links);
        Assert.Equal([false, false, true, true], strands.Bones.Skip(1).Select(bone => bone.End));

        DaggerfallHangingRig lantern = DaggerfallHangingRig.Read(
            [new("anchor", None), new("s00", 0), new("lantern", 1), new("end.lantern", 2), new("light", 2)], "210-25");
        Assert.Equal(4U, lantern.Light);
        Assert.Equal(new DaggerfallHangingBone(2, 3, false, true), lantern.Bones[^1]);
    }

    [Fact]
    public void A_rig_that_names_no_anchor_or_branches_below_one_cannot_hang()
    {
        Assert.Throws<InvalidDataException>(() => DaggerfallHangingRig.Read([new("root0", None), new("end", 0)], "213-8"));
        Assert.Throws<InvalidDataException>(() => DaggerfallHangingRig.Read(
            [new("anchor", None), new("root0", 0), new("left", 1), new("right", 1)], "213-8"));
    }

    [Fact]
    public void The_art_pack_says_what_a_mesh_hangs_as_and_refuses_what_is_no_kind()
    {
        Assert.Equal(DaggerfallHangingKind.Root, Art().Meshes[(213, 8)].Hangs);
        Assert.Null(Art().Meshes[(205, 0)].Hangs);
        Assert.Throws<InvalidOperationException>(() => Art("vine"));
    }

    [Fact]
    public void A_hanging_mesh_hangs_from_its_sprites_top_at_its_sprites_height_turned_its_own_way()
    {
        Site site = Compose();
        using (site)
        {
            site.Appearance.Publish(site.Actors, new Dictionary<long, DaggerfallGroundContainer>());
            AppearanceFact hanging = Assert.Single(site.Graphics.Snapshots[^1], fact => fact.Appearance == site.Animation.Created[0]);
            // The 1.875 m sprite stands centred on its position, its top at 3 m: the 2 m model stands on the sprite's foot, scaled to
            // its height, so it hangs from that top.
            Assert.Equal(new Vector3(4, 3 - 1.875F, 6), hanging.Transform.Translation);
            Assert.Equal(new Vector3(1.875F / 2), hanging.Transform.Scale);
            Assert.NotEqual(Quaternion.Identity, hanging.Transform.Rotation);
        }
    }

    [Fact]
    public void An_actor_walking_near_wakes_a_piece_pushes_it_and_leaves_it_to_settle_and_rest()
    {
        Site site = Compose();
        using (site)
        {
            using DaggerfallHanging hanging = site.Hanging();
            DaggerfallHangingActor far = new(1, new Vector3(20, 0, 6), .25F, 1.8F);
            hanging.Step(.1F, [far], true);
            // Beneath it on the floor below, an actor cannot reach it.
            hanging.Step(.1F, [far with { Feet = new Vector3(5, -5, 6) }], true);
            Assert.Equal(0, site.Animation.Instances);
            Assert.Equal(0, site.Dynamics.Worlds);

            DaggerfallHangingActor near = far with { Feet = new Vector3(5, 0, 6) };
            hanging.Step(.1F, [near], true);
            // Fitted clear at rest, it is posed first, then hangs from that pose on the next call.
            Assert.Equal(1, site.Animation.Instances);
            Assert.Empty(site.Dynamics.Ragdolls);
            hanging.Step(1 / 60F, [near], true);
            DynamicsRagdollRequest ragdoll = Assert.Single(site.Dynamics.Ragdolls);
            Assert.True(ragdoll.Bones.Span[0].Animated);
            Assert.Equal(3, ragdoll.Bones.Length);
            Assert.Equal(2, ragdoll.Links.Length);
            // The actor walks in a kinematic capsule that pushes the piece and nothing else.
            DynamicsCreateCapsuleBodyRequest capsule = Assert.Single(site.Dynamics.Capsules);
            Assert.Equal(new Vector3(5, .9F, 6), site.Dynamics.KinematicMoves[^1].Transform.Translation);
            Assert.Equal(ragdoll.CollisionGroups, capsule.Body.Properties.CollisionMask);
            Assert.Single(site.Dynamics.Steps);
            // Bound to the site's collision as it was made, and again before each step, so a door opened since stands open.
            Assert.Equal(2, site.Dynamics.CollisionBindings);

            // Left alone, it swings on for the release, eases back to rest, and rests.
            DaggerfallHangingActor gone = far;
            for (int update = 0; update < 60 && site.Dynamics.ReleasedRagdolls == 0; update++) hanging.Step(.1F, [gone], true);
            Assert.Equal(1, site.Dynamics.ReleasedRagdolls);
            Assert.Equal(1, site.Animation.ReleasedInstances);
            Assert.Equal(1, site.Dynamics.ReleasedBodies);
            Assert.NotEmpty(site.Dynamics.Blends);
            Assert.True(site.Dynamics.Blends.SequenceEqual(site.Dynamics.Blends.OrderDescending()));
        }
    }

    [Fact]
    public void An_actor_coming_back_while_a_piece_eases_to_rest_swings_it_fully_again()
    {
        Site site = Compose();
        using (site)
        {
            using DaggerfallHanging hanging = site.Hanging();
            DaggerfallHangingActor near = new(1, new Vector3(5, 0, 6), .25F, 1.8F), far = near with { Feet = new Vector3(20, 0, 6) };
            hanging.Step(.1F, [near], true);
            hanging.Step(.1F, [near], true);
            for (int update = 0; update < 60 && site.Dynamics.Blends.Count == 0; update++) hanging.Step(.1F, [far], true);
            Assert.True(site.Dynamics.Blends[^1] < 1);

            hanging.Step(.1F, [near], true);
            Assert.Equal(1, site.Dynamics.Blends[^1]);
            Assert.Equal(0, site.Dynamics.ReleasedRagdolls);
        }
    }

    [Fact]
    public void While_classic_art_is_drawn_nothing_hangs()
    {
        Site site = Compose();
        using (site)
        {
            using DaggerfallHanging hanging = site.Hanging();
            DaggerfallHangingActor near = new(1, new Vector3(5, 0, 6), .25F, 1.8F);
            hanging.Step(.1F, [near], true);
            hanging.Step(.1F, [near], true);
            Assert.Single(site.Dynamics.Ragdolls);

            hanging.Step(.1F, [near], false);
            Assert.Equal(1, site.Dynamics.ReleasedRagdolls);
            Assert.Equal(1, site.Dynamics.ReleasedBodies);
            int steps = site.Dynamics.Steps.Count;
            hanging.Step(.1F, [near], false);
            Assert.Equal(steps, site.Dynamics.Steps.Count);
        }
    }

    [Fact]
    public void A_piece_that_cannot_hang_clear_of_what_is_around_it_stays_at_rest_and_is_said_once()
    {
        Site site = Compose();
        using (site)
        {
            site.Spatial.OverlapHit = _ => new SpatialHit { Present = true };
            List<string> said = [];
            using DaggerfallHanging hanging = site.Hanging(said.Add);
            DaggerfallHangingActor near = new(1, new Vector3(5, 0, 6), .25F, 1.8F);
            for (int update = 0; update < 3; update++) hanging.Step(.1F, [near], true);

            Assert.Empty(site.Dynamics.Ragdolls);
            Assert.Contains("213-8", Assert.Single(said), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_actor_standing_still_a_few_metres_off_lets_a_piece_rest_while_one_against_it_keeps_it_swinging()
    {
        Site site = Compose();
        using (site)
        {
            using DaggerfallHanging hanging = site.Hanging();
            DaggerfallHangingActor walker = new(1, new Vector3(20, 0, 6), .25F, 1.8F);
            hanging.Step(.1F, [walker], true);
            // It walks up to 3 m from the piece and stops there.
            walker = walker with { Feet = new Vector3(7, 0, 6) };
            hanging.Step(.1F, [walker], true);
            hanging.Step(.1F, [walker], true);
            Assert.Single(site.Dynamics.Ragdolls);
            // Still for the release time it no longer counts as moving; the piece swings that long again and eases to rest.
            for (int update = 0; update < 80 && site.Dynamics.ReleasedRagdolls == 0; update++) hanging.Step(.1F, [walker], true);
            Assert.Equal(1, site.Dynamics.ReleasedRagdolls);

            // Moving near wakes it again; standing against it keeps it swinging, still or not.
            DaggerfallHangingActor leaning = walker with { Feet = new Vector3(4.6F, 0, 6) };
            hanging.Step(.1F, [leaning], true);
            hanging.Step(.1F, [leaning], true);
            Assert.Equal(2, site.Dynamics.Ragdolls.Count);
            for (int update = 0; update < 80; update++) hanging.Step(.1F, [leaning], true);
            Assert.Equal(1, site.Dynamics.ReleasedRagdolls);
        }
    }

    [Fact]
    public void A_site_retiring_its_decorations_lets_go_of_the_pieces_first_and_is_made_again()
    {
        Site site = Compose();
        using (site)
        {
            using DaggerfallHanging hanging = site.Hanging();
            DaggerfallHangingActor near = new(1, new Vector3(5, 0, 6), .25F, 1.8F);
            hanging.Step(.1F, [near], true);
            hanging.Step(.1F, [near], true);
            Assert.Single(site.Dynamics.Ragdolls);
            Assert.False(hanging.Stale);

            // A crossing suspends the site: the swinging piece lets go of its ragdoll and instance before its mesh is retired.
            site.Appearance.SuspendLocationResources();
            Assert.Equal(1, site.Dynamics.ReleasedRagdolls);
            Assert.Equal(1, site.Animation.ReleasedInstances);
            Assert.True(hanging.Stale);
        }
    }

    [Fact]
    public void A_piece_whose_foot_meets_something_is_shortened_toward_its_hang_point()
    {
        Site site = Compose();
        using (site)
        {
            // Anything reaching below 1.3 m meets a high shelf.
            site.Spatial.OverlapHit = request => new SpatialHit { Present = request.Center.Y - request.HalfHeight < 1.3F };
            using DaggerfallHanging hanging = site.Hanging();
            DaggerfallHangingActor near = new(1, new Vector3(5, 0, 6), .25F, 1.8F);
            hanging.Step(.1F, [near], true);

            site.Appearance.Publish(site.Actors, new Dictionary<long, DaggerfallGroundContainer>());
            AppearanceFact fitted = Assert.Single(site.Graphics.Snapshots[^1], fact => fact.Appearance == site.Animation.Created[0]);
            float top = fitted.Transform.Translation.Y + 2 * fitted.Transform.Scale.Y;
            Assert.Equal(3, top, 3);
            Assert.True(fitted.Transform.Scale.Y < 1.875F / 2);
        }
    }

    [Fact]
    public void A_piece_whose_top_reaches_into_a_beam_hangs_from_the_beams_underside()
    {
        Site site = Compose();
        using (site)
        {
            // A beam's underside 0.4 m below the sprite's top, at 2.6 m: anything reaching above it meets the beam.
            site.Spatial.FloorHit = request => request.Direction.Y > 0 ? new SpatialHit { Present = true, Point = new Vector3(4, 2.6F, 6) } : default;
            site.Spatial.OverlapHit = request => new SpatialHit { Present = request.Center.Y + request.HalfHeight > 2.6F };
            using DaggerfallHanging hanging = site.Hanging();
            hanging.Step(.1F, [new DaggerfallHangingActor(1, new Vector3(5, 0, 6), .25F, 1.8F)], true);

            site.Appearance.Publish(site.Actors, new Dictionary<long, DaggerfallGroundContainer>());
            AppearanceFact fitted = Assert.Single(site.Graphics.Snapshots[^1], fact => fact.Appearance == site.Animation.Created[0]);
            // Lowered whole, not shortened: it hangs from the beam at its full length.
            Assert.Equal(1.875F / 2, fitted.Transform.Scale.Y, 3);
            Assert.True(fitted.Transform.Translation.Y + 2 * fitted.Transform.Scale.Y <= 2.6F + 1e-3F);
            Assert.False(hanging.Describe().Contains("1 unfit", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_piece_hung_along_the_face_of_a_recess_moves_a_few_centimetres_aside_rather_than_shrink()
    {
        Site site = Compose();
        using (site)
        {
            // The recess's face runs down the hanging line itself, at x 4: a strand along it meets it.
            site.Spatial.OverlapHit = request => new SpatialHit { Present = MathF.Abs(request.Center.X - 4) < .01F };
            using DaggerfallHanging hanging = site.Hanging();
            hanging.Step(.1F, [new DaggerfallHangingActor(1, new Vector3(5, 0, 6), .25F, 1.8F)], true);

            site.Appearance.Publish(site.Actors, new Dictionary<long, DaggerfallGroundContainer>());
            AppearanceFact fitted = Assert.Single(site.Graphics.Snapshots[^1], fact => fact.Appearance == site.Animation.Created[0]);
            Assert.Equal(1.875F / 2, fitted.Transform.Scale.Y, 3);
            Assert.Equal(.06F, MathF.Abs(fitted.Transform.Translation.X - 4) + MathF.Abs(fitted.Transform.Translation.Z - 6), 3);
        }
    }

    [Fact]
    public void A_lantern_carries_its_light_as_it_swings_without_shadows_and_gives_it_back_at_rest()
    {
        Site site = Compose(lantern: true);
        using (site)
        {
            using DaggerfallSiteLighting lighting = site.Lighting();
            using DaggerfallHanging hanging = site.Hanging(lighting: lighting);
            Vector3 authored = new(4, 2.6F, 6);
            // The player stands by it, so the lights' sector rule lets it cast.
            lighting.UpdateShadowSector(new Vector3(5, 0, 6), DaggerfallLampShadowChoice.Default);
            DaggerfallHangingActor near = new(1, new Vector3(5, 0, 6), .25F, 1.8F);
            hanging.Step(.1F, [near], true);
            // At rest its own light, not the block's nearer one, flickers where it was authored, casting.
            LightDescriptor rest = site.Graphics.LightUpdates[^1].Replacement.Descriptor;
            Assert.Equal(authored, rest.Position);
            Assert.Equal(LightShadowIntent.Requested, rest.ShadowIntent);

            hanging.Step(.1F, [near], true);
            site.Appearance.Publish(site.Actors, new Dictionary<long, DaggerfallGroundContainer>());
            AppearanceFact swinging = Assert.Single(site.Graphics.Snapshots[^1], fact => fact.Appearance == site.Animation.Created[0]);
            Assert.Equal(ShadowCasting.None, swinging.ShadowCasting);

            // Swung 30 cm aside, the lantern takes its light with it, which casts no shadow while away.
            Vector3 aside = new(.3F, 0, 0);
            site.Animation.Live = [.. site.Animation.ModelPose(swinging.Transform).ToArray()
                .Select(pose => pose with { World = pose.World with { Translation = pose.World.Translation + aside } })];
            hanging.Step(.1F, [near], true);
            LightRequest carrying = site.Graphics.LightUpdates[^1].Replacement;
            LightDescriptor carried = carrying.Descriptor;
            Assert.True(Vector3.Distance(authored + aside, carried.Position) < 1e-4F);
            Assert.Equal(LightShadowIntent.Disabled, carried.ShadowIntent);

            // The sector rule choosing again while it is away leaves it shadowless, holding the new choice.
            int updates = site.Graphics.LightUpdates.Count;
            lighting.UpdateShadowSector(new Vector3(1000, 0, 6), DaggerfallLampShadowChoice.Default);
            Assert.DoesNotContain(site.Graphics.LightUpdates.Skip(updates), update => update.Replacement.LogicalId == carrying.LogicalId);

            // Classic art stops it: the light goes home with the shadow held for it, and the mesh casts again.
            hanging.Step(.1F, [near], false);
            LightDescriptor home = site.Graphics.LightUpdates[^1].Replacement.Descriptor;
            Assert.Equal(authored, home.Position);
            Assert.Equal(LightShadowIntent.Disabled, home.ShadowIntent);
            site.Appearance.Publish(site.Actors, new Dictionary<long, DaggerfallGroundContainer>());
            Assert.DoesNotContain(site.Graphics.Snapshots[^1], fact => fact.ShadowCasting == ShadowCasting.None);
        }
    }

    private static DaggerfallArtReplacements Art(string kind = "root", bool lantern = false) => DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes("""
        {"artReplacements": {"images": [],
          "meshes": [
            {"replaces": {"archive": ARCHIVE, "record": RECORD}, "path": "refreshed/hanging.glb", "byteLength": 1, "sha256": "HASH", "hangs": "KIND"},
            {"replaces": {"archive": 205, "record": 0}, "path": "refreshed/barrel.glb", "byteLength": 1, "sha256": "HASH"}]}}
        """.Replace("ARCHIVE", lantern ? "210" : "213", StringComparison.Ordinal).Replace("RECORD", lantern ? "25" : "8", StringComparison.Ordinal)
        .Replace("KIND", lantern ? "lantern" : kind, StringComparison.Ordinal).Replace("HASH", HashHex, StringComparison.Ordinal)), "test.art");

    private sealed class Site : IDisposable
    {
        internal required DaggerfallSiteAppearance Appearance { get; init; }
        internal required AppearanceFake Graphics { get; init; }
        internal required AnimationRecorder Animation { get; init; }
        internal required DynamicsRecorder Dynamics { get; init; }
        internal required SpatialFake Spatial { get; init; }
        internal required SpatialMovementSystem Movement { get; init; }
        internal required ActorsState Actors { get; init; }
        internal required EngineContextFake Engine { get; init; }

        // A lantern's own light, placed half a metre above its flat as the importer places an interior lamp.
        // The lantern's own light, named after its flat as the importer names it, and a block's light hung nearer it.
        internal static readonly DaggerfallSiteLight Lamp = new("light-flat/block/0/0/1", new WorldPoint(4, 2.6F, 6), 10, 1.4F, Vector3.One);
        internal static readonly DaggerfallSiteLight Nearer = new("light/block/0/0/9", new WorldPoint(4, 2.1F, 6), 10, 1.4F, Vector3.One);

        internal DaggerfallSiteLighting Lighting() => new(Graphics, Engine.Context.CameraView, new DaggerfallSiteProfile(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()), FixtureGeometry("spatial/site.json", Hash, 1, "mesh/site.json", Hash),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
            site: new DaggerfallSiteId(17, 4), lights: [Lamp, Nearer], logicalProfileId: "worldrpg/test/tavern"),
            DaggerfallTuning.Defaults.SiteLighting, DaggerfallCalendar.Start.Sky);

        internal DaggerfallHanging Hanging(Action<string>? report = null, DaggerfallSiteLighting? lighting = null) =>
            new(Dynamics.Service, Animation.Service, Movement, Appearance, lighting ?? Lighting(), [Lamp, Nearer], DaggerfallTuning.Defaults.Hanging, report ?? (_ => { }));

        public void Dispose()
        {
            Actors.Dispose();
            Movement.Dispose();
            Appearance.Dispose();
        }
    }

    private static Site Compose(bool lantern = false)
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("refreshed/hanging.glb", Hash);
        content.Add("refreshed/barrel.glb", Hash);
        content.Add("sprite/root.png", Hash);
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Joints = lantern ? [.. RootJoints, new("light", 2)] : RootJoints;
        animation.Rest = lantern ? [.. RootRest, new(0, .5F, 0)] : RootRest;
        NormalizedBillboardSprite root = new("sprite/root.png", Hash, 8, 8, [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, .5F), new Vector2(.85F, 1.875F));
        (int, int) source = lantern ? (210, 25) : (213, 8);
        DaggerfallSiteDecoration placed = new("billboard/block/0/0/1", new WorldPoint(4, 3 - 1.875F / 2, 6), false, root, root, source, source);
        AppearanceFake graphics = new(releases);
        DaggerfallSiteAppearance appearance = new(content, graphics, MediaInputs(decorations: [placed]));
        appearance.UseEffectArt(PresentationRecorder.Create().Service, animation.Service, Art(lantern: lantern), () => true);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, graphics);
        return new Site
        {
            Appearance = appearance, Graphics = graphics, Animation = animation, Dynamics = DynamicsRecorder.Create(), Spatial = spatial,
            Movement = new SpatialMovementSystem(spatial.Service, content, null, DaggerfallTuning.Defaults.Spatial),
            Actors = ActorsWithNpc(11, HealthyMechanics(), new(2, 0, 3)), Engine = engine,
        };
    }
}
