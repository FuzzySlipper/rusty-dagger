using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The published UI art the DOM shows, resolved by media identity through the admitted content
/// group's generated inventory.
/// </summary>
/// <remarks>
/// The bytes are read from admitted content by name; the identity is the pack's media id; placement is
/// the publication's (<c>worldrpg/media/...</c>) and is stated by the inventory the publication
/// generated rather than by a hand-kept list here. Nothing about this path stages a copy beside the
/// UI, so a republished artifact reaches the DOM by being republished.
/// </remarks>
internal sealed class DaggerfallUiArt
{
    /// <summary>The published group inventory that names every artifact of the UI content group.</summary>
    internal const string InventoryPath = "worldrpg/media/classic-media-inventory.json";

    /// <summary>
    /// The images every session shows, by the pack's own media identity: the mode screen the product
    /// enters and the window chrome the DOM draws. An artifact belongs here when the DOM draws it, so
    /// the group's remaining art - the classic inventory chrome, the HUD chrome, and the numbered
    /// screens and paper-doll layers later tasks add - is indexed by the inventory and read on demand
    /// by whoever draws it, rather than pushed through every session's snapshot.
    /// </summary>
    private static readonly string[] AlwaysShown =
    [
        "screen.death",
        // The supplied screens a mode is shown with. Each carries its own palette in the file, so the
        // published bytes are the ones the classic reader would paint.
        "screen.character-generation",
        "screen.pick.02",
        "screen.prison",
        "screen.start-menu",
        "screen.title",
        "window.character-sheet.chrome",
        // The authored inventory skins are drawn by the DOM's inventory and loot panels. They are
        // published at a bounded size rather than copied beside the UI, so the panel frame arrives
        // through the same named read as every other artifact.
        "inventory.skin.grid-slot-slate.v1",
        "inventory.skin.panel-slate.v1",
        "inventory.skin.titlebar-slate.v1",
    ];

    /// <summary>
    /// The largest slice one admitted read serves - the Engine refuses a larger request - so an
    /// artifact bigger than this is read in chunks and joined here.
    /// </summary>
    private const uint MaximumReadBytes = 1024 * 1024;

    private DaggerfallUiArt(IReadOnlyList<(string Id, string Image)> images)
    {
        // A new admitted image block invalidates retained DOM art, including across runtime restarts.
        // This token identifies the publication; it does not hash or verify the image bytes.
        Revision = Guid.NewGuid().ToString("N");
        Images = images;
    }

    /// <summary>Publication-stable UI-art identity used by a reconnecting DOM to request the current image block.</summary>
    internal string Revision { get; }

    /// <summary>Every resolved image as a data URL, ordered by media identity.</summary>
    internal IReadOnlyList<(string Id, string Image)> Images { get; }

    /// <summary>
    /// Reads the published UI art this session shows. <paramref name="itemIcons"/> are the inventory
    /// icon identities the content pack names for its items; the rest is what this presentation draws.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A needed identity is absent from the inventory, or admitted content does not carry the bytes the
    /// inventory describes; the message names the artifact so the producer/consumer disagreement is
    /// visible rather than a blank image at runtime.
    /// </exception>
    internal static DaggerfallUiArt Read(IContentService content, IEnumerable<string> itemIcons)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(itemIcons);
        Dictionary<string, InventoryEntry> inventory = ReadInventory(content);
        List<(string Id, string Image)> images = [];
        foreach (string id in AlwaysShown.Concat(itemIcons).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!inventory.TryGetValue(id, out InventoryEntry entry))
            {
                throw new InvalidOperationException($"Published UI art '{id}' is not described by the admitted content inventory '{InventoryPath}'.");
            }

            byte[] bytes = ReadArtifact(content, id, entry);
            images.Add((id, $"data:image/png;base64,{Convert.ToBase64String(bytes)}"));
        }

        return new DaggerfallUiArt(images);
    }

    /// <summary>Reads the generated inventory by name and indexes it by the media identity it states.</summary>
    private static Dictionary<string, InventoryEntry> ReadInventory(IContentService content)
    {
        byte[] bytes = ReadAdmittedFile(content, InventoryPath);
        Dictionary<string, InventoryEntry> entries = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("artifacts", out JsonElement artifacts) || artifacts.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"The admitted content inventory '{InventoryPath}' is not the published shape.");
            }

            foreach (JsonElement artifact in artifacts.EnumerateArray())
            {
                if (!artifact.TryGetProperty("mediaId", out JsonElement mediaIdElement) || mediaIdElement.ValueKind != JsonValueKind.String) continue;
                string mediaId = mediaIdElement.GetString()!;
                string path = artifact.GetProperty("path").GetString()!;
                long byteLength = artifact.GetProperty("byteLength").GetInt64();
                if (string.IsNullOrWhiteSpace(mediaId) || string.IsNullOrWhiteSpace(path) || byteLength <= 0)
                {
                    throw new InvalidOperationException($"The admitted content inventory states media '{mediaId}' without a usable path or length.");
                }

                if (!entries.TryAdd(mediaId, new InventoryEntry(path, byteLength)))
                {
                    throw new InvalidOperationException($"The admitted content inventory names media '{mediaId}' twice.");
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The admitted content inventory '{InventoryPath}' is not valid JSON: {exception.Message}", exception);
        }

        return entries;
    }

    /// <summary>Reads one published artifact by the name the inventory states, and refuses a length that disagrees with it.</summary>
    private static byte[] ReadArtifact(IContentService content, string mediaId, InventoryEntry entry)
    {
        byte[] bytes;
        try
        {
            bytes = ReadAdmittedFile(content, entry.Path);
        }
        catch (Exception exception) when (exception is InvalidOperationException or EngineCallException)
        {
            throw new InvalidOperationException($"Published UI art '{mediaId}' could not be read from admitted content at '{entry.Path}': {exception.Message}", exception);
        }

        if (bytes.LongLength != entry.ByteLength)
        {
            throw new InvalidOperationException($"Published UI art '{mediaId}' at '{entry.Path}' is {bytes.LongLength} bytes where the inventory states {entry.ByteLength}.");
        }

        return bytes;
    }

    /// <summary>Reads one admitted file by name. Its bounded reference read supplies the byte length.</summary>
    private static byte[] ReadAdmittedFile(IContentService content, string path)
    {
        using ContentReference reference = content.OpenReference(new ContentOpenRequest(path));
        ReadOnlyMemory<ContentReferenceInfo> info = content.ReadReferenceInfo(reference);
        if (info.Length != 1)
        {
            throw new InvalidOperationException($"Admitted content returned no readable reference for '{path}'.");
        }

        ulong length = info.Span[0].ByteLength;
        if (length is 0 or > int.MaxValue)
        {
            throw new InvalidOperationException($"Admitted content '{path}' is {length} bytes, which is not a readable published artifact.");
        }

        byte[] bytes = new byte[checked((int)length)];
        ulong offset = 0;
        while (offset < length)
        {
            uint take = (uint)Math.Min(MaximumReadBytes, length - offset);
            ReadOnlyMemory<byte> chunk = content.ReadBytes(new ContentReadBytesRequest(reference, offset, take));
            if (chunk.IsEmpty)
            {
                throw new InvalidOperationException($"Admitted content '{path}' returned no bytes at offset {offset} of its {length}.");
            }

            chunk.CopyTo(bytes.AsMemory(checked((int)offset)));
            offset += (ulong)chunk.Length;
        }

        return bytes;
    }

    private readonly record struct InventoryEntry(string Path, long ByteLength);
}
