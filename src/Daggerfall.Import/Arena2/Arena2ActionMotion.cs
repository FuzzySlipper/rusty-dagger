using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Arena2;

/// <summary>
/// Reads an RDB action record's motion: DFU <c>RDBLayout.GetTranslationActionVector</c> and
/// <c>GetRotationActionVector</c> over the eight motion flags <c>DaggerfallAction</c> registers, converted from the
/// donor's left-handed frame to the published right-handed one. The Z flip negates a Z translation, which the donor
/// moves in world space, and turns about X and Y, which it turns in the model's own space.
/// </summary>
public static class Arena2ActionMotion
{
    private const byte TranslationFlag = 0x01;
    private const byte PositiveXFlag = 0x02;
    private const byte NegativeXFlag = 0x03;
    private const byte PositiveYFlag = 0x04;
    private const byte NegativeYFlag = 0x05;
    private const byte PositiveZFlag = 0x06;
    private const byte NegativeZFlag = 0x07;
    private const byte RotationFlag = 0x08;

    // The source axis field: 1 -X, 2 +X, 3 -Y, 4 +Y, 5 -Z, 6 +Z.
    private const byte NegativeX = 1;
    private const byte PositiveX = 2;
    private const byte NegativeY = 3;
    private const byte PositiveY = 4;
    private const byte NegativeZ = 5;
    private const byte PositiveZ = 6;

    private const float SourceTicksPerSecond = 20F;
    private const float DirectAxisDurationTicks = 50F;
    private const float DirectAxisMagnitudePerUnit = 8F;
    private const float RotationUnitsPerDegree = 5.68888888888889F;

    /// <summary>The motion of one action record, or null when its flag moves nothing.</summary>
    /// <param name="modelDescription">The placed model's source description, for the donor's one TRP axis fix.</param>
    public static NormalizedDungeonMotion? Read(byte actionFlag, byte axis, ushort duration, ushort magnitude, string? modelDescription)
    {
        switch (actionFlag)
        {
            case TranslationFlag:
                return Translation(axis, magnitude, duration / SourceTicksPerSecond);
            case PositiveXFlag or NegativeXFlag or PositiveYFlag or NegativeYFlag or PositiveZFlag or NegativeZFlag:
                byte direct = actionFlag switch
                {
                    PositiveXFlag => PositiveX,
                    NegativeXFlag => NegativeX,
                    PositiveYFlag => PositiveY,
                    NegativeYFlag => NegativeY,
                    PositiveZFlag => PositiveZ,
                    _ => NegativeZ,
                };
                return Translation(direct, axis * DirectAxisMagnitudePerUnit, DirectAxisDurationTicks / SourceTicksPerSecond);
            case RotationFlag:
                return Rotation(axis, magnitude, modelDescription, duration / SourceTicksPerSecond);
            default:
                return null;
        }
    }

    private static NormalizedDungeonMotion Translation(byte axis, float magnitude, float seconds)
    {
        float d = magnitude * Arena2SourceTransform.SourceUnitMetres;
        // The donor's left-handed world vector (a negative axis moves +X or +Z, as RDBLayout has it), Z flipped.
        NormalizedVector3 translation = axis switch
        {
            NegativeX => new(d, 0F, 0F),
            PositiveX => new(-d, 0F, 0F),
            NegativeY => new(0F, -d, 0F),
            PositiveY => new(0F, d, 0F),
            NegativeZ => new(0F, 0F, -d),
            PositiveZ => new(0F, 0F, d),
            _ => new(0F, 0F, 0F),
        };
        return new(NormalizedDungeonMotionKind.Translation, translation, new(0F, 0F, 0F), 0F, seconds);
    }

    private static NormalizedDungeonMotion Rotation(byte axis, ushort magnitude, string? modelDescription, float seconds)
    {
        // DFU's TRP fix for the one raw axis above six the source uses: a -X turn of 400 units.
        if (axis == 13 && string.Equals(modelDescription, "TRP", StringComparison.Ordinal))
            (axis, magnitude) = (NegativeX, 400);
        float degrees = magnitude / RotationUnitsPerDegree;
        // The donor's local Euler turn about the named axis, signed; the Z flip negates X and Y turns.
        (NormalizedVector3 rotationAxis, float angle) = axis switch
        {
            NegativeX => (new NormalizedVector3(1F, 0F, 0F), degrees),
            PositiveX => (new NormalizedVector3(1F, 0F, 0F), -degrees),
            NegativeY => (new NormalizedVector3(0F, 1F, 0F), degrees),
            PositiveY => (new NormalizedVector3(0F, 1F, 0F), -degrees),
            NegativeZ => (new NormalizedVector3(0F, 0F, 1F), -degrees),
            PositiveZ => (new NormalizedVector3(0F, 0F, 1F), degrees),
            _ => (new NormalizedVector3(0F, 0F, 0F), 0F),
        };
        return new(NormalizedDungeonMotionKind.Rotation, new(0F, 0F, 0F), rotationAxis, angle, seconds);
    }
}
