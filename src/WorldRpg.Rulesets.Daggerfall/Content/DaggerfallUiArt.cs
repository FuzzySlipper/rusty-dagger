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
    internal const string CharacterInventoryPath = "worldrpg/media/character/character-media-inventory.json";
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
        "screen.prison",
        "screen.start-menu",
        "screen.title",
        "window.character-sheet.chrome",
    ];

    /// <summary>
    /// Where the publication puts the authored UI kit. Every artifact under it is delivered: the kit is the
    /// skin every screen draws, and the inventory states which images it has, so no list is kept here.
    /// </summary>
    internal const string AuthoredPathSegment = "/media/ui/authored/";

    /// <summary>
    /// The largest slice one admitted read serves - the Engine refuses a larger request - so an
    /// artifact bigger than this is read in chunks and joined here.
    /// </summary>
    private const uint MaximumReadBytes = 1024 * 1024;

    /// <summary>The paper doll's worn-item images, read only when the doll first draws one.</summary>
    internal const string PaperDollInventoryPath = "worldrpg/media/character/paper-doll/paper-doll-inventory.json";

    private readonly IContentService? _content;
    private readonly Dictionary<string, InventoryEntry> _inventory = new(StringComparer.Ordinal);
    private Dictionary<string, InventoryEntry>? _paperDoll;
    private readonly HashSet<string> _delivered = new(StringComparer.Ordinal);
    private readonly List<DaggerfallUiArtImage> _demanded = [];

    private DaggerfallUiArt(IReadOnlyList<DaggerfallUiArtImage> images, IReadOnlyList<string> pickScreens, IContentService content, Dictionary<string, InventoryEntry> inventory)
        : this(images, pickScreens)
    {
        _content = content;
        _inventory = inventory;
    }

    private DaggerfallUiArt(IReadOnlyList<DaggerfallUiArtImage> images, IReadOnlyList<string> pickScreens)
    {
        // A new admitted image block invalidates retained DOM art, including across runtime restarts.
        // This token identifies the publication; it does not hash or verify the image bytes.
        Revision = Guid.NewGuid().ToString("N");
        Images = images;
        PickScreens = pickScreens;
    }

    /// <summary>
    /// The images read on demand since the session began, in the order first drawn. The block grows and is
    /// never trimmed, so a DOM that holds its revision holds every image a projection has named.
    /// </summary>
    internal IReadOnlyList<DaggerfallUiArtImage> Demanded => _demanded;

    /// <summary>The identity of the demanded block as it stands: the publication's revision and its size.</summary>
    internal string DemandedRevision => $"{Revision}.{_demanded.Count}";

    /// <summary>
    /// Reads every named image that this art does not already deliver, from the published inventories.
    /// </summary>
    /// <exception cref="InvalidOperationException">An identity no admitted inventory describes, named.</exception>
    internal void Demand(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        foreach (string id in ids)
        {
            if (_delivered.Contains(id)) continue;
            if (_content is null) throw new InvalidOperationException($"UI art '{id}' was demanded from art that reads no content.");
            if (!_inventory.TryGetValue(id, out InventoryEntry entry))
            {
                _paperDoll ??= ReadInventory(_content, PaperDollInventoryPath);
                if (!_paperDoll.TryGetValue(id, out entry))
                    throw new InvalidOperationException($"Published UI art '{id}' is not described by the admitted content inventories.");
            }

            _demanded.Add(new(id, $"data:image/png;base64,{Convert.ToBase64String(ReadArtifact(_content, id, entry))}"));
            _delivered.Add(id);
        }
    }

    /// <summary>Publication-stable UI-art identity used by a reconnecting DOM to request the current image block.</summary>
    internal string Revision { get; }
    internal IReadOnlyList<string> PickScreens { get; }

    /// <summary>Every resolved image as a data URL, ordered by media identity, with how an authored one is drawn.</summary>
    internal IReadOnlyList<DaggerfallUiArtImage> Images { get; }

    /// <summary>
    /// Reads the published UI art this session shows. <paramref name="itemIcons"/> are the inventory
    /// icon identities the content pack names for its items, <paramref name="characterIcons"/> the
    /// portraits the HUD selects and <paramref name="panelArt"/> the art a panel lays out by identity -
    /// the travel map's world and region canvases; the rest is what this presentation always draws.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A needed identity is absent from the inventory, or admitted content does not carry the bytes the
    /// inventory describes; the message names the artifact so the producer/consumer disagreement is
    /// visible rather than a blank image at runtime.
    /// </exception>
    internal static DaggerfallUiArt Read(IContentService content, IEnumerable<string> itemIcons, IEnumerable<string>? characterIcons = null, IEnumerable<string>? panelArt = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(itemIcons);
        Dictionary<string, InventoryEntry> inventory = ReadInventory(content, InventoryPath);
        string[] portraits = characterIcons?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        // The character inventory also supplies the bodies the paper doll demands.
        if (portraits.Length > 0)
            foreach (var entry in ReadInventory(content, CharacterInventoryPath))
                if (!inventory.TryAdd(entry.Key, entry.Value)) throw new InvalidOperationException($"Published UI art repeats media '{entry.Key}' across inventories.");
        string[] pickScreens = inventory.Where(entry => entry.Value.Slot == "pick").Select(entry => entry.Key).Order(StringComparer.Ordinal).ToArray();
        if (pickScreens.Length == 0 || !pickScreens.Contains("screen.pick.02", StringComparer.Ordinal))
            throw new InvalidOperationException("The published pick slot must supply screen.pick.02.");
        string[] authored = inventory.Where(entry => entry.Value.Path.Contains(AuthoredPathSegment, StringComparison.Ordinal))
            .Select(entry => entry.Key).ToArray();
        List<DaggerfallUiArtImage> images = [];
        foreach (string id in AlwaysShown.Concat(authored).Concat(pickScreens).Concat(itemIcons).Concat(portraits).Concat(panelArt ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!inventory.TryGetValue(id, out InventoryEntry entry))
            {
                throw new InvalidOperationException($"Published UI art '{id}' is not described by the admitted content inventory '{InventoryPath}'.");
            }

            byte[] bytes = ReadArtifact(content, id, entry);
            images.Add(new(id, $"data:image/png;base64,{Convert.ToBase64String(bytes)}", entry.Display));
        }

        DaggerfallUiArt art = new(images, pickScreens, content, inventory);
        art._delivered.UnionWith(images.Select(image => image.Id));
        return art;
    }

    /// <summary>Reads the generated inventory by name and indexes it by the media identity it states.</summary>
    private static Dictionary<string, InventoryEntry> ReadInventory(IContentService content, string inventoryPath)
    {
        byte[] bytes = ReadAdmittedFile(content, inventoryPath);
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

                DaggerfallUiArtDisplay? display = artifact.TryGetProperty("display", out JsonElement drawn) && drawn.ValueKind != JsonValueKind.Null ? ReadDisplay(mediaId, drawn) : null;
                if (!entries.TryAdd(mediaId, new InventoryEntry(path, byteLength, artifact.TryGetProperty("slot", out var slot) ? slot.GetString() : null, display)))
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

    /// <summary>Reads how the publication says an authored image is drawn; a malformed statement names the artifact.</summary>
    private static DaggerfallUiArtDisplay ReadDisplay(string mediaId, JsonElement drawn)
    {
        static int[]? Ints(JsonElement parent, string name, int count) =>
            parent.TryGetProperty(name, out JsonElement values)
                ? values.ValueKind == JsonValueKind.Array && values.GetArrayLength() == count
                    ? values.EnumerateArray().Select(value => value.GetInt32()).ToArray()
                    : throw new FormatException(name)
                : null;
        try
        {
            double scale = drawn.GetProperty("scale").GetDouble();
            if (!double.IsFinite(scale) || scale <= 0) throw new FormatException("scale");
            return new DaggerfallUiArtDisplay(scale, Ints(drawn, "slice", 4), Ints(drawn, "hotspot", 2));
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"The admitted content inventory states an unreadable display for media '{mediaId}': {exception.Message}", exception);
        }
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

    private readonly record struct InventoryEntry(string Path, long ByteLength, string? Slot, DaggerfallUiArtDisplay? Display);
}

/// <summary>One delivered image: its media identity, its bytes as a data URL, and how an authored image is drawn.</summary>
internal sealed record DaggerfallUiArtImage(string Id, string Image, DaggerfallUiArtDisplay? Display = null);

/// <summary>
/// How the DOM draws an authored image, as its author stated it: CSS pixels per image pixel at UI scale 1,
/// the 9-slice insets (top, right, bottom, left) and a cursor hotspot (x, y), both in image pixels.
/// </summary>
internal sealed record DaggerfallUiArtDisplay(double Scale, IReadOnlyList<int>? Slice, IReadOnlyList<int>? Hotspot);
