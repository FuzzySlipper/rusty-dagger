using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Daggerfall.Import.Publication;

/// <summary>A supplied family a media group reads but publishes no artifact from, and why.</summary>
public sealed record UnreadableMediaFamily(string Family, string Kind, IReadOnlyList<string> Files, string Reason, string DonorAnchor);

/// <summary>
/// The generated index a product-wide media group publishes beside its artifacts: the classic, character
/// and music groups each write one, in this one shape, so one reader resolves any group.
/// </summary>
/// <remarks>
/// Every entry names an artifact by its content-relative path with its byte length and digest, so a
/// consumer can tell whether the content it admitted is the content the publication produced. An index
/// is generated rather than maintained by hand: every earlier hand-kept index in this repository drifted
/// from what it indexed, and the point of writing this one is that a rebuild rewrites it.
/// </remarks>
public static class ArtifactInventory
{
    /// <summary>One artifact's entry: its content-relative path, byte length, digest and media identity.</summary>
    public static JsonObject Entry(string contentPath, ReadOnlySpan<byte> bytes, string? mediaId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);
        JsonObject entry = new()
        {
            ["path"] = contentPath,
            ["byteLength"] = bytes.Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        };
        if (mediaId is not null) entry["mediaId"] = mediaId;
        return entry;
    }

    /// <summary>Writes the index, its artifacts ordered by path.</summary>
    public static byte[] Write(string generator, IEnumerable<JsonObject> artifacts, IReadOnlyList<UnreadableMediaFamily>? unreadable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generator);
        ArgumentNullException.ThrowIfNull(artifacts);
        JsonObject document = new()
        {
            ["generator"] = generator,
            ["unreadableFamilies"] = JsonSerializer.SerializeToNode(
                (unreadable ?? []).ToArray(), PublishedJson.Section),
            ["artifacts"] = new JsonArray([.. artifacts.OrderBy(entry => entry["path"]!.GetValue<string>(), StringComparer.Ordinal)]),
        };
        return Encoding.UTF8.GetBytes(document.ToJsonString(PublishedJson.Section) + "\n");
    }
}
