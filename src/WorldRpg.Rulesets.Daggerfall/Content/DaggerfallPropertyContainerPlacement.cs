using System.Text.Json;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallPropertyContainerPlacement(string Id, WorldPoint Position, string[] ItemGroups, WorldPoint[] InteractionPoints)
{
    internal static IReadOnlyList<DaggerfallPropertyContainerPlacement> Read(ReadOnlyMemory<byte>? bytes)
    {
        if (bytes is null) return [];
        using JsonDocument document = JsonDocument.Parse(bytes.Value);
        if (!document.RootElement.GetProperty("world").TryGetProperty("propertyContainers", out var values)) return [];
        List<DaggerfallPropertyContainerPlacement> result = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            string id = value.GetProperty("id").GetString()!;
            var point = value.GetProperty("position");
            var position = new WorldPoint(point.GetProperty("x").GetSingle(), point.GetProperty("y").GetSingle(), point.GetProperty("z").GetSingle());
            string[] groups = value.GetProperty("itemGroups").EnumerateArray().Select(group => group.GetString()!).ToArray();
            var interactionPoints = value.GetProperty("interactionPoints").EnumerateArray().Select(point => new WorldPoint(
                point.GetProperty("x").GetSingle(), point.GetProperty("y").GetSingle(), point.GetProperty("z").GetSingle())).ToArray();
            if (interactionPoints.Any(surface => !float.IsFinite(surface.X) || !float.IsFinite(surface.Y) || !float.IsFinite(surface.Z)))
                throw new ArgumentException("Property interaction points must be finite.");
            if (interactionPoints.Length == 0 || string.IsNullOrWhiteSpace(id) || !ids.Add(id) || groups.Any(string.IsNullOrWhiteSpace)
                || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                throw new ArgumentException("Published property containers require distinct identities, named groups, and finite positions.");
            result.Add(new(id, position, groups, interactionPoints));
        }
        return result;
    }
}
