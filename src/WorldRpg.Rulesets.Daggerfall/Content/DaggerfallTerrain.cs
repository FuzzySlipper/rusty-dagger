using System.Buffers.Binary;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The eroded landscape the import bakes from WOODS: a lattice of heights in metres, and how much
/// of the classic height stays at each node, which returns along the coast so shores don't move. Coordinates are in
/// map pixels of the WOODS heightmap. The bake holds valleys and ridges down to its lattice; the erosion filter cuts
/// the finer gullies and breaks the crests.
/// </summary>
internal sealed class DaggerfallTerrainRelief
{
    internal const string Path = "worldrpg/terrain/relief.bin";
    private const int HeaderBytes = 20;
    private const double MetresPerPixel = 819.2;
    private const double StepsPerMetre = 8;
    /// <summary>The slope the filter follows is measured across this many nodes either side.</summary>
    private const double SlopeNodes = 1.5;
    /// <summary>The filter works below what the lattice holds: its longest gullies are a little over the finest the bake cut, and the bake's own ridges break the skyline.</summary>
    private static readonly ErosionFilter Filter = new(10_502, new(400, 5, .09, .9, 0, 700, 1_500, 3_000));

    private readonly int _width, _height, _perPixel, _shareWidth, _shareHeight, _sharePerPixel;
    private readonly ushort[] _steps;
    private readonly byte[] _share;

    private DaggerfallTerrainRelief(int width, int height, ushort[] steps, int shareWidth, int shareHeight, byte[] share)
    {
        _width = width; _height = height; _steps = steps; _shareWidth = shareWidth; _shareHeight = shareHeight; _share = share;
        _perPixel = width / 1000;
        _sharePerPixel = shareWidth / 1000;
    }

    internal static DaggerfallTerrainRelief Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes || !bytes[..4].SequenceEqual("RLF2"u8))
            throw new InvalidOperationException("The terrain relief is not a relief file.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), height = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        int shareWidth = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]), shareHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        int count = checked(width * height), shares = checked(shareWidth * shareHeight);
        if (bytes.Length != HeaderBytes + (count * sizeof(ushort)) + shares)
            throw new InvalidOperationException($"The terrain relief carries {bytes.Length} bytes for a {width} by {height} lattice and {shareWidth} by {shareHeight} shares.");
        ushort[] steps = new ushort[count];
        for (int i = 0; i < count; i++) steps[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(HeaderBytes + (i * sizeof(ushort)))..]);
        return new(width, height, steps, shareWidth, shareHeight, bytes[(HeaderBytes + (count * sizeof(ushort)))..].ToArray());
    }

    /// <summary>How much of the relief stands at a point, from 0 (the classic height) to 1.</summary>
    internal double Share(double mapX, double mapY)
    {
        double x = Math.Clamp(mapX * _sharePerPixel, 0, _shareWidth - 1.001), y = Math.Clamp(mapY * _sharePerPixel, 0, _shareHeight - 1.001);
        int ix = (int)x, iy = (int)y;
        double fx = x - ix, fy = y - iy;
        int at = (iy * _shareWidth) + ix;
        double top = _share[at] + ((_share[at + 1] - _share[at]) * fx);
        double bottom = _share[at + _shareWidth] + ((_share[at + _shareWidth + 1] - _share[at + _shareWidth]) * fx);
        return (top + ((bottom - top) * fy)) / 255;
    }

    /// <summary>The relief's height at a point in metres, gullies down to <paramref name="finestMetres"/>, as much of them as <paramref name="gullies"/> leaves.</summary>
    internal double Metres(double mapX, double mapY, double finestMetres, double gullies = 1)
    {
        double smooth = Lattice(mapX, mapY), reach = SlopeNodes / _perPixel;
        double slopeX = (Lattice(mapX + reach, mapY) - Lattice(mapX - reach, mapY)) / (2 * reach * MetresPerPixel);
        double slopeZ = (Lattice(mapX, mapY + reach) - Lattice(mapX, mapY - reach)) / (2 * reach * MetresPerPixel);
        double x = mapX * MetresPerPixel, z = mapY * MetresPerPixel;
        return smooth + ((Filter.Offset(x, z, slopeX, slopeZ, finestMetres) + Filter.Crest(x, z, smooth)) * gullies);
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
            rows[j] = Curve(_steps[row + Math.Clamp(ix - 1, 0, _width - 1)], _steps[row + Math.Clamp(ix, 0, _width - 1)],
                _steps[row + Math.Clamp(ix + 1, 0, _width - 1)], _steps[row + Math.Clamp(ix + 2, 0, _width - 1)], fx);
        }

        return Curve(rows[0], rows[1], rows[2], rows[3], fy) / StepsPerMetre;
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
    private readonly Lazy<DaggerfallTerrainRivers?> _rivers;

    internal DaggerfallTerrainSet(int width, int height, byte[] heightmap, byte[] samples)
        : this(width, height, heightmap, () => samples)
    {
    }

    /// <summary>A terrain whose cell samples are read the first time a sample is asked for.</summary>
    internal DaggerfallTerrainSet(int width, int height, byte[] heightmap, Func<byte[]> samples, Func<DaggerfallTerrainRelief?>? relief = null, Func<DaggerfallTerrainRivers?>? rivers = null)
    {
        Width = width;
        Height = height;
        Heightmap = heightmap ?? throw new ArgumentNullException(nameof(heightmap));
        ArgumentNullException.ThrowIfNull(samples);
        _samples = new(samples, LazyThreadSafetyMode.ExecutionAndPublication);
        _relief = new(relief ?? (() => null), LazyThreadSafetyMode.ExecutionAndPublication);
        _rivers = new(rivers ?? (() => null), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal int Width { get; }

    internal int Height { get; }

    internal byte[] Heightmap { get; }

    /// <summary>Every cell's 25 elevation samples in row-major cell order.</summary>
    internal byte[] Samples => _samples.Value;

    /// <summary>The eroded landscape, where the data carries one: the walked and distant ground both stand on it.</summary>
    internal DaggerfallTerrainRelief? Relief => _relief.Value;

    /// <summary>The rivers the relief drained into, where the data carries them.</summary>
    internal DaggerfallTerrainRivers? Rivers => _rivers.Value;

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
