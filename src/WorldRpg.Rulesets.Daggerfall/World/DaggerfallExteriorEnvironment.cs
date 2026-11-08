using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>The season input used when selecting source climate terrain variants.</summary>
internal enum DaggerfallExteriorSeason
{
    Summer,
    Winter,
    Rain,
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
    int TileHeight = 1,
    int RiverSection = 0)
{
    /// <summary>Stable product identity for this source water rectangle, independent of Engine origin.</summary>
    internal ulong StableId => DaggerfallExteriorEnvironment.WaterObjectId(Cell, TileX, TileY, TileWidth, TileHeight, RiverSection);

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
    internal const float BeachElevation = 40F;
    private const float NatureClearance = 4F;
    /// <summary>A noise value that lays soil alone, for asking how much of the ground is soil at all.</summary>
    private const float SoilNoise = .7F;
    private const float MaximumNatureSlope = 50F;
    private const float SlopeSinkRatio = 70F;
    private const float BaseChanceOnDirt = .2F;
    private const float BaseChanceOnGrass = .9F;
    private const float BaseChanceOnStone = .05F;

    private readonly Dictionary<DaggerfallExteriorCellId, CellFacts> _cells = [];
    private readonly HashSet<ulong> _riverBoxes = [];
    private DaggerfallTerrainRivers? _rivers;
    private DaggerfallExteriorWorldOrigin _origin;
    private DaggerfallExteriorSeason _season;
    private readonly HashSet<int> _rainingClimates = [];
    private bool _initialized;
    private readonly IRandomService _random;
    private readonly DaggerfallGroundTuning _ground;

    // Nature decoration draws from one Engine stream per cell, seeded by the cell's terrain key, so a
    // cell's placements are the same on every admission without consuming gameplay draws.
    private const string NatureScope = "daggerfall.exterior.nature.v1";
    private const uint NatureChanceResolution = 1_000_000;

    internal DaggerfallExteriorEnvironment(IRandomService random, DaggerfallExteriorSeason season = DaggerfallExteriorSeason.Summer, DaggerfallGroundTuning? ground = null)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _season = season;
        _ground = ground ?? DaggerfallGroundTuning.Default;
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

    /// <summary>
    /// One resident cell's nature placements. A cell whose facts a reconciliation retained answers the same list
    /// instance, which lets the appearance keep that cell's batches without comparing its placements.
    /// </summary>
    internal IReadOnlyList<DaggerfallExteriorNaturePlacement> NatureFor(DaggerfallExteriorCellId cell) =>
        _cells.TryGetValue(cell, out CellFacts? facts) ? facts.Nature : [];

    internal DaggerfallExteriorWorldOrigin Origin => _initialized
        ? _origin
        : throw new InvalidOperationException("Exterior environment has not been initialized.");

    internal bool UpdateRain(IEnumerable<DaggerfallClimateValueDefinition> climates, Func<int, bool> raining)
    {
        bool changed = false;
        foreach (var climate in climates)
            changed |= raining(climate.Value) ? _rainingClimates.Add(climate.Value) : _rainingClimates.Remove(climate.Value);
        return changed;
    }

    internal void SetSeason(DaggerfallExteriorSeason season, bool weatherChanged = false)
    {
        if (!Enum.IsDefined(season)) throw new ArgumentOutOfRangeException(nameof(season));
        if (_season == season && !weatherChanged) return;
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
            DaggerfallExteriorSeason variantSeason = _season == DaggerfallExteriorSeason.Winter ? _season
                : _rainingClimates.Contains(climate.Value) ? DaggerfallExteriorSeason.Rain : _season;
            DaggerfallExteriorTerrainVariant variant = new(
                cell,
                climate.Value,
                climate.Name,
                GroundTextureArchive(climate.Value, variantSeason),
                variantSeason);
            if (_cells.TryGetValue(cell, out CellFacts? retained) && retained.Variant == variant)
            {
                staged.Add(cell, retained);
                continue;
            }
            DaggerfallTerrainSurface surface = surfaceFactory(cell)
                ?? throw new InvalidOperationException($"Exterior cell ({cell.X},{cell.Y}) produced no terrain surface for environment admission.");
            _rivers ??= surface.Rivers;
            IReadOnlyList<DaggerfallExteriorTerrainTile> terrain = BuildTerrainTiles(cell, surface, location);
            staged.Add(cell, new(
                cell,
                variant,
                [.. BuildWater(cell, terrain), .. BuildRiverWater(cell, surface)],
                terrain,
                BuildNature(cell, surface, variant, location, terrain)));
        }

        _cells.Clear();
        foreach ((DaggerfallExteriorCellId cell, CellFacts facts) in staged)
            _cells.Add(cell, facts);
        _riverBoxes.Clear();
        foreach (DaggerfallExteriorWaterVolume water in _cells.Values.SelectMany(facts => facts.Water).Where(water => water.RiverSection != 0))
            _riverBoxes.Add(water.StableId);
        _origin = origin;
        _initialized = true;
    }

    /// <summary>A river box's depth under its surface: past any channel's bed, which the ground stops at anyway.</summary>
    private const float RiverVolumeDepth = 12;

    /// <summary>
    /// The water someone at a local position is really in, given the volume they are inside. A river's box takes the river's own
    /// surface at that position, however steep it falls and whichever way they came, and past its banks there is none.
    /// Any other water stands at its own top.
    /// </summary>
    internal CharacterWaterVolume? AtSurface(CharacterWaterVolume volume, Vector3 localPosition, DaggerfallExteriorWorldOrigin origin)
    {
        if (_rivers is not { } rivers || !_riverBoxes.Contains(volume.Trigger)) return volume;
        const float size = DaggerfallTerrainSurfaceBuilder.HorizontalSize;
        Vector3 engineLocal = localPosition - origin.Compensation;
        // The rivers are measured from the heightmap's corner, a map pixel west of the ground's.
        double x = engineLocal.X + ((origin.MapPixelX - 1) * (double)size), z = engineLocal.Z + (origin.MapPixelY * (double)size);
        if (rivers.Near(x, z, 0) is not { } river) return null;
        // The box entered last can be the next section's, lower than this water, so the surface is not held to it.
        float surface = (float)river.Surface + origin.Compensation.Y;
        return volume with { Minimum = volume.Minimum with { Y = Math.Min(volume.Minimum.Y, surface - RiverVolumeDepth) }, Maximum = volume.Maximum with { Y = surface } };
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

    internal static ulong WaterObjectId(DaggerfallExteriorCellId cell, int tileX, int tileY) =>
        WaterObjectId(cell, tileX, tileY, 1, 1);

    internal static ulong WaterObjectId(DaggerfallExteriorCellId cell, int tileX, int tileY, int tileWidth, int tileHeight, int riverSection = 0)
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
        if (riverSection != 0) value = MixWaterIdentity(value, (uint)riverSection);
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

    /// <summary>Every ground archive open ground wears, summer and winter.</summary>
    internal static int[] GroundArchives { get; } = [2, 102, 103, 302, 303, 402, 403];

    internal static int GroundTextureArchive(int climateValue, DaggerfallExteriorSeason season)
    {
        int archive = climateValue switch
        {
            223 or 227 or 228 => 402,
            224 or 225 or 229 => 2,
            226 or 230 => 102,
            _ => 302,
        };
        return season == DaggerfallExteriorSeason.Rain ? archive + 2
            : season == DaggerfallExteriorSeason.Winter && GroundSupportsWinter(climateValue) ? archive + 1 : archive;
    }

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

    /// <summary>The first of the donor's nature archives, 500 to 511: the climates' trees, plants and rocks and their winter sets.</summary>
    internal const int FirstNatureArchive = 500;
    internal const int NatureArchiveCount = 12;

    internal static bool IsNatureArchive(int archive) => archive is >= FirstNatureArchive and < FirstNatureArchive + NatureArchiveCount;

    /// <summary>
    /// Whether a tile of a location's map pixel is kept clear of nature. TerrainHelper.SetLocationTiles publishes an
    /// inclusive source footprint and the donor stores its expanded Rect with an exclusive max edge. The extra nature
    /// clearance is applied only for positive location origins, matching DefaultTerrainNature exactly.
    /// </summary>
    internal static bool InNatureClearing(DaggerfallSiteExterior location, int tileX, int tileY)
    {
        DaggerfallTerrainLocationFlattening rect = DaggerfallLocationTerrainFrame.Flattening(location);
        int clearance = (int)NatureClearance;
        return rect.MinX > 0 && rect.MinY > 0
            && tileX >= rect.MinX - clearance && tileX < rect.MaxX + clearance
            && tileY >= rect.MinY - clearance && tileY < rect.MaxY + clearance;
    }

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
                new(rectangle.X * tileSize, 0F, -(rectangle.Y + rectangle.Height) * tileSize),
                new((rectangle.X + rectangle.Width) * tileSize, waterline,
                    -rectangle.Y * tileSize),
                rectangle.Width,
                rectangle.Height))
            .ToArray();
    }

    private readonly record struct WaterRectangle(int X, int Y, int Width, int Height);

    /// <summary>
    /// The water of the rivers in a cell, one box a section wherever a ribbon draws it. A box only says that someone may be in
    /// the river: it reaches the section's higher end, and overhangs its banks and its neighbours on a diagonal, so its top
    /// is no surface. A swimmer's surface comes from <see cref="AtSurface"/>.
    /// </summary>
    private static IEnumerable<DaggerfallExteriorWaterVolume> BuildRiverWater(DaggerfallExteriorCellId cell, DaggerfallTerrainSurface surface)
    {
        if (surface.Rivers is not { } rivers) yield break;
        const float size = DaggerfallTerrainSurfaceBuilder.HorizontalSize, tile = DaggerfallTerrainSurfaceBuilder.SampleSpacing;
        // A cell's corner is heightmap pixel (X - 1, Y), its rows running north from there.
        double cornerX = (cell.X - 1) * (double)size, cornerZ = cell.Y * (double)size;
        int section = 0;
        foreach (DaggerfallRiverSection water in rivers.Sections(cell.X - 1, cell.Y - 1))
        {
            section++;
            double[] xs = [water.From.LeftX, water.From.RightX, water.To.LeftX, water.To.RightX], zs = [water.From.LeftZ, water.From.RightZ, water.To.LeftZ, water.To.RightZ];
            float minX = (float)(xs.Min() - cornerX), maxX = (float)(xs.Max() - cornerX), minZ = (float)(zs.Min() - cornerZ), maxZ = (float)(zs.Max() - cornerZ);
            float high = (float)Math.Max(water.From.Surface, water.To.Surface), low = (float)Math.Min(water.From.Surface, water.To.Surface);
            int tileX = Math.Clamp((int)(minX / tile), 0, TerrainTileDimension - 1), tileY = Math.Clamp((int)(-maxZ / tile), 0, TerrainTileDimension - 1);
            yield return new(cell, tileX, tileY, new(minX, low - RiverVolumeDepth, minZ), new(maxX, high, maxZ), 1, 1, section);
        }
    }

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
                if (source.Contains(sourceIndex))
                {
                    byte authoredBitfield = source.At(sourceIndex);
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

    private IReadOnlyList<DaggerfallExteriorNaturePlacement> BuildNature(
        DaggerfallExteriorCellId cell,
        DaggerfallTerrainSurface surface,
        DaggerfallExteriorTerrainVariant variant,
        DaggerfallSiteExterior? location,
        IReadOnlyList<DaggerfallExteriorTerrainTile> terrain)
    {
        List<DaggerfallExteriorNaturePlacement> nature = [];
        using Rng random = _random.CreateScoped(new ScopedRngCreateRequest(unchecked((ulong)(uint)MakeTerrainKey(cell.X, cell.Y)), NatureScope));
        float elevation = Math.Clamp(surface.SourceWorldHeight / 128F, .4F, 1F);
        float climateScale = variant.IsDesert ? .25F : 1F;
        float chanceDirt = BaseChanceOnDirt * elevation * climateScale;
        float chanceGrass = BaseChanceOnGrass * elevation * climateScale;
        float chanceStone = BaseChanceOnStone * elevation * climateScale;

        for (int tileY = 0; tileY < TerrainTileDimension; tileY++)
        {
            for (int tileX = 0; tileX < TerrainTileDimension; tileX++)
            {
                float slope = Slope(surface, tileX, tileY);
                if (slope > MaximumNatureSlope) continue;

                if (location is not null && InNatureClearing(location, tileX, tileY)) continue;
                if (surface.OnBank(tileX, tileY)) continue;

                int terrainKind = terrain[(tileY * TerrainTileDimension) + tileX].TextureRecord;
                float chance = terrainKind switch
                {
                    1 => chanceDirt,
                    2 => chanceGrass,
                    3 => chanceStone,
                    _ => 0F,
                };
                if (chance <= 0F) continue;
                // Nature grows from soil, so it thins toward a treeline and off bare rock, as the ground under it turns.
                if (NextUnit(random) > chance * Soil(surface, tileX, tileY, slope)) continue;

                int heightIndex = (TerrainSampleCoordinate(tileY) * DaggerfallTerrainSurfaceBuilder.SampleDimension)
                    + TerrainSampleCoordinate(tileX);
                float sourceHeight = surface.NormalizedHeights[heightIndex] * DaggerfallTerrainSurfaceBuilder.MaxTerrainHeight;
                if (sourceHeight < BeachElevation) continue;
                float height = Height(surface, tileX, tileY);
                int record = 1 + checked((int)_random.NextBoundedU32(new ScopedRngBoundedRequest(random, 31)).Value);
                float y = height - (slope / SlopeSinkRatio);
                nature.Add(new(
                    cell,
                    tileX,
                    tileY,
                    record,
                    new(tileX * DaggerfallTerrainSurfaceBuilder.SampleSpacing, y, -tileY * DaggerfallTerrainSurfaceBuilder.SampleSpacing),
                    slope)
                {
                    SpriteArchive = variant.NatureTextureArchive,
                });
            }
        }
        return nature;
    }

    /// <summary>How much of the ground at a tile is soil, from 1 down to 0 under snow or on bare rock.</summary>
    private float Soil(DaggerfallTerrainSurface surface, int tileX, int tileY, float slopeDegrees)
    {
        Vector4 layers = DaggerfallGroundBlend.Weights(_ground, SoilNoise, MathF.Tan(slopeDegrees * MathF.PI / 180F), Height(surface, tileX, tileY));
        return layers.X + layers.Y;
    }

    private float NextUnit(Rng stream) =>
        _random.NextBoundedU32(new ScopedRngBoundedRequest(stream, NatureChanceResolution)).Value / (float)NatureChanceResolution;

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
    /// Indexed beach jitter keeps bit parity with the donor's Unity.Mathematics.Random.CreateFromIndex,
    /// a named algorithm beside the one place it shapes terrain; nature placement draws from the Engine.
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

        internal float NextFloat(float min, float max) => NextFloat() * (max - min) + min;

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
