using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class ControllerInputSystemTests
{
    [Fact]
    public void One_stick_moves_while_the_other_turns_in_the_same_admitted_slice()
    {
        PlayerInputSystem input = new(TestTuning(), Controls(), controller: Pad());
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);
        ProductUpdateState update = new(.1f);
        update.Add(Axis(ControllerAxis.Axis0, 1f));
        update.Add(Axis(ControllerAxis.Axis1, 1f));
        update.Add(Axis(ControllerAxis.Axis2, 1f));
        update.Add(Axis(ControllerAxis.Axis3, 1f));

        input.Apply(player, update);

        // The left stick is inverted vertically because the shell's gamepad axes are positive
        // downwards while planar intent is positive forward, so full-down travel is full reverse.
        Assert.Equal(new Vector2(1f, -1f), update.PlanarIntent);
        Assert.Equal(2.5f * .1f, player.YawRadians, precision: 6);
        Assert.Equal(-1.75f * .1f, player.PitchRadians, precision: 6);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(.19f, 0f)]
    [InlineData(.2f, 0f)]
    [InlineData(.6f, .5f)]
    [InlineData(1f, 1f)]
    [InlineData(-.6f, -.5f)]
    [InlineData(-1f, -1f)]
    public void Deadzone_edges_rescale_the_live_travel_rather_than_jumping_past_them(float deflection, float expected)
    {
        PlayerInputSystem input = new(TestTuning(), Controls(), controller: Pad());
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);
        ProductUpdateState update = new(1f);
        update.Add(Axis(ControllerAxis.Axis0, deflection));

        input.Apply(player, update);

        Assert.Equal(expected, update.PlanarIntent.X, precision: 6);
        Assert.Equal(0f, update.PlanarIntent.Y);
    }

    [Fact]
    public void Per_axis_inversion_and_sensitivity_come_from_tuning_and_saturate_at_one()
    {
        ControllerInputTuning pad = Pad() with
        {
            InvertMovementX = true,
            InvertMovementY = false,
            MovementStrafeSensitivity = .5f,
            MovementForwardSensitivity = 2f,
        };
        PlayerInputSystem input = new(TestTuning(), Controls(), controller: pad);
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);
        ProductUpdateState update = new(.1f);
        update.Add(Axis(ControllerAxis.Axis0, 1f));
        update.Add(Axis(ControllerAxis.Axis1, 1f));
        update.Add(Axis(ControllerAxis.Axis2, 1f));
        update.Add(Axis(ControllerAxis.Axis3, 1f));

        input.Apply(player, update);

        Assert.Equal(new Vector2(-.5f, 1f), update.PlanarIntent);
        // The look axes keep the tuning's own inversion, so the movement inversion left them alone:
        // the shell's positive-down look axis still looks down.
        Assert.Equal(2.5f * .1f, player.YawRadians, precision: 6);
        Assert.Equal(-1.75f * .1f, player.PitchRadians, precision: 6);
    }

    [Fact]
    public void Keyboard_and_stick_ask_for_the_same_movement_and_saturate_at_the_engines_bound()
    {
        PlayerInputSystem input = new(TestTuning(), Controls(), controller: Pad());
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);
        ProductUpdateState update = new(1f);
        update.Add(Input(InputEventKind.Key, InputEdge.Pressed, key: KeyboardControl.KeyW));
        update.Add(Axis(ControllerAxis.Axis1, -1f));

        input.Apply(player, update);

        // One forward from the key and one from the stick is still one forward: the Engine refuses a
        // component beyond the unit travel, so the sum saturates instead of failing the proposal.
        Assert.Equal(new Vector2(0f, 1f), update.PlanarIntent);
    }

    [Fact]
    public void Stick_look_is_a_rate_over_the_admitted_span_and_shares_the_pointer_pitch_clamp()
    {
        PlayerInputSystem input = new(TestTuning(), Controls(), controller: Pad());
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);

        for (int step = 0; step < 10; step++)
        {
            ProductUpdateState update = new(.1f);
            update.Add(Axis(ControllerAxis.Axis3, -1f));
            input.Apply(player, update);
            Assert.True(player.PitchRadians <= TestTuning().PitchMaximumRadians);
        }

        // Ten tenth-second samples at 1.75 rad/s would reach 1.75 rad, and the pointer path's own
        // pitch clamp is what stops it.
        Assert.Equal(TestTuning().PitchMaximumRadians, player.PitchRadians, precision: 6);
        Assert.Equal(0f, player.YawRadians, precision: 6);
    }

    [Fact]
    public void A_button_press_requests_its_action_once_and_a_release_rearms_it()
    {
        InputActionId attack = new("test.attack");
        ControllerInputTuning pad = Pad() with { Actions = [new ControllerActionBinding(ControllerButton.Button0, attack)] };
        PlayerInputSystem input = new(TestTuning(), Controls(), controller: pad);
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);

        ProductUpdateState pressed = new(1f);
        pressed.Add(Button(ControllerButton.Button0, InputEdge.Pressed));
        input.Apply(player, pressed);
        Assert.True(pressed.IsRequested(attack));

        ProductUpdateState idle = new(1f);
        input.Apply(player, idle);
        Assert.False(idle.IsRequested(attack));

        ProductUpdateState unbound = new(1f);
        unbound.Add(Button(ControllerButton.Button4, InputEdge.Pressed));
        input.Apply(player, unbound);
        Assert.False(unbound.IsRequested(attack));

        ProductUpdateState pressedAgain = new(1f);
        pressedAgain.Add(Button(ControllerButton.Button0, InputEdge.Released));
        pressedAgain.Add(Button(ControllerButton.Button0, InputEdge.Pressed));
        input.Apply(player, pressedAgain);
        Assert.True(pressedAgain.IsRequested(attack));
    }

    [Fact]
    public void A_deflected_stick_keeps_asking_until_it_is_cleared_or_neutralised()
    {
        PlayerInputSystem input = new(TestTuning(), Controls(), controller: Pad());
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);
        ProductUpdateState deflected = new(1f);
        deflected.Add(Axis(ControllerAxis.Axis0, 1f));
        input.Apply(player, deflected);
        Assert.Equal(new Vector2(1f, 0f), deflected.PlanarIntent);

        ProductUpdateState held = new(1f);
        input.Apply(player, held);
        Assert.Equal(new Vector2(1f, 0f), held.PlanarIntent);

        ProductUpdateState cleared = new(1f);
        cleared.Add(Input(InputEventKind.Clear));
        input.Apply(player, cleared);
        Assert.Equal(Vector2.Zero, cleared.PlanarIntent);

        ProductUpdateState redeflected = new(1f);
        redeflected.Add(Axis(ControllerAxis.Axis0, 1f));
        input.Apply(player, redeflected);
        input.Neutralize();
        ProductUpdateState afterNeutralize = new(1f);
        input.Apply(player, afterNeutralize);
        Assert.Equal(Vector2.Zero, afterNeutralize.PlanarIntent);
        Assert.Equal(0f, player.YawRadians);
    }

    [Fact]
    public void An_unnamed_or_non_finite_controller_event_is_refused_rather_than_dropped()
    {
        PlayerInputSystem input = new(TestTuning(), Controls(), controller: Pad());
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);

        ProductUpdateState unnamedAxis = new(1f);
        unnamedAxis.Add(Axis(ControllerAxis.None, 1f));
        Assert.Throws<InvalidOperationException>(() => input.Apply(player, unnamedAxis));

        ProductUpdateState unnamedButton = new(1f);
        unnamedButton.Add(Button(ControllerButton.None, InputEdge.Pressed));
        Assert.Throws<InvalidOperationException>(() => input.Apply(player, unnamedButton));

        ProductUpdateState notFinite = new(1f);
        notFinite.Add(Axis(ControllerAxis.Axis0, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.Apply(player, notFinite));
    }

    [Fact]
    public void A_product_without_pad_tuning_interprets_none_of_the_events()
    {
        PlayerInputSystem input = new(TestTuning(), Controls());
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: .25f, pitchRadians: -.25f);
        ProductUpdateState update = new(1f);
        update.Add(Axis(ControllerAxis.Axis0, 1f));
        update.Add(Axis(ControllerAxis.Axis1, 1f));
        update.Add(Axis(ControllerAxis.Axis2, 1f));
        update.Add(Button(ControllerButton.Button0, InputEdge.Pressed));

        input.Apply(player, update);

        Assert.Equal(Vector2.Zero, update.PlanarIntent);
        Assert.Equal(.25f, player.YawRadians);
        Assert.Equal(-.25f, player.PitchRadians);
    }

    [Fact]
    public void Tuning_refuses_a_pad_whose_axes_or_buttons_cannot_mean_one_thing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with { MovementX = ControllerAxis.None }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with { LookY = ControllerAxis.None }));
        Assert.Throws<ArgumentException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with { LookX = ControllerAxis.Axis0 }));
        Assert.Throws<ArgumentException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with { LookY = ControllerAxis.Axis1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with { MovementDeadzone = 1f }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with { MovementDeadzone = -.1f }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with { LookYawRadiansPerSecond = 0f }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with { MovementForwardSensitivity = float.PositiveInfinity }));
        Assert.Throws<ArgumentNullException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with { Actions = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with { Actions = [new ControllerActionBinding(ControllerButton.None, new InputActionId("test.attack"))] }));
        // One press cannot mean two things, and the refusal names the button it collides on.
        ArgumentException collision = Assert.Throws<ArgumentException>(() => new PlayerInputSystem(TestTuning(), Controls(), controller: Pad() with
        {
            Actions =
            [
                new ControllerActionBinding(ControllerButton.Button0, new InputActionId("test.attack")),
                new ControllerActionBinding(ControllerButton.Button0, new InputActionId("test.interact")),
            ],
        }));
        Assert.Contains("Button0", collision.Message, StringComparison.Ordinal);
    }

    private static ControllerInputTuning Pad() => ControllerInputTuning.Standard;

    private static PlayerControlTuning TestTuning() => new(.0035f, -1.5533f, 1.5533f, .35f, InvertHorizontal: false, InvertVertical: false, WrapYaw: true);

    private static PlayerControlBindings Controls() => new([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD);

    private static ProductInputEvent Axis(ControllerAxis axis, float value) => new(
        InputEventKind.ControllerAxis, InputEdge.None, InputDevice.Controller, InputChannel.Axis, InputAxis.None,
        KeyboardControl.None, PointerButton.None, ControllerButton.None, axis, InputClearReason.None, InputValueKind.Axis,
        InputPhase.Axis, InputProvenance.Physical, default, default, default, value, 0f,
        ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

    private static ProductInputEvent Button(ControllerButton button, InputEdge edge) => new(
        InputEventKind.ControllerButton, edge, InputDevice.Controller, InputChannel.Button, InputAxis.None,
        KeyboardControl.None, PointerButton.None, button, ControllerAxis.None, InputClearReason.None, InputValueKind.Digital,
        edge == InputEdge.Pressed ? InputPhase.Pressed : InputPhase.Released, InputProvenance.Physical, default, default, default, 1f, 0f,
        ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

    private static ProductInputEvent Input(InputEventKind kind, InputEdge edge = InputEdge.None, KeyboardControl key = KeyboardControl.None) => new(
        kind, edge, InputDevice.None, InputChannel.None, InputAxis.None, key, PointerButton.None, ControllerButton.None, ControllerAxis.None,
        InputClearReason.None, InputValueKind.None, InputPhase.None, InputProvenance.None, default, default, default, 0f, 0f,
        ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
}
