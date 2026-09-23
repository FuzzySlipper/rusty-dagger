using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The generated collision surface for one wilderness map pixel. Vertices use the donor's local
/// terrain frame: x grows with the source map-pixel x direction and z grows toward decreasing source
/// map-pixel y. The containing map pixel is placed in the world separately.
/// </summary>
internal sealed class DaggerfallTerrainSurface
{
    internal DaggerfallTerrainSurface(
        int mapPixelX,
        int mapPixelY,
        Vector3[] vertices,
        Triangle[] triangles,
        float[] normalizedHeights)
    {
        MapPixelX = mapPixelX;
        MapPixelY = mapPixelY;
        Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        Triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));
        NormalizedHeights = normalizedHeights ?? throw new ArgumentNullException(nameof(normalizedHeights));
    }

    /// <summary>The source map-pixel x identity represented by this surface.</summary>
    internal int MapPixelX { get; }

    /// <summary>The source map-pixel y identity represented by this surface.</summary>
    internal int MapPixelY { get; }

    /// <summary>129 by 129 vertices in x-major, then z-row order.</summary>
    internal Vector3[] Vertices { get; }

    /// <summary>Two upward-facing triangles for every adjacent sample quad.</summary>
    internal Triangle[] Triangles { get; }

    /// <summary>Donor-normalized heights matching <see cref="Vertices"/> by index.</summary>
    internal float[] NormalizedHeights { get; }
}

/// <summary>
/// The source-normalized rectangle the donor uses to flatten one exterior location. The values are
/// in the containing 128-by-128 terrain tile and are divided by 128 by the blend pass.
/// </summary>
/// <param name="MinX">The inclusive terrain-tile x minimum after FLD clearance.</param>
/// <param name="MaxX">The inclusive terrain-tile x maximum after FLD clearance.</param>
/// <param name="MinY">The inclusive terrain-tile y minimum after FLD clearance.</param>
/// <param name="MaxY">The inclusive terrain-tile y maximum after FLD clearance.</param>
internal sealed record DaggerfallTerrainLocationFlattening(int MinX, int MaxX, int MinY, int MaxY)
{
    internal void Validate()
    {
        if (MaxX < MinX || MaxY < MinY)
        {
            throw new ArgumentException($"Terrain flattening rectangle ({MinX},{MinY})..({MaxX},{MaxY}) is inverted.", nameof(DaggerfallTerrainLocationFlattening));
        }
    }
}

/// <summary>
/// Reconstructs Daggerfall Unity's default 129 by 129 wilderness surface from the normalized
/// WOODS heightmap and per-cell 5 by 5 samples. The source algorithm is DefaultTerrainSampler:
/// its 4 by 4 and 9 by 9 source windows, cubic interpolation, scales, ocean clamp, and maximum
/// terrain height are kept here so runtime terrain does not need source-shaped Arena2 readers.
/// </summary>
internal static class DaggerfallTerrainSurfaceBuilder
{
    internal const int SampleDimension = 129;
    internal const int SmallWindowDimension = 4;
    internal const int LargeWindowDimension = 9;
    internal const int LargeCellDimension = 5;
    internal const int LargeSourceCells = 3;
    internal const float BaseHeightScale = 8F;
    internal const float NoiseMapScale = 4F;
    internal const float ExtraNoiseScale = 10F;
    internal const float OceanElevation = 27.2F;
    internal const float MaxTerrainHeight = 1539F;
    internal const float TerrainScale = 1.5F;
    internal const float HorizontalSize = 819.2F;
    internal const float SampleSpacing = 6.4F;
    internal const float TerrainVerticalSize = MaxTerrainHeight * TerrainScale;

    private const int WorldMapTileDimension = 128;
    private const int MaxMapPixelY = 500;

