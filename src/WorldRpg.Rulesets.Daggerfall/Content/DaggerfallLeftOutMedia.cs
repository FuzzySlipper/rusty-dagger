using System.Text.Json;
using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The classic media a player's data container left out because the art pack draws it anew, as the container's
/// description records it by kind, and the check that the art pack a game composes with still draws every one of them:
/// an entry withdrawn from the pack after the import would otherwise draw nothing.
/// </summary>
internal static class DaggerfallLeftOutMedia
{
    /// <summary>
    /// Refuses a composition whose art pack no longer draws anew a classic identity the data container left out, naming
    /// them, unless the classic-art container is open and holds their bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">An identity the container left out that nothing would draw.</exception>
    internal static void Check(DaggerfallData data, DaggerfallArtReplacements? art)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.ClassicArtMissing is null || !data.TryReadFile(InstalledContent.DescriptionPath, out ReadOnlyMemory<byte> description)) return;
        using JsonDocument document = JsonDocument.Parse(description);
        if (!document.RootElement.TryGetProperty("leftOut", out JsonElement leftOut)) return;
        static bool Has<T>(IReadOnlyDictionary<(int Archive, int Record), T>? set, string key) =>
            key.Split('-') is [var archive, var record] && set?.ContainsKey((int.Parse(archive), int.Parse(record))) == true;
        List<string> drawnNowhere = [];
        foreach (JsonProperty kind in leftOut.EnumerateObject().Where(kind => kind.Value.ValueKind == JsonValueKind.Array))
            foreach (string key in kind.Value.EnumerateArray().Select(value => value.GetString()!))
            {
                bool drawn = kind.Name switch
                {
                    "media" => art?.For(key) is not null || art?.Effects.ContainsKey(key) == true,
                    "surfaces" => Has(art?.Surfaces, key),
                    "flats" => Has(art?.Scenery, key), // the import names the scenery section's classic records flats
                    "people" => Has(art?.People, key),
                    "meshes" => Has(art?.Meshes, key),
                    "models" => art?.Models.ContainsKey(key) == true,
                    _ => throw new InvalidOperationException($"{DaggerfallData.DataContainerName} says it left out classic {kind.Name}, which this build does not know."),
                };
                if (!drawn) drawnNowhere.Add($"{kind.Name} {key}");
            }
        if (drawnNowhere.Count != 0)
            throw new InvalidOperationException($"{DaggerfallData.DataContainerName} left out classic media the art pack no longer draws anew, so nothing would draw "
                + $"{drawnNowhere.Count}: {string.Join(", ", drawnNowhere.Take(20))}{(drawnNowhere.Count > 20 ? ", …" : "")}. Import the Daggerfall data again.");
    }
}
