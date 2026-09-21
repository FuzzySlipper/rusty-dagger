namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>The normalized wilderness terrain, loaded from the pack alone.</summary>
/// <param name="Width">The heightmap width.</param>
/// <param name="Height">The heightmap height.</param>
/// <param name="Heightmap">The row-major heightmap bytes.</param>
/// <param name="Samples">The 25 elevation samples of every cell in row-major cell order.</param>
internal sealed record DaggerfallTerrainSet(int Width, int Height, byte[] Heightmap, byte[] Samples)
{
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
