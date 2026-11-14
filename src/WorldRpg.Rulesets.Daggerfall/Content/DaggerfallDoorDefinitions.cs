using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>A source-stable identity for one normalized RDB or RMB door model.</summary>
internal readonly record struct DaggerfallRdbDoorId(string SourceKey, int BlockX, int BlockZ, int ModelIndex)
{
    public override string ToString() => $"{SourceKey}@{BlockX},{BlockZ}:{ModelIndex}";
}

internal enum DaggerfallDoorKind { Normal, Special }

/// <summary>One material binding for an individually projected normalized content mesh.</summary>
internal readonly record struct DaggerfallMeshMaterialBinding(uint MeshSlot, uint WorldMaterialSlot);
/// <summary>Normalized source action facts retained for the dungeon action-graph owner.</summary>
internal readonly record struct DaggerfallDoorActionSource(byte Axis, ushort Duration, ushort Magnitude, int NextObjectOffset, byte Flags);

/// <summary>Content-backed visual that follows the same Engine transform as its door collider.</summary>
internal sealed record DaggerfallDoorVisual(string Path, ContentSha256 Sha256, IReadOnlyList<DaggerfallMeshMaterialBinding> Materials)
{
    /// <summary>
    /// The visual's pose in the door's own frame. A published door visual is written in that frame and
    /// needs none; a product-wide model mesh is in its model frame, which this turns into the door's.
    /// </summary>
    internal Transform? LocalPose { get; init; }

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
    /// <summary>
    /// The source interaction surface admitted with this door.  Interior and dungeon action doors
    /// retain the interior formulas; RMB building doors are admitted as exterior doors by the
    /// normalized profile reader.  Keeping this beside the canonical definition prevents each
    /// caller from guessing the surface from activation mode or source name.
    /// </summary>
    internal DaggerfallLockInteractionSurface LockSurface { get; init; } = DaggerfallLockInteractionSurface.Interior;
    /// <summary>
    /// The placed building this exterior door enters. Such an entrance keeps the RMB model's lock only as its
    /// source fact: activation sets its live lock from the building entry rule (<see cref="DaggerfallDoorRuntime.ApplyBuildingEntry"/>).
    /// </summary>
    internal WorldRpg.Rulesets.Daggerfall.Content.DaggerfallSiteBuildingId? ExteriorBuilding { get; init; }

    /// <summary>Action-door geometry and bounds retain the source model's local frame.</summary>
    internal bool UsesModelCoordinates { get; init; }

    internal DaggerfallRdbDoorDefinition Validate()
    {
        DaggerfallDoorIdentity.Validate(Id);
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        if (!Enum.IsDefined(LockSurface)) throw new ArgumentOutOfRangeException(nameof(LockSurface));
        if (ExteriorBuilding is { } building && (LockSurface != DaggerfallLockInteractionSurface.Exterior
            || building.BlockX < 0 || building.BlockY < 0 || building.Index < 0))
            throw new ArgumentException("An exterior door must name a valid placed building.", nameof(ExteriorBuilding));
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

/// <summary>The ordinary door-local frame shared by baked-door projection and runtime pose.</summary>
internal static class DaggerfallDoorPose
{
    internal static Quaternion ClosedRotation(Vector3 rotationDegrees)
    {
        Vector3 radians = rotationDegrees * (MathF.PI / 180F);
        return Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z));
    }

    /// <summary>
    /// The world-axis box a turned local box occupies, offset from its pose's translation. Engine colliders and
    /// character obstacles take translation-offset axis-aligned bounds and do not turn them by the pose, so a door
    /// turned a quarter turn would otherwise block the wall beside its doorway rather than the doorway.
    /// </summary>
    internal static (Vector3 Min, Vector3 Max) TranslationOffsetBounds(Quaternion rotation, Vector3 min, Vector3 max)
    {
        Vector3 low = new(float.PositiveInfinity), high = new(float.NegativeInfinity);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 turned = Vector3.Transform(new Vector3((corner & 1) == 0 ? min.X : max.X,
                (corner & 2) == 0 ? min.Y : max.Y, (corner & 4) == 0 ? min.Z : max.Z), rotation);
            low = Vector3.Min(low, turned);
            high = Vector3.Max(high, turned);
        }
        return (low, high);
    }
}

internal static class DaggerfallDoorIdentity
{
    internal static void Validate(DaggerfallRdbDoorId id)
    {
        if (string.IsNullOrWhiteSpace(id.SourceKey)
            || (!id.SourceKey.EndsWith(".RDB", StringComparison.OrdinalIgnoreCase)
                && !id.SourceKey.EndsWith(".RMB", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A normalized door identity requires its source RDB or RMB key.", nameof(id));
        if (id.ModelIndex < 0) throw new ArgumentOutOfRangeException(nameof(id));
    }
}

/// <summary>The two source surfaces that retain separate classic lockpicking formulas.</summary>
internal enum DaggerfallLockInteractionSurface
{
    Interior,
    Exterior,
}
