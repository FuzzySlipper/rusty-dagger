using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record DaggerfallPlayerUiAction(string Action, string? Revision = null, string? Item = null, int? TargetGrid = null, string? TargetEquipment = null, string? Container = null);

/// <summary>The small Daggerfall player-action wire contract, consumed during admitted updates.</summary>
internal static class DaggerfallUiAction
{
    internal static DaggerfallPlayerUiAction? Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > 1024) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            HashSet<string> fields = new(StringComparer.Ordinal);
            string? action = null, revision = null, item = null, targetEquipment = null, container = null;
            int? targetGrid = null;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!fields.Add(property.Name)) return null;
                if (property.Name == "targetGrid")
                {
                    if (!property.Value.TryGetInt32(out int grid)) return null;
                    targetGrid = grid;
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.String) return null;
                string? value = property.Value.GetString();
                switch (property.Name)
                {
                    case "action": action = value; break;
                    case "revision": revision = value; break;
                    case "item": item = value; break;
                    case "targetEquipment": targetEquipment = value; break;
                    case "container": container = value; break;
                    default: return null;
                }
            }
            if (action == "inventory-move")
            {
                if (string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(item)
                    || (targetGrid is null) == (targetEquipment is null) || fields.Count != 4) return null;
                return new(action, revision, item, targetGrid, targetEquipment);
            }
            if (action == "loot-take")
                return fields.SetEquals(["action", "revision", "item", "container"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(container)
                    ? new(action, revision, item, Container: container) : null;
            if (action == "loot-close")
                return fields.SetEquals(["action", "container"]) && !string.IsNullOrWhiteSpace(container)
                    ? new(action, Container: container) : null;
            return fields.Count == 1 && action is "attack" or "inventory" or "character" or "loot" ? new(action) : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }
}
