using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>The season input used when selecting source climate terrain variants.</summary>
internal enum DaggerfallExteriorSeason
{
    Summer,
    Winter,
}

/// <summary>
/// The normalized climate answer for one resident terrain cell. The archive ids are source facts
/// from MapsFile.GetClimateSettings and its winter +1 rule; callers own when the season changes.
/// </summary>
internal readonly record struct DaggerfallExteriorTerrainVariant(
    DaggerfallExteriorCellId Cell,
    int ClimateValue,
    string ClimateName,
    int GroundTextureArchive,
    DaggerfallExteriorSeason Season)
{
    internal bool IsWinter => Season == DaggerfallExteriorSeason.Winter;
    /// <summary>The donor nature archive selected from the source climate, including its winter set when supported.</summary>
    internal int NatureTextureArchive => DaggerfallExteriorEnvironment.NatureTextureArchive(ClimateValue, Season);

    /// <summary>The donor suppresses winter for desert and subtropical ground sets.</summary>
    internal bool GroundSupportsWinter => DaggerfallExteriorEnvironment.GroundSupportsWinter(ClimateValue);

    /// <summary>The donor nature archives carrying snow are mountains and the three woodland sets.</summary>
    internal bool NatureSupportsWinter => DaggerfallExteriorEnvironment.NatureSupportsWinter(ClimateValue);

    internal bool IsDesert => DaggerfallExteriorEnvironment.IsDesertClimate(ClimateValue);
}

/// <summary>A deterministic nature flat admitted beside one source terrain cell.</summary>
internal readonly record struct DaggerfallExteriorNaturePlacement(
    DaggerfallExteriorCellId Cell,
    int TileX,
    int TileY,
    int SpriteRecord,
    Vector3 LocalPosition,
    float SlopeDegrees)
{
    /// <summary>The climate-selected donor nature archive used for this sprite record.</summary>
    internal int SpriteArchive { get; init; }

    internal ulong StableId => DaggerfallExteriorEnvironment.NatureObjectId(Cell, TileX, TileY);
}

/// <summary>
/// One source-derived water volume in a cell's local terrain frame. The Engine spatial ground mesh
/// remains the collision owner; movement consumers query this product fact for swimming and breathing
/// policy rather than adding a second collision or trigger mechanism.
/// </summary>
internal readonly record struct DaggerfallExteriorWaterVolume(
    DaggerfallExteriorCellId Cell,
    Vector3 Minimum,
    Vector3 Maximum)
{
    internal bool Contains(Vector3 localPosition, DaggerfallExteriorWorldOrigin origin)
    {
        Vector3 cellLocal = localPosition - origin.LocalTranslation(Cell);
        return cellLocal.X >= Minimum.X && cellLocal.X <= Maximum.X
            && cellLocal.Y >= Minimum.Y && cellLocal.Y <= Maximum.Y
            && cellLocal.Z >= Minimum.Z && cellLocal.Z <= Maximum.Z;
    }
}

/// <summary>
/// Reconciles source terrain facts into a resident, queryable environment projection. This is a
/// product data owner: no Engine handles or alternate update loop are introduced here.
/// </summary>
internal sealed class DaggerfallExteriorEnvironment
{
    private const int TerrainTileDimension = 128;
    private const int WaterTileDimension = 16;
    private const int TerrainSamplesPerWaterTile = (DaggerfallTerrainSurfaceBuilder.SampleDimension - 1) / WaterTileDimension;
    private const float BeachElevation = 40F;
    private const float NatureClearance = 4F;
    private const float MaximumNatureSlope = 50F;
    private const float SlopeSinkRatio = 70F;
    private const float BaseChanceOnDirt = .2F;
    private const float BaseChanceOnGrass = .9F;
    private const float BaseChanceOnStone = .05F;
    private const ulong NatureSeed = 0xD4_4E_41_54_55_52_45UL;
    private const ulong NatureObjectPrefix = 0xD900_0000_0000_0000UL;
    private const int NatureCellBits = 20;
    private const int NatureTileBits = 14;
    private const uint NatureCellMask = (1U << NatureCellBits) - 1U;
    private const uint NatureTileMask = (1U << NatureTileBits) - 1U;

