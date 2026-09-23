using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Engine admission for normalized dungeon action volumes. The action graph owns action/link
/// semantics; this owner supplies the real Engine entities and consumes Engine contact edges.
/// </summary>
internal sealed class DaggerfallDungeonActionTriggerRuntime : IDisposable
{
    private const float FlatHorizontalRadius = .75f;
    private const float FlatVerticalRadius = 1.5f;
    private static readonly EntityTypeId TriggerType = new("daggerfall.dungeon-action-trigger");

    private readonly EntityDirectory _entities;
    private readonly EntityStore _store;
    private readonly ISpatialService _spatial;
    private readonly SpatialMovementSystem _movement;
    private readonly Dictionary<DaggerfallWorldProfileKey, ProfileRuntime> _profiles = [];
    private ulong _triggerRevision;
    private ulong _latestTick;
    private DaggerfallWorldProfileKey _activeProfile;
    private bool _initialized;
    private bool _disposed;

    internal DaggerfallDungeonActionTriggerRuntime(
        EntityDirectory entities,
        ISpatialService spatial,
        SpatialMovementSystem movement,
        IEnumerable<(DaggerfallWorldProfileKey Key, PrivateersHoldInputs Inputs)> profiles,
        DaggerfallWorldProfileKey activeProfile,
        SpatialEntityCollider? restoredPlayerCollider = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _store = entities.Store;
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        ArgumentNullException.ThrowIfNull(profiles);
        activeProfile.Validate();
        EnsureSpatialComponents(_store);

        try
        {
            foreach ((DaggerfallWorldProfileKey key, PrivateersHoldInputs inputs) in profiles)
                AdmitProfile(key, inputs);
            if (!_profiles.ContainsKey(activeProfile))
                throw new InvalidOperationException($"Dungeon action trigger profile '{activeProfile.LogicalId}' is not admitted.");
            _activeProfile = activeProfile;
            RestoreActiveProfile(restoredPlayerCollider);
            _initialized = true;
        }
        catch
        {
            DisposeProfiles();
            throw;
        }
    }

    internal DaggerfallWorldProfileKey ActiveProfile
    {
        get
        {
            ThrowIfDisposed();
            return _activeProfile;
        }
    }

    /// <summary>Admits one additional profile before it can become active.</summary>
    internal void AdmitProfile(DaggerfallWorldProfileKey key, PrivateersHoldInputs inputs)
    {
        ThrowIfDisposed();
        key.Validate();
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.ProfileKey != key)
            throw new InvalidOperationException($"Action trigger profile '{key.LogicalId}' was paired with '{inputs.ProfileKey.LogicalId}'.");
        if (_profiles.ContainsKey(key)) return;

