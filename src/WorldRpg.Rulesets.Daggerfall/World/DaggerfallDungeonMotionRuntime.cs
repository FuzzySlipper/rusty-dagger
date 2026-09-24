using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;

namespace WorldRpg.Rulesets.Daggerfall.World;

internal enum DaggerfallDungeonMotionPhase : byte
{
    Start,
    PlayingForward,
    End,
    PlayingReverse,
}

internal enum DaggerfallDungeonMotionEndpoint : byte
{
    Start,
    End,
}

internal enum DaggerfallDungeonMotionActivation : byte
{
    StartedForward,
    StartedReverse,
    CompletedImmediately,
    IgnoredWhileMoving,
}

/// <summary>One explicitly admitted action-model binding to an existing Engine entity.</summary>
internal sealed record DaggerfallDungeonMotionBinding(
    DaggerfallDungeonActionDefinition Action,
    EntityId Entity,
    string? ModelDescription = null);

/// <summary>
/// Current action animation state. <see cref="EndpointIntent"/> is stored separately from phase
/// so a restored action retains which endpoint it is approaching as well as its elapsed phase.
/// </summary>
internal sealed record DaggerfallDungeonMotionSave(
    string ActionId,
    DaggerfallDungeonMotionPhase Phase,
    DaggerfallDungeonMotionEndpoint EndpointIntent,
    double ElapsedSeconds)
{
    internal DaggerfallDungeonMotionSave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ActionId);
        if (!Enum.IsDefined(Phase)) throw new ArgumentOutOfRangeException(nameof(Phase));
        if (!Enum.IsDefined(EndpointIntent)) throw new ArgumentOutOfRangeException(nameof(EndpointIntent));
        if (!double.IsFinite(ElapsedSeconds) || ElapsedSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(ElapsedSeconds));
        DaggerfallDungeonMotionEndpoint phaseEndpoint = Phase switch
        {
            DaggerfallDungeonMotionPhase.Start or DaggerfallDungeonMotionPhase.PlayingReverse => DaggerfallDungeonMotionEndpoint.Start,
            DaggerfallDungeonMotionPhase.End or DaggerfallDungeonMotionPhase.PlayingForward => DaggerfallDungeonMotionEndpoint.End,
            _ => throw new ArgumentOutOfRangeException(nameof(Phase)),
        };
        if (EndpointIntent != phaseEndpoint)
            throw new ArgumentException($"Saved action '{ActionId}' has a phase and endpoint intent that disagree.", nameof(EndpointIntent));
        if (Phase is DaggerfallDungeonMotionPhase.Start or DaggerfallDungeonMotionPhase.End && ElapsedSeconds != 0d)
            throw new ArgumentException($"Settled action '{ActionId}' cannot retain a playing duration.", nameof(ElapsedSeconds));
        return this;
    }
}

/// <summary>Current dungeon action motion for one loaded profile.</summary>
internal sealed record DaggerfallDungeonMotionSnapshot(
    string ProfileId,
    DaggerfallDungeonMotionSave[] Actions)
{
    internal DaggerfallDungeonMotionSnapshot Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProfileId);
        ArgumentNullException.ThrowIfNull(Actions);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallDungeonMotionSave action in Actions)
        {
            action.Validate();
            if (!ids.Add(action.ActionId))
                throw new ArgumentException($"Dungeon motion snapshot repeats action '{action.ActionId}'.", nameof(Actions));
        }
        return this;
    }
}

/// <summary>
/// Daggerfall's two-endpoint motion state over explicit Engine entity targets. It writes only
/// canonical Engine Transform values; appearance and spatial callers consume those values through
/// their existing Engine projections. Moving mesh collision uses exact Engine triangle residency,
/// and the motion projection supplies entity-bound mesh instances for Engine support/carry.
/// </summary>
internal sealed class DaggerfallDungeonMotionRuntime
{
    private readonly EntityStore _entities;
    private readonly string _profileId;
    private readonly Dictionary<string, MotionState> _actions;

