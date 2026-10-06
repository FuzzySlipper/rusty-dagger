using System.Numerics;
using System.Runtime.CompilerServices;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Admits movable action models as real Engine entities, publishes their triangle collision
/// through the profile's SpatialSession, and owns only the current site's projection lifetime.
/// A site candidate can defer collision admission until its base content replacement succeeds.
/// </summary>
internal sealed class DaggerfallDungeonMotionProjection : IDisposable
{
    private const ulong CollisionAssetPrefix = 0xD800_0000_0000_0000UL;
    private const ulong CollisionInstancePrefix = 0xD900_0000_0000_0000UL;
    private const ulong IdentityMask = 0x00FF_FFFF_FFFF_FFFFUL;
    private static readonly EntityTypeId MotionModelType = new("daggerfall.dungeon-action-model");
    private static readonly ConditionalWeakTable<EntityStore, object> RegisteredStores = [];

    private readonly EntityDirectory _entities;
    private readonly EntityStore _store;
    private readonly ISpatialService _spatial;
    private readonly SpatialSession _session;
    private readonly DaggerfallDoorRuntime _doors;
    private readonly DaggerfallDungeonMotionRuntime _motion;
    private readonly Dictionary<string, DaggerfallDungeonActionModelDefinition> _modelsByAction;
    private readonly Dictionary<string, EntityId> _entitiesByAction;
    private readonly Dictionary<ulong, CollisionModel> _collisionModels;
    private readonly StaticMeshAsset[] _collisionAssets;
    private readonly Vector3[] _collisionVertices;
    private readonly Triangle[] _collisionTriangles;
    private readonly Dictionary<ulong, Transform> _residentTransforms = [];
    private readonly Dictionary<ulong, (Vector3 Linear, Vector3 Angular)> _meshVelocities = [];
    private bool _collisionAdmissionActive;
    private bool _suspended;
    private bool _disposed;

    internal DaggerfallDungeonMotionProjection(
        EntityDirectory entities,
        ISpatialService spatial,
        SpatialSession session,
        DaggerfallDoorRuntime doors,
        string profileId,
        IEnumerable<DaggerfallDungeonActionDefinition> actions,
        IEnumerable<DaggerfallDungeonActionModelDefinition> models,
        DaggerfallDungeonMotionSnapshot? restored = null,
        bool deferCollisionAdmission = false)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _store = entities.Store;
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _doors = doors ?? throw new ArgumentNullException(nameof(doors));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(models);
        EnsureSpatialComponents(_store);

        Dictionary<string, DaggerfallDungeonActionDefinition> definitions = actions
            .ToDictionary(action => action.Id, StringComparer.Ordinal);
        _modelsByAction = new Dictionary<string, DaggerfallDungeonActionModelDefinition>(StringComparer.Ordinal);
        _entitiesByAction = new Dictionary<string, EntityId>(StringComparer.Ordinal);
        _collisionModels = [];
        List<DaggerfallDungeonMotionBinding> bindings = [];
        List<DurableIdentityReference> createdIdentities = [];

