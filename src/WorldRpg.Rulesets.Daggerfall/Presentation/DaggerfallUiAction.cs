using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record DaggerfallPlayerUiAction(string Action, string? Revision = null, string? Item = null, int? TargetGrid = null, string? TargetEquipment = null, string? Container = null, string? Key = null, string? Label = null, bool Confirm = false);

/// <summary>The small Daggerfall player-action wire contract, consumed during admitted updates.</summary>
internal static class DaggerfallUiAction
{
    internal const string BeginAction = "begin";
    internal static DaggerfallPlayerUiAction? Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > 1024) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            HashSet<string> fields = new(StringComparer.Ordinal);
            string? action = null, revision = null, item = null, targetEquipment = null, container = null, key = null, label = null;
            int? targetGrid = null;
            bool confirm = false;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!fields.Add(property.Name)) return null;
                if (property.Name == "targetGrid")
                {
                    if (!property.Value.TryGetInt32(out int grid)) return null;
                    targetGrid = grid;
                    continue;
                }
                if (property.Name == "confirm")
                {
                    if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False) return null;
                    confirm = property.Value.GetBoolean();
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
                    case "key": key = value; break;
                    case "label": label = value; break;
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
            if (action == "art-request")
                return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision)
                    ? new(action, revision) : null;
            if (action == "loot-close")
                return fields.SetEquals(["action", "container"]) && !string.IsNullOrWhiteSpace(container)
                    ? new(action, Container: container) : null;
            if (action == "save-slots") return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "save-slot")
                return fields.IsSubsetOf(["action", "key", "label", "confirm"])
                    && !string.IsNullOrWhiteSpace(label)
                    && (key is null || !string.IsNullOrWhiteSpace(key))
                    ? new(action, Key: key, Label: label, Confirm: confirm) : null;
            if (action == "load-slot")
                return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Key: key) : null;
            if (action == "delete-slot")
                return fields.IsSubsetOf(["action", "key", "confirm"])
                    && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Key: key, Confirm: confirm) : null;
            // "begin" is the entry screen's own action, which the product answers: the session accepts the
            // shape so a slice carrying it is a known action it does not act on, rather than an
            // unrecognized one it reports over the screen that asked.
            return fields.Count == 1 && action is "attack" or "inventory" or "character" or "loot" or "begin" or "save-game" or "load-game"
                ? new(action) : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }
}
