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

/// <summary>One triangle whose index order is explicit at the import boundary.</summary>
public readonly record struct Arena2Triangle(int First, int Second, int Third);

/// <summary>
/// Pure Arena2 source-unit conversions used before artifact normalization.
///
/// Provenance: <c>crates/arena2/src/lib.rs</c> preserves Daggerfall Unity's
/// <c>MeshReader.GlobalScale</c>, <c>Arch3dFile.pointDivisor</c>,
/// <c>textureDivisor</c>, <c>BlocksFile.RotationDivisor</c>, and
/// <c>RDBLayout.RDBSide</c>. The former Rust dungeon importer applies the
/// same conversions in <c>crates/dagger-import/src/dungeon.rs</c>. These
/// helpers deliberately stop before mesh generation, Engine projection, and
/// ruleset interpretation.
/// </summary>
public static class Arena2SourceTransform
{
    // Structural source-format values, kept beside the transformations that
    // consume them. They are not content or ruleset tuning handles.
    private const float GlobalScaleMetres = 0.025F;
    private const float MeshPointDivisor = 256F;
    private const float TextureCoordinateDivisor = 16F;
    private const float RawRotationUnitsPerTurn = 2048F;
    private const float DegreesPerTurn = 360F;
    private const float RawBlockSide = 2048F;

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
    /// Returns the DFU-compatible origin for a MAPDITEM block. A classic block
    /// spans 2048 raw units, or 51.2 metres, on X and Z; its Y origin is zero.
    /// </summary>
    public static Arena2ImportPoint ToBlockOrigin(MapsDungeonBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        float blockSideMetres = RawBlockSide * GlobalScaleMetres;
        return CreatePoint(block.X * blockSideMetres, 0F, block.Z * blockSideMetres, nameof(block));
    }

    /// <summary>
    /// Applies a MAPDITEM block origin to a local importer-space point. It
    /// does not rotate geometry or create a scene object.
    /// </summary>
    public static Arena2ImportPoint PlaceInBlock(Arena2ImportPoint localPoint, MapsDungeonBlock block)
    {
        EnsureFinite(localPoint.XMetres, nameof(localPoint));
        EnsureFinite(localPoint.YMetres, nameof(localPoint));
        EnsureFinite(localPoint.ZMetres, nameof(localPoint));
        Arena2ImportPoint origin = ToBlockOrigin(block);
        return CreatePoint(
            localPoint.XMetres + origin.XMetres,
            localPoint.YMetres + origin.YMetres,
            localPoint.ZMetres + origin.ZMetres,
            nameof(localPoint));
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

    /// <summary>
    /// Reverses one validated triangle winding. The old importer mirrored
    /// DFU-compatible left-handed coordinates to glTF's right-handed space by
    /// negating Z, then explicitly changed each DFU fan triangle from
    /// <c>(0, i + 2, i + 1)</c> to <c>(0, i + 1, i + 2)</c> to preserve facing.
    /// Callers must choose this conversion rather than receiving an implicit
    /// index-order change.
    /// </summary>
    public static Arena2Triangle ReverseWinding(int first, int second, int third)
    {
        ValidateTriangleIndex(first, nameof(first));
        ValidateTriangleIndex(second, nameof(second));
        ValidateTriangleIndex(third, nameof(third));
        if (first == second || first == third || second == third)
        {
            throw new ArgumentException("A triangle requires three distinct vertex indices.");
        }

        return new Arena2Triangle(first, third, second);
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

    private static void ValidateTriangleIndex(int index, string parameterName)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, index, "A triangle index cannot be negative.");
        }
    }
}
