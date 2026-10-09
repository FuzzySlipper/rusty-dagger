using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>One state of a city gate: the mesh it draws and the model-local collision it blocks with.</summary>
internal sealed record DaggerfallCityGateState(DaggerfallDoorVisual Visual, Vector3 LocalBoundsMin, Vector3 LocalBoundsMax,
    Vector3[] CollisionVertices, Triangle[] CollisionTriangles)
{
    internal DaggerfallCityGateState Validate()
    {
        Visual.Validate();
        if (CollisionTriangles.Length == 0) throw new ArgumentException("A city gate state must block with its own collision.", nameof(CollisionTriangles));
        if (CollisionTriangles.Any(triangle => Math.Max(triangle.A, Math.Max(triangle.B, triangle.C)) >= CollisionVertices.Length))
            throw new ArgumentException("A city gate collision triangle names a vertex the state does not carry.", nameof(CollisionTriangles));
        return this;
    }
}

/// <summary>
/// One city gate placed in a profile: its pose in the profile frame and its open and closed states. The donor's
/// <c>DaggerfallCityGate</c> swaps the placement's model between them at dusk and dawn.
/// </summary>
internal sealed record DaggerfallCityGateDefinition(string Id, Transform Pose, DaggerfallCityGateState Open, DaggerfallCityGateState Closed)
{
    internal DaggerfallCityGateDefinition Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        Open.Validate();
        Closed.Validate();
        return this;
    }

    /// <summary>Gates close for the night and open again by day (<c>DaggerfallCityGate.Update</c>).</summary>
    internal static bool OpenAt(DaggerfallCalendar calendar) => calendar.IsDay;
}

/// <summary>
/// A profile's city gates as Engine entities whose triangle collision is the current state's, published through
/// the profile's SpatialSession. Both states' collision assets stay resident; a state change swaps which one the
/// gate's instance places. The gates' state follows the session calendar and is not saved.
/// </summary>
internal sealed class DaggerfallCityGates : IDisposable
{
    private const ulong AssetPrefix = 0xDA00_0000_0000_0000UL;
    private const ulong InstancePrefix = 0xDB00_0000_0000_0000UL;
    private const ulong IdentityMask = 0x00FF_FFFF_FFFF_FFFFUL;
    private static readonly EntityTypeId GateType = new("daggerfall.city-gate");

    private readonly EntityDirectory _entities;
    private readonly EntityStore _store;
    private readonly ISpatialService _spatial;
    private readonly SpatialSession _session;
    private readonly string _profileId;
    private readonly Gate[] _gates;
    private readonly StaticMeshAsset[] _assets;
    private readonly Vector3[] _vertices;
    private readonly Triangle[] _triangles;
    private bool _resident;
    private bool _suspended;
    private bool _disposed;

    internal DaggerfallCityGates(EntityDirectory entities, ISpatialService spatial, SpatialSession session, string profileId,
        IEnumerable<DaggerfallCityGateDefinition> gates, bool open)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _store = entities.Store;
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _profileId = profileId;
        Open = open;
        _gates = [.. (gates ?? throw new ArgumentNullException(nameof(gates))).Select(gate => gate.Validate())
            .OrderBy(gate => gate.Id, StringComparer.Ordinal).Select(gate => new Gate(gate, gate.Pose))];
        if (_gates.Select(gate => gate.Definition.Id).Distinct(StringComparer.Ordinal).Count() != _gates.Length)
            throw new ArgumentException($"Profile '{profileId}' repeats a city gate.", nameof(gates));
        List<StaticMeshAsset> assets = [];
        List<Vector3> vertices = [];
        List<Triangle> triangles = [];
        foreach (Gate gate in _gates)
            foreach ((bool state, DaggerfallCityGateState value) in new[] { (true, gate.Definition.Open), (false, gate.Definition.Closed) })
            {
                // Each asset slices its own vertices and triangles out of the shared arrays, its indices local to it.
                assets.Add(new StaticMeshAsset(Id(AssetPrefix, gate.Definition.Id, state), checked((uint)vertices.Count),
                    checked((uint)value.CollisionVertices.Length), checked((uint)triangles.Count), checked((uint)value.CollisionTriangles.Length)));
                vertices.AddRange(value.CollisionVertices);
                triangles.AddRange(value.CollisionTriangles);
            }

