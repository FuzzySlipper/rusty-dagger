using System.Numerics;
using WorldRpg.Kit.Controls;
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
    int TileX,
    int TileY,
    Vector3 Minimum,
    Vector3 Maximum,
    int TileWidth = 1,
    int TileHeight = 1)
{
    /// <summary>Stable product identity for this source water rectangle, independent of Engine origin.</summary>
    internal ulong StableId => DaggerfallExteriorEnvironment.WaterObjectId(Cell, TileX, TileY, TileWidth, TileHeight);

    /// <summary>Projects this source tile into the current Engine-local frame for movement and triggers.</summary>
    internal CharacterWaterVolume ToCharacterVolume(DaggerfallExteriorWorldOrigin origin) =>
        new CharacterWaterVolume(
            StableId,
            origin.LocalTranslation(Cell) + Minimum,
            origin.LocalTranslation(Cell) + Maximum).Validate();

    internal bool Contains(Vector3 localPosition, DaggerfallExteriorWorldOrigin origin)
    {
        Vector3 cellLocal = localPosition - origin.LocalTranslation(Cell);
        return cellLocal.X >= Minimum.X && cellLocal.X <= Maximum.X
            && cellLocal.Y >= Minimum.Y && cellLocal.Y <= Maximum.Y
            && cellLocal.Z >= Minimum.Z && cellLocal.Z <= Maximum.Z;
    }
}

/// <summary>
/// One source terrain tile in the donor's encoded tilemap. The lower six bits select the terrain
/// archive record; the two flags preserve the donor marching-square rotation and flip bits.
/// </summary>
internal readonly record struct DaggerfallExteriorTerrainTile(int TextureRecord, bool Rotated, bool Flipped)
{
    internal byte Bitfield => checked((byte)(TextureRecord | (Rotated ? 0x40 : 0) | (Flipped ? 0x80 : 0)));
}

/// <summary>
/// Reconciles source terrain facts into a resident, queryable environment projection. This is a
/// product data owner: no Engine handles or alternate update loop are introduced here.
/// </summary>
internal sealed class DaggerfallExteriorEnvironment
{
    private const int TerrainTileDimension = 128;
    private const float BeachElevation = 40F;
    private const float NatureClearance = 4F;
    private const float MaximumNatureSlope = 50F;
    private const float SlopeSinkRatio = 70F;
    private const float BaseChanceOnDirt = .2F;
    private const float BaseChanceOnGrass = .9F;
    private const float BaseChanceOnStone = .05F;
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

    internal bool IsInitialized => _initialized;

    internal IReadOnlyList<DaggerfallExteriorWaterVolume> WaterVolumes => _cells.Values
        .OrderBy(value => value.Cell.Y).ThenBy(value => value.Cell.X)
        .SelectMany(value => value.Water)
        .ToArray();

    /// <summary>
    /// Returns the source water tiles as call-local Engine movement volumes. The environment keeps
    /// only source facts; the lifecycle owns when these values are admitted as active triggers.
    /// </summary>
    internal IReadOnlyList<CharacterWaterVolume> CharacterWaterVolumes(DaggerfallExteriorWorldOrigin origin) =>
        WaterVolumes
            .OrderBy(value => value.TileY)
            .ThenBy(value => value.TileX)
            .Select(value => value.ToCharacterVolume(origin))
            .ToArray();

    internal IReadOnlyList<DaggerfallExteriorNaturePlacement> NaturePlacements => _cells.Values
        .OrderBy(value => value.Cell.Y).ThenBy(value => value.Cell.X)
        .SelectMany(value => value.Nature)
        .ToArray();

    internal IReadOnlyList<DaggerfallExteriorTerrainVariant> TerrainVariants => _cells.Values
        .OrderBy(value => value.Cell.Y).ThenBy(value => value.Cell.X)
        .Select(value => value.Variant)
        .ToArray();

    internal IReadOnlyList<DaggerfallExteriorTerrainTile> TerrainTilesFor(DaggerfallExteriorCellId cell) =>
        _cells.TryGetValue(cell, out CellFacts? facts) ? facts.Terrain : [];

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
            locations.TryGetValue(cell, out DaggerfallSiteExterior? location);
            DaggerfallClimateCell climate = grids.ClimateAtWorldPixel(cell.X, cell.Y);
            DaggerfallExteriorTerrainVariant variant = new(
                cell,
                climate.Value,
                climate.Name,
                GroundTextureArchive(climate.Value, _season),
                _season);
            if (_cells.TryGetValue(cell, out CellFacts? retained) && retained.Variant == variant)
            {
                staged.Add(cell, retained);
                continue;
            }
            DaggerfallTerrainSurface surface = surfaceFactory(cell)
                ?? throw new InvalidOperationException($"Exterior cell ({cell.X},{cell.Y}) produced no terrain surface for environment admission.");
            IReadOnlyList<DaggerfallExteriorTerrainTile> terrain = BuildTerrainTiles(cell, surface, location);
            staged.Add(cell, new(
                cell,
                variant,
                BuildWater(cell, terrain),
                terrain,
                BuildNature(cell, surface, variant, location, terrain)));
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