    internal DaggerfallDungeonMotionRuntime(
        EntityStore entities,
        string profileId,
        IEnumerable<DaggerfallDungeonMotionBinding> bindings,
        DaggerfallDungeonMotionSnapshot? restored = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(bindings);
        _profileId = profileId;

        Dictionary<ulong, EntityComponents<Transform, SpatialCollider>> targets = entities
            .Query(EngineComponentTypes.Transform, EngineComponentTypes.SpatialCollider)
            .ToDictionary(row => row.Entity.Value);
        _actions = new Dictionary<string, MotionState>(StringComparer.Ordinal);

        foreach (DaggerfallDungeonMotionBinding binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            DaggerfallDungeonActionDefinition action = binding.Action
                ?? throw new ArgumentException("A dungeon motion binding requires an action definition.", nameof(bindings));
            if (!DaggerfallDungeonMotionPolicy.TryInterpret(action, binding.ModelDescription, out DaggerfallDungeonMotionSpecification specification))
                throw new ArgumentException($"Dungeon action '{action.Id}' is not one of the retained motion variants.", nameof(bindings));
            if (!targets.TryGetValue(binding.Entity.Value, out EntityComponents<Transform, SpatialCollider> target))
                throw new InvalidOperationException($"Dungeon action '{action.Id}' target {binding.Entity.Value} must be active with Engine Transform and SpatialCollider components.");
            MotionState state = new(action, binding.Entity, target.First, specification);
            if (!_actions.TryAdd(action.Id, state))
                throw new ArgumentException($"Dungeon motion action '{action.Id}' is bound more than once.", nameof(bindings));
        }

        if (restored is not null)
            Restore(restored);
    }

    internal string ProfileId => _profileId;

    /// <summary>
    /// Validates saved motion against the selected profile's actual motion actions without creating
    /// Engine entities. Save admission uses this before the session materializes its projection.
    /// </summary>
    internal static void ValidateSnapshot(
        string profileId,
        IEnumerable<(string ActionId, double DurationSeconds)> expectedActions,
        DaggerfallDungeonMotionSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(expectedActions);
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        if (!string.Equals(snapshot.ProfileId, profileId, StringComparison.Ordinal))
            throw new ArgumentException($"Dungeon motion snapshot belongs to profile '{snapshot.ProfileId}', not '{profileId}'.", nameof(snapshot));

        Dictionary<string, double> expected = new(StringComparer.Ordinal);
        foreach ((string actionId, double durationSeconds) in expectedActions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
            if (!double.IsFinite(durationSeconds) || durationSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(expectedActions), "Expected action durations must be finite and non-negative.");
            if (!expected.TryAdd(actionId, durationSeconds))
                throw new ArgumentException($"Loaded dungeon motion repeats action '{actionId}'.", nameof(expectedActions));
        }

        Dictionary<string, DaggerfallDungeonMotionSave> saved = snapshot.Actions.ToDictionary(row => row.ActionId, StringComparer.Ordinal);
        if (saved.Count != expected.Count)
            throw new ArgumentException("Dungeon motion snapshot does not match the loaded motion actions.", nameof(snapshot));
        foreach ((string actionId, double duration) in expected)
        {
            if (!saved.Remove(actionId, out DaggerfallDungeonMotionSave? row))
                throw new ArgumentException($"Dungeon motion snapshot omits loaded action '{actionId}'.", nameof(snapshot));
            bool playing = row.Phase is DaggerfallDungeonMotionPhase.PlayingForward or DaggerfallDungeonMotionPhase.PlayingReverse;
            if (playing && (duration <= 0d || row.ElapsedSeconds >= duration))
                throw new ArgumentException($"Dungeon motion snapshot has an invalid playing time for action '{actionId}'.", nameof(snapshot));
            if (!playing && row.ElapsedSeconds != 0d)
                throw new ArgumentException($"Dungeon motion snapshot has elapsed time on settled action '{actionId}'.", nameof(snapshot));
        }
    }

    internal IReadOnlyList<EntityId> Targets => _actions.Values
        .OrderBy(state => state.Action.Id, StringComparer.Ordinal)
        .Select(state => state.Entity)
        .ToArray();

    internal bool TryGetTarget(string actionId, out EntityId entity)
    {
        if (_actions.TryGetValue(actionId, out MotionState? state))
        {
            entity = state.Entity;
            return true;
        }
        entity = default;
        return false;
    }

    internal bool TryGetTransform(string actionId, out Transform transform)
    {
        if (_actions.TryGetValue(actionId, out MotionState? state))
        {
            transform = _entities.Get(state.Entity, EngineComponentTypes.Transform);
            return true;
        }
        transform = default;
        return false;
    }

