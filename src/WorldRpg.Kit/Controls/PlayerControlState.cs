using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

public readonly record struct WorldPoint(float X, float Y, float Z)
{
    public float HorizontalDistanceTo(WorldPoint other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    public Vector3 ToVector() => new(X, Y, Z);
    public static WorldPoint From(Vector3 value) => new(value.X, value.Y, value.Z);
}

public sealed class PlayerControlState(WorldPoint? position, float yawRadians, float pitchRadians)
{
    public WorldPoint? Position { get; private set; } = position;
    public CharacterMotion Motion { get; set; }
    public float YawRadians { get; set; } = yawRadians;
    public float PitchRadians { get; set; } = pitchRadians;
    public void MoveTo(Vector3 position) => Position = WorldPoint.From(position);
}

public sealed record PlayerControlTuning(
    float LookSensitivity,
    float PitchMinimumRadians,
    float PitchMaximumRadians,
    float MaximumLookDeltaRadians,
    bool InvertHorizontal,
    bool InvertVertical,
    bool WrapYaw)
{
    public PlayerControlTuning Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(LookSensitivity);
        if (PitchMinimumRadians >= PitchMaximumRadians) throw new ArgumentOutOfRangeException(nameof(PitchMaximumRadians));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumLookDeltaRadians);
        return this;
    }
}

public sealed record SpatialTuning(double CollisionVoxelSize, uint CollisionChunkSize, double NavigationCellSize, uint NavigationChunkSize, uint NavigationMaximumStepCells)
{
    public SpatialTuning Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CollisionVoxelSize);
        ArgumentOutOfRangeException.ThrowIfZero(CollisionChunkSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(NavigationCellSize);
        ArgumentOutOfRangeException.ThrowIfZero(NavigationChunkSize);
        ArgumentOutOfRangeException.ThrowIfZero(NavigationMaximumStepCells);
        return this;
    }
}