    internal static ulong WaterObjectId(DaggerfallExteriorCellId cell, int tileX, int tileY) =>
        WaterObjectId(cell, tileX, tileY, 1, 1);

    internal static ulong WaterObjectId(DaggerfallExteriorCellId cell, int tileX, int tileY, int tileWidth, int tileHeight)
    {
        const int coordinateBits = 24;
        const uint coordinateMask = (1U << coordinateBits) - 1U;
        if ((uint)tileX >= TerrainTileDimension || (uint)tileY >= TerrainTileDimension
            || tileWidth <= 0 || tileHeight <= 0
            || tileWidth > TerrainTileDimension - tileX || tileHeight > TerrainTileDimension - tileY)
            throw new ArgumentOutOfRangeException(nameof(tileX));
        if ((uint)cell.X > coordinateMask || (uint)cell.Y > coordinateMask)
            throw new ArgumentOutOfRangeException(nameof(cell),
                "Exterior water coordinates exceed the stable ID range.");
        // A rectangle needs more identity bits than the old 16x16 tile ordinal. Mix every
        // source coordinate and extent into a stable 56-bit payload under the water namespace.
        // This keeps IDs independent of Engine origin and distinguishes two rectangles with the
        // same anchor but different extents after coalescing.
        ulong value = 14_695_981_039_346_656_037UL;
        value = MixWaterIdentity(value, (uint)cell.X);
        value = MixWaterIdentity(value, (uint)cell.Y);
        value = MixWaterIdentity(value, (uint)tileX);
        value = MixWaterIdentity(value, (uint)tileY);
        value = MixWaterIdentity(value, (uint)tileWidth);
        value = MixWaterIdentity(value, (uint)tileHeight);
        value = 0xDA00_0000_0000_0000UL | (value & 0x00FF_FFFF_FFFF_FFFFUL);
        return value == 0 ? 1UL : value;
    }

    private static ulong MixWaterIdentity(ulong value, uint input)
    {
        unchecked
        {
            value ^= input + 0x9E37_79B9U + (value << 6) + (value >> 2);
            return value * 1_099_511_628_211UL;
        }
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
        IReadOnlyList<DaggerfallExteriorTerrainTile> terrain)
    {
        if (terrain.Count != TerrainTileDimension * TerrainTileDimension)
            throw new InvalidOperationException("Exterior water admission requires the donor 128x128 terrain tilemap.");

        // Water is admitted from the final donor tilemap, including authored source tiles. Build
        // horizontal runs first, then merge only equal-width runs that touch in the next row. A
        // dry terrain tile therefore always cuts a volume boundary; no fixed coarse grid can fill
        // a dry gap or cover a source beach tile. The resulting rectangles keep the exact occupied
        // 128x128 area while greatly reducing trigger registrations for ocean cells.
        List<WaterRectangle> rectangles = [];
        Dictionary<(int X, int Width), int> active = [];
        for (int tileY = 0; tileY < TerrainTileDimension; tileY++)
        {
            Dictionary<(int X, int Width), int> next = [];
            int tileX = 0;
            while (tileX < TerrainTileDimension)
            {
                while (tileX < TerrainTileDimension
                    && terrain[(tileY * TerrainTileDimension) + tileX].TextureRecord != 0)
                    tileX++;
                if (tileX == TerrainTileDimension) break;

                int start = tileX;
                while (tileX < TerrainTileDimension
                    && terrain[(tileY * TerrainTileDimension) + tileX].TextureRecord == 0)
                    tileX++;
                int width = tileX - start;
                (int X, int Width) key = (start, width);
                if (active.TryGetValue(key, out int rectangleIndex))
                {
                    WaterRectangle previous = rectangles[rectangleIndex];
                    rectangles[rectangleIndex] = previous with { Height = previous.Height + 1 };
                    next.Add(key, rectangleIndex);
                }
                else
                {
                    next.Add(key, rectangles.Count);
                    rectangles.Add(new(start, tileY, width, 1));
                }
            }
            active = next;
        }

        float waterline = DaggerfallTerrainSurfaceBuilder.OceanElevation * DaggerfallTerrainSurfaceBuilder.TerrainScale;
        float tileSize = DaggerfallTerrainSurfaceBuilder.SampleSpacing;
        return rectangles
            .Select(rectangle => new DaggerfallExteriorWaterVolume(
                cell,
                rectangle.X,
                rectangle.Y,
                new(rectangle.X * tileSize, 0F, rectangle.Y * tileSize),
                new((rectangle.X + rectangle.Width) * tileSize, waterline,
                    (rectangle.Y + rectangle.Height) * tileSize),
                rectangle.Width,
                rectangle.Height))
            .ToArray();
    }

