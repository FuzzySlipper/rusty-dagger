using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Crouch is a toggle, as the donor's <c>PlayerHeightChanger</c> has it, and a crouched player stays crouched through a
/// save, a recall and a relocation, as the donor saves crouch with the pose (<c>SerializablePlayer.isCrouching</c>),
/// restores it with a recalled anchor and leaves it alone on every other arrival. The toggle reads the body's stance, so
/// a restored crouch is the toggle's state. The stand-in Engine crouches the body on request and stands it only where it
/// is told there is headroom; the real Engine's refusal to stand a body without headroom is the Kit's
/// <c>CrouchedRelocationTests</c>.
/// </summary>
public sealed class CrouchedStanceSessionTests
{
    [Fact]
    public void A_tap_crouches_the_crouch_outlasts_the_key_and_the_next_tap_stands()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        Crouch(s, 1);

        Walk(s, 2);
        Assert.True(s.Spatial().StepRequests.Last().Command.CrouchRequested);
        Assert.Equal(CharacterStance.Crouched, s.State.PlayerControl.Motion.Stance);

        Stand(s, 3);
    }

    /// <summary>Standing is refused under a low ceiling, and the tap is dropped rather than tried again, as the donor drops a stand <c>CanStand</c> refuses.</summary>
    [Fact]
    public void A_tap_to_stand_under_a_low_ceiling_is_refused_and_not_tried_again()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        Crouch(s, 1);

        s.Update(new ProductUpdate(OuterUpdate(2), Tap()));
        Assert.False(s.Spatial().StepRequests.Last().Command.CrouchRequested);
        Assert.Equal(CharacterStance.Crouched, s.State.PlayerControl.Motion.Stance);

        Engine(s, headroom: true);
        Walk(s, 3);
        Assert.True(s.Spatial().StepRequests.Last().Command.CrouchRequested);
        Assert.Equal(CharacterStance.Crouched, s.State.PlayerControl.Motion.Stance);
        Stand(s, 4);
    }

    [Fact]
    public void A_crouched_player_comes_back_crouched_from_a_save_and_steps_on_crouched()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        Crouch(s, 1);
        WorldPoint saved = s.State.PlayerControl.Position!.Value;
        RulesetSavePayload payload = s.CaptureSave();
        Assert.True(DaggerfallSavePayload.Read(payload).Player.Crouched);

        using DaggerfallSession restored = f.Restore(payload);
        Assert.Equal(saved, restored.State.PlayerControl.Position);
        Assert.Equal(CharacterStance.Crouched, restored.State.PlayerControl.Motion.Stance);

        // The restored crouch is the toggle's state: with headroom and no tap, the body steps on crouched.
        Engine(restored, headroom: true);
        Walk(restored, 2);
        Assert.Equal(CharacterStance.Crouched, restored.Spatial().StepRequests.Last().Motion.Stance);
        Assert.True(restored.Spatial().StepRequests.Last().Command.CrouchRequested);
        Assert.Equal(CharacterStance.Crouched, restored.State.PlayerControl.Motion.Stance);
        Assert.True(DaggerfallSavePayload.Read(restored.CaptureSave()).Player.Crouched);
        Stand(restored, 3);
    }

    [Fact]
    public void A_standing_player_comes_back_standing_from_a_save()
    {
        using Fixture f = new();
        Assert.False(DaggerfallSavePayload.Read(f.Session.CaptureSave()).Player.Crouched);
        using DaggerfallSession restored = f.Restore(f.Session.CaptureSave());
        Assert.Equal(CharacterStance.Standing, restored.State.PlayerControl.Motion.Stance);
    }

    /// <summary>An anchor names a floor: the arriving crouched body keeps its stance and is stood on that floor at its crouched height.</summary>
    [Fact]
    public void A_named_relocation_keeps_a_crouched_player_crouched_and_stands_them_at_crouched_height()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        Crouch(s, 1);
        DaggerfallSiteAnchor start = f.Composition.StartSite.RequireAnchor("start");
        float floor = start.Position.Y - .2f;
        s.Spatial().FloorHit = request => request.Direction.Y < -.5f
            ? new SpatialHit { Present = true, Kind = SpatialHitKind.StaticMesh, Point = request.Origin with { Y = floor }, Normal = Vector3.UnitY, Distance = request.Origin.Y - floor }
            : default;

        Assert.True(s.Sites.TryRelocate(new(f.Composition.StartSite.ProfileKey, "start")));

        Assert.Equal(CharacterStance.Crouched, s.State.PlayerControl.Motion.Stance);
        CharacterShapeConfig shape = s.Spatial.CurrentController.Shape;
        Assert.Equal(floor + (shape.CrouchedHeight * .5f) + shape.ContactSkin + shape.ClearancePadding, s.State.PlayerControl.Position!.Value.Y, precision: 4);
    }

    /// <summary>A door's way back returns the player to the pose they entered from, in the stance they entered in.</summary>
    [Fact]
    public void Leaving_by_the_way_in_restores_the_stance_the_player_entered_in()
    {
        using Fixture f = new(twoSites: true);
        DaggerfallSession s = f.Session;
        Crouch(s, 1);
        WorldPoint entered = s.State.PlayerControl.Position!.Value;

        Assert.True(s.TryTransitionTo(f.Destination!.ProfileKey));
        Assert.Equal(CharacterStance.Crouched, s.State.PlayerControl.Motion.Stance);
        Stand(s, 2);

        Assert.True(s.TryTransitionTo(f.Composition.StartSite.ProfileKey));
        Assert.Equal(entered, s.State.PlayerControl.Position);
        Assert.Equal(CharacterStance.Crouched, s.State.PlayerControl.Motion.Stance);
    }

    /// <summary>The donor's recall restores the anchor as a saved pose, crouch included, whatever stance the player recalls from.</summary>
    [Fact]
    public void A_recall_restores_the_stance_the_anchor_was_set_in_through_a_save()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        Crouch(s, 1);
        WorldPoint anchored = s.State.PlayerControl.Position!.Value;
        s.TeleportChoice.Request("set");
        s.TeleportChoice.Choose("set", "anchor");
        Stand(s, 2);
        RulesetSavePayload payload = s.CaptureSave();
        Assert.True(DaggerfallSavePayload.Read(payload).TeleportAnchor!.Pose.Crouched);

        using DaggerfallSession restored = f.Restore(payload);
        Assert.Equal(CharacterStance.Standing, restored.State.PlayerControl.Motion.Stance);
        restored.TeleportChoice.Request("recall");
        restored.TeleportChoice.Choose("recall", "recall");

        Assert.Null(restored.TeleportChoice.Anchor);
        Assert.Equal(anchored, restored.State.PlayerControl.Position);
        Assert.Equal(CharacterStance.Crouched, restored.State.PlayerControl.Motion.Stance);

        // The recalled crouch is the toggle's state too: it stays with headroom until the next tap.
        Engine(restored, headroom: true);
        Walk(restored, 3);
        Assert.Equal(CharacterStance.Crouched, restored.State.PlayerControl.Motion.Stance);
        Stand(restored, 4);
    }

    /// <summary>Taps crouch under a low ceiling, which crouches the body.</summary>
    private static void Crouch(DaggerfallSession s, ulong step)
    {
        Engine(s, headroom: false);
        s.Update(new ProductUpdate(OuterUpdate(step), Tap()));
        Assert.Equal(CharacterStance.Crouched, s.State.PlayerControl.Motion.Stance);
    }

    /// <summary>Taps crouch again where there is headroom, which stands the body.</summary>
    private static void Stand(DaggerfallSession s, ulong step)
    {
        Engine(s, headroom: true);
        s.Update(new ProductUpdate(OuterUpdate(step), Tap()));
        Assert.Equal(CharacterStance.Standing, s.State.PlayerControl.Motion.Stance);
    }

    /// <summary>Walks forward for a step with no crouch input.</summary>
    private static void Walk(DaggerfallSession s, ulong step) =>
        s.Update(new ProductUpdate(OuterUpdate(step), [Key(KeyboardControl.KeyW, InputEdge.Pressed)]));

    /// <summary>The crouch key pressed and let go within one update.</summary>
    private static ProductInputEvent[] Tap() => [Key(KeyboardControl.ControlLeft, InputEdge.Pressed), Key(KeyboardControl.ControlLeft, InputEdge.Released)];

    /// <summary>The stand-in Engine crouches a body on request and stands it again only with <paramref name="headroom"/>.</summary>
    private static void Engine(DaggerfallSession s, bool headroom) => s.Spatial().StepResult = (request, result) => result with
    {
        Motion = result.Motion with
        {
            Stance = request.Command.CrouchRequested ? CharacterStance.Crouched : headroom ? CharacterStance.Standing : request.Motion.Stance,
        },
    };

    private static ProductInputEvent Key(KeyboardControl key, InputEdge edge) => Input(InputEventKind.Key, edge, keyboard: key);

    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSiteProfile? Destination { get; }
        internal DaggerfallSession Session { get; }

        internal Fixture(bool twoSites = false)
        {
            DaggerfallSiteProfile source = ReadInputs(TestData.RepositoryRoot);
            if (twoSites) Destination = Assembled(DaggerfallWorldProfileIds.Dungeon(CastleNecromoghan));
            Composition = new(TestPayload.Definitions, source, DaggerfallTuning.Defaults)
            {
                Profiles = new DaggerfallSiteProfiles(Destination is null ? [source] : [source, Destination]),
            };
            Session = DaggerfallSession.StartNew(Engine().Context, Composition);
        }

        private EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, Composition.StartSite);
            if (Destination is not null) PopulateContent(content, Destination);
            SpatialFake spatial = SpatialFake.Create(Composition.StartSite.Geometry.Spatial[0].Sha256, releases);
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        }

        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(Engine().Context, Composition, payload);

        public void Dispose() => Session.Dispose();
    }
}
