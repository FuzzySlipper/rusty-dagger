using WorldRpg.Rulesets.Daggerfall;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallLevitationPolicyTests
{
    private readonly DaggerfallLevitationPolicy _policy = new();

    [Fact]
    public void Grant_selects_vertical_drive_for_up_down_and_hover()
    {
        DaggerfallLevitationStep up = _policy.Resolve(Context(Granted: true, UpHeld: true));
        DaggerfallLevitationStep down = _policy.Resolve(Context(Granted: true, DownHeld: true));
        DaggerfallLevitationStep hover = _policy.Resolve(Context(Granted: true));

        Assert.True(up.Granted);
        Assert.True(up.IsLevitating);
        Assert.Equal(DaggerfallVerticalMovementMode.Levitation, up.Mode);
        Assert.Equal(4f, up.VerticalVelocity);
        Assert.Equal(-4f, down.VerticalVelocity);
        Assert.Equal(0f, hover.VerticalVelocity);
    }

    [Fact]
    public void Up_input_wins_when_up_and_down_are_both_held()
    {
        DaggerfallLevitationStep step = _policy.Resolve(Context(Granted: true, UpHeld: true, DownHeld: true));

        Assert.Equal(4f, step.VerticalVelocity);
    }

    [Fact]
    public void Climbing_owns_motion_without_erasing_the_effect_grant()
    {
        DaggerfallLevitationStep climbing = _policy.Resolve(Context(Granted: true, Swimming: true, Climbing: true, UpHeld: true));
        DaggerfallLevitationStep released = _policy.Resolve(Context(Granted: true, Swimming: true));

        Assert.True(climbing.Granted);
        Assert.False(climbing.IsLevitating);
        Assert.Equal(DaggerfallVerticalMovementMode.Climbing, climbing.Mode);
        Assert.Null(climbing.VerticalVelocity);
        Assert.Equal(DaggerfallVerticalMovementMode.Levitation, released.Mode);
        Assert.Equal(0f, released.VerticalVelocity);
    }

    [Fact]
    public void Swimming_and_gravity_remain_the_selected_owners_after_grant_loss()
    {
        DaggerfallLevitationStep swimming = _policy.Resolve(Context(Swimming: true));
        DaggerfallLevitationStep gravity = _policy.Resolve(Context());

        Assert.False(swimming.Granted);
        Assert.Equal(DaggerfallVerticalMovementMode.Swimming, swimming.Mode);
        Assert.Null(swimming.VerticalVelocity);
        Assert.Equal(DaggerfallVerticalMovementMode.Gravity, gravity.Mode);
        Assert.Null(gravity.VerticalVelocity);
    }

    [Fact]
    public void Immobilized_levitation_holds_position_and_stays_readable_as_granted()
    {
        DaggerfallLevitationStep step = _policy.Resolve(Context(Granted: true, CanMove: false, UpHeld: true));

        Assert.True(step.Granted);
        Assert.True(step.IsLevitating);
        Assert.Equal(0f, step.VerticalVelocity);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Vertical_speed_must_be_finite_and_positive(float speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _policy.Resolve(Context(Granted: true, VerticalSpeed: speed)));
    }

    private static DaggerfallLevitationContext Context(
        bool Granted = false,
        bool Swimming = false,
        bool Climbing = false,
        bool CanMove = true,
        bool UpHeld = false,
        bool DownHeld = false,
        float VerticalSpeed = 4f) => new(
            Granted,
            Swimming,
            Climbing,
            CanMove,
            UpHeld,
            DownHeld,
            VerticalSpeed);
}
