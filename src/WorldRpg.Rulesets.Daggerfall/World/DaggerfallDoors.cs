using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>A source-stable identity for one normalized RDB action-door model.</summary>
internal readonly record struct DaggerfallRdbDoorId(string SourceKey, int BlockX, int BlockZ, int ModelIndex)
{
    public override string ToString() => $"{SourceKey}@{BlockX},{BlockZ}:{ModelIndex}";
}

internal enum DaggerfallDoorKind { Normal, Special }
internal enum DaggerfallDoorMotion { Closed, Opening, Open, Closing }
internal enum DaggerfallDoorOperationSource { Player, Spell, DungeonAction }
internal enum DaggerfallDoorOperationKind { Open, Close, Lock, Unlock, Bash }
internal enum DaggerfallDoorOperationResult
{
    Started,
    AlreadyOpen,
    AlreadyClosed,
    AlreadyLocked,
    AlreadyUnlocked,
    Locked,
    MagicallyHeld,
    SpecialDoor,
    BashFailed,
}

/// <summary>One material binding for an individually projected normalized door mesh.</summary>
internal readonly record struct DaggerfallDoorMaterialBinding(uint MeshSlot, uint WorldMaterialSlot);
/// <summary>Normalized source action facts retained for the dungeon action-graph owner.</summary>
internal readonly record struct DaggerfallDoorActionSource(byte Axis, ushort Duration, ushort Magnitude, int NextObjectOffset, byte Flags);

/// <summary>Content-backed visual that follows the same Engine transform as its door collider.</summary>
internal sealed record DaggerfallDoorVisual(string Path, ContentSha256 Sha256, IReadOnlyList<DaggerfallDoorMaterialBinding> Materials)
{
    internal DaggerfallDoorVisual Validate()
    {
        if (string.IsNullOrWhiteSpace(Path)) throw new ArgumentException("A door visual needs admitted content.", nameof(Path));
        ArgumentNullException.ThrowIfNull(Materials);
        if (Materials.Count == 0 || Materials.Select(binding => binding.MeshSlot).Distinct().Count() != Materials.Count)
            throw new ArgumentException("A door visual needs distinct mesh material slots.", nameof(Materials));
        return this;
    }
}

