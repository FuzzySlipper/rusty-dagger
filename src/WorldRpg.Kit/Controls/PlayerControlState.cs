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

public sealed class PlayerControlState(WorldPoint? position)
{
    public WorldPoint? Position { get; private set; } = position;
    public CharacterMotion Motion { get; set; }
    public float YawRadians { get; set; } = MathF.PI;
    public float PitchRadians { get; set; }
    public void MoveTo(Vector3 position) => Position = WorldPoint.From(position);
}

public sealed record PlayerControlTuning(float LookSensitivity, float PitchMinimumRadians, float PitchMaximumRadians, float MaximumLookDeltaRadians)
{
    public static PlayerControlTuning Defaults { get; } = new(.0035f, -1.5533f, 1.5533f, .35f);
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
    public static SpatialTuning Defaults { get; } = new(.5, 32, .5, 32, 2);
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
