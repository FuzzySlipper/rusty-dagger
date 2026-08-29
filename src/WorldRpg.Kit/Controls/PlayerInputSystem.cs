using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

public readonly record struct InputActionId(string Value);

/// <summary>A ruleset-owned binding from an Engine semantic intent to a typed action handle.</summary>
public sealed record InputActionBinding(InputActionId Action, ReadOnlyMemory<byte> Intent);

/// <summary>Ruleset-configured semantic intents for the four planar movement directions.</summary>
public sealed record DirectionalMovementBindings(
    ReadOnlyMemory<byte> Forward,
    ReadOnlyMemory<byte> Backward,
    ReadOnlyMemory<byte> Left,
    ReadOnlyMemory<byte> Right);

/// <summary>Ruleset-supplied semantic and keyboard movement bindings.</summary>
public sealed record PlayerControlBindings(
    IReadOnlyList<ReadOnlyMemory<byte>> MovementIntents,
    KeyboardControl Forward,
    KeyboardControl Backward,
    KeyboardControl Left,
    KeyboardControl Right,
    DirectionalMovementBindings? DirectionalIntents = null);

public sealed class PlayerInputSystem(PlayerControlTuning tuning, ILookService look, PlayerControlBindings controls, IEnumerable<InputActionBinding>? bindings = null)
{
    private readonly HashSet<KeyboardControl> _held = [];
    private readonly HashSet<MovementDirection> _mappedDirections = [];
    private readonly PlayerControlBindings _controls = controls ?? throw new ArgumentNullException(nameof(controls));
    private readonly InputActionBinding[] _bindings = bindings?.ToArray() ?? [];

    public void Apply(PlayerControlState player, ProductUpdateState update)
    {
        foreach (ProductInputEvent input in update.Inputs)
        {
            if (input.Kind == InputEventKind.Clear)
            {
                _held.Clear();
                _mappedDirections.Clear();
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
                else if (input.Edge == InputEdge.Released)
                {
                    _held.Remove(input.Keyboard);
                    ReleaseMappedDirection(input.Keyboard);
                }
            }
        }
        if (update.PlanarIntent == Vector2.Zero)
            update.PlanarIntent = new(
                (IsActive(MovementDirection.Right, _controls.Right) ? 1f : 0f) - (IsActive(MovementDirection.Left, _controls.Left) ? 1f : 0f),
                (IsActive(MovementDirection.Forward, _controls.Forward) ? 1f : 0f) - (IsActive(MovementDirection.Backward, _controls.Backward) ? 1f : 0f));
    }

    private bool IsMovementKey(KeyboardControl key) => key == _controls.Forward || key == _controls.Backward || key == _controls.Left || key == _controls.Right;

    private void ApplyDigitalIntent(ProductInputEvent input, ProductUpdateState update)
    {
        ReadOnlySpan<byte> intent = input.Intent.Span;
        if (input.Kind == InputEventKind.MappedDigital && TryGetDirectionalIntent(intent, out MovementDirection direction))
        {
            if (input.Edge == InputEdge.Released || input.X <= 0f) _mappedDirections.Remove(direction);
            else _mappedDirections.Add(direction);
        }
        else if (IsMovementIntent(intent))
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
        if (input.X <= 0f || !IsActionActivation(input)) return;
        foreach (InputActionBinding binding in _bindings)
            if (input.Intent.Span.SequenceEqual(binding.Intent.Span)) update.Request(binding.Action);
    }

    private static bool IsActionActivation(ProductInputEvent input) =>
        input.Edge == InputEdge.Pressed
        || (input.Kind == InputEventKind.DirectDigital && input.Phase == InputPhase.DirectUi && input.Edge == InputEdge.None);

    private bool IsMovementIntent(ReadOnlySpan<byte> intent)
    {
        foreach (ReadOnlyMemory<byte> binding in _controls.MovementIntents)
            if (intent.SequenceEqual(binding.Span)) return true;
        return false;
    }

    private bool TryGetDirectionalIntent(ReadOnlySpan<byte> intent, out MovementDirection direction)
    {
        DirectionalMovementBindings? bindings = _controls.DirectionalIntents;
        if (bindings is not null)
        {
            if (intent.SequenceEqual(bindings.Forward.Span)) { direction = MovementDirection.Forward; return true; }
            if (intent.SequenceEqual(bindings.Backward.Span)) { direction = MovementDirection.Backward; return true; }
            if (intent.SequenceEqual(bindings.Left.Span)) { direction = MovementDirection.Left; return true; }
            if (intent.SequenceEqual(bindings.Right.Span)) { direction = MovementDirection.Right; return true; }
        }
        direction = default;
        return false;
    }

    private bool IsActive(MovementDirection direction, KeyboardControl key) => _mappedDirections.Contains(direction) || _held.Contains(key);

    private void ReleaseMappedDirection(KeyboardControl key)
    {
        if (key == _controls.Forward) _mappedDirections.Remove(MovementDirection.Forward);
        else if (key == _controls.Backward) _mappedDirections.Remove(MovementDirection.Backward);
        else if (key == _controls.Left) _mappedDirections.Remove(MovementDirection.Left);
        else if (key == _controls.Right) _mappedDirections.Remove(MovementDirection.Right);
    }

    private enum MovementDirection { Forward, Backward, Left, Right }

    private LookConfig LookConfiguration() => new(
        tuning.LookSensitivity,
        tuning.LookSensitivity,
        tuning.PitchMinimumRadians,
        tuning.PitchMaximumRadians,
        tuning.MaximumLookDeltaRadians,
        tuning.InvertHorizontal,
        tuning.InvertVertical,
        tuning.WrapYaw);
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
