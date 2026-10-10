using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Where one exterior location's content lies on its map pixel's terrain. The donor places a location's RMB blocks
/// from its terrain-tile origin (<c>TerrainHelper.GetLocationTerrainTileOrigin</c>), block rows growing with the
/// terrain's tile rows, and paints the blocks' ground tiles and flattens the terrain under that same footprint.
/// Published and assembled location content is normalized into the right-handed frame instead: block (0, 0) at the
/// profile origin and block rows growing toward -Z, while the terrain keeps the donor's tilemap orientation (tile rows
/// growing toward +Z). The profile frame therefore sits at the footprint's west edge and its last tile row, and the
/// location's ground tiles and flattening rectangle are mirrored across the footprint's rows, so each block draws and
/// stands on its own ground exactly as the donor's does.
/// </summary>
internal static class DaggerfallLocationTerrainFrame
{
    private const int TilesPerBlock = 16;
    private const int TileDimension = DaggerfallTerrainSurfaceBuilder.SampleDimension - 1;

    /// <summary>The navigation cells one terrain tile spans: a map pixel is 1,024 cells and 128 tiles wide.</summary>
    internal const long NavigationCellsPerTile = DaggerfallSiteLifecycle.NavigationCellsPerExteriorCell / TileDimension;

    /// <summary>The profile origin in terrain tiles from the map pixel's corner: the footprint's west edge and its row past the last.</summary>
    internal static (int X, int Y) TileOffset(DaggerfallSiteExterior location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return (location.TileOriginX, checked(location.TileOriginY + (location.Height * TilesPerBlock)));
    }

    /// <summary>The profile origin from the map pixel's corner, in the exterior frame (heights are the terrain's).</summary>
    internal static Vector3 Offset(DaggerfallSiteExterior location)
    {
        (int x, int y) = TileOffset(location);
        return new(x * DaggerfallTerrainSurfaceBuilder.SampleSpacing, 0F, y * DaggerfallTerrainSurfaceBuilder.SampleSpacing);
    }

    /// <summary>The terrain tile row the location's source tilemap row <paramref name="row"/> lies under the normalized content.</summary>
    internal static int MirrorRow(DaggerfallSiteExterior location, int row)
    {
        ArgumentNullException.ThrowIfNull(location);
        return checked((2 * location.TileOriginY) + (location.Height * TilesPerBlock) - 1 - row);
    }

    /// <summary>The donor's flattening rectangle (FLD footprint plus clearance) under the normalized content.</summary>
    internal static DaggerfallTerrainLocationFlattening Flattening(DaggerfallSiteExterior location) =>
        new(location.MinX, location.MaxX, MirrorRow(location, location.MaxY), MirrorRow(location, location.MinY));
}
