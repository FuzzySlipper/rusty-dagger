namespace Daggerfall.Import.Arena2;

/// <summary>One raw-coordinate ARCH3D mesh point with corrected source UV coordinates.</summary>
public readonly record struct Arch3dPoint(int X, int Y, int Z, int U, int V);

/// <summary>One decoded ARCH3D textured polygon.</summary>
public sealed record Arch3dPlane(ushort TextureArchive, ushort TextureRecord, IReadOnlyList<Arch3dPoint> Points);

/// <summary>Decoded ARCH3D mesh facts. Coordinates and UVs retain their source units.</summary>
public sealed record Arch3dMesh(string Source, string Version, int DeclaredPointCount, IReadOnlyList<Arch3dPlane> Planes);

/// <summary>Decoder for numeric ARCH3D.BSA mesh records.</summary>
public static class Arch3dDecoder
{
    private const int HeaderBytes = 64;
    private const int PlaneHeaderBytes = 8;
    private const int PlanePointBytes = 8;
    private const int PointBytes = 12;

    /// <summary>Decodes one ARCH3D mesh record, including the donor's UV correction rules.</summary>
    public static Arch3dMesh Decode(ReadOnlySpan<byte> bytes, string source, uint recordId)
    {
        CheckedLittleEndianReader header = new(bytes, source);
        if (bytes.Length < HeaderBytes)
        {
            throw header.Error($"ARCH3D record requires at least {HeaderBytes} bytes, got {bytes.Length}");
        }

        string version = header.ReadNullTerminatedAscii(4);
        if (version is not "v2.5" and not "v2.6" and not "v2.7")
        {
            throw header.Error($"unsupported ARCH3D version {version}");
        }

        int declaredPointCount = header.ReadInt32();
        int planeCount = header.ReadInt32();
        if (declaredPointCount < 0 || planeCount < 0)
        {
            throw header.Error($"negative ARCH3D counts: points {declaredPointCount}, planes {planeCount}");
        }

        _ = header.ReadUInt32();
        _ = header.ReadBytes(8);
        _ = header.ReadInt32();
        _ = header.ReadInt32();
        _ = header.ReadInt32();
        _ = header.ReadUInt32();
        _ = header.ReadBytes(8);
        int pointListOffset = header.ReadInt32();
        _ = header.ReadInt32();
        _ = header.ReadUInt32();
        int planeListOffset = header.ReadInt32();
        if (pointListOffset < 0 || planeListOffset < 0)
        {
            throw header.Error("ARCH3D list offset is negative");
        }

        if (planeCount > bytes.Length / PlaneHeaderBytes)
        {
            throw header.Error($"ARCH3D plane count {planeCount} exceeds source bounds");
        }

        int planeOffset = planeListOffset;
        List<Arch3dPlane> planes = new(planeCount);
        for (int planeIndex = 0; planeIndex < planeCount; planeIndex++)
        {
            CheckedLittleEndianReader planeReader = At(bytes, source, planeOffset, "ARCH3D plane header");
            byte pointCount = planeReader.ReadByte();
            _ = planeReader.ReadByte();
            ushort textureBitfield = planeReader.ReadUInt16();
            _ = planeReader.ReadUInt32();

            int pointTableOffset = CheckedAdd(planeOffset, PlaneHeaderBytes, source, "ARCH3D plane point table");
            List<Arch3dPoint> points = new(pointCount);
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                int entryOffset = CheckedAdd(pointTableOffset, CheckedMultiply(pointIndex, PlanePointBytes, source, "ARCH3D plane point table"), source, "ARCH3D plane point table");
                CheckedLittleEndianReader entry = At(bytes, source, entryOffset, "ARCH3D plane point");
                int rawPointOffset = entry.ReadInt32();
                if (rawPointOffset < 0)
                {
                    throw entry.Error("ARCH3D point offset is negative");
                }

                int relativeOffset = version == "v2.5"
                    ? CheckedMultiply(rawPointOffset, 3, source, "ARCH3D v2.5 point offset")
                    : rawPointOffset;
                int coordinateOffset = CheckedAdd(pointListOffset, relativeOffset, source, "ARCH3D point coordinates");
                CheckedLittleEndianReader coordinates = At(bytes, source, coordinateOffset, "ARCH3D point coordinates");
                int x = coordinates.ReadInt32();
                int y = coordinates.ReadInt32();
                int z = coordinates.ReadInt32();
                points.Add(new Arch3dPoint(x, y, z, entry.ReadInt16(), entry.ReadInt16()));
            }

            FixPlaneUvs(points, recordId);
            planes.Add(new Arch3dPlane((ushort)(textureBitfield >> 7), (ushort)(textureBitfield & 0x7F), points));
            planeOffset = CheckedAdd(pointTableOffset, CheckedMultiply(pointCount, PlanePointBytes, source, "ARCH3D plane point table"), source, "ARCH3D next plane");
        }