    /// <summary>
    /// Builds one surface in the donor orientation. Source map-pixel edges are clamped by the
    /// normalized terrain set, matching WoodsFile's edge behavior for neighboring source windows.
    /// </summary>
    internal static DaggerfallTerrainSurface Build(DaggerfallTerrainSet terrain, int mapPixelX, int mapPixelY)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ValidateTerrain(terrain, mapPixelX, mapPixelY);

        float[] smallHeightmap = ReadSmallHeightmap(terrain, mapPixelX, mapPixelY);
        float[] largeHeightmap = ReadLargeHeightmap(terrain, mapPixelX, mapPixelY);
        int sampleCount = checked(SampleDimension * SampleDimension);
        Vector3[] vertices = new Vector3[sampleCount];
        float[] normalizedHeights = new float[sampleCount];
        float div = (SampleDimension - 1) / 3F;

        for (int y = 0; y < SampleDimension; y++)
        {
            float ry = y / div;
            int iy = (int)MathF.Floor(ry);
            float sfracy = y / (float)(SampleDimension - 1);
            float fracy = (y - (iy * div)) / div;

            for (int x = 0; x < SampleDimension; x++)
            {
                float rx = x / div;
                int ix = (int)MathF.Floor(rx);
                float sfracx = x / (float)(SampleDimension - 1);
                float fracx = (x - (ix * div)) / div;

                float baseHeight = SampleSmallHeight(smallHeightmap, sfracx, sfracy);
                float noiseHeight = SampleLargeHeight(largeHeightmap, ix, iy, fracx, fracy);
                int noisex = checked((mapPixelX * WorldMapTileDimension) + x);
                int noisey = checked(((MaxMapPixelY - mapPixelY) * WorldMapTileDimension) + y);
                float lowFrequencyNoise = DonorNoise.GetNoise(noisex, noisey, .3F, .5F);
                float highFrequencyNoise = DonorNoise.GetNoise(noisex, noisey, .9F, .5F);

                float scaledHeight = (baseHeight * BaseHeightScale)
                    + (noiseHeight * NoiseMapScale)
                    + ((lowFrequencyNoise * highFrequencyNoise) * ExtraNoiseScale);
                if (scaledHeight < OceanElevation)
                    scaledHeight = OceanElevation;

                float normalizedHeight = Math.Clamp(scaledHeight / MaxTerrainHeight, 0F, 1F);
                int index = Index(x, y, SampleDimension);
                normalizedHeights[index] = normalizedHeight;
                vertices[index] = new(
                    x * SampleSpacing,
                    normalizedHeight * TerrainVerticalSize,
                    y * SampleSpacing);
            }
        }

