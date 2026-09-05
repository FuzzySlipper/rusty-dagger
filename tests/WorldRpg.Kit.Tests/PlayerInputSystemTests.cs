using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class PlayerInputSystemTests
{
    [Fact]
    public void Movement_uses_ruleset_supplied_intents_and_keys()
    {
        PlayerControlBindings controls = new(["stride"u8.ToArray()], KeyboardControl.KeyI, KeyboardControl.KeyK, KeyboardControl.KeyJ, KeyboardControl.KeyL);
        PlayerInputSystem input = new(TestTuning(), controls);
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);

        ProductUpdateState semantic = new(1f);
        semantic.Add(Input(InputEventKind.MappedAxis, x: .5f, y: -.25f, intent: "stride"));
        input.Apply(player, semantic);
        Assert.Equal(new Vector2(.5f, -.25f), semantic.PlanarIntent);

        ProductUpdateState keys = new(1f);
        keys.Add(Input(InputEventKind.Key, edge: InputEdge.Pressed, key: KeyboardControl.KeyI));
        input.Apply(player, keys);
        Assert.Equal(new Vector2(0f, 1f), keys.PlanarIntent);

        ProductUpdateState unrelated = new(1f);
        unrelated.Add(Input(InputEventKind.Clear));
        unrelated.Add(Input(InputEventKind.MappedAxis, x: 1f, y: 1f, intent: "move"));
        input.Apply(player, unrelated);
        Assert.Equal(Vector2.Zero, unrelated.PlanarIntent);
    }

    [Theory]
    [InlineData("go.forward", 0f, 1f)]
    [InlineData("go.backward", 0f, -1f)]
    [InlineData("go.left", -1f, 0f)]
    [InlineData("go.right", 1f, 0f)]
    public void Mapped_digital_directional_intents_use_ruleset_bindings(string intent, float expectedX, float expectedY)
    {
        PlayerInputSystem input = new(TestTuning(), DirectionalControls());
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);
        ProductUpdateState update = new(1f);
        update.Add(Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1f, phase: InputPhase.Pressed, intent: intent));

        input.Apply(player, update);

        Assert.Equal(new Vector2(expectedX, expectedY), update.PlanarIntent);
    }

    [Fact]
    public void Mapped_digital_directions_persist_aggregate_and_clear()
    {
        PlayerInputSystem input = new(TestTuning(), DirectionalControls());
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);

        ProductUpdateState simultaneous = new(1f);
        simultaneous.Add(Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1f, phase: InputPhase.Pressed, intent: "go.forward"));
        simultaneous.Add(Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1f, phase: InputPhase.Pressed, intent: "go.right"));
        input.Apply(player, simultaneous);
        Assert.Equal(new Vector2(1f, 1f), simultaneous.PlanarIntent);

        ProductUpdateState opposed = new(1f);
        opposed.Add(Input(InputEventKind.MappedDigital, InputEdge.Held, x: 1f, phase: InputPhase.Held, intent: "go.backward"));
        input.Apply(player, opposed);
        Assert.Equal(new Vector2(1f, 0f), opposed.PlanarIntent);

        ProductUpdateState released = new(1f);
        released.Add(Input(InputEventKind.MappedDigital, InputEdge.Released, phase: InputPhase.Released, intent: "go.forward"));
        input.Apply(player, released);
        Assert.Equal(new Vector2(1f, -1f), released.PlanarIntent);

        ProductUpdateState physicalRelease = new(1f);
        physicalRelease.Add(Input(InputEventKind.Key, InputEdge.Released, key: KeyboardControl.KeyD));
        input.Apply(player, physicalRelease);
        Assert.Equal(new Vector2(0f, -1f), physicalRelease.PlanarIntent);

        ProductUpdateState cleared = new(1f);
        cleared.Add(Input(InputEventKind.Clear));
        input.Apply(player, cleared);
        Assert.Equal(Vector2.Zero, cleared.PlanarIntent);
    }

    [Fact]
    public void Look_configuration_comes_from_tuning()
    {
        PlayerInputSystem input = new(new PlayerControlTuning(.01f, -.5f, .5f, 1f, InvertHorizontal: true, InvertVertical: true, WrapYaw: false), new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD));
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: .25f, pitchRadians: -.25f);
        ProductUpdateState update = new(1f);
        update.Add(Input(InputEventKind.PointerDelta, x: .125f, y: -.25f));

        input.Apply(player, update);

        Assert.Equal(.24875f, player.YawRadians);
        Assert.Equal(-.2475f, player.PitchRadians);
    }

    [Fact]
    public void Direct_ui_digital_claims_activate_actions_without_turning_unedged_or_held_input_into_actions()
    {
        InputActionId action = new("test.activate");
        PlayerInputSystem input = new(TestTuning(), new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD), [new InputActionBinding(action, "activate"u8.ToArray())]);
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);

        ProductUpdateState nativeDirectClaim = new(1f);
        nativeDirectClaim.Add(Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "activate"));
        input.Apply(player, nativeDirectClaim);
        Assert.True(nativeDirectClaim.IsRequested(action));

        ProductUpdateState releasedDirectClaim = new(1f);
        releasedDirectClaim.Add(Input(InputEventKind.DirectDigital, x: 0f, phase: InputPhase.DirectUi, intent: "activate"));
        input.Apply(player, releasedDirectClaim);
        Assert.False(releasedDirectClaim.IsRequested(action));

        ProductUpdateState heldDirectClaim = new(1f);
        heldDirectClaim.Add(Input(InputEventKind.DirectDigital, edge: InputEdge.Held, x: 1f, phase: InputPhase.Held, intent: "activate"));
        input.Apply(player, heldDirectClaim);
        Assert.False(heldDirectClaim.IsRequested(action));

        ProductUpdateState unedgedMappedClaim = new(1f);
        unedgedMappedClaim.Add(Input(InputEventKind.MappedDigital, x: 1f, phase: InputPhase.DirectUi, intent: "activate"));
        input.Apply(player, unedgedMappedClaim);
        Assert.False(unedgedMappedClaim.IsRequested(action));
    }

    [Fact]
    public void Nonfinite_input_is_rejected_before_look_or_actions_mutate_state()
    {
        InputActionId action = new("test.activate");
        PlayerInputSystem input = new(TestTuning(), new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD), [new InputActionBinding(action, "activate"u8.ToArray())]);
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: .25f, pitchRadians: -.25f);
        ProductUpdateState update = new(1f);
        update.Add(Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "activate"));
        update.Add(Input(InputEventKind.PointerDelta, x: float.NaN, y: 0f));

        Assert.Throws<ArgumentOutOfRangeException>(() => input.Apply(player, update));

        Assert.False(update.IsRequested(action));
        Assert.Equal(.25f, player.YawRadians);
        Assert.Equal(-.25f, player.PitchRadians);
    }

    [Fact]
    public void Out_of_limit_look_diagnosis_leaves_prepared_input_uncommitted()
    {
        InputActionId action = new("test.activate");
        PlayerInputSystem input = new(TestTuning(), new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD), [new InputActionBinding(action, "activate"u8.ToArray())]);
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), .25f, -.25f);
        ProductUpdateState update = new(1f);
        update.Add(Input(InputEventKind.Key, InputEdge.Pressed, key: KeyboardControl.KeyW));
        update.Add(Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "activate"));
        update.Add(Input(InputEventKind.PointerDelta, x: 200f, y: .2f));

        Assert.Throws<InvalidOperationException>(() => input.Apply(player, update));

        Assert.Equal(.25f, player.YawRadians);
        Assert.Equal(-.25f, player.PitchRadians);
        Assert.Equal(Vector2.Zero, update.PlanarIntent);
        Assert.False(update.IsRequested(action));
        ProductUpdateState afterRejection = new(1f);
        input.Apply(player, afterRejection);
        Assert.Equal(Vector2.Zero, afterRejection.PlanarIntent);
    }

    [Fact]
    public void Prepared_input_is_owner_bound_and_single_use()
    {
        InputActionId action = new("test.activate");
        PlayerInputSystem first = new(TestTuning(), new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD), [new InputActionBinding(action, "activate"u8.ToArray())]);
        PlayerInputSystem second = new(TestTuning(), new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD), [new InputActionBinding(action, "activate"u8.ToArray())]);
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        ProductUpdateState preparedFor = new(1f);
        preparedFor.Add(Input(InputEventKind.Key, InputEdge.Pressed, key: KeyboardControl.KeyW));
        preparedFor.Add(Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "activate"));
        PreparedPlayerInput candidate = first.Prepare(player, preparedFor);

        Assert.Throws<InvalidOperationException>(() => second.Commit(candidate, player, preparedFor));
        Assert.Equal(Vector2.Zero, preparedFor.PlanarIntent);
        Assert.False(preparedFor.IsRequested(action));
        Assert.Equal(0f, player.YawRadians);

        first.Commit(candidate, player, preparedFor);
        Assert.Equal(new Vector2(0f, 1f), preparedFor.PlanarIntent);
        Assert.True(preparedFor.IsRequested(action));
        ProductUpdateState replay = new(1f);
        Assert.Throws<InvalidOperationException>(() => first.Commit(candidate, player, replay));
        Assert.Equal(Vector2.Zero, replay.PlanarIntent);
        Assert.False(replay.IsRequested(action));
    }

    [Fact]
    public void Prepared_input_rejects_an_out_of_order_commit()
    {
        InputActionId action = new("test.activate");
        PlayerInputSystem input = new(TestTuning(), new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD), [new InputActionBinding(action, "activate"u8.ToArray())]);
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        ProductUpdateState firstUpdate = new(1f);
        firstUpdate.Add(Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "activate"));
        ProductUpdateState secondUpdate = new(1f);
        secondUpdate.Add(Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "activate"));
        PreparedPlayerInput first = input.Prepare(player, firstUpdate);
        PreparedPlayerInput second = input.Prepare(player, secondUpdate);

        input.Commit(first, player, firstUpdate);
        Assert.True(firstUpdate.IsRequested(action));

        Assert.Throws<InvalidOperationException>(() => input.Commit(second, player, secondUpdate));
        Assert.False(secondUpdate.IsRequested(action));
        Assert.Equal(0f, player.YawRadians);
        Assert.Equal(0f, player.PitchRadians);
    }

    [Fact]
    public void Prepared_input_rejects_a_changed_or_different_source_player_before_commit()
    {
        PlayerInputSystem input = new(TestTuning(), new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD));
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), .25f, -.25f);
        ProductUpdateState update = new(1f);
        PreparedPlayerInput candidate = input.Prepare(player, update);

        player.YawRadians = .5f;
        Assert.Throws<InvalidOperationException>(() => input.EnsureCommittable(candidate, player));
        Assert.Throws<InvalidOperationException>(() => input.Commit(candidate, player, update));
        Assert.Equal(Vector2.Zero, update.PlanarIntent);
        Assert.Equal(.5f, player.YawRadians);

        PlayerControlState differentPlayer = new(new WorldPoint(0f, 0f, 0f), .25f, -.25f);
        Assert.Throws<InvalidOperationException>(() => input.EnsureCommittable(candidate, differentPlayer));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Nonfinite_tuning_or_delta_is_rejected(float invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerControlTuning(invalid, -.5f, .5f, 1f, false, false, true).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterControllerTuning(Radius: invalid).Validate());

        PlayerInputSystem input = new(TestTuning(), new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.Apply(new PlayerControlState(new WorldPoint(0f, 0f, 0f), 0f, 0f), new ProductUpdateState(invalid)));
    }

    private static ProductInputEvent Input(InputEventKind kind, InputEdge edge = InputEdge.None, KeyboardControl key = KeyboardControl.None, float x = 0f, float y = 0f, InputPhase phase = InputPhase.None, string intent = "") => new(
        kind, edge, InputDevice.None, InputChannel.None, InputAxis.None, key, PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None, InputValueKind.None, phase, InputProvenance.None, default, default, default, x, y, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, System.Text.Encoding.UTF8.GetBytes(intent), ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

    private static PlayerControlTuning TestTuning() => new(.0035f, -1.5533f, 1.5533f, .35f, InvertHorizontal: false, InvertVertical: false, WrapYaw: true);

    private static PlayerControlBindings DirectionalControls() => new(
        [],
        KeyboardControl.KeyW,
        KeyboardControl.KeyS,
        KeyboardControl.KeyA,
        KeyboardControl.KeyD,
        new DirectionalMovementBindings("go.forward"u8.ToArray(), "go.backward"u8.ToArray(), "go.left"u8.ToArray(), "go.right"u8.ToArray()));

}
