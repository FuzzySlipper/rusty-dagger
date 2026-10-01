using System.Collections.ObjectModel;
using System.Text.Json;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>A stable identity for one actual RMB building slot in the normalized block archive.</summary>
internal readonly record struct DaggerfallRmbBuildingId(string SourceKey, int Index)
{
    public override string ToString() => $"{SourceKey}:{Index}";
}

/// <summary>The real building fields the donor's naming formula reads.</summary>
internal sealed record DaggerfallRmbBuildingSource(
    DaggerfallRmbBuildingId Id,
    int BuildingType,
    int FactionId,
    int NameSeed)
{
    internal WorldPoint MapPosition { get; init; }
}

internal sealed record DaggerfallCityFootprint(float MinX, float MinZ, float MaxX, float MaxZ, int Kind);
internal sealed record DaggerfallCityBlockMap(string Block, float BlockSize, IReadOnlyList<DaggerfallCityFootprint> Footprints);

/// <summary>Immutable block-derived inputs consumed by building, map, talk, and quest-place owners.</summary>
internal sealed class DaggerfallBlocksSnapshot(
    IReadOnlyDictionary<DaggerfallRmbBuildingId, DaggerfallRmbBuildingSource> rmbBuildings,
    IReadOnlyDictionary<string, DaggerfallCityBlockMap>? maps = null)
{
    internal IReadOnlyDictionary<DaggerfallRmbBuildingId, DaggerfallRmbBuildingSource> RmbBuildings { get; } =
        new ReadOnlyDictionary<DaggerfallRmbBuildingId, DaggerfallRmbBuildingSource>(rmbBuildings.ToDictionary());

    internal IReadOnlyDictionary<string, DaggerfallCityBlockMap> Maps { get; } =
        new ReadOnlyDictionary<string, DaggerfallCityBlockMap>((maps ?? new Dictionary<string, DaggerfallCityBlockMap>()).ToDictionary());

    /// <summary>Joins location placements to their source catalog once at selected-content admission.</summary>
    internal void AdmitLocations(DaggerfallLocationSet locations)
    {
        foreach (DaggerfallSiteRecord site in locations.Records)
        {
            if (Maps.Count > 0)
                foreach (DaggerfallSiteBlock block in site.Exterior?.Blocks ?? [])
                    if (!Maps.ContainsKey(block.SourceName)) throw new InvalidOperationException($"Site '{site.Id}' names unpublished RMB map '{block.SourceName}'.");
            foreach (DaggerfallSiteBuildingSource building in site.Exterior?.Buildings.Values ?? [])
                if (!RmbBuildings.ContainsKey(building.Source.Id))
                    throw new InvalidOperationException($"Site '{site.Id}' building '{building.Id}' names unpublished RMB source '{building.Source.Id}'.");
        }
    }
}

/// <summary>
/// Reads published building fields and compact map footprints once from the runtime pack. The complete document, with every placement, stays an importer record.
/// </summary>
internal static class DaggerfallBlocksContent
{
    private static WorldPoint ReadPoint(JsonElement building, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement point = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(building, "mapPosition", diagnostics), "building map position", diagnostics);
        return new(DaggerfallBaseContent.Number(point, "x", diagnostics), DaggerfallBaseContent.Number(point, "y", diagnostics),
            DaggerfallBaseContent.Number(point, "z", diagnostics));
    }

    internal static DaggerfallBlocksSnapshot Read(ReadOnlyMemory<byte> payload)
    {
        DaggerfallContentDiagnostics diagnostics = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "blocks root", diagnostics);
            Dictionary<DaggerfallRmbBuildingId, DaggerfallRmbBuildingSource> buildings = [];
            foreach (JsonElement building in DaggerfallBaseContent.Array(root, "buildings", diagnostics))
            {
                DaggerfallRmbBuildingId id = new(
                    DaggerfallBaseContent.Text(building, "block", diagnostics),
                    DaggerfallBaseContent.Integer(building, "index", diagnostics));
                DaggerfallRmbBuildingSource source = new(
                    id,
                    DaggerfallBaseContent.Integer(building, "buildingType", diagnostics),
                    DaggerfallBaseContent.Integer(building, "factionId", diagnostics),
                    DaggerfallBaseContent.Integer(building, "nameSeed", diagnostics))
                {
                    MapPosition = ReadPoint(building, diagnostics),
                };
                if (!buildings.TryAdd(id, source)) diagnostics.Add($"Block payload carries RMB building '{id}' twice.");
            }

            Dictionary<string, DaggerfallCityBlockMap> maps = [];
            foreach (JsonElement map in DaggerfallBaseContent.Array(root, "maps", diagnostics))
            {
                string block = DaggerfallBaseContent.Text(map, "block", diagnostics);
                float size = DaggerfallBaseContent.Number(map, "blockSize", diagnostics);
                List<DaggerfallCityFootprint> footprints = [];
                foreach (JsonElement rect in DaggerfallBaseContent.Array(map, "footprints", diagnostics))
                {
                    DaggerfallCityFootprint footprint = new(
                        DaggerfallBaseContent.Number(rect, "minX", diagnostics), DaggerfallBaseContent.Number(rect, "minZ", diagnostics),
                        DaggerfallBaseContent.Number(rect, "maxX", diagnostics), DaggerfallBaseContent.Number(rect, "maxZ", diagnostics),
                        DaggerfallBaseContent.Integer(rect, "kind", diagnostics));
                    if (footprint.MinX < 0 || footprint.MaxX > size || footprint.MinZ < -size || footprint.MaxZ > 0
                        || footprint.MinX >= footprint.MaxX || footprint.MinZ >= footprint.MaxZ || footprint.Kind is < 1 or > 255)
                        diagnostics.Add($"Block '{block}' carries an invalid automap footprint.");
                    footprints.Add(footprint);
                }
                if (size <= 0 || !maps.TryAdd(block, new(block, size, footprints.AsReadOnly())))
                    diagnostics.Add($"Block '{block}' carries an invalid or repeated map.");
            }
            foreach (DaggerfallRmbBuildingSource building in buildings.Values)
                if (!maps.ContainsKey(building.Id.SourceKey)) diagnostics.Add($"Building '{building.Id}' has no published automap.");
            if (buildings.Count == 0) diagnostics.Add("Block payload carries no readable RMB building slot.");
            diagnostics.ThrowIfAny();
            return new DaggerfallBlocksSnapshot(buildings, maps);
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Block payload is not valid JSON: {exception.Message}");
            throw diagnostics.Exception();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or OverflowException && exception is not DaggerfallContentException)
        {
            diagnostics.Add($"Block payload is malformed: {exception.Message}");
            throw diagnostics.Exception();
        }
    }
}
