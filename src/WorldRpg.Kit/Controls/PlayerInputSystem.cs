using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

public readonly record struct InputActionId(string Value);

internal enum MovementDirection { Forward, Backward, Left, Right }

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

/// <summary>Input state that outlives one admitted slice until the device releases it or focus drops it.</summary>
internal sealed class HeldPlayerInput
{
    internal HashSet<KeyboardControl> Keys { get; } = [];
    internal HashSet<MovementDirection> Directions { get; } = [];
    internal Dictionary<ControllerAxis, float> Axes { get; } = [];
    internal HashSet<ControllerButton> Buttons { get; } = [];

    internal HeldPlayerInput Copy()
    {
        HeldPlayerInput copy = new();
        copy.CopyFrom(this);
        return copy;
    }

    internal void CopyFrom(HeldPlayerInput source)
    {
        Keys.Clear();
        Keys.UnionWith(source.Keys);
        Directions.Clear();
        Directions.UnionWith(source.Directions);
        Axes.Clear();
        foreach ((ControllerAxis axis, float value) in source.Axes) Axes[axis] = value;
        Buttons.Clear();
        Buttons.UnionWith(source.Buttons);
    }

    internal void Clear()
    {
        Keys.Clear();
        Directions.Clear();
        Axes.Clear();
        Buttons.Clear();
    }
}

/// <summary>A fully diagnosed input interpretation that has not changed persistent product state.</summary>
public sealed class PreparedPlayerInput
{
    private readonly PlayerInputSystem _owner;
    private readonly HeldPlayerInput _held;
    private readonly HashSet<InputActionId> _actions;
    private readonly PlayerControlState _player;
    private readonly ulong _ownerRevision;
    private readonly float _startingYawRadians;
    private readonly float _startingPitchRadians;

    private bool _consumed;

    internal PreparedPlayerInput(PlayerInputSystem owner, PlayerControlState player, ulong ownerRevision, float startingYawRadians, float startingPitchRadians, HeldPlayerInput held, HashSet<InputActionId> actions, Vector2 planarIntent, float yawRadians, float pitchRadians)
    {
        _owner = owner;
        _player = player;
        _ownerRevision = ownerRevision;
        _startingYawRadians = startingYawRadians;
        _startingPitchRadians = startingPitchRadians;
        _held = held;
        _actions = actions;
        PlanarIntent = planarIntent;
        YawRadians = yawRadians;
        PitchRadians = pitchRadians;
    }

    public Vector2 PlanarIntent { get; }
    public float YawRadians { get; }
    public float PitchRadians { get; }

    internal void EnsureCommittableBy(PlayerInputSystem owner, PlayerControlState player)
    {
        if (!ReferenceEquals(_owner, owner)) throw new InvalidOperationException("Prepared input belongs to a different input system.");
        if (!ReferenceEquals(_player, player)) throw new InvalidOperationException("Prepared input belongs to a different player state.");
        if (_ownerRevision != owner.Revision) throw new InvalidOperationException("Prepared input is stale relative to input interpreter state.");
        if (player.YawRadians != _startingYawRadians || player.PitchRadians != _startingPitchRadians) throw new InvalidOperationException("Prepared input is stale relative to player look state.");
        if (_consumed) throw new InvalidOperationException("Prepared input was already consumed.");
    }

    internal void CommitTo(PlayerInputSystem owner, HeldPlayerInput held, ProductUpdateState update, PlayerControlState player)
    {
        EnsureCommittableBy(owner, player);
        _consumed = true;
        held.CopyFrom(_held);
        player.YawRadians = YawRadians;
        player.PitchRadians = PitchRadians;
        update.PlanarIntent = PlanarIntent;
        foreach (InputActionId action in _actions) update.Request(action);
    }
}

public sealed class PlayerInputSystem
{
    private readonly HeldPlayerInput _held = new();
    private readonly PlayerControlTuning _tuning;
    private readonly PlayerControlBindings _controls;
    private readonly ControllerInputTuning? _controller;
    private readonly InputActionBinding[] _bindings;
    private ulong _revision;

    internal ulong Revision => _revision;

    public PlayerInputSystem(PlayerControlTuning tuning, PlayerControlBindings controls, IEnumerable<InputActionBinding>? bindings = null, ControllerInputTuning? controller = null)
    {
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        _controller = controller?.Validate();
        _bindings = bindings?.ToArray() ?? [];
    }

    /// <summary>Interprets one admitted input slice without changing held state, player state, or semantic actions.</summary>
    public PreparedPlayerInput Prepare(PlayerControlState player, ProductUpdateState update)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(update);
        player.ValidateForInput();
        update.Validate();
        foreach (ProductInputEvent input in update.Inputs) Validate(input);

