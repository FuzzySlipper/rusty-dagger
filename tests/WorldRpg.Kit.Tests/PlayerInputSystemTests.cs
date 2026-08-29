using System.Numerics;
using System.Reflection;
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
        PlayerInputSystem input = new(TestTuning(), LookDouble.Create().Service, controls);
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

    [Fact]
    public void Look_configuration_comes_from_tuning()
    {
        LookDouble look = LookDouble.Create();
        PlayerInputSystem input = new(new PlayerControlTuning(.01f, -.5f, .5f, 1f, InvertHorizontal: true, InvertVertical: true, WrapYaw: false), look.Service, new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD));
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: .25f, pitchRadians: -.25f);
        ProductUpdateState update = new(1f);
        update.Add(Input(InputEventKind.PointerDelta, x: .125f, y: -.25f));

        input.Apply(player, update);

        Assert.NotNull(look.LastRequest);
        LookConfig configuration = look.LastRequest.Value.Config;
        Assert.Equal(.01f, configuration.HorizontalRadiansPerUnit);
        Assert.Equal(.01f, configuration.VerticalRadiansPerUnit);
        Assert.True(configuration.InvertHorizontal);
        Assert.True(configuration.InvertVertical);
        Assert.False(configuration.WrapYaw);
    }

    [Fact]
    public void Direct_ui_digital_claims_activate_actions_without_turning_unedged_or_held_input_into_actions()
    {
        InputActionId action = new("test.activate");
        PlayerInputSystem input = new(TestTuning(), LookDouble.Create().Service, new PlayerControlBindings([], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD), [new InputActionBinding(action, "activate"u8.ToArray())]);
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

    private static ProductInputEvent Input(InputEventKind kind, InputEdge edge = InputEdge.None, KeyboardControl key = KeyboardControl.None, float x = 0f, float y = 0f, InputPhase phase = InputPhase.None, string intent = "") => new(
        kind, edge, InputDevice.None, InputChannel.None, InputAxis.None, key, PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None, InputValueKind.None, phase, InputProvenance.None, default, default, default, x, y, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, System.Text.Encoding.UTF8.GetBytes(intent), ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

    private static PlayerControlTuning TestTuning() => new(.0035f, -1.5533f, 1.5533f, .35f, InvertHorizontal: false, InvertVertical: false, WrapYaw: true);

    private class LookDouble : DispatchProxy
    {
        internal ILookService Service { get; private set; } = null!;
        internal LookRequest? LastRequest { get; private set; }
        internal static LookDouble Create()
        {
            ILookService service = DispatchProxy.Create<ILookService, LookDouble>();
            LookDouble proxy = (LookDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(ILookService.Integrate)) throw new NotSupportedException(method?.Name);
            LookRequest request = (LookRequest)args![0]!;
            LastRequest = request;
            return new LookReceipt(request.State, request.State, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
        }
    }
}