        Triangle[] triangles = BuildTriangles();
        return new DaggerfallTerrainSurface(mapPixelX, mapPixelY, vertices, triangles, normalizedHeights);
    }

    /// <summary>
    /// Builds one generated surface and applies the donor's location flattening pass before the
    /// surface is admitted to Engine Spatial. The metadata is normalized import output, so this
    /// overload keeps source readers out of the runtime path.
    /// </summary>
    internal static DaggerfallTerrainSurface Build(
        DaggerfallTerrainSet terrain,
        int mapPixelX,
        int mapPixelY,
        DaggerfallTerrainLocationFlattening flattening)
    {
        ArgumentNullException.ThrowIfNull(flattening);
        return ApplyLocationFlattening(Build(terrain, mapPixelX, mapPixelY), flattening);
    }

    /// <summary>
    /// Applies <c>TerrainHelper.BlendLocationTerrainJob</c> to a generated surface. The target is
    /// the generated surface's pre-blend average; interior samples become that target and the edge
    /// samples use the donor's linear or bilinear blend strength.
    /// </summary>
    internal static DaggerfallTerrainSurface ApplyLocationFlattening(
        DaggerfallTerrainSurface surface,
        DaggerfallTerrainLocationFlattening flattening)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(flattening);
        flattening.Validate();
        if (surface.NormalizedHeights.Length != checked(SampleDimension * SampleDimension)
            || surface.Vertices.Length != surface.NormalizedHeights.Length)
        {
            throw new ArgumentException("Terrain surface does not carry the donor 129 by 129 samples.", nameof(surface));
        }

        float targetHeight = surface.NormalizedHeights.Average();
        float xMin = flattening.MinX / (float)WorldMapTileDimension;
        float xMax = flattening.MaxX / (float)WorldMapTileDimension;
        float yMin = flattening.MinY / (float)WorldMapTileDimension;
        float yMax = flattening.MaxY / (float)WorldMapTileDimension;
        float leftScale = 1F / xMin;
        float rightScale = 1F / (1F - xMax);
        float topScale = 1F / yMin;
        float bottomScale = 1F / (1F - yMax);

        float[] normalizedHeights = (float[])surface.NormalizedHeights.Clone();
        Vector3[] vertices = (Vector3[])surface.Vertices.Clone();
        for (int y = 0; y < SampleDimension; y++)
        {
            float v = y / (float)(SampleDimension - 1);
            bool insideY = v >= yMin && v <= yMax;
            for (int x = 0; x < SampleDimension; x++)
            {
                float u = x / (float)(SampleDimension - 1);
                bool insideX = u >= xMin && u <= xMax;
                float strength;
                if (insideX || insideY)
                {
                    if (insideY && u <= xMin)
                    {
                        strength = u * leftScale;
                    }
                    else if (insideY && u >= xMax)
                    {
                        strength = (1F - u) * rightScale;
                    }
                    else if (insideX && v <= yMin)
                    {
                        strength = v * topScale;
                    }
                    else if (insideX && v >= yMax)
                    {
                        strength = (1F - v) * bottomScale;
                    }
                    else
                    {
                        strength = 0F;
                    }
                }
                else
                {
                    float xs = u <= xMin ? u * leftScale : u >= xMax ? (1F - u) * rightScale : 0F;
                    float ys = v <= yMin ? v * topScale : v >= yMax ? (1F - v) * bottomScale : 0F;
                    // TerrainHelper.BilinearInterpolator(0, 0, 0, 1, xs, ys) reduces to xs*ys.
                    strength = xs * ys;
                }

                int index = Index(x, y, SampleDimension);
                float height = insideX && insideY
                    ? targetHeight
                    : Lerp(normalizedHeights[index], targetHeight, strength);
                normalizedHeights[index] = height;
                Vector3 vertex = vertices[index];
                vertices[index] = new(vertex.X, height * TerrainVerticalSize, vertex.Z);
            }
        }

        return new DaggerfallTerrainSurface(
            surface.MapPixelX,
            surface.MapPixelY,
            vertices,
            surface.Triangles,
            normalizedHeights);
    }

    private static float Lerp(float from, float to, float amount)
    {
        // Unity's Mathf.Lerp clamps t; keeping the clamp matters for rectangles that extend past
        // a terrain edge after the donor's clearance expansion.
        float clamped = Math.Clamp(amount, 0F, 1F);
        return from + ((to - from) * clamped);
    }

    private static float[] ReadSmallHeightmap(DaggerfallTerrainSet terrain, int mapPixelX, int mapPixelY)
    {
        float[] result = new float[SmallWindowDimension * SmallWindowDimension];
        for (int y = 0; y < SmallWindowDimension; y++)
        {
            for (int x = 0; x < SmallWindowDimension; x++)
            {
                result[Index(x, y, SmallWindowDimension)] = terrain.GetHeight(
                    mapPixelX - 2 + x,
                    mapPixelY - 2 + y);
            }
        }

        return result;
    }

    private static float[] ReadLargeHeightmap(DaggerfallTerrainSet terrain, int mapPixelX, int mapPixelY)
    {
        float[] result = new float[LargeWindowDimension * LargeWindowDimension];
        for (int sourceY = 0; sourceY < LargeSourceCells; sourceY++)
        {
            for (int sourceX = 0; sourceX < LargeSourceCells; sourceX++)
            {
                byte[] source = terrain.GetSamples(mapPixelX - 1 + sourceX, mapPixelY - sourceY);
                for (int iy = 1; iy <= 3; iy++)
                {
                    for (int ix = 1; ix <= 3; ix++)
                    {
                        int destinationX = (sourceX * 3) + ix - 1;
                        int destinationY = (sourceY * 3) + iy - 1;
                        // WoodsFile.GetLargeHeightMapValuesRange reverses the source y axis and
                        // strips the unknown outer ring from each 5 by 5 cell record.
                        result[Index(destinationX, destinationY, LargeWindowDimension)] = source[
                            (4 - iy) * LargeCellDimension + ix];
                    }
                }
            }
        }

        return result;
    }

    private static float SampleSmallHeight(float[] source, float fractionX, float fractionY)
    {
        float x1 = Cubic(
            source[Index(0, 3, SmallWindowDimension)],
            source[Index(1, 3, SmallWindowDimension)],
            source[Index(2, 3, SmallWindowDimension)],
            source[Index(3, 3, SmallWindowDimension)],
            fractionX);
        float x2 = Cubic(
            source[Index(0, 2, SmallWindowDimension)],
            source[Index(1, 2, SmallWindowDimension)],
            source[Index(2, 2, SmallWindowDimension)],
            source[Index(3, 2, SmallWindowDimension)],
            fractionX);
        float x3 = Cubic(
            source[Index(0, 1, SmallWindowDimension)],
            source[Index(1, 1, SmallWindowDimension)],
            source[Index(2, 1, SmallWindowDimension)],
            source[Index(3, 1, SmallWindowDimension)],
            fractionX);
        float x4 = Cubic(
            source[Index(0, 0, SmallWindowDimension)],
            source[Index(1, 0, SmallWindowDimension)],
            source[Index(2, 0, SmallWindowDimension)],
            source[Index(3, 0, SmallWindowDimension)],
            fractionX);
        return Cubic(x1, x2, x3, x4, fractionY);
    }

    private static float SampleLargeHeight(float[] source, int ix, int iy, float fractionX, float fractionY)
    {
        int width = LargeWindowDimension;
        float x1 = Cubic(
            source[Index(ix, iy, width)],
            source[Index(ix + 1, iy, width)],
            source[Index(ix + 2, iy, width)],
            source[Index(ix + 3, iy, width)],
            fractionX);
        float x2 = Cubic(
            source[Index(ix, iy + 1, width)],
            source[Index(ix + 1, iy + 1, width)],
            source[Index(ix + 2, iy + 1, width)],
            source[Index(ix + 3, iy + 1, width)],
            fractionX);
        float x3 = Cubic(
            source[Index(ix, iy + 2, width)],
            source[Index(ix + 1, iy + 2, width)],
            source[Index(ix + 2, iy + 2, width)],
            source[Index(ix + 3, iy + 2, width)],
            fractionX);
        float x4 = Cubic(
            source[Index(ix, iy + 3, width)],
            source[Index(ix + 1, iy + 3, width)],
            source[Index(ix + 2, iy + 3, width)],
            source[Index(ix + 3, iy + 3, width)],
            fractionX);
        return Cubic(x1, x2, x3, x4, fractionY);
    }

    private static float Cubic(float v0, float v1, float v2, float v3, float fraction)
    {
        // This is TerrainHelper.CubicInterpolator verbatim, including its operation order.
        float a = (v3 - v2) - (v0 - v1);
        float b = (v0 - v1) - a;
        float c = v2 - v0;
        float d = v1;
        return (a * (fraction * fraction * fraction))
            + (b * (fraction * fraction))
            + (c * fraction)
            + d;
    }

    private static Triangle[] BuildTriangles()
    {
        int quadsPerSide = SampleDimension - 1;
        Triangle[] triangles = new Triangle[checked(quadsPerSide * quadsPerSide * 2)];
        int index = 0;
        for (int y = 0; y < quadsPerSide; y++)
        {
            for (int x = 0; x < quadsPerSide; x++)
            {
                uint lowerLeft = checked((uint)Index(x, y, SampleDimension));
                uint lowerRight = checked((uint)Index(x + 1, y, SampleDimension));
                uint upperLeft = checked((uint)Index(x, y + 1, SampleDimension));
                uint upperRight = checked((uint)Index(x + 1, y + 1, SampleDimension));
                // Match the Engine fixture's upward-facing right-handed winding.
                triangles[index++] = new(lowerLeft, upperRight, lowerRight);
                triangles[index++] = new(lowerLeft, upperLeft, upperRight);
            }
        }

        return triangles;
    }

    private static void ValidateTerrain(DaggerfallTerrainSet terrain, int mapPixelX, int mapPixelY)
    {
        if (terrain.Width <= 0 || terrain.Height <= 0)
            throw new ArgumentException("Terrain dimensions must be positive.", nameof(terrain));
        if (terrain.Heightmap.Length != checked(terrain.Width * terrain.Height))
            throw new ArgumentException("Terrain heightmap length must match its published dimensions.", nameof(terrain));
        if (terrain.Samples.Length != checked(terrain.Width * terrain.Height * 25))
            throw new ArgumentException("Terrain sample length must carry 25 values for every published cell.", nameof(terrain));
        if ((uint)mapPixelX >= (uint)terrain.Width)
            throw new ArgumentOutOfRangeException(nameof(mapPixelX));
        if ((uint)mapPixelY >= (uint)terrain.Height)
            throw new ArgumentOutOfRangeException(nameof(mapPixelY));
    }

    private static int Index(int x, int y, int dimension) => x + (y * dimension);

    /// <summary>
    /// Deterministic two-dimensional gradient noise used for the donor's two Mathf.PerlinNoise
    /// calls. Unity exposes that function as a native implementation, but the donor repository
    /// does not contain its permutation/gradient table. This canonical improved-Perlin table keeps
    /// the donor's deterministic [0,1] contract; bit-for-bit Unity parity remains an explicit
    /// verification gap until a Unity reference sample or implementation is supplied.
    /// </summary>
    private static class DonorNoise
    {
        private static readonly byte[] Permutation =
        [
            151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
            140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
            247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
            57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
            74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
            60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
            65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
            200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
            52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
            207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
            119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
            129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
            218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
            81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
            184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
            222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
        ];

        internal static float GetNoise(int x, int y, float frequency, float amplitude)
        {
            float value = Perlin(x * frequency, y * frequency) * amplitude;
            return Math.Clamp(value, 0F, 1F);
        }

        private static float Perlin(float x, float y)
        {
            int floorX = (int)MathF.Floor(x);
            int floorY = (int)MathF.Floor(y);
            float localX = x - floorX;
            float localY = y - floorY;
            int x0 = floorX & 255;
            int y0 = floorY & 255;
            int x1 = (x0 + 1) & 255;
            int y1 = (y0 + 1) & 255;

            float sx = Fade(localX);
            float sy = Fade(localY);
            float top = Lerp(
                Gradient(Hash(x0, y0), localX, localY),
                Gradient(Hash(x1, y0), localX - 1F, localY),
                sx);
            float bottom = Lerp(
                Gradient(Hash(x0, y1), localX, localY - 1F),
                Gradient(Hash(x1, y1), localX - 1F, localY - 1F),
                sx);
            return Math.Clamp((Lerp(top, bottom, sy) + 1F) * .5F, 0F, 1F);
        }

        private static int Hash(int x, int y) => Permutation[(Permutation[x] + y) & 255];

        private static float Gradient(int hash, float x, float y)
        {
            return (hash & 3) switch
            {
                0 => x + y,
                1 => -x + y,
                2 => x - y,
                _ => -x - y,
            };
        }

        private static float Fade(float value) => value * value * value * (value * ((value * 6F) - 15F) + 10F);

        private static float Lerp(float from, float to, float amount) => from + (amount * (to - from));
    }
}
