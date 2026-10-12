using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Where one exterior location's content lies on its map pixel's terrain. The donor places a location's RMB blocks
/// from its terrain-tile origin (<c>TerrainHelper.GetLocationTerrainTileOrigin</c>), block rows growing with the
/// terrain's tile rows, and paints the blocks' ground tiles and flattens the terrain under that same footprint.
/// Terrain and location content both run source block/tile rows toward -Z. The location begins at
/// its donor tile origin; ground tiles, flattening and nature clearance retain their source indices.
/// </summary>
internal static class DaggerfallLocationTerrainFrame
{
    private const int TileDimension = DaggerfallTerrainSurfaceBuilder.SampleDimension - 1;

    /// <summary>The navigation cells one terrain tile spans: a map pixel is 1,024 cells and 128 tiles wide.</summary>
    internal const long NavigationCellsPerTile = DaggerfallSiteLifecycle.NavigationCellsPerExteriorCell / TileDimension;

    /// <summary>The profile origin in donor terrain tiles from the map pixel's south-west corner.</summary>
    internal static (int X, int Y) TileOffset(DaggerfallSiteExterior location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return (location.TileOriginX, location.TileOriginY);
    }

    /// <summary>The profile origin from the map pixel's corner, in the exterior frame (heights are the terrain's).</summary>
    internal static Vector3 Offset(DaggerfallSiteExterior location)
    {
        (int x, int y) = TileOffset(location);
        return new(x * DaggerfallTerrainSurfaceBuilder.SampleSpacing, 0F, -y * DaggerfallTerrainSurfaceBuilder.SampleSpacing);
    }

    /// <summary>The donor's flattening rectangle (FLD footprint plus clearance) under the normalized content.</summary>
    internal static DaggerfallTerrainLocationFlattening Flattening(DaggerfallSiteExterior location) =>
        new(location.MinX, location.MaxX, location.MinY, location.MaxY);
}