/// <summary>Authoritative source-derived shape and policy inputs for one runtime RDB door.</summary>
internal sealed record DaggerfallRdbDoorDefinition(
    DaggerfallRdbDoorId Id,
    Vector3 Position,
    Vector3 RotationDegrees,
    Vector3 BoundsMin,
    Vector3 BoundsMax,
    DaggerfallDoorKind Kind,
    int StartingLockValue = 0,
    float OpenAngleDegrees = -90F,
    float OpenDurationSeconds = 1.5F,
    DaggerfallDoorVisual? Visual = null,
    DaggerfallDoorActionSource? Action = null)
{
    internal DaggerfallRdbDoorDefinition Validate()
    {
        DaggerfallDoorIdentity.Validate(Id);
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        if (!IsFinite(Position) || !IsFinite(RotationDegrees) || !IsFinite(BoundsMin) || !IsFinite(BoundsMax))
            throw new ArgumentOutOfRangeException(nameof(Position), "Door pose and bounds must be finite.");
        if (BoundsMin.X >= BoundsMax.X || BoundsMin.Y >= BoundsMax.Y || BoundsMin.Z >= BoundsMax.Z)
            throw new ArgumentException("Door collision bounds must have positive extent.", nameof(BoundsMin));
        if (StartingLockValue < 0) throw new ArgumentOutOfRangeException(nameof(StartingLockValue));
        if (!float.IsFinite(OpenAngleDegrees) || OpenAngleDegrees == 0F) throw new ArgumentOutOfRangeException(nameof(OpenAngleDegrees));
        if (!float.IsFinite(OpenDurationSeconds) || OpenDurationSeconds <= 0F) throw new ArgumentOutOfRangeException(nameof(OpenDurationSeconds));
        if (Kind == DaggerfallDoorKind.Special && StartingLockValue != 0)
            throw new ArgumentException("A special door has no lock state.", nameof(StartingLockValue));
        Visual?.Validate();
        return this;
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

/// <summary>Current meaningful door state; progress belongs to the selected terminal state and survives unload.</summary>
internal sealed record DaggerfallDoorSave(
    string SourceKey,
    int BlockX,
    int BlockZ,
    int ModelIndex,
    DaggerfallDoorMotion Motion,
    float Progress,
    int LockValue,
    ulong BashAttempts)
{
    internal DaggerfallRdbDoorId Id => new(SourceKey, BlockX, BlockZ, ModelIndex);

    internal DaggerfallDoorSave Validate()
    {
        DaggerfallDoorIdentity.Validate(Id);
        if (!Enum.IsDefined(Motion)) throw new ArgumentOutOfRangeException(nameof(Motion));
        if (!float.IsFinite(Progress) || Progress < 0F || Progress > 1F) throw new ArgumentOutOfRangeException(nameof(Progress));
        if (LockValue < 0) throw new ArgumentOutOfRangeException(nameof(LockValue));
        if (Motion == DaggerfallDoorMotion.Closed && Progress != 0F || Motion == DaggerfallDoorMotion.Open && Progress != 1F)
            throw new ArgumentException("A terminal door motion must carry its terminal progress.");
        return this;
    }
}

/// <summary>Read-only projection used by activation, presentation and the one Engine collision source.</summary>
internal readonly record struct DaggerfallDoorView(
    DaggerfallRdbDoorId Id,
    EntityId Entity,
    Transform Pose,
    DaggerfallDoorMotion Motion,
    float Progress,
    int LockValue,
    bool CollisionEnabled,
    DaggerfallDoorKind Kind)
{
    internal bool IsLocked => LockValue > 0;
    internal bool IsMagicallyHeld => LockValue >= 20;
}

/// <summary>
/// Daggerfall door authority over normalized RDB identities. Its Engine entity provides the one
/// pose/collider source; its matching CharacterObstacle is borrowed by the admitted spatial step.
/// </summary>
internal sealed class DaggerfallDoorRuntime : IDisposable
{
    private const int DungeonActionLockValue = 16;
    private const ulong BashRandomSeed = 0x444F4F52UL;
    private const string BashRandomScope = "daggerfall.door.bash.v1";
    private readonly EntityStore _store;
    private readonly IRandomService _random;
    private readonly Dictionary<DaggerfallRdbDoorId, Door> _doors = [];
    private bool _disposed;

    internal DaggerfallDoorRuntime(EntityStore store, IRandomService random, IEnumerable<DaggerfallRdbDoorDefinition> definitions,
        IEnumerable<DaggerfallDoorSave>? restored = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        // These exact SDK component descriptors are registered before any door entity exists.
        // The actor store otherwise contains only product components, and an automatic descriptor
        // would not be usable by the named Engine projection adapters.
        _store.Register(EngineComponentTypes.Transform);
        _store.Register(EngineComponentTypes.SpatialCollider);
        ArgumentNullException.ThrowIfNull(definitions);
        Dictionary<DaggerfallRdbDoorId, DaggerfallDoorSave> saves = (restored ?? [])
            .Select(save => save.Validate())
            .ToDictionary(save => save.Id);
        foreach (DaggerfallRdbDoorDefinition definition in definitions.Select(definition => definition.Validate()).OrderBy(definition => definition.Id.SourceKey, StringComparer.Ordinal).ThenBy(definition => definition.Id.BlockX).ThenBy(definition => definition.Id.BlockZ).ThenBy(definition => definition.Id.ModelIndex))
        {
            if (saves.Remove(definition.Id, out DaggerfallDoorSave? save))
                Add(definition, save);
            else
                Add(definition, null);
        }
        if (saves.Count != 0)
            throw new ArgumentException($"The selected content has no RDB door '{saves.Keys.First()}' saved by this session.", nameof(restored));
    }

    internal IEnumerable<DaggerfallDoorView> All => _doors.Values.OrderBy(door => door.Definition.Id.SourceKey, StringComparer.Ordinal).ThenBy(door => door.Definition.Id.BlockX).ThenBy(door => door.Definition.Id.BlockZ).ThenBy(door => door.Definition.Id.ModelIndex).Select(View);

    internal bool TryRead(DaggerfallRdbDoorId id, out DaggerfallDoorView view)
    {
        if (_doors.TryGetValue(id, out Door? door)) { view = View(door); return true; }
        view = default;
        return false;
    }

    internal DaggerfallDoorView Read(DaggerfallRdbDoorId id) => TryRead(id, out DaggerfallDoorView view)
        ? view : throw new KeyNotFoundException($"RDB door '{id}' is not loaded.");

    internal DaggerfallDoorOperationResult Open(DaggerfallRdbDoorId id, DaggerfallDoorOperationSource source)
    {
        Door door = Require(id);
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        if (door.Definition.Kind == DaggerfallDoorKind.Special && source != DaggerfallDoorOperationSource.DungeonAction)
            return DaggerfallDoorOperationResult.SpecialDoor;
        if (source != DaggerfallDoorOperationSource.DungeonAction && door.LockValue >= 20)
            return DaggerfallDoorOperationResult.MagicallyHeld;
        if (source != DaggerfallDoorOperationSource.DungeonAction && door.LockValue > 0)
            return DaggerfallDoorOperationResult.Locked;
        if (door.Motion == DaggerfallDoorMotion.Open || door.Motion == DaggerfallDoorMotion.Opening)
            return DaggerfallDoorOperationResult.AlreadyOpen;
        if (source == DaggerfallDoorOperationSource.DungeonAction) door.LockValue = 0;
        door.Motion = DaggerfallDoorMotion.Opening;
        Apply(door);
        return DaggerfallDoorOperationResult.Started;
    }

    internal DaggerfallDoorOperationResult Close(DaggerfallRdbDoorId id, DaggerfallDoorOperationSource source)
    {
        Door door = Require(id);
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        if (door.Definition.Kind == DaggerfallDoorKind.Special && source != DaggerfallDoorOperationSource.DungeonAction)
            return DaggerfallDoorOperationResult.SpecialDoor;
        if (door.Motion == DaggerfallDoorMotion.Closed || door.Motion == DaggerfallDoorMotion.Closing)
            return DaggerfallDoorOperationResult.AlreadyClosed;
        door.Motion = DaggerfallDoorMotion.Closing;
        // The linked action restores the authored lock at the same operation boundary, matching
        // DaggerfallAction.CloseDoor while its visual close tween continues.
        if (source == DaggerfallDoorOperationSource.DungeonAction) door.LockValue = door.Definition.StartingLockValue;
        Apply(door);
        return DaggerfallDoorOperationResult.Started;
    }

    internal DaggerfallDoorOperationResult Lock(DaggerfallRdbDoorId id, DaggerfallDoorOperationSource source, int lockValue = DungeonActionLockValue)
    {
        Door door = Require(id);
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        if (lockValue <= 0) throw new ArgumentOutOfRangeException(nameof(lockValue));
        if (door.Definition.Kind == DaggerfallDoorKind.Special) return DaggerfallDoorOperationResult.SpecialDoor;
        if (door.LockValue > 0) return DaggerfallDoorOperationResult.AlreadyLocked;
        door.LockValue = lockValue;
        Apply(door);
        return DaggerfallDoorOperationResult.Started;
    }

    internal DaggerfallDoorOperationResult Unlock(DaggerfallRdbDoorId id, DaggerfallDoorOperationSource source)
    {
        Door door = Require(id);
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        if (door.Definition.Kind == DaggerfallDoorKind.Special) return DaggerfallDoorOperationResult.SpecialDoor;
        if (door.LockValue == 0) return DaggerfallDoorOperationResult.AlreadyUnlocked;
        if (source != DaggerfallDoorOperationSource.DungeonAction && door.LockValue >= 20)
            return DaggerfallDoorOperationResult.MagicallyHeld;
        door.LockValue = 0;
        Apply(door);
        return DaggerfallDoorOperationResult.Started;
    }

    internal DaggerfallDoorOperationResult Bash(DaggerfallRdbDoorId id)
    {
        Door door = Require(id);
        if (door.Definition.Kind == DaggerfallDoorKind.Special) return DaggerfallDoorOperationResult.SpecialDoor;
        if (door.Motion == DaggerfallDoorMotion.Open) return Close(id, DaggerfallDoorOperationSource.Player);
        if (door.LockValue >= 20) return DaggerfallDoorOperationResult.MagicallyHeld;
        int chance = 20 - door.LockValue;
        ulong attempt = checked(++door.BashAttempts);
        int roll = checked((int)_random.DrawKeyed(new KeyedRngRequest(BashRandomSeed, BashRandomScope, $"{door.Definition.Id}:{attempt}", 1, 100)).Value);
        if (roll > chance) return DaggerfallDoorOperationResult.BashFailed;
        door.LockValue = 0;
        return Open(id, DaggerfallDoorOperationSource.Player);
    }

    /// <summary>Advances the one admitted door motion phase before activation or character stepping reads it.</summary>
    internal void Advance(double deltaSeconds)
    {
        if (_disposed) return;
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        foreach (Door door in _doors.Values)
        {
            float amount = checked((float)(deltaSeconds / door.Definition.OpenDurationSeconds));
            switch (door.Motion)
            {
                case DaggerfallDoorMotion.Opening:
                    door.Progress = MathF.Min(1F, door.Progress + amount);
                    if (door.Progress == 1F) door.Motion = DaggerfallDoorMotion.Open;
                    Apply(door);
                    break;
                case DaggerfallDoorMotion.Closing:
                    door.Progress = MathF.Max(0F, door.Progress - amount);
                    if (door.Progress == 0F) door.Motion = DaggerfallDoorMotion.Closed;
                    Apply(door);
                    break;
            }
        }
    }

    internal CharacterStepEnvironment CharacterEnvironment()
    {
        ThrowIfDisposed();
        CharacterObstacle[] obstacles = _doors.Values.OrderBy(door => door.Entity.Value).Select(door =>
        {
            Transform pose = _store.Get(door.Entity, EngineComponentTypes.Transform);
            SpatialCollider collider = _store.Get(door.Entity, EngineComponentTypes.SpatialCollider);
            return new CharacterObstacle(door.Entity.Value, pose, collider.Min, collider.Max, collider.Enabled, Vector3.Zero, Vector3.Zero);
        }).ToArray();
        return new CharacterStepEnvironment(default, obstacles);
    }

    internal DaggerfallDoorSave[] Capture() => _doors.Values
        .OrderBy(door => door.Definition.Id.SourceKey, StringComparer.Ordinal).ThenBy(door => door.Definition.Id.BlockX).ThenBy(door => door.Definition.Id.BlockZ).ThenBy(door => door.Definition.Id.ModelIndex)
        .Select(door => new DaggerfallDoorSave(door.Definition.Id.SourceKey, door.Definition.Id.BlockX, door.Definition.Id.BlockZ, door.Definition.Id.ModelIndex, door.Motion, door.Progress, door.LockValue, door.BashAttempts))
        .ToArray();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Door door in _doors.Values)
            if (_store.IsAlive(door.Entity)) _store.Destroy(door.Entity);
        _doors.Clear();
    }

    private void Add(DaggerfallRdbDoorDefinition definition, DaggerfallDoorSave? saved)
    {
        if (!_doors.TryAdd(definition.Id, null!)) throw new InvalidOperationException($"Normalized RDB content repeats door '{definition.Id}'.");
        DaggerfallDoorMotion motion = saved?.Motion ?? DaggerfallDoorMotion.Closed;
        float progress = saved?.Progress ?? 0F;
        int lockValue = saved?.LockValue ?? definition.StartingLockValue;
        if (definition.Kind == DaggerfallDoorKind.Special && lockValue != 0)
            throw new ArgumentException($"Special door '{definition.Id}' cannot restore a lock.", nameof(saved));
        EntityId entity = _store.Create(new EntityTypeId("daggerfall.rdb-door"));
        Door door = new(definition, entity, motion, progress, lockValue, saved?.BashAttempts ?? 0UL);
        _doors[definition.Id] = door;
        Apply(door);
    }

    private Door Require(DaggerfallRdbDoorId id) => _doors.TryGetValue(id, out Door? door)
        ? door : throw new KeyNotFoundException($"RDB door '{id}' is not loaded.");

    private DaggerfallDoorView View(Door door)
    {
        Transform pose = _store.Get(door.Entity, EngineComponentTypes.Transform);
        SpatialCollider collider = _store.Get(door.Entity, EngineComponentTypes.SpatialCollider);
        return new(door.Definition.Id, door.Entity, pose, door.Motion, door.Progress, door.LockValue, collider.Enabled, door.Definition.Kind);
    }

    private void Apply(Door door)
    {
        Transform pose = Pose(door);
        // DFU switches its collider to a trigger when opening starts, and restores solidity only
        // after the closing tween completes. Motion is therefore the authoritative boundary,
        // not merely a zero-valued interpolant at the first opening frame.
        bool collisionEnabled = door.Motion == DaggerfallDoorMotion.Closed;
        SpatialCollider collider = new(door.Definition.BoundsMin, door.Definition.BoundsMax, uint.MaxValue, uint.MaxValue, collisionEnabled, false, !collisionEnabled);
        _store.Set(door.Entity, EngineComponentTypes.Transform, pose);
        _store.Set(door.Entity, EngineComponentTypes.SpatialCollider, collider);
    }

    private static Transform Pose(Door door)
    {
        Quaternion closed = DaggerfallDoorPose.ClosedRotation(door.Definition.RotationDegrees);
        Quaternion swing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, door.Definition.OpenAngleDegrees * (MathF.PI / 180F) * door.Progress);
        return new Transform(door.Definition.Position, Quaternion.Normalize(closed * swing), Vector3.One);
    }

    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallDoorRuntime)); }

    private sealed class Door(DaggerfallRdbDoorDefinition definition, EntityId entity, DaggerfallDoorMotion motion, float progress, int lockValue, ulong bashAttempts)
    {
        internal DaggerfallRdbDoorDefinition Definition { get; } = definition;
        internal EntityId Entity { get; } = entity;
        internal DaggerfallDoorMotion Motion { get; set; } = motion;
        internal float Progress { get; set; } = progress;
        internal int LockValue { get; set; } = lockValue;
        internal ulong BashAttempts { get; set; } = bashAttempts;
    }
}

/// <summary>One normalized RDB rotation convention shared by offline-content projection and runtime pose.</summary>
internal static class DaggerfallDoorPose
{
    internal static Quaternion ClosedRotation(Vector3 rotationDegrees)
    {
        Vector3 radians = rotationDegrees * (MathF.PI / 180F);
        return Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z));
    }
}

internal static class DaggerfallDoorIdentity
{
    internal static void Validate(DaggerfallRdbDoorId id)
    {
        if (string.IsNullOrWhiteSpace(id.SourceKey) || !id.SourceKey.EndsWith(".RDB", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An RDB door identity requires its source RDB key.", nameof(id));
        if (id.ModelIndex < 0) throw new ArgumentOutOfRangeException(nameof(id));
    }
}
