using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// The terrain-tile rectangle the donor uses when flattening one exterior location. Coordinates are
/// in the containing 128-by-128 map-pixel terrain tile, before the rectangle is normalized for the
/// generated 129-by-129 surface.
/// </summary>
/// <param name="MinX">The inclusive minimum terrain-tile x coordinate.</param>
/// <param name="MaxX">The inclusive maximum terrain-tile x coordinate.</param>
/// <param name="MinY">The inclusive minimum terrain-tile y coordinate.</param>
/// <param name="MaxY">The inclusive maximum terrain-tile y coordinate.</param>
public sealed record DaggerfallLocationTerrainRect(int MinX, int MaxX, int MinY, int MaxY)
{
    public void Validate(string owner)
    {
        if (MaxX < MinX || MaxY < MinY)
        {
            throw new InvalidOperationException($"Exterior location '{owner}' carries an inverted terrain rectangle ({MinX},{MinY})..({MaxX},{MaxY}).");
        }
    }
}

/// <summary>One RMB source block in an exterior location's ordered MAPPITEM grid.</summary>
/// <param name="SourceName">The exact BLOCKS.BSA source key the MAPPITEM record names.</param>
/// <param name="X">The block's zero-based source-grid x coordinate.</param>
/// <param name="Y">The block's zero-based source-grid y coordinate.</param>
public sealed record DaggerfallLocationExteriorBlock(string SourceName, byte X, byte Y)
{
    public void Validate(string owner, byte width, byte height)
    {
        if (string.IsNullOrWhiteSpace(SourceName))
        {
            throw new InvalidOperationException($"Exterior location '{owner}' carries an RMB block with no source name.");
        }

        if (X >= width || Y >= height)
        {
            throw new InvalidOperationException($"Exterior location '{owner}' places block '{SourceName}' at ({X},{Y}) outside its {width}x{height} grid.");
        }
    }
}

/// <summary>
/// One source FLD ground tile in the location's normalized 128-by-128 terrain frame. The frame
/// already applies the donor block ordering and Y reversal, so runtime terrain consumers never need
/// to reopen an RMB header to select a ground material or reproduce its UV orientation.
/// </summary>
/// <param name="X">The location-frame terrain tile x coordinate.</param>
/// <param name="Y">The location-frame terrain tile y coordinate.</param>
/// <param name="TextureRecord">The source four-frame ground texture record.</param>
/// <param name="Rotated">Whether the donor rotates the tile's UVs.</param>
/// <param name="Flipped">Whether the donor reverses the tile's UVs.</param>
public sealed record DaggerfallLocationGroundTile(int X, int Y, byte TextureRecord, bool Rotated, bool Flipped)
{
    /// <summary>Compact source tile value: six record bits followed by the donor UV flags.</summary>
    public byte Bitfield => checked((byte)(TextureRecord | (Rotated ? 0x40 : 0) | (Flipped ? 0x80 : 0)));

    public void Validate(string owner)
    {
        if (X is < 0 or >= 128 || Y is < 0 or >= 128)
            throw new InvalidOperationException($"Exterior location '{owner}' carries ground tile ({X},{Y}) outside its 128-by-128 terrain frame.");
        if (TextureRecord >= 56)
            throw new InvalidOperationException($"Exterior location '{owner}' carries unsupported ground texture record {TextureRecord}.");
    }
}

