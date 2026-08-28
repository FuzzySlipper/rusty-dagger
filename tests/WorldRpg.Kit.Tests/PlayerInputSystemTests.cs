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
        PlayerInputSystem input = new(PlayerControlTuning.Defaults, LookDouble.Create(), controls);
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f));

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

    private static ProductInputEvent Input(InputEventKind kind, InputEdge edge = InputEdge.None, KeyboardControl key = KeyboardControl.None, float x = 0f, float y = 0f, string intent = "") => new(
        kind, edge, InputDevice.None, InputChannel.None, InputAxis.None, key, PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None, InputValueKind.None, InputPhase.None, InputProvenance.None, default, default, default, x, y, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, System.Text.Encoding.UTF8.GetBytes(intent), ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

    private class LookDouble : DispatchProxy
    {
        internal static ILookService Create() => DispatchProxy.Create<ILookService, LookDouble>();
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException(method?.Name);
    }
}