    private readonly record struct WaterRectangle(int X, int Y, int Width, int Height);

    private static IReadOnlyList<DaggerfallExteriorTerrainTile> BuildTerrainTiles(
        DaggerfallExteriorCellId cell,
        DaggerfallTerrainSurface surface,
        DaggerfallSiteExterior? location)
    {
        const int tileDimension = TerrainTileDimension;
        DaggerfallGroundTileGrid source = location?.GroundTiles ?? DaggerfallGroundTileGrid.Empty;
        byte[] kinds = new byte[DaggerfallTerrainSurfaceBuilder.SampleDimension * DaggerfallTerrainSurfaceBuilder.SampleDimension];
        for (int y = 0; y < DaggerfallTerrainSurfaceBuilder.SampleDimension; y++)
        {
            for (int x = 0; x < DaggerfallTerrainSurfaceBuilder.SampleDimension; x++)
            {
                float height = surface.NormalizedHeights[(y * DaggerfallTerrainSurfaceBuilder.SampleDimension) + x]
                    * DaggerfallTerrainSurfaceBuilder.MaxTerrainHeight;
                if (height <= DaggerfallTerrainSurfaceBuilder.OceanElevation)
                {
                    kinds[(y * DaggerfallTerrainSurfaceBuilder.SampleDimension) + x] = 0;
                    continue;
                }
                int sampleIndex = (y * DaggerfallTerrainSurfaceBuilder.SampleDimension) + x;
                float beachJitter = BeachJitter((uint)sampleIndex);
                if (height <= BeachElevation + beachJitter)
                {
                    kinds[(y * DaggerfallTerrainSurfaceBuilder.SampleDimension) + x] = 1;
                    continue;
                }
                int latitude = checked((cell.X * tileDimension) + x);
                int longitude = checked(64_000 - (cell.Y * tileDimension) + y);
                float weight = DaggerfallTerrainSurfaceBuilder.DonorTerrainWeight(latitude, longitude);
                kinds[(y * DaggerfallTerrainSurfaceBuilder.SampleDimension) + x] = weight < .5F ? (byte)1
                    : weight > .95F ? (byte)3 : (byte)2;
            }
        }

        byte[] lookup = TerrainLookupTable();
        List<DaggerfallExteriorTerrainTile> result = new(tileDimension * tileDimension);
        for (int y = 0; y < tileDimension; y++)
        {
            for (int x = 0; x < tileDimension; x++)
            {
                int sourceIndex = (y * tileDimension) + x;
                byte authoredBitfield = source.At(sourceIndex);
                if (source.Contains(sourceIndex))
                {
                    result.Add(new(authoredBitfield & 0x3F, (authoredBitfield & 0x40) != 0, (authoredBitfield & 0x80) != 0));
                    continue;
                }

                int sampleDimension = DaggerfallTerrainSurfaceBuilder.SampleDimension;
                int b0 = kinds[(y * sampleDimension) + x];
                int b1 = kinds[(y * sampleDimension) + x + 1];
                int b2 = kinds[((y + 1) * sampleDimension) + x];
                int b3 = kinds[((y + 1) * sampleDimension) + x + 1];
                int shape = (b0 & 1) | ((b1 & 1) << 1) | ((b2 & 1) << 2) | ((b3 & 1) << 3);
                int ring = (b0 + b1 + b2 + b3) >> 2;
                byte bitfield = lookup[shape | (ring << 4)];
                result.Add(new(bitfield & 0x3F, (bitfield & 0x40) != 0, (bitfield & 0x80) != 0));
            }
        }
        return result;
    }

