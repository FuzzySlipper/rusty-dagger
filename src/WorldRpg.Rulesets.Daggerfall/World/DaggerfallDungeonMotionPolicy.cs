using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.World;

internal enum DaggerfallDungeonMotionKind : byte
{
    Translation,
    Rotation,
}

/// <summary>
/// A normalized interpretation of one retained Arena2 motion action. Values here are in
/// Engine world units and radians; the source record remains the authority for action identity.
/// </summary>
internal readonly record struct DaggerfallDungeonMotionSpecification(
    DaggerfallDungeonMotionKind Kind,
    Vector3 Translation,
    Vector3 RotationAxis,
    float RotationRadians,
    double DurationSeconds);

/// <summary>
/// Converts the eight motion flags registered by DFU's <c>DaggerfallAction</c> using the raw
/// action and axis values retained by the normalizer. This is Daggerfall policy, not an Engine
/// motion implementation.
/// </summary>
internal static class DaggerfallDungeonMotionPolicy
{
    private const byte TranslationFlag = 0x01;
    private const byte PositiveXFlag = 0x02;
    private const byte NegativeXFlag = 0x03;
    private const byte PositiveYFlag = 0x04;
    private const byte NegativeYFlag = 0x05;
    private const byte PositiveZFlag = 0x06;
    private const byte NegativeZFlag = 0x07;
    private const byte RotationFlag = 0x08;

    private const double SourceTicksPerSecond = 20d;
    private const double DirectAxisDurationSourceTicks = 50d;
    private const float TranslationSourceUnitScale = 0.025f;
    private const float RotationSourceUnitDivisor = 5.68888888888889f;

    /// <summary>Builds the initial Engine pose from the normalized source Euler tuple.</summary>
    internal static Transform InitialTransform(Vector3 position, Vector3 rotationDegrees)
    {
        if (!IsFinite(position) || !IsFinite(rotationDegrees))
            throw new ArgumentOutOfRangeException(nameof(position), "Action model pose must be finite.");
        Vector3 radians = rotationDegrees * (MathF.PI / 180F);
        Quaternion rotation = Quaternion.CreateFromAxisAngle(-Vector3.UnitZ, radians.Z)
            * Quaternion.CreateFromAxisAngle(-Vector3.UnitX, radians.X)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians.Y);
        return new Transform(position, Quaternion.Normalize(rotation), Vector3.One);
    }

    /// <summary>
    /// Interprets a retained model action. The optional description is only used for DFU's
    /// explicitly named TRP rotation correction; it must come from source/model admission.
    /// </summary>
    internal static bool TryInterpret(
        DaggerfallDungeonActionDefinition action,
        string? modelDescription,
        out DaggerfallDungeonMotionSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action.ActionFlag)
        {
            case TranslationFlag:
                specification = new(
                    DaggerfallDungeonMotionKind.Translation,
                    Translation(action.Axis, action.Magnitude),
                    Vector3.Zero,
                    0f,
                    action.Duration / SourceTicksPerSecond);
                return true;

            case RotationFlag:
                (Vector3 rotationAxis, ushort magnitude) = Rotation(action.Axis, action.Magnitude, modelDescription);
                specification = new(
                    DaggerfallDungeonMotionKind.Rotation,
                    Vector3.Zero,
                    rotationAxis,
                    magnitude / RotationSourceUnitDivisor * (MathF.PI / 180f),
                    action.Duration / SourceTicksPerSecond);
                return true;

            case PositiveXFlag:
            case NegativeXFlag:
            case PositiveYFlag:
            case NegativeYFlag:
            case PositiveZFlag:
            case NegativeZFlag:
                byte axis = action.ActionFlag switch
                {
                    PositiveXFlag => 2,
                    NegativeXFlag => 1,
                    PositiveYFlag => 4,
                    NegativeYFlag => 3,
                    PositiveZFlag => 6,
                    NegativeZFlag => 5,
                    _ => throw new InvalidOperationException("A direct-axis action did not resolve to an axis."),
                };
                int directMagnitude = action.Axis * 8;
                specification = new(
                    DaggerfallDungeonMotionKind.Translation,
                    Translation(axis, checked((ushort)directMagnitude)),
                    Vector3.Zero,
                    0f,
                    DirectAxisDurationSourceTicks / SourceTicksPerSecond);
                return true;

            default:
                specification = default;
                return false;
        }
    }

    private static Vector3 Translation(byte axis, ushort magnitude)
    {
        float distance = magnitude * TranslationSourceUnitScale;
        return axis switch
        {
            1 => new Vector3(distance, 0f, 0f),       // Negative X in the source axis field.
            2 => new Vector3(-distance, 0f, 0f),      // Positive X.
            3 => new Vector3(0f, -distance, 0f),      // Negative Y.
            4 => new Vector3(0f, distance, 0f),       // Positive Y.
            5 => new Vector3(0f, 0f, distance),       // Negative Z.
            6 => new Vector3(0f, 0f, -distance),      // Positive Z.
            _ => Vector3.Zero,
        };
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static (Vector3 Axis, ushort Magnitude) Rotation(byte axis, ushort magnitude, string? modelDescription)
    {
        // DFU's retained TRP fix handles this one otherwise-unmapped raw axis. The current
        // normalized contract does not yet carry ModelDescription, so callers must only pass
        // this value when the importer has positively identified that source model.
        if (axis == 13 && string.Equals(modelDescription, "TRP", StringComparison.Ordinal))
            return (-Vector3.UnitX, 400);

        Vector3 rotationAxis = axis switch
        {
            1 => -Vector3.UnitX,
            2 => Vector3.UnitX,
            3 => -Vector3.UnitY,
            4 => Vector3.UnitY,
            5 => -Vector3.UnitZ,
            6 => Vector3.UnitZ,
            _ => Vector3.Zero,
        };
        return (rotationAxis, rotationAxis == Vector3.Zero ? (ushort)0 : magnitude);
    }
}