    /// <summary>Applies the source Start/End toggle. A request during either tween is ignored by DFU.</summary>
    internal DaggerfallDungeonMotionActivation Activate(string actionId)
    {
        MotionState state = Require(actionId);
        switch (state.Phase)
        {
            case DaggerfallDungeonMotionPhase.Start:
                state.Phase = DaggerfallDungeonMotionPhase.PlayingForward;
                state.EndpointIntent = DaggerfallDungeonMotionEndpoint.End;
                state.ElapsedSeconds = 0d;
                if (state.Specification.DurationSeconds == 0d)
                {
                    state.Phase = DaggerfallDungeonMotionPhase.End;
                    state.EndpointIntent = DaggerfallDungeonMotionEndpoint.End;
                    _entities.Set(state.Entity, EngineComponentTypes.Transform, Pose(state, 1d));
                    return DaggerfallDungeonMotionActivation.CompletedImmediately;
                }
                return DaggerfallDungeonMotionActivation.StartedForward;

            case DaggerfallDungeonMotionPhase.End:
                state.Phase = DaggerfallDungeonMotionPhase.PlayingReverse;
                state.EndpointIntent = DaggerfallDungeonMotionEndpoint.Start;
                state.ElapsedSeconds = 0d;
                if (state.Specification.DurationSeconds == 0d)
                {
                    state.Phase = DaggerfallDungeonMotionPhase.Start;
                    state.EndpointIntent = DaggerfallDungeonMotionEndpoint.Start;
                    _entities.Set(state.Entity, EngineComponentTypes.Transform, Pose(state, 0d));
                    return DaggerfallDungeonMotionActivation.CompletedImmediately;
                }
                return DaggerfallDungeonMotionActivation.StartedReverse;

            case DaggerfallDungeonMotionPhase.PlayingForward:
            case DaggerfallDungeonMotionPhase.PlayingReverse:
                return DaggerfallDungeonMotionActivation.IgnoredWhileMoving;

            default:
                throw new InvalidOperationException($"Dungeon action '{actionId}' has invalid motion phase {state.Phase}.");
        }
    }

