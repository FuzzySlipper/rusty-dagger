using System.Text.Json;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal static class DaggerfallQuestMarkerContent
{
    internal static IReadOnlyList<DaggerfallSiteMarker> ReadWorld(ReadOnlyMemory<byte>? bytes, DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null) return [];
        using JsonDocument document = JsonDocument.Parse(bytes.Value);
        JsonElement world = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(document.RootElement, "world", diagnostics), "normalized world", diagnostics);
        return Read(world, "questMarkers", diagnostics);
    }

    internal static IReadOnlyList<DaggerfallSiteMarker> Read(JsonElement owner, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (!owner.TryGetProperty(property, out _)) return [];
        List<DaggerfallSiteMarker> result = [];
        HashSet<string> ids = [];
        foreach (JsonElement value in DaggerfallBaseContent.Array(owner, property, diagnostics))
        {
            string id = DaggerfallBaseContent.Text(value, "id", diagnostics);
            DaggerfallSiteMarkerKind kind = DaggerfallBaseContent.Text(value, "kind", diagnostics) switch
            {
                "spawn" => DaggerfallSiteMarkerKind.QuestSpawn,
                "item" => DaggerfallSiteMarkerKind.QuestItem,
                _ => (DaggerfallSiteMarkerKind)(-1),
            };
            JsonElement point = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(value, "position", diagnostics), "quest marker position", diagnostics);
            WorldPoint position = new(DaggerfallBaseContent.Number(point, "x", diagnostics), DaggerfallBaseContent.Number(point, "y", diagnostics), DaggerfallBaseContent.Number(point, "z", diagnostics));
            DaggerfallSiteMarker marker = new(id, kind, position)
            {
                SourceOrdinal = DaggerfallBaseContent.Integer(value, "sourceOrdinal", diagnostics),
                BlockX = DaggerfallBaseContent.Integer(value, "blockX", diagnostics),
                BlockZ = DaggerfallBaseContent.Integer(value, "blockZ", diagnostics),
            };
            try
            {
                marker.Validate();
                if (!ids.Add(id)) diagnostics.Add($"Quest marker '{id}' appears more than once.");
                result.Add(marker);
            }
            catch (ArgumentException error) { diagnostics.Add($"Quest marker '{id}' is malformed: {error.Message}"); }
        }
        return result.AsReadOnly();
    }
}
