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

    internal void Validate()
    {
        if (!float.IsFinite(X)) throw new ArgumentOutOfRangeException(nameof(X));
        if (!float.IsFinite(Y)) throw new ArgumentOutOfRangeException(nameof(Y));
        if (!float.IsFinite(Z)) throw new ArgumentOutOfRangeException(nameof(Z));
    }
}

public sealed class PlayerControlState(WorldPoint? position, float yawRadians, float pitchRadians)
{
    public WorldPoint? Position { get; private set; } = position;
    public CharacterMotion Motion { get; set; }
    public CharacterGround Ground { get; private set; }
    public float YawRadians { get; set; } = yawRadians;
    public float PitchRadians { get; set; } = pitchRadians;
    public void MoveTo(Vector3 position) => Position = WorldPoint.From(position);

    internal void ValidateForInput()
    {
        if (!float.IsFinite(YawRadians)) throw new ArgumentOutOfRangeException(nameof(YawRadians));
        if (!float.IsFinite(PitchRadians)) throw new ArgumentOutOfRangeException(nameof(PitchRadians));
        Position?.Validate();
    }

    /// <summary>Commits the Engine receipt as the product-held transform, motion, and ground continuation.</summary>
    public void Apply(CharacterStepReceipt receipt)
    {
        MoveTo(receipt.Transform.Translation);
        Motion = receipt.Motion;
        Ground = receipt.Ground;
    }
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
        if (!float.IsFinite(LookSensitivity) || LookSensitivity <= 0f) throw new ArgumentOutOfRangeException(nameof(LookSensitivity));
        if (!float.IsFinite(PitchMinimumRadians)) throw new ArgumentOutOfRangeException(nameof(PitchMinimumRadians));
        if (!float.IsFinite(PitchMaximumRadians) || PitchMinimumRadians >= PitchMaximumRadians) throw new ArgumentOutOfRangeException(nameof(PitchMaximumRadians));
        if (!float.IsFinite(MaximumLookDeltaRadians) || MaximumLookDeltaRadians <= 0f) throw new ArgumentOutOfRangeException(nameof(MaximumLookDeltaRadians));
        return this;
    }
}

/// <summary>Explicit product overrides layered over the Engine's current default character-controller configuration.</summary>
public sealed record CharacterControllerTuning(
    float? StandingHeight = null,
    float? Radius = null,
    float? ForwardSpeed = null,
    float? BackwardSpeed = null,
    float? StrafeSpeed = null,
    float? RecoveryMaximumDistance = null,
    float? MaximumStepHeight = null)
{
    public CharacterControllerTuning Validate()
    {
        ValidateFinite(StandingHeight, nameof(StandingHeight));
        ValidateFinite(Radius, nameof(Radius));
        ValidateFinite(ForwardSpeed, nameof(ForwardSpeed));
        ValidateFinite(BackwardSpeed, nameof(BackwardSpeed));
        ValidateFinite(StrafeSpeed, nameof(StrafeSpeed));
        ValidateFinite(RecoveryMaximumDistance, nameof(RecoveryMaximumDistance));
        ValidateFinite(MaximumStepHeight, nameof(MaximumStepHeight));
        return this;
    }

    public CharacterControllerConfig ApplyTo(CharacterControllerConfig defaults)
    {
        Validate();
        return defaults with
        {
            Shape = defaults.Shape with
            {
                StandingHeight = StandingHeight ?? defaults.Shape.StandingHeight,
                Radius = Radius ?? defaults.Shape.Radius,
            },
            Ground = defaults.Ground with
            {
                ForwardSpeed = ForwardSpeed ?? defaults.Ground.ForwardSpeed,
                BackwardSpeed = BackwardSpeed ?? defaults.Ground.BackwardSpeed,
                StrafeSpeed = StrafeSpeed ?? defaults.Ground.StrafeSpeed,
            },
            Recovery = defaults.Recovery with
            {
                MaximumDistance = RecoveryMaximumDistance ?? defaults.Recovery.MaximumDistance,
            },
            Surface = defaults.Surface with
            {
                MaximumStepHeight = MaximumStepHeight ?? defaults.Surface.MaximumStepHeight,
            },
        };
    }

    private static void ValidateFinite(float? value, string name)
    {
        if (value is not { } supplied) return;
        if (!float.IsFinite(supplied)) throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record SpatialTuning(
    double CollisionVoxelSize,
    uint CollisionChunkSize,
    uint NavigationChunkSize,
    uint NavigationMaximumStepCells,
    CharacterControllerTuning? CharacterController = null)
{
    public SpatialTuning Validate()
    {
        if (!double.IsFinite(CollisionVoxelSize) || CollisionVoxelSize <= 0d) throw new ArgumentOutOfRangeException(nameof(CollisionVoxelSize));
        ArgumentOutOfRangeException.ThrowIfZero(CollisionChunkSize);
        ArgumentOutOfRangeException.ThrowIfZero(NavigationChunkSize);
        ArgumentOutOfRangeException.ThrowIfZero(NavigationMaximumStepCells);
        CharacterController?.Validate();
        return this;
    }
}