    private readonly Dictionary<DaggerfallExteriorCellId, CellFacts> _cells = [];
    private DaggerfallExteriorWorldOrigin _origin;
    private DaggerfallExteriorSeason _season;
    private bool _initialized;

    internal DaggerfallExteriorEnvironment(DaggerfallExteriorSeason season = DaggerfallExteriorSeason.Summer)
    {
        _season = season;
    }

    internal DaggerfallExteriorSeason Season => _season;

    internal IReadOnlyList<DaggerfallExteriorWaterVolume> WaterVolumes => _cells.Values
        .OrderBy(value => value.Cell.Y).ThenBy(value => value.Cell.X)
        .SelectMany(value => value.Water)
        .ToArray();

    internal IReadOnlyList<DaggerfallExteriorNaturePlacement> NaturePlacements => _cells.Values
        .OrderBy(value => value.Cell.Y).ThenBy(value => value.Cell.X)
        .SelectMany(value => value.Nature)
        .ToArray();

    internal IReadOnlyList<DaggerfallExteriorTerrainVariant> TerrainVariants => _cells.Values
        .OrderBy(value => value.Cell.Y).ThenBy(value => value.Cell.X)
        .Select(value => value.Variant)
        .ToArray();

    internal DaggerfallExteriorWorldOrigin Origin => _initialized
        ? _origin
        : throw new InvalidOperationException("Exterior environment has not been initialized.");

    internal void SetSeason(DaggerfallExteriorSeason season)
    {
        if (!Enum.IsDefined(season)) throw new ArgumentOutOfRangeException(nameof(season));
        if (_season == season) return;
        _season = season;
        if (_initialized)
        {
            // Season changes are material policy, not a reason to discard source placement facts.
            // The next residency reconciliation rebuilds the compact variant records atomically.
            _cells.Clear();
            _initialized = false;
        }
    }

    internal void Reconcile(
        IEnumerable<DaggerfallExteriorCellId> residentCells,
        DaggerfallExteriorWorldOrigin origin,
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory,
        IReadOnlyDictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> locations,
        DaggerfallWorldGridsSet grids)
    {
        ArgumentNullException.ThrowIfNull(residentCells);
        ArgumentNullException.ThrowIfNull(surfaceFactory);
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(grids);

        DaggerfallExteriorCellId[] desired = residentCells
            .OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        if (desired.Distinct().Count() != desired.Length)
            throw new ArgumentException("Exterior environment residency contains a duplicate cell.", nameof(residentCells));

        Dictionary<DaggerfallExteriorCellId, CellFacts> staged = [];
        foreach (DaggerfallExteriorCellId cell in desired)
        {
            DaggerfallTerrainSurface surface = surfaceFactory(cell)
                ?? throw new InvalidOperationException($"Exterior cell ({cell.X},{cell.Y}) produced no terrain surface for environment admission.");
            locations.TryGetValue(cell, out DaggerfallSiteExterior? location);
            DaggerfallClimateCell climate = grids.ClimateAtWorldPixel(cell.X, cell.Y);
            DaggerfallExteriorTerrainVariant variant = new(
                cell,
                climate.Value,
                climate.Name,
                GroundTextureArchive(climate.Value, _season),
                _season);
            staged.Add(cell, new(
                cell,
                variant,
                BuildWater(cell, surface),
                BuildNature(cell, surface, variant, location)));
        }

        _cells.Clear();
        foreach ((DaggerfallExteriorCellId cell, CellFacts facts) in staged)
            _cells.Add(cell, facts);
        _origin = origin;
        _initialized = true;
    }

    internal bool TryReadWater(Vector3 localPosition, out DaggerfallExteriorWaterVolume volume)
    {
        foreach (DaggerfallExteriorWaterVolume candidate in WaterVolumes)
        {
            if (candidate.Contains(localPosition, _origin))
            {
                volume = candidate;
                return true;
            }
        }
        volume = default;
        return false;
    }