        HeldPlayerInput held = _held.Copy();
        HashSet<InputActionId> actions = [];
        // Direct axes and digital movement describe this slice only; keyboard and mapped directions
        // are held, and a controller axis stays where it was left until a later event moves it.
        Vector2 sliceIntent = update.PlanarIntent;
        float startingYawRadians = player.YawRadians;
        float startingPitchRadians = player.PitchRadians;
        float yawRadians = startingYawRadians;
        float pitchRadians = startingPitchRadians;

        foreach (ProductInputEvent input in update.Inputs)
        {
            if (input.Kind == InputEventKind.Clear)
            {
                held.Clear();
                sliceIntent = default;
            }
            else if (input.Kind == InputEventKind.PointerDelta)
            {
                LookRequest request = new(new LookState(yawRadians, pitchRadians), new Vector2(input.X, input.Y), LookConfiguration());
                LookDiagnostic diagnostic = Look.Diagnose(request);
                if (diagnostic != LookDiagnostic.Accepted) throw new InvalidOperationException($"Look request rejected: {diagnostic}.");
                LookReceipt receipt = Look.Integrate(request);
                yawRadians = receipt.After.YawRadians;
                pitchRadians = receipt.After.PitchRadians;
            }
            else if (input.Kind == InputEventKind.DirectDigital || input.Kind == InputEventKind.MappedDigital)
            {
                ApplyDigitalIntent(input, held.Directions, actions, ref sliceIntent);
            }
            else if (input.Kind == InputEventKind.DirectAxis || input.Kind == InputEventKind.MappedAxis)
            {
                ApplyAxisIntent(input, actions, ref sliceIntent);
            }
            else if (input.Kind == InputEventKind.Key && IsMovementKey(input.Keyboard))
            {
                if (input.Edge is InputEdge.Pressed or InputEdge.Held) held.Keys.Add(input.Keyboard);
                else if (input.Edge == InputEdge.Released)
                {
                    held.Keys.Remove(input.Keyboard);
                    ReleaseMappedDirection(input.Keyboard, held.Directions);
                }
            }
            else if (input.Kind == InputEventKind.ControllerAxis) held.Axes[input.ControllerAxis] = input.X;
            else if (input.Kind == InputEventKind.ControllerButton) ApplyControllerButton(input, held.Buttons, actions);
        }

        if (sliceIntent == Vector2.Zero)
            sliceIntent = PlanarIntent(held.Keys, held.Directions);
        // A stick turned by an admitted span of time is a rate, so its look sample is scaled by that
        // span and saturated at the same angular bound the pointer path already honours.
        Vector2 stickLook = ControllerLookDelta(held.Axes, update.DeltaSeconds);
        if (stickLook != Vector2.Zero)
        {
            LookRequest request = new(new LookState(yawRadians, pitchRadians), stickLook, ControllerLookConfiguration());
            LookDiagnostic diagnostic = Look.Diagnose(request);
            if (diagnostic is not (LookDiagnostic.Accepted or LookDiagnostic.DeltaLimitExceeded)) throw new InvalidOperationException($"Controller look request rejected: {diagnostic}.");
            LookReceipt receipt = Look.IntegrateClamped(request);
            yawRadians = receipt.After.YawRadians;
            pitchRadians = receipt.After.PitchRadians;
        }