    private static IReadOnlyList<DaggerfallExteriorNaturePlacement> BuildNature(
        DaggerfallExteriorCellId cell,
        DaggerfallTerrainSurface surface,
        DaggerfallExteriorTerrainVariant variant,
        DaggerfallSiteExterior? location,
        IReadOnlyList<DaggerfallExteriorTerrainTile> terrain)
    {
        List<DaggerfallExteriorNaturePlacement> nature = [];
        DonorRandom random = DonorRandom.FromSeed(unchecked((uint)MakeTerrainKey(cell.X, cell.Y)));
        float elevation = Math.Clamp(surface.SourceWorldHeight / 128F, .4F, 1F);
        float climateScale = variant.IsDesert ? .25F : 1F;
        float chanceDirt = BaseChanceOnDirt * elevation * climateScale;
        float chanceGrass = BaseChanceOnGrass * elevation * climateScale;
        float chanceStone = BaseChanceOnStone * elevation * climateScale;

        // TerrainHelper.SetLocationTiles publishes an inclusive source footprint and the donor
        // stores its expanded Rect with an exclusive max edge. The extra nature clearance is
        // applied only for positive location origins, matching DefaultTerrainNature exactly.
        bool hasLocationRect = location is not null && location.MinX > 0 && location.MinY > 0;
        int locationMinX = hasLocationRect ? location!.MinX - (int)NatureClearance : 0;
        int locationMaxX = hasLocationRect ? location!.MaxX + (int)NatureClearance : 0;
        int locationMinY = hasLocationRect ? location!.MinY - (int)NatureClearance : 0;
        int locationMaxY = hasLocationRect ? location!.MaxY + (int)NatureClearance : 0;
        for (int tileY = 0; tileY < TerrainTileDimension; tileY++)
        {
            for (int tileX = 0; tileX < TerrainTileDimension; tileX++)
            {
                float slope = Slope(surface, tileX, tileY);
                if (slope > MaximumNatureSlope) continue;

                if (hasLocationRect && tileX >= locationMinX && tileX < locationMaxX
                    && tileY >= locationMinY && tileY < locationMaxY)
                    continue;

                int terrainKind = terrain[(tileY * TerrainTileDimension) + tileX].TextureRecord;
                float chance = terrainKind switch
                {
                    1 => chanceDirt,
                    2 => chanceGrass,
                    3 => chanceStone,
                    _ => 0F,
                };
                if (chance <= 0F || random.NextFloat() > chance) continue;

                int heightIndex = (TerrainSampleCoordinate(tileY) * DaggerfallTerrainSurfaceBuilder.SampleDimension)
                    + TerrainSampleCoordinate(tileX);
                float sourceHeight = surface.NormalizedHeights[heightIndex] * DaggerfallTerrainSurfaceBuilder.MaxTerrainHeight;
                if (sourceHeight < BeachElevation) continue;
                float height = Height(surface, tileX, tileY);
                int record = random.NextInt(1, 32);
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

    internal static float BeachJitter(uint sampleIndex) => DonorRandom.CreateFromIndex(sampleIndex).NextFloat(-1.5F, 1.5F);

    private static byte[] TerrainLookupTable()
    {
        byte[] table = new byte[64];
        AddLookupRange(table, 0, 1, 5, 48, false, 0);
        AddLookupRange(table, 2, 1, 10, 51, true, 16);
        AddLookupRange(table, 2, 3, 15, 53, false, 32);
        AddLookupRange(table, 3, 3, 15, 53, true, 48);
        return table;
    }

    private static void AddLookupRange(byte[] table, int baseStart, int baseEnd, int shapeStart,
        int saddleIndex, bool reverse, int offset)
    {
        int[] records = reverse
            ? [baseStart, shapeStart + 2, shapeStart + 2, shapeStart + 1, shapeStart + 2, shapeStart + 1,
                saddleIndex, shapeStart, shapeStart + 2, saddleIndex, shapeStart + 1, shapeStart,
                shapeStart + 1, shapeStart, shapeStart, baseEnd]
            : [baseStart, shapeStart, shapeStart, shapeStart + 1, shapeStart, shapeStart + 1,
                saddleIndex, shapeStart + 2, shapeStart, saddleIndex, shapeStart + 1, shapeStart + 2,
                shapeStart + 1, shapeStart + 2, shapeStart + 2, baseEnd];
        bool[] rotations = reverse
            ? [false, true, false, true, false, false, true, true, true, false, false, false, true, false, true, false]
            : [false, true, false, true, false, false, false, true, true, true, false, false, true, false, true, false];
        bool[] flips = reverse
            ? [false, true, false, true, true, true, false, true, false, false, false, false, false, true, false, false]
            : [false, false, true, false, false, false, false, false, true, false, true, true, true, false, true, false];
        for (int index = 0; index < 16; index++)
            table[offset + index] = checked((byte)(records[index] | (rotations[index] ? 0x40 : 0) | (flips[index] ? 0x80 : 0)));
    }

    private static float Height(DaggerfallTerrainSurface surface, int tileX, int tileY)
    {
        int x = TerrainSampleCoordinate(tileX);
        int y = TerrainSampleCoordinate(tileY);
        return SampleHeight(surface, x, y);
    }

    private static float Slope(DaggerfallTerrainSurface surface, int tileX, int tileY)
    {
        int x = TerrainSampleCoordinate(tileX);
        int y = TerrainSampleCoordinate(tileY);
        int left = Math.Max(0, x - 1), right = Math.Min(DaggerfallTerrainSurfaceBuilder.SampleDimension - 1, x + 1);
        int down = Math.Max(0, y - 1), up = Math.Min(DaggerfallTerrainSurfaceBuilder.SampleDimension - 1, y + 1);
        float dx = (SampleHeight(surface, right, y) - SampleHeight(surface, left, y))
            / ((right - left) * DaggerfallTerrainSurfaceBuilder.SampleSpacing);
        float dz = (SampleHeight(surface, x, up) - SampleHeight(surface, x, down))
            / ((up - down) * DaggerfallTerrainSurfaceBuilder.SampleSpacing);
        return MathF.Atan(MathF.Sqrt((dx * dx) + (dz * dz))) * (180F / MathF.PI);
    }

    private static int TerrainSampleCoordinate(int tile) =>
        Math.Clamp((int)MathF.Floor(DaggerfallTerrainSurfaceBuilder.SampleDimension
            * (tile / (float)TerrainTileDimension)), 0, DaggerfallTerrainSurfaceBuilder.SampleDimension - 1);

    private static float SampleHeight(DaggerfallTerrainSurface surface, int sampleX, int sampleY) =>
        surface.Vertices[(sampleY * DaggerfallTerrainSurfaceBuilder.SampleDimension) + sampleX].Y;

    // Exact TerrainHelper.MakeTerrainKey packing from the donor: signed 16-bit pixel coordinates
    // occupy the high and low halves of one deterministic stream seed.
    private static int MakeTerrainKey(int mapPixelX, int mapPixelY) =>
        unchecked(((short)mapPixelY << 16) + (short)mapPixelX);

    private sealed record CellFacts(
        DaggerfallExteriorCellId Cell,
        DaggerfallExteriorTerrainVariant Variant,
        IReadOnlyList<DaggerfallExteriorWaterVolume> Water,
        IReadOnlyList<DaggerfallExteriorTerrainTile> Terrain,
        IReadOnlyList<DaggerfallExteriorNaturePlacement> Nature);

    /// <summary>
    /// Indexed beach jitter follows Unity.Mathematics.Random. Nature uses a stable local sequence
    /// for deterministic terrain decoration; it does not consume the Engine gameplay random stream.
    /// The decoration sequence does not claim native UnityEngine.Random bit parity.
    /// </summary>
    private struct DonorRandom(uint state)
    {
        private uint _state = state == 0 ? 1U : state;

        internal static DonorRandom CreateFromIndex(uint index)
        {
            // Unity.Mathematics.Random.CreateFromIndex uses WangHash(index + 62), then the
            // Random(uint) constructor advances once before the first draw.
            DonorRandom random = new(WangHash(index + 62U));
            random.NextState();
            return random;
        }

        internal static DonorRandom FromSeed(uint seed)
        {
            DonorRandom random = new(seed);
            random.NextState();
            return random;
        }

        internal float NextFloat(float min, float max) => NextFloat() * (max - min) + min;

        internal int NextInt(int min, int max)
        {
            uint range = checked((uint)(max - min));
            return checked((int)((NextState() * (ulong)range) >> 32) + min);
        }

        internal float NextFloat()
        {
            return BitConverter.UInt32BitsToSingle(0x3F80_0000U | (NextState() >> 9)) - 1F;
        }

        private uint NextState()
        {
            uint previous = _state;
            uint value = previous;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return previous;
        }

        private static uint WangHash(uint value)
        {
            unchecked
            {
                value = (value ^ 61U) ^ (value >> 16);
                value *= 9U;
                value ^= value >> 4;
                value *= 0x27D4_EB2DU;
                value ^= value >> 15;
                return value == 0 ? 1U : value;
            }
        }
    }
}