/// <summary>
/// Normalized MAPPITEM plus FLD facts required to place and flatten an exterior location. Runtime
/// consumers can use this section without reopening MAPS, BLOCKS, RMB or FLD source records.
/// </summary>
/// <param name="LocationId">The source MAPPITEM location identity.</param>
/// <param name="MapPixelX">The containing wilderness map-pixel x coordinate.</param>
/// <param name="MapPixelY">The containing wilderness map-pixel y coordinate.</param>
/// <param name="Width">The source RMB grid width in blocks.</param>
/// <param name="Height">The source RMB grid height in blocks.</param>
/// <param name="Letter1">The source MAPPITEM first-letter selector used in RMB names.</param>
/// <param name="TileOriginX">The donor terrain-tile x origin for the location grid.</param>
/// <param name="TileOriginY">The donor terrain-tile y origin for the location grid.</param>
/// <param name="UsesCustomLocationPosition">Whether the donor's CUST 1x1 override supplied the origin.</param>
/// <param name="BlendClearance">The donor FLD footprint clearance: 3 for TownCity, otherwise 2.</param>
/// <param name="FlattenRect">The FLD-derived location rectangle after clearance.</param>
/// <param name="Blocks">The ordered RMB source blocks and their source-grid coordinates.</param>
public sealed record DaggerfallLocationExterior(
    uint LocationId,
    int MapPixelX,
    int MapPixelY,
    byte Width,
    byte Height,
    char Letter1,
    int TileOriginX,
    int TileOriginY,
    bool UsesCustomLocationPosition,
    int BlendClearance,
    DaggerfallLocationTerrainRect FlattenRect,
    IReadOnlyList<DaggerfallLocationExteriorBlock> Blocks)
{
    /// <summary>Placed building facts specialized for this location, in donor traversal order.</summary>
    public IReadOnlyList<DaggerfallLocationBuilding> Buildings { get; init; } = [];
    /// <summary>
    /// Source ground tiles in row-major 128-by-128 terrain order. Each byte is the donor FLD
    /// tile bitfield: records use bits 0..5, rotation is bit 6, and flip is bit 7. A zero byte
    /// means the donor left that tile for generated terrain texturing. byte[] is intentional:
    /// System.Text.Json writes it as one base64 logical grid instead of retaining thousands of
    /// per-tile objects for every corpus location.
    /// </summary>
    public byte[] GroundTiles { get; init; } = [];
    /// <summary>Raw MAPPITEM building references, distinct from named-building policy assignment.</summary>
    public IReadOnlyList<DaggerfallLocationBuildingReference> BuildingReferences { get; init; } = [];
    public byte PortTownAndUnknown { get; init; }

    /// <summary>Recoverable exhausted city-pool placements; their donor zero values remain published.</summary>
    public IReadOnlyList<string> MissingCityBuildings { get; init; } = [];

    private const int MapWidth = 1000;
    private const int MapHeight = 500;

    public void Validate(string owner)
    {
        ArgumentNullException.ThrowIfNull(FlattenRect);
        ArgumentNullException.ThrowIfNull(Blocks);
        ArgumentNullException.ThrowIfNull(GroundTiles);
        if ((uint)MapPixelX >= MapWidth || (uint)MapPixelY >= MapHeight)
        {
            throw new InvalidOperationException($"Exterior location '{owner}' places its map pixel at ({MapPixelX},{MapPixelY}) outside the {MapWidth}x{MapHeight} wilderness.");
        }

        if (Width == 0 || Height == 0)
        {
            throw new InvalidOperationException($"Exterior location '{owner}' carries a zero-sized {Width}x{Height} RMB grid.");
        }

        int expected = checked(Width * Height);
        if (Blocks.Count != expected)
        {
            throw new InvalidOperationException($"Exterior location '{owner}' carries {Blocks.Count} RMB blocks for its {Width}x{Height} grid, expected {expected}.");
        }

        HashSet<(byte X, byte Y)> coordinates = [];
        foreach (DaggerfallLocationExteriorBlock block in Blocks)
        {
            block.Validate(owner, Width, Height);
            if (!coordinates.Add((block.X, block.Y)))
            {
                throw new InvalidOperationException($"Exterior location '{owner}' names RMB grid coordinate ({block.X},{block.Y}) twice.");
            }
        }

        if (BlendClearance is not 2 and not 3)
        {
            throw new InvalidOperationException($"Exterior location '{owner}' carries FLD blend clearance {BlendClearance}; the donor uses 2 or 3.");
        }

        FlattenRect.Validate(owner);
        if (GroundTiles.Length != 0 && GroundTiles.Length != 128 * 128)
        {
            throw new InvalidOperationException($"Exterior location '{owner}' carries {GroundTiles.Length} ground-grid bytes instead of the donor 128-by-128 terrain frame.");
        }

        foreach (byte bitfield in GroundTiles)
        {
            if ((bitfield & 0x3F) >= 56)
            {
                throw new InvalidOperationException($"Exterior location '{owner}' carries unsupported ground texture record {bitfield & 0x3F}.");
            }
        }
    }
}

/// <summary>A building's source placement and resolved location-specific policy inputs.</summary>
public sealed record DaggerfallLocationBuilding(string SourceKey, int BlockX, int BlockY, int BuildingIndex,
    int BuildingType, int FactionId, int NameSeed, int Quality)
{
    public ushort SourceLocationId { get; init; }
    public string? ModelId { get; init; }
    public float? ModelRadius { get; init; }
}

/// <summary>A source quest building id and its MAPS sector ordinal, before RMB matching.</summary>
public sealed record DaggerfallLocationBuildingReference(ushort LocationId, short Sector)
{
    public int BuildingType { get; init; }
}