        ProfileRuntime? admitted = null;
        try
        {
            admitted = BuildProfile(key, inputs);
            if (_initialized)
                DeactivateProfileTriggers(admitted);
            _profiles.Add(key, admitted);
        }
        catch
        {
            admitted?.Dispose(_entities);
            throw;
        }
    }

    /// <summary>
    /// Replaces the active trigger set and rebases the Engine overlap baseline. Restore is
    /// intentionally fact-free; the next ordinary movement reconciliation emits a real entry.
    /// </summary>
    internal void ActivateProfile(DaggerfallWorldProfileKey key)
    {
        ThrowIfDisposed();
        key.Validate();
        if (!_profiles.ContainsKey(key))
            throw new InvalidOperationException($"Dungeon action trigger profile '{key.LogicalId}' is not admitted.");
        _activeProfile = key;
        RestoreActiveProfile();
    }

    /// <summary>
    /// Rebases the active profile's overlap baseline against a player pose restored after session
    /// construction. The restore is fact-free, so a save loaded inside a trigger does not replay
    /// its entry until the player leaves and re-enters the volume.
    /// </summary>
    internal void RebaseRestoredPlayer(PlayerControlState player, EntityId playerEntity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(player);
        RestoreActiveProfile(_movement.ProjectCharacterCollider(player, playerEntity.Value));
    }

    /// <summary>Returns the active profile's trigger entities for one direct/attack ray query.</summary>
    internal ReadOnlyMemory<SpatialEntityCollider> ActiveRayEntities()
    {
        ThrowIfDisposed();
        ProfileRuntime profile = RequireActiveProfile();
        SpatialEntityCollider[] entities = profile.Triggers
            .Select(Project)
            .ToArray();
        return entities;
    }

    /// <summary>Resolves a direct/attack ray hit to the action source admitted by this runtime.</summary>
    internal bool TryResolveAction(EntityId entity, out string actionId)
    {
        ThrowIfDisposed();
        ProfileRuntime profile = RequireActiveProfile();
        if (profile.ByEntity.TryGetValue(entity.Value, out TriggerRuntime? trigger))
        {
            actionId = trigger.Action.Id;
            return true;
        }

        actionId = string.Empty;
        return false;
    }

    /// <summary>
    /// Reconciles active trigger volumes and dispatches only Engine-reported enter facts for the
    /// player. Exit/continued facts remain Engine state and do not get mirrored into the graph.
    /// </summary>
    internal IReadOnlyList<DaggerfallDungeonActionDispatch> Reconcile(
        DaggerfallDungeonActionGraph graph,
        PlayerControlState player,
        EntityId playerEntity,
        ulong tick)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(player);
        ProfileRuntime profile = RequireActiveProfile();
        _latestTick = tick;
        if (!StringComparer.Ordinal.Equals(graph.ProfileId, _activeProfile.LogicalId))
            throw new InvalidOperationException($"Dungeon action graph '{graph.ProfileId}' is not active for trigger profile '{_activeProfile.LogicalId}'.");

        List<SpatialEntityCollider> entities = new(profile.Triggers.Count + 1);
        foreach (TriggerRuntime trigger in profile.Triggers.Where(trigger => trigger.ContactEvent is not null))
            entities.Add(Project(trigger));
        entities.Add(_movement.ProjectCharacterCollider(player, playerEntity.Value));

        SpatialTriggerReceipt receipt = _spatial.ReconcileTriggers(new SpatialTriggerReconcileRequest(
            _movement.Session,
            tick,
            SpatialTriggerCause.Movement,
            entities.ToArray()));
        _triggerRevision = receipt.Revision;

        List<DaggerfallDungeonActionDispatch> dispatches = [];
        for (uint index = 0; index < receipt.FactCount; index++)
        {
            SpatialTriggerFactAtReceipt fact = _spatial.ReadTriggerFactAt(
                new SpatialTriggerFactAtRequest(_movement.Session, index));
            if (!fact.Present || !fact.Enter || fact.Subject != playerEntity.Value)
                continue;
            if (!profile.ByEntity.TryGetValue(fact.Trigger, out TriggerRuntime? trigger)
                || trigger.ContactEvent is not DaggerfallDungeonActionEvent @event)
                continue;
            dispatches.Add(graph.Trigger(trigger.Action.Id, @event));
        }

        return dispatches;
    }

    private void DeactivateProfileTriggers(ProfileRuntime profile)
    {
        foreach (TriggerRuntime trigger in profile.Triggers.Where(trigger => trigger.ContactEvent is not null))
        {
            SpatialTriggerLifecycleReceipt receipt = _spatial.SetTriggerActive(new SpatialTriggerSetActiveRequest(
                _movement.Session,
                trigger.Entity.Value,
                _triggerRevision,
                Active: false,
                Tick: _latestTick));
            _triggerRevision = receipt.RevisionAfter;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeProfiles();
    }

    private ProfileRuntime BuildProfile(DaggerfallWorldProfileKey key, PrivateersHoldInputs inputs)
    {
        if (inputs.DungeonActions.Count > 4096)
            throw new InvalidOperationException($"Dungeon profile '{key.LogicalId}' admits {inputs.DungeonActions.Count} actions; Engine trigger definitions are bounded to 4096.");

        ProfileRuntime profile = new(key);
        try
        {
            foreach (DaggerfallDungeonActionDefinition action in inputs.DungeonActions.OrderBy(action => action.Id, StringComparer.Ordinal))
            {
                // Action-door geometry already has a canonical Door entity. Keeping a second
                // proxy would make a ray choose between two product identities at one surface.
                if (action.DoorId is not null) continue;
                if (!TryGeometry(action, inputs.DungeonMap, out Bounds bounds)) continue;

                DurableIdentityReference identity = new(DurableIdentityKind.Resource,
                    StableIdentity(key.LogicalId, action.Id));
                EntityId entity = _entities.Create(identity, TriggerType);
                Transform transform = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
                SpatialCollider collider = new(
                    bounds.Min,
                    bounds.Max,
                    0,
                    0,
                    Enabled: true,
                    StaticCollider: false,
                    Trigger: true);
                _store.Set(entity, EngineComponentTypes.Transform, transform);
                _store.Set(entity, EngineComponentTypes.SpatialCollider, collider);

                DaggerfallDungeonActionEvent? contactEvent = ContactEvent(action);
                TriggerRuntime trigger = new(action, identity, entity, contactEvent, bounds);
                profile.Triggers.Add(trigger);
                profile.ByEntity.Add(entity.Value, trigger);

                if (contactEvent is not null)
                {
                    _spatial.RegisterTrigger(new SpatialTriggerRegisterRequest(
                        _movement.Session,
                        entity.Value,
                        Scope(key),
                        "action-trigger",
                        SpatialTriggerGeometry.EntityBounds));
                }
            }
            return profile;
        }
        catch
        {
            profile.Dispose(_entities);
            throw;
        }
    }

    private void RestoreActiveProfile(SpatialEntityCollider? restoredPlayerCollider = null)
    {
        ProfileRuntime profile = RequireActiveProfile();
        ulong[] activeTriggers = profile.Triggers
            .Where(trigger => trigger.ContactEvent is not null)
            .Select(trigger => trigger.Entity.Value)
            .OrderBy(value => value)
            .ToArray();
        SpatialEntityCollider[] baseline = profile.Triggers
            .Where(trigger => trigger.ContactEvent is not null)
            .OrderBy(trigger => trigger.Entity.Value)
            .Select(Project)
            .ToArray();
        if (restoredPlayerCollider is { } player)
        {
            if (player.Entity == 0 || !player.Enabled || player.Trigger)
                throw new ArgumentException("A restored player collider must be enabled, non-trigger, and have a non-zero entity.", nameof(restoredPlayerCollider));
            baseline = [.. baseline, player];
        }

        SpatialTriggerRestoreReceipt receipt = _spatial.RestoreTriggers(new SpatialTriggerRestoreRequest(
            _movement.Session,
            _triggerRevision,
            activeTriggers,
            baseline));
        _triggerRevision = receipt.RevisionAfter;
    }

    private ProfileRuntime RequireActiveProfile() => _profiles.TryGetValue(_activeProfile, out ProfileRuntime? profile)
        ? profile
        : throw new InvalidOperationException($"Active dungeon action trigger profile '{_activeProfile.LogicalId}' is not admitted.");

    private SpatialEntityCollider Project(TriggerRuntime trigger)
    {
        Transform transform = _store.Get(trigger.Entity, EngineComponentTypes.Transform);
        SpatialCollider collider = _store.Get(trigger.Entity, EngineComponentTypes.SpatialCollider);
        Vector3 min = Vector3.Transform(collider.Min * transform.Scale, transform.Rotation) + transform.Translation;
        Vector3 max = min;
        foreach (Vector3 corner in Corners(collider.Min, collider.Max))
        {
            Vector3 point = Vector3.Transform(corner * transform.Scale, transform.Rotation) + transform.Translation;
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return new SpatialEntityCollider(
            trigger.Entity.Value,
            min,
            max,
            collider.CollisionGroup,
            collider.CollisionMask,
            collider.Enabled,
            collider.StaticCollider,
            collider.Trigger);
    }

    private void DisposeProfiles()
    {
        foreach (ProfileRuntime profile in _profiles.Values)
            profile.Dispose(_entities);
        _profiles.Clear();
    }

    private static bool TryGeometry(
        DaggerfallDungeonActionDefinition action,
        DaggerfallDungeonMapContent? map,
        out Bounds bounds)
    {
        if (action.IsFlat)
        {
            if (action.SourcePosition is not Vector3 source)
            {
                bounds = default;
                return false;
            }
            bounds = new Bounds(
                source - new Vector3(FlatHorizontalRadius, FlatVerticalRadius, FlatHorizontalRadius),
                source + new Vector3(FlatHorizontalRadius, FlatVerticalRadius, FlatHorizontalRadius));
            return true;
        }

        if (map is not null && ModelPlacementId(action.Id) is string placementId
            && map.GeometryPlacements.SingleOrDefault(geometry => geometry.PlacementId == placementId) is DaggerfallDungeonMapGeometry geometry)
        {
            bounds = new Bounds(geometry.BoundsMin, geometry.BoundsMax);
            return true;
        }

        bounds = default;
        return false;
    }

    private static DaggerfallDungeonActionEvent? ContactEvent(DaggerfallDungeonActionDefinition action)
    {
        uint trigger = action.DoorId is not null && action.TriggerFlag > 0x0A
            ? action.TriggerFlag & 0x0F
            : action.TriggerFlag;
        return trigger switch
        {
            (uint)DaggerfallDungeonTriggerFlag.Collision01 => DaggerfallDungeonActionEvent.WalkOn,
            (uint)DaggerfallDungeonTriggerFlag.Collision03
                or (uint)DaggerfallDungeonTriggerFlag.MultiTrigger
                or (uint)DaggerfallDungeonTriggerFlag.Collision09 => DaggerfallDungeonActionEvent.WalkInto,
            _ => null,
        };
    }

    private static string? ModelPlacementId(string actionId)
    {
        if (!actionId.StartsWith("action/", StringComparison.Ordinal)) return null;
        int separator = actionId.LastIndexOf('/');
        if (separator <= "action/".Length || separator == actionId.Length - 1) return null;
        ReadOnlySpan<char> suffix = actionId.AsSpan(separator + 1);
        if (!suffix.StartsWith("model-", StringComparison.Ordinal)) return null;
        return $"model/{actionId["action/".Length..separator]}/{suffix["model-".Length..]}";
    }

    private static string Scope(DaggerfallWorldProfileKey key) => Identifier($"daggerfall.action.{key.LogicalId}");

    private static string Identifier(string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(128, Math.Max(1, value.Length))];
        int written = 0;
        foreach (char character in value)
        {
            if (written == buffer.Length) break;
            buffer[written++] = char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'
                ? character
                : '-';
        }
        return written == 0 ? "action" : new string(buffer[..written]);
    }

    private static ulong StableIdentity(string profile, string action)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char value in $"dungeon-action-trigger:{profile}:{action}")
        {
            hash ^= value;
            hash *= prime;
        }
        return hash == 0 ? 1 : hash;
    }

    private static IEnumerable<Vector3> Corners(Vector3 min, Vector3 max)
    {
        yield return new Vector3(min.X, min.Y, min.Z);
        yield return new Vector3(min.X, min.Y, max.Z);
        yield return new Vector3(min.X, max.Y, min.Z);
        yield return new Vector3(min.X, max.Y, max.Z);
        yield return new Vector3(max.X, min.Y, min.Z);
        yield return new Vector3(max.X, min.Y, max.Z);
        yield return new Vector3(max.X, max.Y, min.Z);
        yield return new Vector3(max.X, max.Y, max.Z);
    }

    private static void EnsureSpatialComponents(EntityStore store)
    {
        EntityStoreDiagnostics diagnostics = store.Diagnostics(0);
        if (!diagnostics.Components.Any(component => component.Key == EngineComponentTypes.Transform.Key))
            store.Register(EngineComponentTypes.Transform);
        diagnostics = store.Diagnostics(0);
        if (!diagnostics.Components.Any(component => component.Key == EngineComponentTypes.SpatialCollider.Key))
            store.Register(EngineComponentTypes.SpatialCollider);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallDungeonActionTriggerRuntime));
    }

    private readonly record struct Bounds(Vector3 Min, Vector3 Max);

    private sealed class ProfileRuntime(DaggerfallWorldProfileKey key)
    {
        internal DaggerfallWorldProfileKey Key { get; } = key;
        internal List<TriggerRuntime> Triggers { get; } = [];
        internal Dictionary<ulong, TriggerRuntime> ByEntity { get; } = [];

        internal void Dispose(EntityDirectory entities)
        {
            foreach (TriggerRuntime trigger in Triggers)
                entities.Destroy(trigger.Identity);
            Triggers.Clear();
            ByEntity.Clear();
        }
    }

    private sealed record TriggerRuntime(
        DaggerfallDungeonActionDefinition Action,
        DurableIdentityReference Identity,
        EntityId Entity,
        DaggerfallDungeonActionEvent? ContactEvent,
        Bounds Bounds);
}
