namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The normalized wilderness terrain: the row-major heightmap, and the 25 elevation samples of every cell, which
/// are the bulk of it and are read when first asked for.
/// </summary>
internal sealed class DaggerfallTerrainSet
{
    private readonly Lazy<byte[]> _samples;

    internal DaggerfallTerrainSet(int width, int height, byte[] heightmap, byte[] samples)
        : this(width, height, heightmap, () => samples)
    {
    }

    /// <summary>A terrain whose cell samples are read the first time a sample is asked for.</summary>
    internal DaggerfallTerrainSet(int width, int height, byte[] heightmap, Func<byte[]> samples)
    {
        Width = width;
        Height = height;
        Heightmap = heightmap ?? throw new ArgumentNullException(nameof(heightmap));
        ArgumentNullException.ThrowIfNull(samples);
        _samples = new(samples, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal int Width { get; }

    internal int Height { get; }

    internal byte[] Heightmap { get; }

    /// <summary>Every cell's 25 elevation samples in row-major cell order.</summary>
    internal byte[] Samples => _samples.Value;

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