    internal static ulong NatureObjectId(DaggerfallExteriorCellId cell, int tileX, int tileY)
    {
        if ((uint)tileX >= TerrainTileDimension || (uint)tileY >= TerrainTileDimension)
            throw new ArgumentOutOfRangeException(nameof(tileX));
        if ((uint)cell.X > NatureCellMask || (uint)cell.Y > NatureCellMask)
            throw new ArgumentOutOfRangeException(nameof(cell),
                "Exterior nature coordinates exceed the stable ID range.");
        uint tile = checked((uint)((tileY * TerrainTileDimension) + tileX));
        ulong value = NatureObjectPrefix
            | ((ulong)(uint)cell.X << (NatureCellBits + NatureTileBits))
            | ((ulong)(uint)cell.Y << NatureTileBits)
            | (tile & NatureTileMask);
        return value == 0 ? 1UL : value;
    }

    internal static int GroundTextureArchive(int climateValue, DaggerfallExteriorSeason season) => climateValue switch
    {
        223 or 227 or 228 => season == DaggerfallExteriorSeason.Winter ? 403 : 402,
        224 or 225 or 229 => 2,
        226 or 230 => season == DaggerfallExteriorSeason.Winter ? 103 : 102,
        231 or 232 => season == DaggerfallExteriorSeason.Winter ? 303 : 302,
        _ => season == DaggerfallExteriorSeason.Winter ? 303 : 302,
    };

    /// <summary>Maps MapsFile.GetWorldClimateSettings to its nature set and winter variant.</summary>
    internal static int NatureTextureArchive(int climateValue, DaggerfallExteriorSeason season)
    {
        int archive = climateValue switch
        {
            223 or 231 => 504,
            224 or 225 => 503,
            226 => 510,
            227 => 500,
            228 => 502,
            229 => 501,
            230 => 506,
            232 => 508,
            _ => 504,
        };
        return season == DaggerfallExteriorSeason.Winter && NatureSupportsWinter(climateValue)
            ? archive + 1
            : archive;
    }

    internal static bool IsDesertClimate(int climateValue) => climateValue is 224 or 225 or 229;

    internal static bool GroundSupportsWinter(int climateValue) => !IsDesertClimate(climateValue);

    internal static bool NatureSupportsWinter(int climateValue) => climateValue is 223 or 226 or 230 or 231 or 232;

    private static IReadOnlyList<DaggerfallExteriorWaterVolume> BuildWater(
        DaggerfallExteriorCellId cell,
        DaggerfallTerrainSurface surface)
    {
        List<DaggerfallExteriorWaterVolume> water = [];
        float waterline = DaggerfallTerrainSurfaceBuilder.OceanElevation * DaggerfallTerrainSurfaceBuilder.TerrainScale;
        float waterNormalized = DaggerfallTerrainSurfaceBuilder.OceanElevation / DaggerfallTerrainSurfaceBuilder.MaxTerrainHeight;
        float tileSize = DaggerfallTerrainSurfaceBuilder.HorizontalSize / WaterTileDimension;
        for (int tileY = 0; tileY < WaterTileDimension; tileY++)
        {
            for (int tileX = 0; tileX < WaterTileDimension; tileX++)
            {
                int sampleX = tileX * TerrainSamplesPerWaterTile;
                int sampleY = tileY * TerrainSamplesPerWaterTile;
                if (!IsWaterSample(surface, sampleX, sampleY, waterNormalized)
                    || !IsWaterSample(surface, sampleX + TerrainSamplesPerWaterTile, sampleY, waterNormalized)
                    || !IsWaterSample(surface, sampleX, sampleY + TerrainSamplesPerWaterTile, waterNormalized)
                    || !IsWaterSample(surface, sampleX + TerrainSamplesPerWaterTile, sampleY + TerrainSamplesPerWaterTile, waterNormalized))
                    continue;

                water.Add(new(
                    cell,
                    new(tileX * tileSize, 0F, tileY * tileSize),
                    new((tileX + 1) * tileSize, waterline, (tileY + 1) * tileSize)));
            }
        }
        return water;
    }

    private static bool IsWaterSample(DaggerfallTerrainSurface surface, int x, int y, float threshold) =>
        surface.NormalizedHeights[(y * DaggerfallTerrainSurfaceBuilder.SampleDimension) + x] <= threshold + .0001F;