        return new Arch3dMesh(source, version, declaredPointCount, planes);
    }

    private static CheckedLittleEndianReader At(ReadOnlySpan<byte> bytes, string source, int offset, string context)
    {
        CheckedLittleEndianReader reader = new(bytes, source);
        try
        {
            reader.Seek(offset);
        }
        catch (Arena2FormatException exception)
        {
            throw new Arena2FormatException(source, exception.Offset, $"{context}: {exception.Message}");
        }

        return reader;
    }

    private static int CheckedAdd(int left, int right, string source, string context)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw new Arena2FormatException(source, left, $"{context} offset arithmetic overflows a 32-bit offset");
        }
    }

    private static int CheckedMultiply(int left, int right, string source, string context)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            throw new Arena2FormatException(source, 0, $"{context} size overflows a 32-bit offset");
        }
    }

    private static void FixPlaneUvs(List<Arch3dPoint> points, uint recordId)
    {
        if (recordId < 905)
        {
            for (int index = 0; index < Math.Min(3, points.Count); index++)
            {
                Arch3dPoint point = points[index];
                points[index] = point with { U = UnpackUv(point.U), V = UnpackUv(point.V) };
            }
        }

        if (points.Count == 3)
        {
            points[1] = points[1] with { U = points[0].U + points[1].U, V = points[0].V + points[1].V };
            points[2] = points[2] with { U = points[1].U + points[2].U, V = points[1].V + points[2].V };
            return;
        }

        if (points.Count > 3)
        {
            TryComputeFaceUvs(points);
        }
    }

    private static int UnpackUv(int value)
    {
        if (value > -14336 && value < 14336 && value != -7168)
        {
            return value;
        }

        int nextMultiple = (((value - 1) >> 13) + 1) << 13;
        int previousMultiple = nextMultiple - 8192;
        int nearest = value - previousMultiple < nextMultiple - value ? previousMultiple : nextMultiple;
        return value - nearest;
    }

    private static void TryComputeFaceUvs(List<Arch3dPoint> points)
    {
        Vector3 p0 = Vector3.From(points[0]);
        Vector3 p1 = Vector3.From(points[1]);
        Vector3 p2 = Vector3.From(points[2]);
        if (!Vector3.TryNormalize(p1 - p0, out Vector3 v0))
        {
            return;
        }

        Vector3 v1Raw = p2 - p0;
        double d = Vector3.Dot(v1Raw, v0) / Vector3.Dot(v0, v0);
        if (!Vector3.TryNormalize(v1Raw - (v0 * d), out Vector3 v1))
        {
            return;
        }

        (double X, double Y) c0 = Project(points[0], v0, v1);
        (double X, double Y) c1 = Project(points[1], v0, v1);
        (double X, double Y) c2 = Project(points[2], v0, v1);
        double determinant = (c0.X * c1.Y) + (c0.Y * c2.X) + (c1.X * c2.Y) - (c1.Y * c2.X) - (c0.Y * c1.X) - (c0.X * c2.Y);
        if (determinant == 0 || !double.IsFinite(determinant))
        {
            return;
        }

        double[] xi = [(c1.Y - c2.Y) / determinant, (-c1.X + c2.X) / determinant, ((c1.X * c2.Y) - (c2.X * c1.Y)) / determinant];
        double[] yi = [(-c0.Y + c2.Y) / determinant, (c0.X - c2.X) / determinant, ((c0.X * c2.Y) - (c2.X * c0.Y)) / determinant];
        double[] zi = [(c0.Y - c1.Y) / determinant, (-c0.X + c1.X) / determinant, ((c0.X * c1.Y) - (c1.X * c0.Y)) / determinant];
        double[] us = [points[0].U, points[0].U + points[1].U, points[0].U + points[1].U + points[2].U];
        double[] vs = [points[0].V, points[0].V + points[1].V, points[0].V + points[1].V + points[2].V];
        double ua = Dot(us, xi, yi, zi, 0);
        double ub = Dot(us, xi, yi, zi, 1);
        double ud = Dot(us, xi, yi, zi, 2);
        double va = Dot(vs, xi, yi, zi, 0);
        double vb = Dot(vs, xi, yi, zi, 1);
        double vd = Dot(vs, xi, yi, zi, 2);
        if (!double.IsFinite(ua) || !double.IsFinite(ub) || !double.IsFinite(ud) || !double.IsFinite(va) || !double.IsFinite(vb) || !double.IsFinite(vd))
        {
            return;
        }

        points[0] = points[0] with { U = (int)us[0], V = (int)vs[0] };
        points[1] = points[1] with { U = (int)us[1], V = (int)vs[1] };
        points[2] = points[2] with { U = (int)us[2], V = (int)vs[2] };
        for (int index = 3; index < points.Count; index++)
        {
            (double X, double Y) coordinate = Project(points[index], v0, v1);
            points[index] = points[index] with
            {
                U = (int)((coordinate.X * ua) + (coordinate.Y * ub) + ud),
                V = (int)((coordinate.X * va) + (coordinate.Y * vb) + vd),
            };
        }
    }

    private static double Dot(double[] values, double[] xi, double[] yi, double[] zi, int coefficient)
        => (values[0] * xi[coefficient]) + (values[1] * yi[coefficient]) + (values[2] * zi[coefficient]);

    private static (double X, double Y) Project(Arch3dPoint point, Vector3 v0, Vector3 v1)
        => ((int)Vector3.Dot(Vector3.From(point), v0), (int)Vector3.Dot(Vector3.From(point), v1));

    private readonly record struct Vector3(double X, double Y, double Z)
    {
        public static Vector3 From(Arch3dPoint point) => new(point.X, point.Y, point.Z);

        public static Vector3 operator -(Vector3 left, Vector3 right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static Vector3 operator *(Vector3 value, double factor) => new(value.X * factor, value.Y * factor, value.Z * factor);

        public static double Dot(Vector3 left, Vector3 right) => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

        public static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            double length = Math.Sqrt(Dot(value, value));
            if (length == 0 || !double.IsFinite(length))
            {
                normalized = default;
                return false;
            }

            normalized = value * (1 / length);
            return true;
        }
    }
}
