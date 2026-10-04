using System.Numerics;
using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal enum DaggerfallAmbientZoneKind { Castle, SpecialArea }
internal sealed record DaggerfallAmbientZone(string Id, DaggerfallAmbientZoneKind Kind, Vector3 Minimum, Vector3 Maximum)
{
    internal bool Contains(Vector3 position) => position.X >= Minimum.X && position.X < Maximum.X
        && position.Y >= Minimum.Y && position.Y <= Maximum.Y && position.Z >= Minimum.Z && position.Z < Maximum.Z;
    internal DaggerfallAmbientZone Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || !Enum.IsDefined(Kind)
            || !Finite(Minimum) || !Finite(Maximum) || Minimum.X >= Maximum.X || Minimum.Y > Maximum.Y || Minimum.Z >= Maximum.Z)
            throw new ArgumentException("Ambient zones require a named kind and finite ordered source bounds.");
        return this;
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

internal static class DaggerfallAmbientZones
{
    internal static IReadOnlyList<DaggerfallAmbientZone> Read(ReadOnlyMemory<byte>? bytes, DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null) { diagnostics.Add("Normalized world closure is absent for ambient zone admission."); return []; }
        try
        {
            using var document = JsonDocument.Parse(bytes.Value);
            JsonElement world = document.RootElement.GetProperty("world");
            // The section is optional for worlds that author no ambient variants.
            if (!world.TryGetProperty("ambientZones", out var zones)) return [];
            Dictionary<string, DaggerfallAmbientZone> admitted = new(StringComparer.Ordinal);
            foreach (JsonElement zone in zones.EnumerateArray())
            {
                var kind = zone.GetProperty("kind").GetString() switch
                {
                    "castle" => DaggerfallAmbientZoneKind.Castle,
                    "specialArea" => DaggerfallAmbientZoneKind.SpecialArea,
                    var unknown => throw new ArgumentException($"Unknown ambient zone kind '{unknown}'."),
                };
                JsonElement bounds = zone.GetProperty("bounds");
                var value = new DaggerfallAmbientZone(zone.GetProperty("id").GetString()!, kind,
                    Vector(bounds.GetProperty("minimum")), Vector(bounds.GetProperty("maximum"))).Validate();
                if (!admitted.TryAdd(value.Id, value)) throw new ArgumentException($"Ambient zone '{value.Id}' is repeated.");
            }
            return admitted.Values.OrderBy(zone => zone.Id, StringComparer.Ordinal).ToArray();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { diagnostics.Add($"Normalized ambient zones are malformed: {error.Message}"); return []; }
    }
    private static Vector3 Vector(JsonElement e) => new(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());
}
