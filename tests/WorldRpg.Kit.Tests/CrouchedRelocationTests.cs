using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>
/// A relocation moves the body as it is, as the donor moves a crouched player crouched: the position is the capsule's
/// centre, which holds only in the stance it was taken in. A crouched body put back under a ceiling only a crouched body
/// fits beneath moves on there with the real Engine, which will not stand it without headroom, rather than being stood
/// clear or held as a standing body put there is.
/// </summary>
public sealed class CrouchedRelocationTests
{
    private static readonly SpatialTuning Tuning = new(.5d, 8, 8, 1, new CharacterControllerTuning(RecoveryMaximumDistance: 1f));

    private static readonly CharacterObstacle Floor = Box(1, new(-6, -1, -6), new(6, 0, 6));

    /// <summary>A ceiling 1.2 m up, reaching past the recovery reach every way: a crouched body fits under it, a standing one nowhere in reach.</summary>
    private static readonly CharacterObstacle LowCeiling = Box(2, new(-3, 1.2f, -3), new(3, 1.5f, 3));

    [Fact]
    public void A_crouched_player_relocated_under_a_low_ceiling_stays_crouched_and_moves()
    {
        Run(system =>
        {
            CharacterStepEnvironment covered = new(default, new[] { Floor, LowCeiling });
            PlayerControlState player = new(new WorldPoint(5, Centre(system, CharacterStance.Standing), 5), 0, 0);
            Assert.NotNull(system.Step(player, Update(), covered, new CharacterStepControls(CrouchRequested: true)));
            Assert.Equal(CharacterStance.Crouched, player.Motion.Stance);

            WorldPoint under = new(0, Centre(system, CharacterStance.Crouched), 0);
            system.Relocate(player, under);
            Assert.Equal(CharacterStance.Crouched, player.Motion.Stance);

            // Asked to stand, as a second crouch press asks, the Engine refuses for want of headroom, and the body moves on crouched.
            Assert.NotNull(system.Step(player, Update(new Vector2(0, 1)), covered));
            Assert.Equal(CharacterStance.Crouched, player.Motion.Stance);
            Vector3 moved = player.Position!.Value.ToVector() - under.ToVector();
            Assert.True(new Vector2(moved.X, moved.Z).Length() > .001f, $"The crouched body did not move: {moved}.");
        });
    }

    [Fact]
    public void A_remembered_pose_is_restored_in_the_stance_it_was_taken_in()
    {
        Run(system =>
        {
            CharacterStepEnvironment covered = new(default, new[] { Floor, LowCeiling });
            PlayerControlState player = new(new WorldPoint(5, Centre(system, CharacterStance.Standing), 5), 0, 0);
            Assert.Equal(CharacterStance.Standing, player.Motion.Stance);

            WorldPoint remembered = new(0, Centre(system, CharacterStance.Crouched), 0);
            system.Relocate(player, remembered, CharacterStance.Crouched);

            Assert.Equal(CharacterStance.Crouched, player.Motion.Stance);
            Assert.NotNull(system.Step(player, Update(new Vector2(0, 1)), covered));
            Assert.Equal(CharacterStance.Crouched, player.Motion.Stance);

            system.Relocate(player, new WorldPoint(5, Centre(system, CharacterStance.Standing), 5), CharacterStance.Standing);
            Assert.Equal(CharacterStance.Standing, player.Motion.Stance);
        });
    }

    /// <summary>An anchor names a floor: the arriving body is stood on it at its own stance's height, as the donor's FixStanding uses the controller's.</summary>
    [Theory]
    [InlineData(CharacterStance.Standing)]
    [InlineData(CharacterStance.Crouched)]
    public void An_arrival_is_stood_on_its_floor_at_the_height_of_the_bodys_stance(CharacterStance stance)
    {
        Run(system =>
        {
            CharacterStepEnvironment open = new(default, new[] { Floor });
            Assert.Equal(Centre(system, stance), system.StandingPosition(new WorldPoint(1, .05f, 1), stance, open).Y, precision: 4);
        });
    }

    /// <summary>The centre of a body in <paramref name="stance"/> on the floor at zero: half its height and its skin and padding above it.</summary>
    private static float Centre(SpatialMovementSystem system, CharacterStance stance)
    {
        CharacterShapeConfig shape = system.CurrentController.Shape;
        return (system.StanceHeight(stance) * .5f) + shape.ContactSkin + shape.ClearancePadding;
    }

    private static void Run(Action<SpatialMovementSystem> test)
    {
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            using SpatialMovementSystem system = new(engine.Spatial, engine.Content, null, Tuning);
            test(system);
        });
    }

    private static ProductUpdateState Update(Vector2 intent = default) => new(1f / 60f) { PlanarIntent = intent };

    private static CharacterObstacle Box(ulong entity, Vector3 min, Vector3 max) =>
        new(entity, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), min, max, true, Vector3.Zero, Vector3.Zero);
}
