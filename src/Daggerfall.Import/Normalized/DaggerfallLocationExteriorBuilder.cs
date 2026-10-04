using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// Combines the identity-keyed MAPS MAPPITEM layout with the RMB FLD ground headers into the
/// location metadata runtime terrain admission needs. This is deliberately offline: runtime code
/// consumes <see cref="DaggerfallLocationExterior"/> and never opens an Arena2 archive.
/// </summary>
internal sealed class DaggerfallLocationExteriorBuilder
{
    private const int RmbTilesPerTerrain = 128;
    private const int RmbTilesPerBlock = 16;
    private const int GroundTextureCount = 56;
    private const int TownCityLocationType = 0;

    private readonly BsaArchive blocks;
    private readonly BsaArchive? models;
    private readonly Dictionary<string, RmbBlockPlacements> placements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> radii = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RmbBlockSummary> summaries = new(StringComparer.Ordinal);

    public DaggerfallLocationExteriorBuilder(BsaArchive blocks, BsaArchive? models = null)
    {
        this.blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        this.models = models;
    }

    public DaggerfallLocationExterior Build(BsaArchive maps, DaggerfallLocationMap location)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(location);

        // The index is the source identity. Name lookup is insufficient because MAPNAMES repeats
        // shrine, lodge and monument names within a region.
        MapsExteriorLayout layout = MapsDecoder.DecodeExteriorLayout(maps, location.Region, location.Index);
        if (layout.Region != location.Region || layout.LocationIndex != location.Index)
        {
            throw new InvalidOperationException($"MAPS exterior resolver returned region {layout.Region}, index {layout.LocationIndex} for location {location.Region}:{location.Index}.");
        }

        (int mapPixelX, int mapPixelY) = MapsDecoder.ToMapPixel(location.Longitude, location.Latitude);
        if (layout.MapId != location.MapId || layout.Longitude != location.Longitude || layout.Latitude != location.Latitude)
        {
            throw new InvalidOperationException($"MAPS exterior record for location {location.Region}:{location.Index} disagrees with its MAPTABLE identity.");
        }

        bool customPosition = layout.Width == 1
            && layout.Height == 1
            && layout.Blocks.Count == 1
            && layout.Blocks[0].SourceName.StartsWith("CUST", StringComparison.Ordinal);
        int tileOriginX = customPosition
            ? 72
            : (RmbTilesPerTerrain - (layout.Width * RmbTilesPerBlock)) / 2;
        int tileOriginY = customPosition
            ? 55
            : (RmbTilesPerTerrain - (layout.Height * RmbTilesPerBlock)) / 2;

