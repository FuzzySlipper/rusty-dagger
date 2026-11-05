using System.Buffers.Binary;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The eroded landscape baked from WOODS (<c>authoring/tools/relief</c>): a lattice of heights in metres, and how much
/// of the classic height stays at each node, which returns along the coast so shores don't move. Coordinates are in
/// map pixels of the WOODS heightmap. The bake holds valleys and ridges down to its lattice; the erosion filter cuts
/// the finer gullies and breaks the crests.
/// </summary>
internal sealed class DaggerfallTerrainRelief
{
    internal const string Path = "worldrpg/terrain/relief.bin";
    private const int HeaderBytes = 12;
    private const double MetresPerPixel = 819.2;
    /// <summary>The lattice-scale slope the filter follows is measured across this share of a map pixel either side.</summary>
    private const double SlopeReach = .5;
    private static readonly ErosionFilter Filter = new(10_502, new(3_200, 6, .09, .9, 280, 700, 1_500, 3_000));

    private readonly int _width, _height, _perPixel;
    private readonly float[] _metres;
    private readonly byte[] _share;

    private DaggerfallTerrainRelief(int width, int height, float[] metres, byte[] share)
    {
        _width = width; _height = height; _metres = metres; _share = share;
        _perPixel = width / 1000;
    }

    internal static DaggerfallTerrainRelief Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes || !bytes[..4].SequenceEqual("RLF1"u8))
            throw new InvalidOperationException("The terrain relief is not a relief file.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), height = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        int count = checked(width * height);
        if (bytes.Length != HeaderBytes + (count * 5))
            throw new InvalidOperationException($"The terrain relief carries {bytes.Length} bytes for a {width} by {height} lattice.");
        float[] metres = new float[count];
        for (int i = 0; i < count; i++) metres[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(HeaderBytes + (i * 4))..]);
        return new(width, height, metres, bytes[(HeaderBytes + (count * 4))..].ToArray());
    }

    /// <summary>How much of the relief stands at a point, from 0 (the classic height) to 1.</summary>
    internal double Share(double mapX, double mapY)
    {
        double x = Math.Clamp(mapX * _perPixel, 0, _width - 1.001), y = Math.Clamp(mapY * _perPixel, 0, _height - 1.001);
        int ix = (int)x, iy = (int)y;
        double fx = x - ix, fy = y - iy;
        int at = (iy * _width) + ix;
        double top = _share[at] + ((_share[at + 1] - _share[at]) * fx);
        double bottom = _share[at + _width] + ((_share[at + _width + 1] - _share[at + _width]) * fx);
        return (top + ((bottom - top) * fy)) / 255;
    }

    /// <summary>The relief's height at a point in metres, gullies down to <paramref name="finestMetres"/>.</summary>
    internal double Metres(double mapX, double mapY, double finestMetres)
    {
        double smooth = Lattice(mapX, mapY);
        double slopeX = (Lattice(mapX + SlopeReach, mapY) - Lattice(mapX - SlopeReach, mapY)) / (2 * SlopeReach * MetresPerPixel);
        double slopeZ = (Lattice(mapX, mapY + SlopeReach) - Lattice(mapX, mapY - SlopeReach)) / (2 * SlopeReach * MetresPerPixel);
        double x = mapX * MetresPerPixel, z = mapY * MetresPerPixel;
        return smooth + Filter.Offset(x, z, slopeX, slopeZ, finestMetres) + Filter.Crest(x, z, smooth);
    }

    /// <summary>The lattice alone, a Catmull-Rom surface through its nodes.</summary>
    private double Lattice(double mapX, double mapY)
    {
        double x = mapX * _perPixel, y = mapY * _perPixel;
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        Span<double> rows = stackalloc double[4];
        for (int j = 0; j < 4; j++)
        {
            int row = Math.Clamp(iy + j - 1, 0, _height - 1) * _width;
            rows[j] = Curve(_metres[row + Math.Clamp(ix - 1, 0, _width - 1)], _metres[row + Math.Clamp(ix, 0, _width - 1)],
                _metres[row + Math.Clamp(ix + 1, 0, _width - 1)], _metres[row + Math.Clamp(ix + 2, 0, _width - 1)], fx);
        }

        return Curve(rows[0], rows[1], rows[2], rows[3], fy);
    }

    private static double Curve(double a, double b, double c, double d, double t) =>
        b + (.5 * t * (c - a + (t * ((2 * a) - (5 * b) + (4 * c) - d + (t * ((3 * (b - c)) + d - a))))));
}

/// <summary>
/// The normalized wilderness terrain: the row-major heightmap, and the 25 elevation samples of every cell, which
/// are the bulk of it and are read when first asked for.
/// </summary>
internal sealed class DaggerfallTerrainSet
{
    private readonly Lazy<byte[]> _samples;
    private readonly Lazy<DaggerfallTerrainRelief?> _relief;

    internal DaggerfallTerrainSet(int width, int height, byte[] heightmap, byte[] samples)
        : this(width, height, heightmap, () => samples)
    {
    }

    /// <summary>A terrain whose cell samples are read the first time a sample is asked for.</summary>
    internal DaggerfallTerrainSet(int width, int height, byte[] heightmap, Func<byte[]> samples, Func<DaggerfallTerrainRelief?>? relief = null)
    {
        Width = width;
        Height = height;
        Heightmap = heightmap ?? throw new ArgumentNullException(nameof(heightmap));
        ArgumentNullException.ThrowIfNull(samples);
        _samples = new(samples, LazyThreadSafetyMode.ExecutionAndPublication);
        _relief = new(relief ?? (() => null), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal int Width { get; }

    internal int Height { get; }

    internal byte[] Heightmap { get; }

    /// <summary>Every cell's 25 elevation samples in row-major cell order.</summary>
    internal byte[] Samples => _samples.Value;

    /// <summary>The eroded landscape, where the data carries one: the walked and distant ground both stand on it.</summary>
    internal DaggerfallTerrainRelief? Relief => _relief.Value;

    /// <summary>
    /// Reads one heightmap value: the elevation the source states for the map pixel. Coordinates
    /// past the map clamp to its edge, the way the donor reads: a sample past the wilderness is
    /// the edge's sample rather than a miss.
    /// </summary>
    internal int GetHeight(int x, int y) => Heightmap[(Clamp(y, Height) * Width) + Clamp(x, Width)];

    /// <summary>
    /// Reads one cell's 25 elevation samples in row-major 5-by-5 order, with the same edge clamp
    /// as the heightmap.
    /// </summary>
    internal byte[] GetSamples(int x, int y)
    {
        byte[] samples = new byte[25];
        Array.Copy(Samples, ((Clamp(y, Height) * Width) + Clamp(x, Width)) * 25, samples, 0, 25);
        return samples;
    }

    private static int Clamp(int value, int extent) => value < 0 ? 0 : value >= extent ? extent - 1 : value;
}