        try
        {
            foreach (DaggerfallDungeonActionModelDefinition model in models.Select(model => model.Validate()).OrderBy(model => model.ActionId, StringComparer.Ordinal))
            {
                if (!_modelsByAction.TryAdd(model.ActionId, model))
                    throw new ArgumentException($"Dungeon action model '{model.ActionId}' is admitted more than once.", nameof(models));
                EntityId entity;
                DaggerfallDungeonActionDefinition action;
                if (model.DoorIdentity is DaggerfallRdbDoorId doorIdentity)
                {
                    DaggerfallDoorView door = _doors.Read(doorIdentity);
                    entity = door.Entity;
                    if (model.CollisionTriangles.Length != 0)
                        AddCollisionModel(profileId, model, entity);
                    continue;
                }

                if (!definitions.TryGetValue(model.ActionId, out action!))
                    throw new ArgumentException($"Dungeon action model '{model.ActionId}' has no action definition.", nameof(actions));
                bool isMotion = DaggerfallDungeonMotionPolicy.TryInterpret(action, model.Description, out _);

                DurableIdentityReference identity = MotionIdentity(profileId, model.ActionId);
                entity = _entities.Create(identity, MotionModelType);
                createdIdentities.Add(identity);
                _store.Set(entity, EngineComponentTypes.Transform, model.InitialTransform);
                bool collisionEnabled = model.CollisionTriangles.Length != 0;
                _store.Set(entity, EngineComponentTypes.SpatialCollider, new SpatialCollider(
                    model.LocalBoundsMin,
                    model.LocalBoundsMax,
                    uint.MaxValue,
                    uint.MaxValue,
                    collisionEnabled,
                    false,
                    false));
                _entitiesByAction.Add(model.ActionId, entity);
                if (isMotion) bindings.Add(new DaggerfallDungeonMotionBinding(action, entity, model.Description));
                if (collisionEnabled) AddCollisionModel(profileId, model, entity);
            }

            _motion = new DaggerfallDungeonMotionRuntime(_store, profileId, bindings, restored);
            (_collisionAssets, _collisionVertices, _collisionTriangles) = BuildAssets(_collisionModels.Values);
            if (!deferCollisionAdmission) RebuildCollisionResidency();
        }
        catch
        {
            foreach (DurableIdentityReference identity in createdIdentities) _entities.Destroy(identity);
            throw;
        }
    }

    internal string ProfileId => _motion.ProfileId;

    internal void Rebase(Vector3 delta)
    {
        _motion.Rebase(delta);
        foreach (EntityId entity in _entitiesByAction.Values)
        {
            if (entity.Value == 0) continue;
            Transform transform = _store.Get(entity, EngineComponentTypes.Transform);
            _store.Set(entity, EngineComponentTypes.Transform, transform with { Translation = transform.Translation + delta });
        }
        // Native origin commit shifted retained meshes; replacing with these same new local poses
        // also updates this projection's cache, so a later motion step cannot restore the old frame.
        if (!_suspended) RebuildCollisionResidency();
    }

    internal IReadOnlyList<(DaggerfallDungeonActionModelDefinition Model, EntityId Entity)> Visuals =>
        _entitiesByAction.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Where(pair => pair.Value.Value != 0)
            .Select(pair => (_modelsByAction[pair.Key], pair.Value))
            .ToArray();

    internal bool TryGetEntity(string actionId, out EntityId entity) => _entitiesByAction.TryGetValue(actionId, out entity);

    internal bool TryGetTransform(string actionId, out Transform transform)
    {
        if (_entitiesByAction.TryGetValue(actionId, out EntityId entity))
        {
            transform = _store.Get(entity, EngineComponentTypes.Transform);
            return true;
        }
        transform = default;
        return false;
    }

    /// <summary>
    /// Motion models contribute exact triangle collision and stable entity-bound support through
    /// Spatial residency. Do not also project their bounds as call-local AABB obstacles.
    /// </summary>
    internal CharacterStepEnvironment CharacterEnvironment()
    {
        ThrowIfDisposed();
        if (_suspended) return CharacterStepEnvironment.Empty;
        CharacterMeshInstance[] meshInstances = _collisionModels.Values
            .Where(model => _residentTransforms.ContainsKey(model.InstanceId))
            .OrderBy(model => model.Definition.ActionId, StringComparer.Ordinal)
            .Select(model =>
            {
                EntityId entity = model.Definition.DoorIdentity is DaggerfallRdbDoorId doorIdentity
                    ? _doors.Read(doorIdentity).Entity
                    : model.Entity;
                (Vector3 linear, Vector3 angular) = _meshVelocities.GetValueOrDefault(model.InstanceId);
                return new CharacterMeshInstance(model.InstanceId, entity.Value, linear, angular);
            })
            .ToArray();
        return new CharacterStepEnvironment(
            default,
            ReadOnlyMemory<CharacterObstacle>.Empty,
            meshInstances);
    }

    internal DaggerfallDungeonMotionActivation Activate(string actionId)
    {
        ThrowIfDisposed();
        if (_suspended) return DaggerfallDungeonMotionActivation.IgnoredWhileMoving;
        DaggerfallDungeonMotionActivation result = _motion.Activate(actionId);
        SyncCollisionInstances(deltaSeconds: 0d);
        return result;
    }

    internal void Advance(double deltaSeconds)
    {
        ThrowIfDisposed();
        if (_suspended) return;
        _motion.Advance(deltaSeconds);
        SyncCollisionInstances(deltaSeconds);
    }

    internal DaggerfallDungeonMotionSnapshot Capture() => _motion.Capture();

    /// <summary>Initializes this profile after its canonical content artifact has been replaced.</summary>
    internal void ActivateCollisionResidency()
    {
        ThrowIfDisposed();
        if (_suspended) return;
        RebuildCollisionResidency();
    }

    /// <summary>
    /// Retires movable model geometry and Engine entities while retaining the authored action phase.
    /// Re-admission creates fresh entities under the same durable resource identities.
    /// </summary>
    internal void Suspend()
    {
        ThrowIfDisposed();
        if (_suspended) return;
        RemoveCollisionResidency();
        foreach ((string actionId, EntityId entity) in _entitiesByAction.ToArray())
        {
            _entities.Destroy(MotionIdentity(ProfileId, actionId));
            _entitiesByAction[actionId] = default;
        }
        // The runtime is rebound by Resume after fresh entities exist; its durable phases remain
        // untouched while suspended.
        _suspended = true;
    }

    /// <summary>Recreates suspended movable model entities and reapplies their retained action phases.</summary>
    internal void Resume()
    {
        ThrowIfDisposed();
        if (!_suspended) return;
        Dictionary<string, EntityId> rebound = [];
        List<string> created = [];
        try
        {
        foreach ((string actionId, DaggerfallDungeonActionModelDefinition model) in _modelsByAction
                .Where(pair => pair.Value.DoorIdentity is null)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                DurableIdentityReference identity = MotionIdentity(ProfileId, actionId);
                EntityId entity = _entities.Create(identity, MotionModelType);
                _store.Set(entity, EngineComponentTypes.Transform, model.InitialTransform);
                _store.Set(entity, EngineComponentTypes.SpatialCollider, new SpatialCollider(
                    model.LocalBoundsMin, model.LocalBoundsMax, uint.MaxValue, uint.MaxValue,
                    model.CollisionTriangles.Length != 0, false, false));
                _entitiesByAction[actionId] = entity;
                rebound.Add(actionId, entity);
                created.Add(actionId);
            }
            foreach (CollisionModel model in _collisionModels.Values.Where(model => model.Definition.DoorIdentity is null))
            {
                if (!_entitiesByAction.TryGetValue(model.Definition.ActionId, out EntityId entity) || entity.Value == 0)
                    throw new InvalidOperationException($"Motion collision model '{model.Definition.ActionId}' was not rebound.");
                model.Entity = entity;
            }
            _motion.Rebind(rebound);
            _suspended = false;
            RebuildCollisionResidency();
        }
        catch
        {
            foreach (string actionId in created)
            {
                _entities.Destroy(MotionIdentity(ProfileId, actionId));
                _entitiesByAction[actionId] = default;
            }
            throw;
        }
    }

    /// <summary>
    /// Rebuilds every action asset and current instance after ReplaceContentArtifact, which
    /// replaces the complete static-mesh collider set in the Engine SpatialSession.
    /// </summary>
    internal void RebuildCollisionResidency()
    {
        ThrowIfDisposed();
        _residentTransforms.Clear();
        _meshVelocities.Clear();
        List<StaticMeshInstance> instances = BuildInstances().ToList();
        if (_collisionAssets.Length != 0 || instances.Count != 0)
        {
            _ = _spatial.ApplyCollisionResidency(new CollisionResidencyRequest(
                _session,
                _collisionAssets,
                _collisionVertices,
                _collisionTriangles,
                instances.ToArray(),
                Array.Empty<ulong>(),
                Array.Empty<ulong>()));
        }
        foreach (StaticMeshInstance instance in instances)
        {
            _residentTransforms.Add(instance.Id, instance.Transform);
            _meshVelocities.Add(instance.Id, (Vector3.Zero, Vector3.Zero));
        }
        _collisionAdmissionActive = true;
    }

    private void RemoveCollisionResidency()
    {
        if (!_collisionAdmissionActive) return;
        if (_collisionAssets.Length != 0 || _residentTransforms.Count != 0)
        {
            _ = _spatial.ApplyCollisionResidency(new CollisionResidencyRequest(
                _session,
                Array.Empty<StaticMeshAsset>(),
                Array.Empty<Vector3>(),
                Array.Empty<Triangle>(),
                Array.Empty<StaticMeshInstance>(),
                _collisionAssets.Select(asset => asset.Id).ToArray(),
                _collisionModels.Values.Select(model => model.InstanceId).ToArray()));
        }
        _residentTransforms.Clear();
        _meshVelocities.Clear();
        _collisionAdmissionActive = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        try { RemoveCollisionResidency(); }
        catch (Exception exception) { failures = [exception]; }
        foreach ((string actionId, EntityId entity) in _entitiesByAction)
        {
            if (entity.Value == 0) continue;
            try { _entities.Destroy(MotionIdentity(ProfileId, actionId)); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        _entitiesByAction.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private void AddCollisionModel(string profileId, DaggerfallDungeonActionModelDefinition model, EntityId entity)
    {
        ulong assetId = CollisionId(CollisionAssetPrefix, profileId, model.ActionId);
        ulong instanceId = CollisionId(CollisionInstancePrefix, profileId, model.ActionId);
        if (!_collisionModels.TryAdd(assetId, new CollisionModel(model, entity, assetId, instanceId)))
            throw new InvalidOperationException($"Dungeon action model '{model.ActionId}' collides with an existing dynamic collision asset identity.");
        if (_collisionModels.Values.Count(value => value.InstanceId == instanceId) != 1)
            throw new InvalidOperationException($"Dungeon action model '{model.ActionId}' collides with an existing dynamic collision instance identity.");
    }

    private (StaticMeshAsset[] Assets, Vector3[] Vertices, Triangle[] Triangles) BuildAssets(IEnumerable<CollisionModel> collisionModels)
    {
        List<StaticMeshAsset> assets = [];
        List<Vector3> vertices = [];
        List<Triangle> triangles = [];
        foreach (CollisionModel item in collisionModels.OrderBy(value => value.Definition.ActionId, StringComparer.Ordinal))
        {
            uint firstVertex = checked((uint)vertices.Count);
            uint firstTriangle = checked((uint)triangles.Count);
            vertices.AddRange(item.Definition.CollisionVertices);
            // The Engine slices each asset's own vertices and triangles out of these shared arrays, so a
            // triangle index stays local to its own asset exactly as the authored model carries it.
            // Offset indices name vertices the asset's slice does not hold, which Engine rejects
            // outright instead of resolving them against the neighbouring asset's vertices.
            triangles.AddRange(item.Definition.CollisionTriangles);
            assets.Add(new StaticMeshAsset(
                item.AssetId,
                firstVertex,
                checked((uint)item.Definition.CollisionVertices.Length),
                firstTriangle,
                checked((uint)item.Definition.CollisionTriangles.Length)));
        }
        return (assets.ToArray(), vertices.ToArray(), triangles.ToArray());
    }

    private StaticMeshInstance[] BuildInstances()
    {
        List<StaticMeshInstance> instances = [];
        foreach (CollisionModel item in _collisionModels.Values.OrderBy(value => value.Definition.ActionId, StringComparer.Ordinal))
        {
            if (item.Definition.DoorIdentity is DaggerfallRdbDoorId doorIdentity)
            {
                DaggerfallDoorView door = _doors.Read(doorIdentity);
                if (!door.CollisionEnabled) continue;
                instances.Add(new StaticMeshInstance(item.InstanceId, item.AssetId,
                    _store.Get(door.Entity, EngineComponentTypes.Transform)));
            }
            else
            {
                instances.Add(new StaticMeshInstance(item.InstanceId, item.AssetId,
                    _store.Get(item.Entity, EngineComponentTypes.Transform)));
            }
        }
        return instances.ToArray();
    }

    private void SyncCollisionInstances(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!_collisionAdmissionActive || _disposed || _collisionModels.Count == 0) return;
        List<StaticMeshInstance> upserts = [];
        List<ulong> removals = [];
        foreach (CollisionModel item in _collisionModels.Values.OrderBy(value => value.Definition.ActionId, StringComparer.Ordinal))
        {
            bool enabled = true;
            Transform transform;
            if (item.Definition.DoorIdentity is DaggerfallRdbDoorId doorIdentity)
            {
                DaggerfallDoorView door = _doors.Read(doorIdentity);
                enabled = door.CollisionEnabled;
                transform = _store.Get(door.Entity, EngineComponentTypes.Transform);
            }
            else
            {
                transform = _store.Get(item.Entity, EngineComponentTypes.Transform);
            }

            bool wasResident = _residentTransforms.TryGetValue(item.InstanceId, out Transform previous);
            if (!enabled)
            {
                if (wasResident)
                {
                    _residentTransforms.Remove(item.InstanceId);
                    removals.Add(item.InstanceId);
                }
                _meshVelocities.Remove(item.InstanceId);
                continue;
            }
            if (!wasResident)
            {
                upserts.Add(new StaticMeshInstance(item.InstanceId, item.AssetId, transform));
                _residentTransforms[item.InstanceId] = transform;
                _meshVelocities[item.InstanceId] = (Vector3.Zero, Vector3.Zero);
                continue;
            }

            (Vector3 linear, Vector3 angular) = deltaSeconds > 0d
                ? PoseVelocity(previous, transform, deltaSeconds)
                : (Vector3.Zero, Vector3.Zero);
            _meshVelocities[item.InstanceId] = (linear, angular);
            if (previous == transform) continue;
            upserts.Add(new StaticMeshInstance(item.InstanceId, item.AssetId, transform));
            _residentTransforms[item.InstanceId] = transform;
        }
        if (upserts.Count == 0 && removals.Count == 0) return;
        _ = _spatial.ApplyCollisionResidency(new CollisionResidencyRequest(
            _session,
            Array.Empty<StaticMeshAsset>(),
            Array.Empty<Vector3>(),
            Array.Empty<Triangle>(),
            upserts.ToArray(),
            Array.Empty<ulong>(),
            removals.ToArray()));
    }

    private static (Vector3 Linear, Vector3 Angular) PoseVelocity(Transform previous, Transform current, double deltaSeconds)
    {
        float inverseDelta = checked((float)(1d / deltaSeconds));
        Vector3 linear = (current.Translation - previous.Translation) * inverseDelta;
        Quaternion delta = Quaternion.Normalize(current.Rotation * Quaternion.Inverse(previous.Rotation));
        if (delta.W < 0f) delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        float halfAngle = MathF.Acos(Math.Clamp(delta.W, -1f, 1f));
        float sine = MathF.Sin(halfAngle);
        if (MathF.Abs(sine) <= 1e-6f) return (linear, Vector3.Zero);
        Vector3 axis = new(delta.X / sine, delta.Y / sine, delta.Z / sine);
        Vector3 angular = axis * (2f * halfAngle * inverseDelta);
        return (linear, angular);
    }

    private static void EnsureSpatialComponents(EntityStore store)
    {
        _ = RegisteredStores.GetValue(store, static value =>
        {
            EntityStoreDiagnostics diagnostics = value.Diagnostics(0);
            if (!diagnostics.Components.Any(component => component.Key == EngineComponentTypes.Transform.Key))
                value.Register(EngineComponentTypes.Transform);
            diagnostics = value.Diagnostics(0);
            if (!diagnostics.Components.Any(component => component.Key == EngineComponentTypes.SpatialCollider.Key))
                value.Register(EngineComponentTypes.SpatialCollider);
            return new object();
        });
    }

    private static DurableIdentityReference MotionIdentity(string profileId, string actionId) =>
        new DurableIdentityReference(DurableIdentityKind.Resource, HashIdentity($"dungeon-action-motion:{profileId}:{actionId}")).Validate();

    private static ulong CollisionId(ulong prefix, string profileId, string actionId)
    {
        ulong hash = HashIdentity($"dungeon-action-motion:{profileId}:{actionId}") & IdentityMask;
        return prefix | (hash == 0 ? 1UL : hash);
    }

    private static ulong HashIdentity(string value) => StableHash.NonZeroFnv1a64(value);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallDungeonMotionProjection));
    }

    private sealed class CollisionModel(
        DaggerfallDungeonActionModelDefinition definition,
        EntityId entity,
        ulong assetId,
        ulong instanceId)
    {
        internal DaggerfallDungeonActionModelDefinition Definition { get; } = definition;
        internal EntityId Entity { get; set; } = entity;
        internal ulong AssetId { get; } = assetId;
        internal ulong InstanceId { get; } = instanceId;
    }
}
