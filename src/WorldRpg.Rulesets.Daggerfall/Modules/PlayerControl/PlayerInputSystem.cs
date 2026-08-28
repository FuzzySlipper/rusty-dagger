using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Modules.PlayerControl;

internal sealed class PlayerInputSystem(PlayerControlTuning tuning, ILookService look)
{
    private readonly HashSet<KeyboardControl> _held = [];

    internal void Apply(PlayerControlState player, ProductUpdateState update)
    {
        foreach (ProductInputEvent input in update.Inputs)
        {
            if (input.Kind == InputEventKind.Clear)
            {
                _held.Clear();
                update.PlanarIntent = default;
            }
            else if (input.Kind == InputEventKind.PointerDelta)
            {
                LookReceipt receipt = look.Integrate(new LookRequest(new LookState(player.YawRadians, player.PitchRadians), new Vector2(input.X, input.Y), LookConfiguration()));
                player.YawRadians = receipt.After.YawRadians;
                player.PitchRadians = receipt.After.PitchRadians;
            }
            else if (input.Kind == InputEventKind.DirectDigital || input.Kind == InputEventKind.MappedDigital)
            {
                ApplyDigitalIntent(input, update);
            }
            else if (input.Kind == InputEventKind.DirectAxis || input.Kind == InputEventKind.MappedAxis)
            {
                ApplyAxisIntent(input, update);
            }
            else if (input.Kind == InputEventKind.Key && IsMovementKey(input.Keyboard))
            {
                if (input.Edge is InputEdge.Pressed or InputEdge.Held) _held.Add(input.Keyboard);
                else if (input.Edge == InputEdge.Released) _held.Remove(input.Keyboard);
            }
            else if ((input.Kind == InputEventKind.PointerButton && input.PointerButton == PointerButton.Primary && input.Edge == InputEdge.Pressed)
                || (input.Kind == InputEventKind.Key && input.Keyboard == KeyboardControl.Space && input.Edge == InputEdge.Pressed))
            {
                update.AttackRequested = true;
            }
        }
        if (update.PlanarIntent == Vector2.Zero)
            update.PlanarIntent = new((_held.Contains(KeyboardControl.KeyD) ? 1f : 0f) - (_held.Contains(KeyboardControl.KeyA) ? 1f : 0f), (_held.Contains(KeyboardControl.KeyW) ? 1f : 0f) - (_held.Contains(KeyboardControl.KeyS) ? 1f : 0f));
    }

    private static bool IsMovementKey(KeyboardControl key) => key is KeyboardControl.KeyW or KeyboardControl.KeyA or KeyboardControl.KeyS or KeyboardControl.KeyD;

    private static void ApplyDigitalIntent(ProductInputEvent input, ProductUpdateState update)
    {
        ReadOnlySpan<byte> intent = input.Intent.Span;
        if (intent.SequenceEqual("attack"u8) && input.X > 0f && (input.Edge == InputEdge.Pressed || input.Phase == InputPhase.DirectUi))
        {
            update.AttackRequested = true;
        }
        else if (intent.SequenceEqual("move"u8) || intent.SequenceEqual("movement"u8))
        {
            update.PlanarIntent = input.X > 0f ? new Vector2(0f, 1f) : Vector2.Zero;
        }
    }

    private static void ApplyAxisIntent(ProductInputEvent input, ProductUpdateState update)
    {
        ReadOnlySpan<byte> intent = input.Intent.Span;
        if (intent.SequenceEqual("move"u8) || intent.SequenceEqual("movement"u8))
        {
            update.PlanarIntent = new Vector2(input.X, input.Y);
        }
    }

    private LookConfig LookConfiguration() => new(tuning.LookSensitivity, tuning.LookSensitivity, tuning.PitchMinimumRadians, tuning.PitchMaximumRadians, tuning.MaximumLookDeltaRadians, InvertHorizontal: false, InvertVertical: false, WrapYaw: true);
}

internal sealed class ProductUpdateState(float deltaSeconds)
{
    internal float DeltaSeconds { get; } = deltaSeconds;
    internal List<ProductInputEvent> Inputs { get; } = [];
    internal Vector2 PlanarIntent { get; set; }
    internal bool AttackRequested { get; set; }
    internal void Add(ProductInputEvent input) => Inputs.Add(input);
}
