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
/// Reads the normalized block sidecar once and projects only the source facts runtime consumers use.
/// The full offline document remains an import artifact; this reader admits no placements or policy
/// beyond RMB building fields.
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
            foreach (JsonElement record in DaggerfallBaseContent.Array(root, "records", diagnostics))
            {
                string kind = DaggerfallBaseContent.Text(record, "kind", diagnostics);
                string state = DaggerfallBaseContent.Text(record, "state", diagnostics);
                if (state != "read") continue;
                string sourceKey = DaggerfallBaseContent.Text(record, "sourceKey", diagnostics);
                if (kind == "rmb")
                {
                    ReadBuildings(record, sourceKey, buildings, diagnostics);
                }
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

    private static void ReadBuildings(
        JsonElement record,
        string sourceKey,
        Dictionary<DaggerfallRmbBuildingId, DaggerfallRmbBuildingSource> buildings,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (!record.TryGetProperty("rmb", out JsonElement rmb) || rmb.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add($"Readable RMB block '{sourceKey}' carries no RMB header.");
            return;
        }

        foreach (JsonElement building in DaggerfallBaseContent.Array(rmb, "buildings", diagnostics))
        {
            DaggerfallRmbBuildingId id = new(sourceKey, DaggerfallBaseContent.Integer(building, "index", diagnostics));
            DaggerfallRmbBuildingSource source = new(
                id,
                DaggerfallBaseContent.Integer(building, "buildingType", diagnostics),
                DaggerfallBaseContent.Integer(building, "factionId", diagnostics),
                DaggerfallBaseContent.Integer(building, "nameSeed", diagnostics));
            if (!buildings.TryAdd(id, source)) diagnostics.Add($"Block payload carries RMB building '{id}' twice.");
        }
    }

}