        return new PreparedPlayerInput(this, player, _revision, startingYawRadians, startingPitchRadians, held, actions, Combine(sliceIntent, ControllerMoveIntent(held.Axes)), yawRadians, pitchRadians);
    }

    /// <summary>Commits an already prepared candidate after the enclosing spatial proposal has succeeded.</summary>
    public void Commit(PreparedPlayerInput candidate, PlayerControlState player, ProductUpdateState update)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(update);
        candidate.CommitTo(this, _held, update, player);
        AdvanceRevision();
    }

    /// <summary>Rejects a foreign, stale, or consumed candidate before another owner attempts a dependent Engine proposal.</summary>
    public void EnsureCommittable(PreparedPlayerInput candidate, PlayerControlState player)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(player);
        candidate.EnsureCommittableBy(this, player);
    }

    /// <summary>Resolves the current view basis from committed product look state without changing it.</summary>
    public LookReceipt ResolveCurrentLook(PlayerControlState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.ValidateForInput();
        LookRequest request = new(new LookState(player.YawRadians, player.PitchRadians), Vector2.Zero, LookConfiguration());
        LookDiagnostic diagnostic = Look.Diagnose(request);
        if (diagnostic != LookDiagnostic.Accepted) throw new InvalidOperationException($"Look request rejected: {diagnostic}.");
        return Look.Integrate(request);
    }

    /// <summary>Convenience path for callers that do not need to coordinate an Engine character proposal.</summary>
    public void Apply(PlayerControlState player, ProductUpdateState update) => Commit(Prepare(player, update), player, update);

    /// <summary>
    /// Drops held movement keys, mapped-direction intent, and controller state without interpreting
    /// an input slice.
    /// </summary>
    /// <remarks>
    /// This is what a focus or mode change needs: a key that was held when play paused, a modal
    /// opened or the player died must not keep moving the character after focus returns, and a
    /// release the interpreter never sees would otherwise leave the key held forever. A stick is
    /// dropped for the same reason — a pad left deflected through a mode change must not keep turning
    /// the camera — and it re-engages on its next event rather than on the state it held before. The
    /// revision advances, so input prepared against the previous focus is rejected as stale
    /// instead of being committed after the change.
    /// </remarks>
    public void Neutralize()
    {
        _held.Clear();
        AdvanceRevision();
    }

    private void AdvanceRevision() => _revision = checked(_revision + 1);

    private bool IsMovementKey(KeyboardControl key) => key == _controls.Forward || key == _controls.Backward || key == _controls.Left || key == _controls.Right;

    private void ApplyDigitalIntent(ProductInputEvent input, HashSet<MovementDirection> mappedDirections, HashSet<InputActionId> actions, ref Vector2 planarIntent)
    {
        ReadOnlySpan<byte> intent = input.Intent.Span;
        if (input.Kind == InputEventKind.MappedDigital && TryGetDirectionalIntent(intent, out MovementDirection direction))
        {
            if (input.Edge == InputEdge.Released || input.X <= 0f) mappedDirections.Remove(direction);
            else mappedDirections.Add(direction);
        }
        else if (IsMovementIntent(intent))
        {
            planarIntent = input.X > 0f ? new Vector2(0f, 1f) : Vector2.Zero;
        }
        CaptureSemanticAction(input, actions);
    }

    private void ApplyAxisIntent(ProductInputEvent input, HashSet<InputActionId> actions, ref Vector2 planarIntent)
    {
        ReadOnlySpan<byte> intent = input.Intent.Span;
        if (IsMovementIntent(intent)) planarIntent = new Vector2(input.X, input.Y);
        CaptureSemanticAction(input, actions);
    }

    /// <summary>
    /// Reads one positional controller button as a held button and, on the press edge, as the
    /// semantic action the ruleset bound to it. The shell publishes an analog travel event for
    /// trigger buttons alongside that press edge, so the edge is what an action reads and no
    /// binding has to know whether its button is analog.
    /// </summary>
    private void ApplyControllerButton(ProductInputEvent input, HashSet<ControllerButton> buttons, HashSet<InputActionId> actions)
    {
        if (input.Edge == InputEdge.Released)
        {
            buttons.Remove(input.ControllerButton);
            return;
        }

        if (input.Edge != InputEdge.Pressed) return;
        buttons.Add(input.ControllerButton);
        if (_controller is null) return;
        foreach (ControllerActionBinding binding in _controller.Actions)
            if (binding.Button == input.ControllerButton) actions.Add(binding.Action);
    }

    private void CaptureSemanticAction(ProductInputEvent input, HashSet<InputActionId> actions)
    {
        if (input.X <= 0f || !IsActionActivation(input)) return;
        foreach (InputActionBinding binding in _bindings)
            if (input.Intent.Span.SequenceEqual(binding.Intent.Span)) actions.Add(binding.Action);
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

    private Vector2 PlanarIntent(IReadOnlySet<KeyboardControl> held, IReadOnlySet<MovementDirection> mappedDirections) => new(
        (IsActive(MovementDirection.Right, _controls.Right, held, mappedDirections) ? 1f : 0f) - (IsActive(MovementDirection.Left, _controls.Left, held, mappedDirections) ? 1f : 0f),
        (IsActive(MovementDirection.Forward, _controls.Forward, held, mappedDirections) ? 1f : 0f) - (IsActive(MovementDirection.Backward, _controls.Backward, held, mappedDirections) ? 1f : 0f));

    private static bool IsActive(MovementDirection direction, KeyboardControl key, IReadOnlySet<KeyboardControl> held, IReadOnlySet<MovementDirection> mappedDirections) => mappedDirections.Contains(direction) || held.Contains(key);

    /// <summary>One stick's contribution to planar intent, in the same units the keyboard path produces.</summary>
    private Vector2 ControllerMoveIntent(IReadOnlyDictionary<ControllerAxis, float> axes)
    {
        if (_controller is null) return Vector2.Zero;
        return new Vector2(
            Math.Clamp(Axis(axes, _controller.MovementX, _controller.MovementDeadzone) * _controller.MovementStrafeSensitivity * (_controller.InvertMovementX ? -1f : 1f), -1f, 1f),
            Math.Clamp(Axis(axes, _controller.MovementY, _controller.MovementDeadzone) * _controller.MovementForwardSensitivity * (_controller.InvertMovementY ? -1f : 1f), -1f, 1f));
    }

    /// <summary>One stick's look travel for this admitted span, before the Engine's look scaling.</summary>
    private Vector2 ControllerLookDelta(IReadOnlyDictionary<ControllerAxis, float> axes, float deltaSeconds)
    {
        if (_controller is null) return Vector2.Zero;
        return new Vector2(
            Axis(axes, _controller.LookX, _controller.LookDeadzone),
            Axis(axes, _controller.LookY, _controller.LookDeadzone)) * deltaSeconds;
    }

    /// <summary>
    /// The deflection of one axis with the deadzone removed: inside the deadzone the axis is
    /// neutral, and beyond it the live travel is rescaled so the first movement past the deadzone
    /// is slow rather than a jump to it.
    /// </summary>
    private static float Axis(IReadOnlyDictionary<ControllerAxis, float> axes, ControllerAxis axis, float deadzone)
    {
        if (!axes.TryGetValue(axis, out float value)) return 0f;
        float magnitude = MathF.Abs(value);
        if (magnitude <= deadzone) return 0f;
        return MathF.CopySign(MathF.Min(1f, (magnitude - deadzone) / (1f - deadzone)), value);
    }

    /// <summary>Keyboard and stick intent add, because they are two devices asking for the same movement.</summary>
    private static Vector2 Combine(Vector2 sliceIntent, Vector2 controllerIntent) => new(
        Math.Clamp(sliceIntent.X + controllerIntent.X, -1f, 1f),
        Math.Clamp(sliceIntent.Y + controllerIntent.Y, -1f, 1f));

    private void ReleaseMappedDirection(KeyboardControl key, HashSet<MovementDirection> mappedDirections)
    {
        if (key == _controls.Forward) mappedDirections.Remove(MovementDirection.Forward);
        else if (key == _controls.Backward) mappedDirections.Remove(MovementDirection.Backward);
        else if (key == _controls.Left) mappedDirections.Remove(MovementDirection.Left);
        else if (key == _controls.Right) mappedDirections.Remove(MovementDirection.Right);
    }

    private LookConfig LookConfiguration() => new(
        _tuning.LookSensitivity,
        _tuning.LookSensitivity,
        _tuning.PitchMinimumRadians,
        _tuning.PitchMaximumRadians,
        _tuning.MaximumLookDeltaRadians,
        _tuning.InvertHorizontal,
        _tuning.InvertVertical,
        _tuning.WrapYaw);

    /// <summary>
    /// The stick's own look scaling: one unit of stick travel per second is this many radians, with
    /// the same pitch clamp, per-update bound and yaw wrapping the pointer path uses.
    /// </summary>
    private LookConfig ControllerLookConfiguration() => new(
        _controller!.LookYawRadiansPerSecond,
        _controller.LookPitchRadiansPerSecond,
        _tuning.PitchMinimumRadians,
        _tuning.PitchMaximumRadians,
        _tuning.MaximumLookDeltaRadians,
        _controller.InvertLookX,
        _controller.InvertLookY,
        _tuning.WrapYaw);

    private static void Validate(ProductInputEvent input)
    {
        if (!float.IsFinite(input.X)) throw new ArgumentOutOfRangeException(nameof(input.X));
        if (!float.IsFinite(input.Y)) throw new ArgumentOutOfRangeException(nameof(input.Y));
        // A press is a claim about a button and an axis event about an axis; an unnamed one cannot be
        // interpreted, and silently dropping it would hide a device the product never hears from.
        if (input.Kind == InputEventKind.ControllerAxis && input.ControllerAxis == ControllerAxis.None)
            throw new InvalidOperationException("A controller axis event names no axis.");
        if (input.Kind is InputEventKind.ControllerButton or InputEventKind.ControllerButtonValue && input.ControllerButton == ControllerButton.None)
            throw new InvalidOperationException("A controller button event names no button.");
    }
}

public sealed class ProductUpdateState(float deltaSeconds)
{
    public float DeltaSeconds { get; } = deltaSeconds;
    public List<ProductInputEvent> Inputs { get; } = [];
    public Vector2 PlanarIntent
    {
        get => _planarIntent;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) throw new ArgumentOutOfRangeException(nameof(value));
            _planarIntent = value;
        }
    }
    public void Add(ProductInputEvent input) => Inputs.Add(input);
    public void Request(InputActionId action) => _actions.Add(action);
    public bool IsRequested(InputActionId action) => _actions.Contains(action);
    internal void Validate()
    {
        if (!float.IsFinite(DeltaSeconds) || DeltaSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(DeltaSeconds));
        if (!float.IsFinite(PlanarIntent.X) || !float.IsFinite(PlanarIntent.Y)) throw new ArgumentOutOfRangeException(nameof(PlanarIntent));
    }

    private Vector2 _planarIntent;
    private readonly HashSet<InputActionId> _actions = [];
}
