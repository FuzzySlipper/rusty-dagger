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
    internal static SpatialTuning Defaults { get; } = new(.5, 32, .5, 32, 2, new CharacterControllerConfig(
        Shape: new CharacterShapeConfig(StandingHeight: 1.8f, CrouchedHeight: 1.2f, Radius: .3f, ContactSkin: .02f, ClearancePadding: .01f),
        Ground: new CharacterGroundConfig(ForwardSpeed: 3.5f, BackwardSpeed: 3.5f, StrafeSpeed: 3.5f, Acceleration: 25f, Braking: 30f, Friction: 8f, StopSpeed: 2f, DirectionChangeMultiplier: 1f),
        Air: new CharacterAirConfig(MaximumSpeed: 5f, Acceleration: 12f, Braking: 0f, WishSpeedCap: 5f, LateralControl: 1f, Drag: 0f),
        Vertical: new CharacterVerticalConfig(Gravity: 19.6f, TerminalRiseSpeed: 55f, TerminalFallSpeed: 55f, JumpSpeed: 5f, GroundedDownwardBias: .5f),
        Jump: new CharacterJumpConfig(BufferSeconds: .12f, CoyoteSeconds: .10f, LandingLockoutSeconds: 0f, HeldInputRetriggers: false),
        Surface: new CharacterSurfaceConfig(MaximumSlopeRadians: .78f, SlopeHysteresisRadians: MathF.PI / 180f, SteepSlideAcceleration: 20f, SteepSlideSpeed: 12f, MaximumStepHeight: .4f, MinimumStepWidth: .05f, FloorSnapDistance: .15f, FloorSnapSpeedLimit: 10f, LedgeSupportFraction: .25f),
        Recovery: new CharacterRecoveryConfig(MaximumDistance: .5f, MaximumSpeed: 20f, NormalNudge: .001f, UnresolvedTolerance: .002f),
        Platform: new CharacterPlatformConfig(CarryTranslation: true, CarryRotation: true, InheritDepartureVelocity: true, DepartureVelocityFactor: 1f, SupportLossGraceSeconds: 0f, CrushTolerance: .02f),
        ExternalMotion: new CharacterExternalMotionConfig(ImpulseScale: 1f, ExternalDecayPerSecond: 0f, MaximumExternalSpeed: 50f, AuthoredMass: 80f, DynamicImpulseFactor: 1f, MaximumDynamicImpulse: 500f),
        Solver: new CharacterSolverConfig(MaximumSlidePlanes: 5, MaximumCastIterations: 8, MaximumRecoveryPasses: 4, MaximumContacts: 32, MaximumStepAttempts: 1, MaximumDisplacementPerStep: .35f, MaximumQueriesPerStep: 64)));
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