    /// <summary>Advances active action motions inside the caller's Engine-admitted update.</summary>
    internal void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        foreach (MotionState state in _actions.Values)
        {
            if (state.Phase is not (DaggerfallDungeonMotionPhase.PlayingForward or DaggerfallDungeonMotionPhase.PlayingReverse))
                continue;
            if (deltaSeconds == 0d)
                continue;

            double duration = state.Specification.DurationSeconds;
            if (duration <= 0d)
                throw new InvalidOperationException($"Dungeon action '{state.Action.Id}' is playing without a positive duration.");

            double elapsed = Math.Min(duration, state.ElapsedSeconds + deltaSeconds);
            double progress = Progress(state, elapsed / duration);
            Transform current = Pose(state, progress);

            state.ElapsedSeconds = elapsed;
            if (elapsed >= duration)
            {
                state.Phase = state.EndpointIntent == DaggerfallDungeonMotionEndpoint.End
                    ? DaggerfallDungeonMotionPhase.End
                    : DaggerfallDungeonMotionPhase.Start;
                state.ElapsedSeconds = 0d;
            }

            _entities.Set(state.Entity, EngineComponentTypes.Transform, current);
        }
    }

    internal IReadOnlyDictionary<string, (DaggerfallDungeonMotionPhase Phase, DaggerfallDungeonMotionEndpoint EndpointIntent, double ElapsedSeconds)> State =>
        _actions.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value.Phase, pair.Value.EndpointIntent, pair.Value.ElapsedSeconds),
            StringComparer.Ordinal);

    internal DaggerfallDungeonMotionSnapshot Capture() => new(
        _profileId,
        _actions.Values
            .OrderBy(state => state.Action.Id, StringComparer.Ordinal)
            .Select(state => new DaggerfallDungeonMotionSave(
                state.Action.Id,
                state.Phase,
                state.EndpointIntent,
                state.ElapsedSeconds))
            .ToArray());

    private void Restore(DaggerfallDungeonMotionSnapshot snapshot)
    {
        snapshot.Validate();
        if (!string.Equals(snapshot.ProfileId, _profileId, StringComparison.Ordinal))
            throw new ArgumentException($"Dungeon motion snapshot belongs to profile '{snapshot.ProfileId}', not '{_profileId}'.", nameof(snapshot));

        Dictionary<string, DaggerfallDungeonMotionSave> saved = snapshot.Actions.ToDictionary(row => row.ActionId, StringComparer.Ordinal);
        if (saved.Count != _actions.Count)
            throw new ArgumentException("Dungeon motion snapshot does not match the loaded motion actions.", nameof(snapshot));

        foreach ((string actionId, MotionState state) in _actions)
        {
            if (!saved.Remove(actionId, out DaggerfallDungeonMotionSave? row))
                throw new ArgumentException($"Dungeon motion snapshot omits loaded action '{actionId}'.", nameof(snapshot));
            double duration = state.Specification.DurationSeconds;
            if (row.Phase is DaggerfallDungeonMotionPhase.PlayingForward or DaggerfallDungeonMotionPhase.PlayingReverse)
            {
                if (duration <= 0d || row.ElapsedSeconds >= duration)
                    throw new ArgumentException($"Dungeon motion snapshot has an invalid playing time for action '{actionId}'.", nameof(snapshot));
            }
            else if (row.ElapsedSeconds != 0d)
            {
                throw new ArgumentException($"Dungeon motion snapshot has elapsed time on settled action '{actionId}'.", nameof(snapshot));
            }

            state.Phase = row.Phase;
            state.EndpointIntent = row.EndpointIntent;
            state.ElapsedSeconds = row.ElapsedSeconds;
        }

        if (saved.Count != 0)
            throw new ArgumentException($"Dungeon motion snapshot names unknown action '{saved.Keys.First()}'.", nameof(snapshot));

        foreach (MotionState state in _actions.Values)
        {
            double progress = state.Phase switch
            {
                DaggerfallDungeonMotionPhase.Start => 0d,
                DaggerfallDungeonMotionPhase.End => 1d,
                DaggerfallDungeonMotionPhase.PlayingForward => state.ElapsedSeconds / state.Specification.DurationSeconds,
                DaggerfallDungeonMotionPhase.PlayingReverse => 1d - state.ElapsedSeconds / state.Specification.DurationSeconds,
                _ => throw new InvalidOperationException($"Dungeon action '{state.Action.Id}' has invalid motion phase {state.Phase}."),
            };
            _entities.Set(state.Entity, EngineComponentTypes.Transform, Pose(state, progress));
        }
    }

    private MotionState Require(string actionId) => _actions.TryGetValue(actionId, out MotionState? state)
        ? state
        : throw new KeyNotFoundException($"Dungeon motion action '{actionId}' is not bound in profile '{_profileId}'.");

    private static double Progress(MotionState state, double phaseProgress) =>
        state.Phase == DaggerfallDungeonMotionPhase.PlayingReverse ? 1d - phaseProgress : phaseProgress;

    private static Transform Pose(MotionState state, double progress)
    {
        float fraction = Math.Clamp(checked((float)progress), 0f, 1f);
        DaggerfallDungeonMotionSpecification specification = state.Specification;
        if (specification.Kind == DaggerfallDungeonMotionKind.Translation)
        {
            return state.StartTransform with
            {
                Translation = state.StartTransform.Translation + specification.Translation * fraction,
            };
        }

        Quaternion delta = Quaternion.CreateFromAxisAngle(specification.RotationAxis, specification.RotationRadians * fraction);
        return state.StartTransform with
        {
            Rotation = Quaternion.Normalize(state.StartTransform.Rotation * delta),
        };
    }

    private sealed class MotionState(
        DaggerfallDungeonActionDefinition action,
        EntityId entity,
        Transform startTransform,
        DaggerfallDungeonMotionSpecification specification)
    {
        internal DaggerfallDungeonActionDefinition Action { get; } = action;
        internal EntityId Entity { get; } = entity;
        internal Transform StartTransform { get; } = startTransform;
        internal DaggerfallDungeonMotionSpecification Specification { get; } = specification;
        internal DaggerfallDungeonMotionPhase Phase { get; set; } = DaggerfallDungeonMotionPhase.Start;
        internal DaggerfallDungeonMotionEndpoint EndpointIntent { get; set; } = DaggerfallDungeonMotionEndpoint.Start;
        internal double ElapsedSeconds { get; set; }
    }
}
