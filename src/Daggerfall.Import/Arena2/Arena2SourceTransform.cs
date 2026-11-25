using System.Numerics;

namespace Daggerfall.Import.Arena2;

/// <summary>
/// Daggerfall Unity-compatible importer-space point in metres. This is a
/// normalized source fact, not an Engine spatial value or a runtime object.
/// </summary>
public readonly record struct Arena2ImportPoint(float XMetres, float YMetres, float ZMetres);

/// <summary>One normalized texture coordinate derived from an ARCH3D source point.</summary>
public readonly record struct Arena2TextureUv(float U, float V);

/// <summary>One source-model Euler rotation expressed in degrees.</summary>
public readonly record struct Arena2EulerDegrees(float X, float Y, float Z);

/// <summary>
/// Pure Arena2 source-unit conversions used before artifact normalization.
///
/// Provenance: migrated Arena2 and Daggerfall Unity importer semantics preserve
/// the source format's global scale, mesh-point and texture divisors, block
/// rotation divisor, and RMB block side. These helpers deliberately stop before
/// mesh generation, Engine projection, and ruleset interpretation.
/// </summary>
public static class Arena2SourceTransform
{
    /// <summary>Metres per classic world unit, the one scale every source position and size is converted by.</summary>
    public const float SourceUnitMetres = 0.025F;

    // Structural source-format values, kept beside the transformations that
    // consume them. They are not content or ruleset tuning handles.
    private const float GlobalScaleMetres = SourceUnitMetres;
    private const float MeshPointDivisor = 256F;
    private const float TextureCoordinateDivisor = 16F;
    private const float RawRotationUnitsPerTurn = 2048F;
    private const float DegreesPerTurn = 360F;
    private const float RawRmbBlockSide = 4096F;
    private const float RmbRotationDivisor = 5.68888888888889F;

    /// <summary>
    /// Converts an ARCH3D mesh point to DFU-compatible importer space. Arena2
    /// mesh coordinates are 1/256 raw units; Arena2 is Y-down and the target
    /// source convention is left-handed Y-up: <c>(x, -y, z)</c>.
    /// </summary>
    public static Arena2ImportPoint ToImportPoint(Arch3dPoint point)
    {
        return CreatePoint(
            (point.X / MeshPointDivisor) * GlobalScaleMetres,
            -(point.Y / MeshPointDivisor) * GlobalScaleMetres,
            (point.Z / MeshPointDivisor) * GlobalScaleMetres,
            nameof(point));
    }

    /// <summary>
    /// Converts an RDB object, flat, light, or marker position to
    /// DFU-compatible importer space. RDB positions are whole raw units and
    /// use the same Y-down to Y-up conversion: <c>(x, -y, z) * 0.025</c>.
    /// </summary>
    public static Arena2ImportPoint ToImportPoint(int x, int y, int z)
    {
        return CreatePoint(
            x * GlobalScaleMetres,
            -y * GlobalScaleMetres,
            z * GlobalScaleMetres,
            "RDB position");
    }

    /// <summary>
    /// Converts an RMB model position to the donor's local block frame. The caller applies its
    /// building-subrecord transform or exterior block origin explicitly.
    /// </summary>
    public static Arena2ImportPoint ToRmbImportPoint(int x, int y, int z) =>
        CreatePoint(x * GlobalScaleMetres, -y * GlobalScaleMetres, z * GlobalScaleMetres, "RMB position");

    /// <summary>
    /// Returns the local transform origin a building subrecord supplies: its source Z origin is
    /// measured from the opposite RMB edge, matching the donor's <c>RMBDimension - ZPos</c>.
    /// </summary>
    public static Arena2ImportPoint ToRmbBuildingOrigin(RmbBuildingSlot building)
    {
        ArgumentNullException.ThrowIfNull(building);
        return CreatePoint(building.X * GlobalScaleMetres, 0F, (RawRmbBlockSide - building.Z) * GlobalScaleMetres, nameof(building));
    }

    /// <summary>Converts the donor's RMB yaw units to the same source Euler convention as its layout.</summary>
    public static float ToRmbYawDegrees(int sourceYaw)
    {
        float degrees = -sourceYaw / RmbRotationDivisor;
        EnsureFinite(degrees, nameof(sourceYaw));
        return degrees;
    }

    /// <summary>
    /// Converts RDB model rotation units to the DFU-compatible Euler degrees
    /// used by the donor's <c>T * Rz * Rx * Ry</c> model matrix. Arena2 stores
    /// 2048 units per turn and the conversion negates each source axis.
    /// Matrix construction and rotation ordering remain artifact-generation
    /// concerns, so this method intentionally returns only typed angles.
    /// </summary>
    public static Arena2EulerDegrees ToEulerDegrees(RdbModelSource model)
    {
        ArgumentNullException.ThrowIfNull(model);
        float degreesPerRawUnit = DegreesPerTurn / RawRotationUnitsPerTurn;
        return new Arena2EulerDegrees(
            ConvertRotation(model.XRotation, degreesPerRawUnit),
            ConvertRotation(model.YRotation, degreesPerRawUnit),
            ConvertRotation(model.ZRotation, degreesPerRawUnit));
    }

    /// <summary>
    /// The right-handed rotation source Euler degrees turn a model by. The donor turns importer-space points by
    /// <c>Rz * Rx * Ry</c> (column vectors) before the right-handed flip of Z, and that flip negates turns about X
    /// and Y, so in the published frame the same turn is <c>Rz(z) * Rx(-x) * Ry(-y)</c>.
    /// </summary>
    public static Quaternion ToRightHandedRotation(Arena2EulerDegrees degrees)
    {
        const float radiansPerDegree = MathF.PI / 180F;
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees.Z * radiansPerDegree)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -degrees.X * radiansPerDegree)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -degrees.Y * radiansPerDegree));
    }

    /// <summary>The right-handed rotation of an RMB yaw in source Euler degrees (<see cref="ToRmbYawDegrees"/>).</summary>
    public static Quaternion ToRightHandedYaw(float sourceYawDegrees) => ToRightHandedRotation(new(0F, sourceYawDegrees, 0F));

    /// <summary>
    /// Converts corrected ARCH3D UV sub-units to texture fractions. ARCH3D UVs
    /// are 1/16 of a source texel; texture dimensions must be finite positive
    /// pixel counts supplied by the decoded texture record.
    /// </summary>
    public static Arena2TextureUv ToTextureUv(Arch3dPoint point, int textureWidth, int textureHeight)
    {
        if (textureWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureWidth), textureWidth, "A source texture width must be positive.");
        }

        if (textureHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureHeight), textureHeight, "A source texture height must be positive.");
        }

        float u = (point.U / TextureCoordinateDivisor) / textureWidth;
        float v = (point.V / TextureCoordinateDivisor) / textureHeight;
        EnsureFinite(u, nameof(point));
        EnsureFinite(v, nameof(point));
        return new Arena2TextureUv(u, v);
    }

    private static Arena2ImportPoint CreatePoint(float x, float y, float z, string parameterName)
    {
        EnsureFinite(x, parameterName);
        EnsureFinite(y, parameterName);
        EnsureFinite(z, parameterName);
        return new Arena2ImportPoint(x, y, z);
    }

    private static float ConvertRotation(int rawRotation, float degreesPerRawUnit)
    {
        float degrees = -rawRotation * degreesPerRawUnit;
        EnsureFinite(degrees, nameof(rawRotation));
        return degrees;
    }

    private static void EnsureFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A converted Arena2 value must be finite.");
        }
    }
}