    private static IReadOnlyList<DaggerfallExteriorNaturePlacement> BuildNature(
        DaggerfallExteriorCellId cell,
        DaggerfallTerrainSurface surface,
        DaggerfallExteriorTerrainVariant variant,
        DaggerfallSiteExterior? location)
    {
        List<DaggerfallExteriorNaturePlacement> nature = [];
        Dictionary<(int X, int Y), DaggerfallSiteGroundTile> ground = location?.GroundTiles
            .ToDictionary(tile => (tile.X, tile.Y)) ?? [];
        int seed = Hash(cell.X, cell.Y, NatureSeed);
        float elevation = Math.Clamp(surface.NormalizedHeights.Average() * DaggerfallTerrainSurfaceBuilder.MaxTerrainHeight / 128F, .4F, 1F);
        float climateScale = variant.IsDesert ? .25F : 1F;
        float chanceDirt = BaseChanceOnDirt * elevation * climateScale;
        float chanceGrass = BaseChanceOnGrass * elevation * climateScale;
        float chanceStone = BaseChanceOnStone * elevation * climateScale;
        for (int tileY = 0; tileY < TerrainTileDimension; tileY++)
        {
            for (int tileX = 0; tileX < TerrainTileDimension; tileX++)
            {
                if (location is not null
                    && tileX >= location.MinX - (int)NatureClearance && tileX <= location.MaxX + (int)NatureClearance
                    && tileY >= location.MinY - (int)NatureClearance && tileY <= location.MaxY + (int)NatureClearance)
                    continue;

                float slope = Slope(surface, tileX, tileY);
                if (slope > MaximumNatureSlope) continue;
                float height = Height(surface, tileX, tileY);
                if (height < BeachElevation) continue;
                int terrainKind = ground.TryGetValue((tileX, tileY), out DaggerfallSiteGroundTile? tile)
                    ? tile!.TextureRecord & 0x3F
                    : GeneratedTerrainKind(surface, cell, tileX, tileY);
                float chance = terrainKind switch
                {
                    1 => chanceDirt,
                    2 => chanceGrass,
                    3 => chanceStone,
                    _ => 0F,
                };
                int random = Hash(seed, tileX, tileY);
                if ((random / (float)int.MaxValue) > chance) continue;
                int record = 1 + (Math.Abs(Hash(seed ^ 0x4E415455, tileX, tileY)) % 31);
                float y = height - (slope / SlopeSinkRatio);
                nature.Add(new(
                    cell,
                    tileX,
                    tileY,
                    record,
                    new(tileX * DaggerfallTerrainSurfaceBuilder.SampleSpacing, y, tileY * DaggerfallTerrainSurfaceBuilder.SampleSpacing),
                    slope)
                {
                    SpriteArchive = variant.NatureTextureArchive,
                });
            }
        }
        return nature;
    }

    /// <summary>
    /// Reproduces DefaultTerrainTexturing.GenerateTileData: water and beach are height ranges,
    /// then seeded latitude/longitude noise selects dirt, grass, or stone. Location ground records
    /// are supplied by the normalized site and bypass this generated branch exactly as donor tilemap
    /// samples do.
    /// </summary>
    private static int GeneratedTerrainKind(DaggerfallTerrainSurface surface, DaggerfallExteriorCellId cell, int tileX, int tileY)
    {
        int sampleX = Math.Clamp((int)(DaggerfallTerrainSurfaceBuilder.SampleDimension * (tileX / (float)TerrainTileDimension)), 0, DaggerfallTerrainSurfaceBuilder.SampleDimension - 1);
        int sampleY = Math.Clamp((int)(DaggerfallTerrainSurfaceBuilder.SampleDimension * (tileY / (float)TerrainTileDimension)), 0, DaggerfallTerrainSurfaceBuilder.SampleDimension - 1);
        float height = surface.NormalizedHeights[(sampleY * DaggerfallTerrainSurfaceBuilder.SampleDimension) + sampleX]
            * DaggerfallTerrainSurfaceBuilder.MaxTerrainHeight;
        if (height <= DaggerfallTerrainSurfaceBuilder.OceanElevation)
            return 0;

        int index = (tileY * TerrainTileDimension) + tileX;
        float beachJitter = DeterministicRandom.CreateFromIndex((uint)index).NextFloat(-1.5F, 1.5F);
        if (height <= BeachElevation + beachJitter)
            return 1;

        int latitude = checked((cell.X * TerrainTileDimension) + tileX);
        int longitude = checked(64_000 - (cell.Y * TerrainTileDimension) + tileY);
        float weight = DaggerfallTerrainSurfaceBuilder.DonorTerrainWeight(latitude, longitude);
        return weight < .5F ? 1 : weight > .95F ? 3 : 2;
    }