        (_assets, _vertices, _triangles) = ([.. assets], [.. vertices], [.. triangles]);
        if (_gates.Length != 0) EnsureComponents(_store);
        try { CreateEntities(); }
        catch
        {
            DestroyEntities();
            throw;
        }
    }

    /// <summary>Whether the gates stand open.</summary>
    internal bool Open { get; private set; }

    /// <summary>Each gate's entity, its current state's visual and its pose in the session's frame.</summary>
    internal IEnumerable<(DaggerfallCityGateDefinition Gate, EntityId Entity, DaggerfallDoorVisual Visual, Transform Pose)> Visuals =>
        _suspended ? [] : _gates.Select(gate => (gate.Definition, gate.Entity, State(gate).Visual, gate.Pose));

    /// <summary>Admits both states' collision and places each gate's current one, once the profile's own artifact is placed.</summary>
    internal void ActivateCollisionResidency()
    {
        ThrowIfDisposed();
        if (_suspended || _gates.Length == 0) return;
        Apply(_assets, _vertices, _triangles, [.. _gates.Select(Instance)], [], []);
        _resident = true;
    }

    /// <summary>Opens or closes every gate; a change swaps the collision each gate places.</summary>
    internal void Sync(bool open)
    {
        ThrowIfDisposed();
        if (open == Open) return;
        ulong[] removed = [.. _gates.Select(gate => Id(InstancePrefix, gate.Definition.Id, Open))];
        Open = open;
        foreach (Gate gate in _gates) SetCollider(gate);
        if (!_resident) return;
        Apply([], [], [], [.. _gates.Select(Instance)], [], removed);
    }

    internal void Rebase(Vector3 delta)
    {
        ThrowIfDisposed();
        foreach (Gate gate in _gates)
        {
            gate.Pose = gate.Pose with { Translation = gate.Pose.Translation + delta };
            if (gate.Entity.Value != 0) _store.Set(gate.Entity, EngineComponentTypes.Transform, gate.Pose);
        }

        if (_resident) Apply([], [], [], [.. _gates.Select(Instance)], [], []);
    }

    /// <summary>The current gates' triangle instances, bound to their entities, for the one admitted character step.</summary>
    internal CharacterStepEnvironment CharacterEnvironment() => _suspended || !_resident || _gates.Length == 0
        ? CharacterStepEnvironment.Empty
        : new CharacterStepEnvironment(default, ReadOnlyMemory<CharacterObstacle>.Empty,
            _gates.Select(gate => new CharacterMeshInstance(Id(InstancePrefix, gate.Definition.Id, Open), gate.Entity.Value, Vector3.Zero, Vector3.Zero)).ToArray());

    /// <summary>Retires the gates' collision and entities with an unloaded location; resuming recreates them.</summary>
    internal void Suspend()
    {
        ThrowIfDisposed();
        if (_suspended) return;
        RemoveCollisionResidency();
        DestroyEntities();
        _suspended = true;
    }

    internal void Resume()
    {
        ThrowIfDisposed();
        if (!_suspended) return;
        _suspended = false;
        try
        {
            CreateEntities();
            ActivateCollisionResidency();
        }
        catch
        {
            DestroyEntities();
            _suspended = true;
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        try { RemoveCollisionResidency(); }
        catch (Exception exception) { failures = [exception]; }
        try { DestroyEntities(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private void Apply(StaticMeshAsset[] assets, Vector3[] vertices, Triangle[] triangles, StaticMeshInstance[] upserts,
        ulong[] removedAssets, ulong[] removedInstances) =>
        _ = _spatial.ApplyCollisionResidency(new CollisionResidencyRequest(_session, assets, vertices, triangles, upserts, removedAssets, removedInstances));

    private DaggerfallCityGateState State(Gate gate) => Open ? gate.Definition.Open : gate.Definition.Closed;

    private StaticMeshInstance Instance(Gate gate) =>
        new(Id(InstancePrefix, gate.Definition.Id, Open), Id(AssetPrefix, gate.Definition.Id, Open), gate.Pose);

    private void CreateEntities()
    {
        foreach (Gate gate in _gates)
        {
            gate.Entity = _entities.Create(Identity(gate.Definition.Id), GateType);
            _store.Set(gate.Entity, EngineComponentTypes.Transform, gate.Pose);
            SetCollider(gate);
        }
    }

    private void SetCollider(Gate gate)
    {
        if (gate.Entity.Value == 0) return;
        DaggerfallCityGateState state = State(gate);
        _store.Set(gate.Entity, EngineComponentTypes.SpatialCollider,
            new SpatialCollider(state.LocalBoundsMin, state.LocalBoundsMax, uint.MaxValue, uint.MaxValue, true, false, false));
    }

    private void DestroyEntities()
    {
        foreach (Gate gate in _gates.Where(gate => gate.Entity.Value != 0))
        {
            _entities.Destroy(Identity(gate.Definition.Id));
            gate.Entity = default;
        }
    }

    private void RemoveCollisionResidency()
    {
        if (!_resident) return;
        Apply([], [], [], [], [.. _assets.Select(asset => asset.Id)], [.. _gates.Select(gate => Id(InstancePrefix, gate.Definition.Id, Open))]);
        _resident = false;
    }

    private DurableIdentityReference Identity(string gate) =>
        new DurableIdentityReference(DurableIdentityKind.Resource, StableHash.NonZeroFnv1a64($"city-gate:{_profileId}:{gate}")).Validate();

    private ulong Id(ulong prefix, string gate, bool open)
    {
        ulong hash = StableHash.NonZeroFnv1a64($"city-gate:{_profileId}:{gate}:{(open ? "open" : "closed")}") & IdentityMask;
        return prefix | (hash == 0 ? 1UL : hash);
    }

    private static void EnsureComponents(EntityStore store)
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
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallCityGates));
    }

    private sealed class Gate(DaggerfallCityGateDefinition definition, Transform pose)
    {
        internal DaggerfallCityGateDefinition Definition { get; } = definition;
        internal Transform Pose { get; set; } = pose;
        internal EntityId Entity { get; set; }
    }
}
