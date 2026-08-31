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

/// <summary>A fully diagnosed input interpretation that has not changed persistent product state.</summary>
public sealed class PreparedPlayerInput
{
    private readonly PlayerInputSystem _owner;
    private readonly HashSet<KeyboardControl> _held;
    private readonly HashSet<MovementDirection> _mappedDirections;
    private readonly HashSet<InputActionId> _actions;
    private readonly PlayerControlState _player;
    private readonly ulong _ownerRevision;
    private readonly float _startingYawRadians;
    private readonly float _startingPitchRadians;

    private bool _consumed;

    internal PreparedPlayerInput(PlayerInputSystem owner, PlayerControlState player, ulong ownerRevision, float startingYawRadians, float startingPitchRadians, HashSet<KeyboardControl> held, HashSet<MovementDirection> mappedDirections, HashSet<InputActionId> actions, Vector2 planarIntent, float yawRadians, float pitchRadians)
    {
        _owner = owner;
        _player = player;
        _ownerRevision = ownerRevision;
        _startingYawRadians = startingYawRadians;
        _startingPitchRadians = startingPitchRadians;
        _held = held;
        _mappedDirections = mappedDirections;
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

    internal void CommitTo(PlayerInputSystem owner, HashSet<KeyboardControl> held, HashSet<MovementDirection> mappedDirections, ProductUpdateState update, PlayerControlState player)
    {
        EnsureCommittableBy(owner, player);
        _consumed = true;
        held.Clear();
        held.UnionWith(_held);
        mappedDirections.Clear();
        mappedDirections.UnionWith(_mappedDirections);
        player.YawRadians = YawRadians;
        player.PitchRadians = PitchRadians;
        update.PlanarIntent = PlanarIntent;
        foreach (InputActionId action in _actions) update.Request(action);
    }
}

public sealed class PlayerInputSystem
{
    private readonly HashSet<KeyboardControl> _held = [];
    private readonly HashSet<MovementDirection> _mappedDirections = [];
    private readonly PlayerControlTuning _tuning;
    private readonly ILookService _look;
    private readonly PlayerControlBindings _controls;
    private readonly InputActionBinding[] _bindings;
    private ulong _revision;

    internal ulong Revision => _revision;

    public PlayerInputSystem(PlayerControlTuning tuning, ILookService look, PlayerControlBindings controls, IEnumerable<InputActionBinding>? bindings = null)
    {
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _look = look ?? throw new ArgumentNullException(nameof(look));
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
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

        HashSet<KeyboardControl> held = new(_held);
        HashSet<MovementDirection> mappedDirections = new(_mappedDirections);
        HashSet<InputActionId> actions = [];
        Vector2 planarIntent = update.PlanarIntent;
        float startingYawRadians = player.YawRadians;
        float startingPitchRadians = player.PitchRadians;
        float yawRadians = startingYawRadians;
        float pitchRadians = startingPitchRadians;

        foreach (ProductInputEvent input in update.Inputs)
        {
            if (input.Kind == InputEventKind.Clear)
            {
                held.Clear();
                mappedDirections.Clear();
                planarIntent = default;
            }
            else if (input.Kind == InputEventKind.PointerDelta)
            {
                LookRequest request = new(new LookState(yawRadians, pitchRadians), new Vector2(input.X, input.Y), LookConfiguration());
                LookDiagnostic diagnostic = _look.Diagnose(request);
                if (diagnostic != LookDiagnostic.Accepted) throw new InvalidOperationException($"Engine look request rejected: {diagnostic}.");
                LookReceipt receipt = _look.Integrate(request);
                yawRadians = receipt.After.YawRadians;
                pitchRadians = receipt.After.PitchRadians;
            }
            else if (input.Kind == InputEventKind.DirectDigital || input.Kind == InputEventKind.MappedDigital)
            {
                ApplyDigitalIntent(input, mappedDirections, actions, ref planarIntent);
            }
            else if (input.Kind == InputEventKind.DirectAxis || input.Kind == InputEventKind.MappedAxis)
            {
                ApplyAxisIntent(input, actions, ref planarIntent);
            }
            else if (input.Kind == InputEventKind.Key && IsMovementKey(input.Keyboard))
            {
                if (input.Edge is InputEdge.Pressed or InputEdge.Held) held.Add(input.Keyboard);
                else if (input.Edge == InputEdge.Released)
                {
                    held.Remove(input.Keyboard);
                    ReleaseMappedDirection(input.Keyboard, mappedDirections);
                }
            }
        }

        if (planarIntent == Vector2.Zero)
            planarIntent = PlanarIntent(held, mappedDirections);
        return new PreparedPlayerInput(this, player, _revision, startingYawRadians, startingPitchRadians, held, mappedDirections, actions, planarIntent, yawRadians, pitchRadians);
    }

    /// <summary>Commits an already prepared candidate after the enclosing spatial proposal has succeeded.</summary>
    public void Commit(PreparedPlayerInput candidate, PlayerControlState player, ProductUpdateState update)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(update);
        candidate.CommitTo(this, _held, _mappedDirections, update, player);
        AdvanceRevision();
    }

    /// <summary>Rejects a foreign, stale, or consumed candidate before another owner attempts a dependent Engine proposal.</summary>
    public void EnsureCommittable(PreparedPlayerInput candidate, PlayerControlState player)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(player);
        candidate.EnsureCommittableBy(this, player);
    }

    /// <summary>Resolves the Engine-owned current view basis from committed product look state without changing it.</summary>
    public LookReceipt ResolveCurrentLook(PlayerControlState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.ValidateForInput();
        LookRequest request = new(new LookState(player.YawRadians, player.PitchRadians), Vector2.Zero, LookConfiguration());
        LookDiagnostic diagnostic = _look.Diagnose(request);
        if (diagnostic != LookDiagnostic.Accepted) throw new InvalidOperationException($"Engine look request rejected: {diagnostic}.");
        return _look.Integrate(request);
    }

    /// <summary>Convenience path for callers that do not need to coordinate an Engine character proposal.</summary>
    public void Apply(PlayerControlState player, ProductUpdateState update) => Commit(Prepare(player, update), player, update);

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

    private static void Validate(ProductInputEvent input)
    {
        if (!float.IsFinite(input.X)) throw new ArgumentOutOfRangeException(nameof(input.X));
        if (!float.IsFinite(input.Y)) throw new ArgumentOutOfRangeException(nameof(input.Y));
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
