using WorldRpg.Kit.World;
using System.Numerics;
using System.Text.Json;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal static class DaggerfallDungeonWater
{
    internal static IReadOnlyList<CharacterWaterVolume> Read(ReadOnlyMemory<byte>? bytes, string profile,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null) { diagnostics.Add("Normalized world closure is absent for dungeon water admission."); return []; }
        try
        {
            using var document = JsonDocument.Parse(bytes.Value);
            if (!document.RootElement.GetProperty("world").TryGetProperty("waterVolumes", out var volumes)) return [];
            Dictionary<ulong, CharacterWaterVolume> admitted = [];
            foreach (JsonElement volume in volumes.EnumerateArray())
            {
                string id = volume.GetProperty("id").GetString()!;
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Water volume has no identity.");
                JsonElement bounds = volume.GetProperty("bounds");
                ulong trigger = StableHash.NonZeroFnv1a64($"daggerfall.dungeon-water:{profile}:{id}");
                var value = new CharacterWaterVolume(trigger, Vector(bounds.GetProperty("minimum")), Vector(bounds.GetProperty("maximum"))).Validate();
                if (!admitted.TryAdd(trigger, value)) throw new ArgumentException($"Water volume identity '{id}' collides with another volume.");
            }
            return admitted.Values.OrderBy(volume => volume.Trigger).ToArray();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { diagnostics.Add($"Normalized dungeon water is malformed: {error.Message}"); return []; }
    }

    private static Vector3 Vector(JsonElement e) => new(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());
}
