using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Where a replacement image is drawn in the paper doll, in doll units (the classic 110x184 grid). It is the
/// generated image's own rect, independent of its pixel size, and may extend past the doll's frame.
/// </summary>
internal sealed record DaggerfallDollRect(double X, double Y, double Width, double Height);

/// <summary>One refreshed image that stands in for a classic media identity.</summary>
/// <param name="Replaces">The classic media identity it stands in for.</param>
/// <param name="MediaId">Its own media identity.</param>
/// <param name="Path">Its content-root path.</param>
/// <param name="ByteLength">Its length, as promotion recorded it.</param>
/// <param name="Doll">Where the paper doll draws it, for a paper-doll layer.</param>
internal sealed record DaggerfallArtReplacement(string Replaces, string MediaId, string Path, long ByteLength, DaggerfallDollRect? Doll);

/// <summary>
/// The refreshed art set: generated images promoted from <c>authoring/</c> (<c>promote.py</c>), each standing in for
/// one classic media identity. A presentation that draws a classic identity draws its replacement instead when the
/// player has not chosen classic art, so every category substitutes through this one map and classic art stays
/// the fallback.
/// </summary>
internal sealed class DaggerfallArtReplacements
{
    private readonly Dictionary<string, DaggerfallArtReplacement> _byClassic;

    private DaggerfallArtReplacements(Dictionary<string, DaggerfallArtReplacement> byClassic) => _byClassic = byClassic;

    /// <summary>Every replacement.</summary>
    internal IEnumerable<DaggerfallArtReplacement> All => _byClassic.Values;

    /// <summary>The replacement for a classic media identity, if the set has one.</summary>
    internal DaggerfallArtReplacement? For(string classicMediaId) =>
        _byClassic.TryGetValue(classicMediaId, out DaggerfallArtReplacement? replacement) ? replacement : null;

    /// <summary>Reads the <c>artReplacements</c> section of the pack payload.</summary>
    /// <exception cref="InvalidOperationException">The section is unreadable, or names one identity twice.</exception>
    internal static DaggerfallArtReplacements Read(ReadOnlyMemory<byte> payload, string packId)
    {
        Dictionary<string, DaggerfallArtReplacement> byClassic = new(StringComparer.Ordinal);
        HashSet<string> ids = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            foreach (JsonElement image in document.RootElement.GetProperty("artReplacements").GetProperty("images").EnumerateArray())
            {
                string replaces = image.GetProperty("replaces").GetString()!;
                string mediaId = image.GetProperty("mediaId").GetString()!;
                string path = image.GetProperty("path").GetString()!;
                long byteLength = image.GetProperty("byteLength").GetInt64();
                if (string.IsNullOrWhiteSpace(replaces) || string.IsNullOrWhiteSpace(mediaId) || string.IsNullOrWhiteSpace(path) || byteLength <= 0)
                    throw new InvalidOperationException($"Content pack '{packId}' states a replacement without a usable identity, path or length.");
                DaggerfallDollRect? doll = image.TryGetProperty("doll", out JsonElement rect) && rect.ValueKind == JsonValueKind.Object
                    ? new(rect.GetProperty("x").GetDouble(), rect.GetProperty("y").GetDouble(), rect.GetProperty("width").GetDouble(), rect.GetProperty("height").GetDouble())
                    : null;
                if (doll is { Width: <= 0 } or { Height: <= 0 })
                    throw new InvalidOperationException($"Content pack '{packId}' places replacement '{mediaId}' in an empty doll rect.");
                if (!ids.Add(mediaId) || !byClassic.TryAdd(replaces, new(replaces, mediaId, path, byteLength, doll)))
                    throw new InvalidOperationException($"Content pack '{packId}' names replacement '{mediaId}' for '{replaces}' twice.");
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException($"Content pack '{packId}' carries an unreadable artReplacements section: {exception.Message}", exception);
        }

        return new DaggerfallArtReplacements(byClassic);
    }
}
