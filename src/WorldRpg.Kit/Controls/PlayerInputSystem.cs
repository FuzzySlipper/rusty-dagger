using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

public readonly record struct InputActionId(string Value);

/// <summary>A ruleset-owned binding from an Engine semantic intent to a typed action handle.</summary>
public sealed record InputActionBinding(InputActionId Action, ReadOnlyMemory<byte> Intent);

/// <summary>Ruleset-supplied semantic and keyboard movement bindings.</summary>
public sealed record PlayerControlBindings(
    IReadOnlyList<ReadOnlyMemory<byte>> MovementIntents,
    KeyboardControl Forward,
    KeyboardControl Backward,
    KeyboardControl Left,
    KeyboardControl Right);

public sealed class PlayerInputSystem(PlayerControlTuning tuning, ILookService look, PlayerControlBindings controls, IEnumerable<InputActionBinding>? bindings = null)
{
    private readonly HashSet<KeyboardControl> _held = [];
    private readonly PlayerControlBindings _controls = controls ?? throw new ArgumentNullException(nameof(controls));
    private readonly InputActionBinding[] _bindings = bindings?.ToArray() ?? [];

    public void Apply(PlayerControlState player, ProductUpdateState update)
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
        }
        if (update.PlanarIntent == Vector2.Zero)
            update.PlanarIntent = new((_held.Contains(_controls.Right) ? 1f : 0f) - (_held.Contains(_controls.Left) ? 1f : 0f), (_held.Contains(_controls.Forward) ? 1f : 0f) - (_held.Contains(_controls.Backward) ? 1f : 0f));
    }

    private bool IsMovementKey(KeyboardControl key) => key == _controls.Forward || key == _controls.Backward || key == _controls.Left || key == _controls.Right;

    private void ApplyDigitalIntent(ProductInputEvent input, ProductUpdateState update)
    {
        ReadOnlySpan<byte> intent = input.Intent.Span;
        if (IsMovementIntent(intent))
        {
            update.PlanarIntent = input.X > 0f ? new Vector2(0f, 1f) : Vector2.Zero;
        }
        CaptureSemanticAction(input, update);
    }

    private void ApplyAxisIntent(ProductInputEvent input, ProductUpdateState update)
    {
        ReadOnlySpan<byte> intent = input.Intent.Span;
        if (IsMovementIntent(intent))
        {
            update.PlanarIntent = new Vector2(input.X, input.Y);
        }
        CaptureSemanticAction(input, update);
    }

    private void CaptureSemanticAction(ProductInputEvent input, ProductUpdateState update)
    {
        if (input.X <= 0f || input.Edge is not InputEdge.Pressed || input.Phase == InputPhase.Held) return;
        foreach (InputActionBinding binding in _bindings)
            if (input.Intent.Span.SequenceEqual(binding.Intent.Span)) update.Request(binding.Action);
    }

    private bool IsMovementIntent(ReadOnlySpan<byte> intent)
    {
        foreach (ReadOnlyMemory<byte> binding in _controls.MovementIntents)
            if (intent.SequenceEqual(binding.Span)) return true;
        return false;
    }

    private LookConfig LookConfiguration() => new(tuning.LookSensitivity, tuning.LookSensitivity, tuning.PitchMinimumRadians, tuning.PitchMaximumRadians, tuning.MaximumLookDeltaRadians, InvertHorizontal: false, InvertVertical: false, WrapYaw: true);
}

public sealed class ProductUpdateState(float deltaSeconds)
{
    public float DeltaSeconds { get; } = deltaSeconds;
    public List<ProductInputEvent> Inputs { get; } = [];
    public Vector2 PlanarIntent { get; set; }
    public void Add(ProductInputEvent input) => Inputs.Add(input);
    public void Request(InputActionId action) => _actions.Add(action);
    public bool IsRequested(InputActionId action) => _actions.Contains(action);
    private readonly HashSet<InputActionId> _actions = [];
}