    private static float Height(DaggerfallTerrainSurface surface, int tileX, int tileY)
    {
        int x = Math.Min(tileX, DaggerfallTerrainSurfaceBuilder.SampleDimension - 1);
        int y = Math.Min(tileY, DaggerfallTerrainSurfaceBuilder.SampleDimension - 1);
        return surface.Vertices[(y * DaggerfallTerrainSurfaceBuilder.SampleDimension) + x].Y;
    }

    private static float Slope(DaggerfallTerrainSurface surface, int tileX, int tileY)
    {
        int x = Math.Min(tileX, DaggerfallTerrainSurfaceBuilder.SampleDimension - 1);
        int y = Math.Min(tileY, DaggerfallTerrainSurfaceBuilder.SampleDimension - 1);
        int left = Math.Max(0, x - 1), right = Math.Min(DaggerfallTerrainSurfaceBuilder.SampleDimension - 1, x + 1);
        int down = Math.Max(0, y - 1), up = Math.Min(DaggerfallTerrainSurfaceBuilder.SampleDimension - 1, y + 1);
        float dx = (Height(surface, right, y) - Height(surface, left, y)) / ((right - left) * DaggerfallTerrainSurfaceBuilder.SampleSpacing);
        float dz = (Height(surface, x, up) - Height(surface, x, down)) / ((up - down) * DaggerfallTerrainSurfaceBuilder.SampleSpacing);
        return MathF.Atan(MathF.Sqrt((dx * dx) + (dz * dz))) * (180F / MathF.PI);
    }

    private static int Hash(int a, int b, ulong seed)
    {
        unchecked
        {
            uint value = (uint)seed ^ (uint)(seed >> 32);
            value ^= (uint)a * 0x9E37_79B9U;
            value = (value << 13) | (value >> 19);
            value ^= (uint)b * 0x85EB_CA6BU;
            value ^= value >> 16;
            value *= 0xC2B2_AE35U;
            value ^= value >> 13;
            return (int)(value & 0x7FFF_FFFFU);
        }
    }

    private static int Hash(int a, int b, int c) => Hash(a, b, unchecked((ulong)(uint)c));

    private sealed record CellFacts(
        DaggerfallExteriorCellId Cell,
        DaggerfallExteriorTerrainVariant Variant,
        IReadOnlyList<DaggerfallExteriorWaterVolume> Water,
        IReadOnlyList<DaggerfallExteriorNaturePlacement> Nature);

    /// <summary>
    /// Unity.Mathematics.Random.CreateFromIndex/NextFloat's small value path, kept local to the
    /// donor-derived nature admission so no product-wide random stream is consumed by terrain.
    /// </summary>
    private struct DeterministicRandom(uint state)
    {
        private uint _state = state == 0 ? 1U : state;

        internal static DeterministicRandom CreateFromIndex(uint index)
        {
            uint hash = index;
            hash ^= 2_747_636_419U;
            hash *= 2_654_435_769U;
            hash ^= hash >> 16;
            hash *= 2_654_435_769U;
            hash ^= hash >> 16;
            hash *= 2_654_435_769U;
            return new(hash);
        }

        internal float NextFloat(float min, float max) => min + ((max - min) * NextFloat());

        private float NextFloat()
        {
            _state = (_state * 1_664_525U) + 1_013_904_223U;
            uint bits = (_state >> 9) | 0x3F80_0000U;
            return BitConverter.UInt32BitsToSingle(bits) - 1F;
        }
    }
}
