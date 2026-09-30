using System.Collections.ObjectModel;
using System.Text.Json;

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
    int NameSeed);

/// <summary>Immutable block-derived inputs consumed by building, map, talk, and quest-place owners.</summary>
internal sealed class DaggerfallBlocksSnapshot(
    IReadOnlyDictionary<DaggerfallRmbBuildingId, DaggerfallRmbBuildingSource> rmbBuildings)
{
    internal IReadOnlyDictionary<DaggerfallRmbBuildingId, DaggerfallRmbBuildingSource> RmbBuildings { get; } =
        new ReadOnlyDictionary<DaggerfallRmbBuildingId, DaggerfallRmbBuildingSource>(rmbBuildings.ToDictionary());
}

/// <summary>
/// Reads the published building fields once: the one slice of the importer's block document runtime
/// consumers use. The complete document, with every placement, stays an importer record.
/// </summary>
internal static class DaggerfallBlocksContent
{
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
                    DaggerfallBaseContent.Integer(building, "nameSeed", diagnostics));
                if (!buildings.TryAdd(id, source)) diagnostics.Add($"Block payload carries RMB building '{id}' twice.");
            }

            if (buildings.Count == 0) diagnostics.Add("Block payload carries no readable RMB building slot.");
            diagnostics.ThrowIfAny();
            return new DaggerfallBlocksSnapshot(buildings);
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
