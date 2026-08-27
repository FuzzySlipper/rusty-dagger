using System.Numerics;
using Rusty.Engine;

namespace RustyDagger.Game.Modules.PlayerControl;

internal readonly record struct WorldPoint(float X, float Y, float Z)
{
    internal float HorizontalDistanceTo(WorldPoint other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    internal Vector3 ToVector() => new(X, Y, Z);
    internal static WorldPoint From(Vector3 value) => new(value.X, value.Y, value.Z);
}

internal sealed class PlayerControlState(WorldPoint? position)
{
    internal WorldPoint? Position { get; private set; } = position;
    internal CharacterMotion Motion { get; set; }
    internal float YawRadians { get; set; } = MathF.PI;
    internal float PitchRadians { get; set; }
    internal void MoveTo(Vector3 position) => Position = WorldPoint.From(position);
}

internal sealed record PlayerControlTuning(float LookSensitivity, float PitchMinimumRadians, float PitchMaximumRadians, float MaximumLookDeltaRadians)
{
    internal static PlayerControlTuning Defaults { get; } = new(.0035f, -1.5533f, 1.5533f, .35f);
    internal PlayerControlTuning Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(LookSensitivity);
        if (PitchMinimumRadians >= PitchMaximumRadians) throw new ArgumentOutOfRangeException(nameof(PitchMaximumRadians));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumLookDeltaRadians);
        return this;
    }
}

internal sealed record SpatialTuning(double CollisionVoxelSize, uint CollisionChunkSize, double NavigationCellSize, uint NavigationChunkSize, uint NavigationMaximumStepCells, CharacterControllerConfig Controller)
{
    internal static SpatialTuning Defaults { get; } = new(.5, 32, .5, 32, 2, new CharacterControllerConfig(1.8f, 1.2f, .3f, .02f, 3.5f, 3.5f, 3.5f, 25f, 30f, 8f, 19.6f, 5f, .78f, .4f, .15f, .35f));
    internal SpatialTuning Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CollisionVoxelSize);
        ArgumentOutOfRangeException.ThrowIfZero(CollisionChunkSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(NavigationCellSize);
        ArgumentOutOfRangeException.ThrowIfZero(NavigationChunkSize);
        ArgumentOutOfRangeException.ThrowIfZero(NavigationMaximumStepCells);
        return this;
    }
}