        List<MapsExteriorBuilding> pool = [.. layout.Buildings.Where(building => IsNamedBuilding(building.BuildingType))];
        List<DaggerfallLocationBuilding> buildings = [];
        byte[] groundTiles = new byte[128 * 128];
        List<string> missingCityBuildings = [];
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        foreach (MapsExteriorBlock block in layout.Blocks)
        {
            RmbBlockSummary summary = ReadSummary(block.SourceName, location);
            if (summary.GroundTiles.Count != RmbTilesPerBlock * RmbTilesPerBlock)
            {
                throw new InvalidOperationException($"RMB block '{block.SourceName}' for location {location.Region}:{location.Index} carries {summary.GroundTiles.Count} FLD ground tiles instead of {RmbTilesPerBlock * RmbTilesPerBlock}.");
            }

            foreach (RmbBuildingSlot building in summary.Buildings)
            {
                int type = building.BuildingType;
                int faction = building.FactionId;
                int seed = building.NameSeed;
                int quality = building.Quality;
                if (IsNamedBuilding(type))
                {
                    int match = pool.FindIndex(candidate => candidate.BuildingType == type);
                    // The donor's exhausted pool returns a zero-initialized record, then reports it.
                    // Keep those observable values rather than substituting the generic block data.
                    MapsExteriorBuilding city = match >= 0 ? pool[match] : new(0, 0, type, 0);
                    if (match >= 0) pool.RemoveAt(match);
                    else missingCityBuildings.Add($"{block.X}/{block.Y}/{building.Index}: no remaining city building of type {type}; name seed, faction and quality are zero.");
                    faction = city.FactionId;
                    seed = city.NameSeed;
                    quality = city.Quality;
                    if (summary.OtherNameSlots[building.Index] == "KRAVE01.HS2")
                    {
                        type = 11;
                        faction = 414;
                    }
                }
                string? modelId = placements[block.SourceName].Buildings[building.Index].Exterior.Models.FirstOrDefault()?.ModelId;
                float? radius = modelId is null || models is null ? null : ReadRadius(modelId);
                buildings.Add(new(block.SourceName, block.X, block.Y, building.Index, type, faction, seed, quality)
                { SourceLocationId = building.LocationId, ModelId = modelId, ModelRadius = radius });
            }

            foreach (RmbGroundTile tile in summary.GroundTiles)
            {
                // The source array is addressed as [tileX, 15-tileY] by TerrainHelper. Iterating its
                // stored coordinates and reversing the y coordinate produces the same terrain frame.
                // Donor TerrainHelper leaves blend-space records (>55) at zero. Zero is the
                // compact representation for generated terrain and must not be normalized to a
                // visible ground archive, which would erase the donor's marching-squares result.
                if (tile.TextureRecord >= GroundTextureCount) continue;

                int x = checked(tileOriginX + (block.X * RmbTilesPerBlock) + tile.X);
                int y = checked(tileOriginY + (block.Y * RmbTilesPerBlock) + (15 - tile.Y));
                groundTiles[(y * 128) + x] =
                    (byte)(tile.TextureRecord | (tile.Rotated ? 0x40 : 0) | (tile.Flipped ? 0x80 : 0));
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        if (minX == int.MaxValue)
        {
            throw new InvalidOperationException($"Exterior location {location.Region}:{location.Index} has no FLD ground tile with texture record below {GroundTextureCount}; its flattening footprint cannot be derived.");
        }

        int clearance = location.LocationType == TownCityLocationType ? 3 : 2;
        DaggerfallLocationExterior result = new(
            layout.LocationId,
            mapPixelX,
            mapPixelY,
            layout.Width,
            layout.Height,
            layout.Letter1,
            tileOriginX,
            tileOriginY,
            customPosition,
            clearance,
            new DaggerfallLocationTerrainRect(
                checked(minX - clearance),
                checked(maxX + clearance),
                checked(minY - clearance),
                checked(maxY + clearance)),
            [.. layout.Blocks.Select(block => new DaggerfallLocationExteriorBlock(block.SourceName, block.X, block.Y))])
        {
            Buildings = buildings,
            GroundTiles = groundTiles,
            BuildingReferences = [.. layout.Buildings.Select(building => new DaggerfallLocationBuildingReference(building.LocationId, building.Sector) { BuildingType = building.BuildingType })],
            PortTownAndUnknown = layout.PortTownAndUnknown,
            MissingCityBuildings = missingCityBuildings,
        };
        result.Validate($"{location.Region}:{location.Index} '{location.Name}'");
        return result;
    }

    internal static bool IsNamedBuilding(int type) => type is 0 or 2 or 3 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16;

    private float ReadRadius(string modelId)
    {
        if (radii.TryGetValue(modelId, out float radius)) return radius;
        if (!uint.TryParse(modelId, out uint id) || !models!.TryGetByNumericId(id, out BsaRecord? record) || record is null)
            throw new InvalidOperationException($"ARCH3D.BSA has no building model '{modelId}'.");
        radius = Arch3dDecoder.ReadModelRadius(models.GetPayload(record).Span, models.Source);
        radii.Add(modelId, radius);
        return radius;
    }

    private RmbBlockSummary ReadSummary(string sourceName, DaggerfallLocationMap location)
    {
        if (summaries.TryGetValue(sourceName, out RmbBlockSummary? cached))
        {
            return cached;
        }

        if (!blocks.TryGetByName(sourceName, out BsaRecord? record) || record is null)
        {
            throw new InvalidOperationException($"BLOCKS.BSA is missing RMB block '{sourceName}' referenced by location {location.Region}:{location.Index} '{location.Name}'.");
        }

        ReadOnlyMemory<byte> payload = blocks.GetPayload(record);
        if (!RmbBlockSummaryReader.TryRead(payload.ToArray(), blocks.Source, 0, payload.Length, out RmbBlockSummary? summary, out string reason)
            || summary is null)
        {
            throw new InvalidOperationException($"RMB block '{sourceName}' referenced by location {location.Region}:{location.Index} cannot be read: {reason}.");
        }

        placements.Add(sourceName, RmbPlacementReader.Read(payload.ToArray(), 0, summary, blocks.Source));
        summaries.Add(sourceName, summary);
        return summary;
    }
}
