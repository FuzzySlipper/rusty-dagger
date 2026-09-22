using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Admits the public classic-media inventory against the selected normalized site's descriptors.
/// The sidecar owns source interpretation; this join gives the ruleset one source-free public
/// path for each descriptor without reopening generated image or audio bodies at session start.
/// </summary>
internal sealed class DaggerfallPublishedClassicMedia
{
    internal const string InventoryPath = "worldrpg/media/classic-media-inventory.json";

    private DaggerfallPublishedClassicMedia(IReadOnlyDictionary<string, string> paths) => Paths = paths;

    /// <summary>Public content path for each admitted classic media identity.</summary>
    internal IReadOnlyDictionary<string, string> Paths { get; }

    internal static DaggerfallPublishedClassicMedia Read(ProductContent content, NormalizedClassicPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(presentation);
        if (presentation.Resources.Count == 0)
        {
            throw new InvalidOperationException("The selected classic-media sidecar contains no descriptors.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(content.ReadBytes(InventoryPath).ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("generator", out JsonElement generator) || generator.GetString() != "daggerfall-import-tool classic-media"
                || !root.TryGetProperty("artifacts", out JsonElement artifacts) || artifacts.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"Published classic media inventory '{InventoryPath}' is not the generated shape.");
            }

            Dictionary<string, string> paths = new(StringComparer.Ordinal);
            foreach (JsonElement artifact in artifacts.EnumerateArray())
            {
                if (!artifact.TryGetProperty("mediaId", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.String) continue;
                string id = idElement.GetString()!;
                string path = RequiredString(artifact, "path", id);
                long byteLength = RequiredInt64(artifact, "byteLength", id);
                string sha256 = RequiredString(artifact, "sha256", id);
                if (!presentation.Resources.TryGetValue(id, out NormalizedClassicMediaResource? resource))
                {
                    throw new InvalidOperationException($"Published classic media inventory names unknown descriptor '{id}'.");
                }

                string expectedPath = $"worldrpg/{resource.RelativePath}";
                if (!StringComparer.Ordinal.Equals(path, expectedPath)
                    || byteLength != resource.ByteLength
                    || resource.Sha256 != ParseSha256(sha256, id))
                {
                    throw new InvalidOperationException($"Published classic media '{id}' does not agree with its normalized descriptor.");
                }

                if (!paths.TryAdd(id, path))
                {
                    throw new InvalidOperationException($"Published classic media inventory names descriptor '{id}' more than once.");
                }
            }

            if (paths.Count != presentation.Resources.Count
                || presentation.Resources.Keys.Except(paths.Keys, StringComparer.Ordinal).Any())
            {
                throw new InvalidOperationException("Published classic media inventory does not close over the selected normalized descriptor set.");
            }

            return new DaggerfallPublishedClassicMedia(new ReadOnlyDictionary<string, string>(paths));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Published classic media inventory '{InventoryPath}' is not valid JSON: {exception.Message}", exception);
        }
    }

    private static string RequiredString(JsonElement value, string property, string id) =>
        value.TryGetProperty(property, out JsonElement candidate) && candidate.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(candidate.GetString())
            ? candidate.GetString()!
            : throw new InvalidOperationException($"Published classic media '{id}' has no usable '{property}'.");

    private static long RequiredInt64(JsonElement value, string property, string id) =>
        value.TryGetProperty(property, out JsonElement candidate) && candidate.TryGetInt64(out long parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"Published classic media '{id}' has no usable '{property}'.");

    private static ContentSha256 ParseSha256(string value, string id)
    {
        try
        {
            byte[] bytes = Convert.FromHexString(value);
            if (bytes.Length != 32) throw new FormatException();
            return new ContentSha256(
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(0, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(16, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(24, 8)));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"Published classic media '{id}' has an invalid SHA-256.");
        }
    }
}
