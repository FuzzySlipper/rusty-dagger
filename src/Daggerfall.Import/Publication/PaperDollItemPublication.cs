using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>One published worn-item image: a texture record in one dye, placed in the doll.</summary>
/// <param name="Archive">The texture archive.</param>
/// <param name="Record">The texture record.</param>
/// <param name="Dyes">The media identity each dye draws with; dyes that leave the record unchanged share one.</param>
/// <param name="Placement">Where the doll draws the record.</param>
/// <param name="MaskMediaId">The record's mask, which erases what the doll drew beneath it, or null when it has none.</param>
public sealed record PaperDollItemImage(int Archive, int Record, IReadOnlyDictionary<string, string> Dyes, DaggerfallPaperDollPlacement Placement, string? MaskMediaId);

/// <summary>One emitted PNG.</summary>
public sealed record PaperDollArtifact(string MediaId, string RelativePath, byte[] Bytes, int Width, int Height);

/// <summary>The paper doll's worn-item images, the other items' inventory images, and the artifacts they name.</summary>
public sealed record PaperDollItemPass(IReadOnlyList<PaperDollItemImage> Images, IReadOnlyList<PaperDollArtifact> Artifacts)
{
    /// <summary>The media identity of every published inventory image (<c>item.ARCHIVE.RECORD</c>).</summary>
    public IReadOnlyList<string> Icons { get; init; } = [];
}

/// <summary>
/// Publishes the image the paper doll draws for every record of the worn-item archives, in every dye, placed in
/// the doll, and every record of the inventory image archives. Which record an item draws is the ruleset's
/// question, answered from its item templates, which are authored data the import does not read.
/// </summary>
/// <remarks>
/// <para>
/// The donor draws a worn item from its player texture archive and record (<c>ItemHelper.GetItemImage</c>
/// with <c>forPaperDoll</c>): an armour template's archive is 245 for a female and 249 for a male
/// (<c>ItemBuilder.ApplyArmorSettings</c>) and clothing's is the template's own, both offset by the body
/// morphology (argonian 0, elf 1, human 2, khajiit 3; <c>ItemBuilder.SetRace</c>); weapons are drawn from
/// the template's archive for everyone. A variant is the template's record plus the variant, a cloak's
/// variants start one record later because its first record is the interior, and a one-handed weapon in
/// the right hand is drawn from the next record. Every record of those archives is published, so the ruleset
/// only has to say which one an item is.
/// </para>
/// <para>
/// The colour is a palette-index swap (<c>ImageProcessing.ChangeDye</c>): clothing swaps indices 0x60-0x6F
/// for its dye's sixteen, and weapons and armour swap 0x70-0x7F for their metal's table, where steel,
/// leather and chain keep the record as it is. A dye that leaves a record unchanged names the undyed
/// image rather than a copy of it. Index 0xFF is the mask (<c>maskColor</c> 255,0,200): it draws as
/// transparent, and a record that has one also publishes the mask, which the doll uses to clear what it
/// drew beneath the item - hair around a helm. One record carries a wrong header offset, which the donor
/// corrects and this does too.
/// </para>
/// <para>
/// An artifact is drawn from its own texture rather than its base template's (<c>DaggerfallUnityItem.SetArtifact</c>
/// with <c>ItemHelper.GetArtifactTextureIndices</c>): archive 432 for a male and 433 for a female, at a record
/// each artifact names, with the next record for a one-handed artifact weapon in the right hand. Those
/// records between them cover both archives, so both are published whole, undyed - an artifact keeps its
/// colours - under the <see cref="UnchangedDye"/> name.
/// </para>
/// <para>
/// The inventory draws a worn item from these same images (<c>ItemHelper.GetItemImage</c> without
/// <c>forPaperDoll</c>), so they are its icons too. Every other template is drawn from a texture of its own
/// (<c>DaggerfallUnityItem.GetInventoryTextureArchive</c>): its world texture when <c>UseWorldTexture</c> holds
/// (useless items, ingredients, arrows, religious and miscellaneous items) or it has no player texture, else
/// its player texture record plus its variant (books, transportation). Every record of those archives is
/// published too, as <c>item.ARCHIVE.RECORD</c>, and the <c>icons</c> section lists them.
/// </para>
/// </remarks>
public static class PaperDollItemPublication
{
    /// <summary>The relative path the generated index is written to.</summary>
    public const string IndexRelativePath = "media/character/paper-doll/paper-doll-inventory.json";

    /// <summary>The index's generator identity.</summary>
    public const string IndexGenerator = "daggerfall-import-tool paper-doll-media";

    /// <summary>The male and female artifact archives, which are published whole.</summary>
    public static readonly int[] ArtifactArchives = [432, 433];

    /// <summary>The texture archives the worn items are drawn from.</summary>
    public static readonly int[] Archives = [234, 235, 236, 237, 238, 239, 240, 241, 242, 245, 246, 247, 248, 249, 250, 251, 252, .. ArtifactArchives];

    /// <summary>
    /// The texture archives the classic items' inventory images are drawn from, where the paper doll does not draw
    /// them: ingredients, books, religious and miscellaneous items, arrows, transport and the rest.
    /// </summary>
    public static readonly int[] IconArchives = [200, 201, 202, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 216, 218, 254, 380];

    /// <summary>The dye an artifact image is published under: the record as it is stored.</summary>
    public const string UnchangedDye = "unchanged";

    private const byte MaskIndex = 0xFF;
    private const int ClothingDyeStart = 0x60;
    private const int MetalDyeStart = 0x70;

    /// <summary>Each clothing dye's first replacement index; the sixteen run on from it.</summary>
    private static readonly (string Dye, int Start)[] ClothingDyes =
    [
        ("blue", 0x60), ("grey", 0x50), ("red", 0xEF), ("dark-brown", 0x20), ("purple", 0x30),
        ("light-brown", 0x40), ("white", 0x70), ("aquamarine", 0x80), ("yellow", 0x90), ("green", 0xA0),
    ];

    /// <summary>Each material's metal table; leather, chain and steel leave the record unchanged.</summary>
    private static readonly (string Dye, byte[]? Table)[] MetalDyes =
    [
        ("iron", [0x77, 0x78, 0x57, 0x79, 0x58, 0x59, 0x7A, 0x5A, 0x7B, 0x5B, 0x7C, 0x5C, 0x7D, 0x5D, 0x5E, 0x5F]),
        ("steel", null),
        ("silver", [0xE0, 0x70, 0x50, 0x71, 0x51, 0x72, 0x73, 0x52, 0x74, 0x53, 0x75, 0x54, 0x55, 0x56, 0x57, 0x58]),
        ("elven", [0xE0, 0x70, 0x50, 0x71, 0x51, 0x72, 0x73, 0x52, 0x74, 0x53, 0x75, 0x54, 0x76, 0x56, 0x77, 0x78]),
        ("dwarven", [0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0x9B, 0x9C, 0x9D, 0x9E, 0x9F]),
        ("mithril", [0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F, 0xD8, 0xD9, 0xDA, 0xDB, 0xDC, 0xDD, 0xDE]),
        ("adamantium", [0x5A, 0x5B, 0x7C, 0x5C, 0x7D, 0x5D, 0x7E, 0x5E, 0x7F, 0xD8, 0xD9, 0xDA, 0xDB, 0xDC, 0xDD, 0xDE]),
        ("ebony", [0x77, 0x78, 0x79, 0x7A, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F, 0xD8, 0xD9, 0xDA, 0xDB, 0xDC, 0xDD, 0xDE]),
        ("orcish", [0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xCB, 0xCC, 0xCD, 0xCE, 0xCF, 0xD8, 0xD9, 0xDA, 0xDB, 0xDC, 0xDD]),
        ("daedric", [0xEF, 0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA, 0xFB, 0xFC, 0xFD, 0xFE]),
        ("leather", null),
        ("chain", null),
    ];

    /// <summary>The media identity of one record's inventory image.</summary>
    public static string IconMediaId(int archive, int record) => $"item.{archive}.{record}";

    /// <summary>The media identity of one record, optionally in one dye, or its mask.</summary>
    public static string MediaId(int archive, int record, string? variant = null) =>
        variant is null ? $"paper-doll.{archive}.{record}" : $"paper-doll.{archive}.{record}.{variant}";

    /// <summary>Publishes every record of the worn-item and inventory image archives.</summary>
    /// <param name="archives">The supplied texture archives by number.</param>
    /// <param name="palette">The palette the records are painted in (<c>ART_PAL.COL</c>).</param>
    public static PaperDollItemPass Publish(IReadOnlyDictionary<int, ReadOnlyMemory<byte>> archives, Arena2Palette palette)
    {
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentNullException.ThrowIfNull(palette);
        List<PaperDollItemImage> images = [];
        List<PaperDollArtifact> artifacts = [];
        foreach (int number in Archives.Order())
        {
            string source = $"arena2/TEXTURE.{number:000}";
            if (!archives.TryGetValue(number, out ReadOnlyMemory<byte> bytes))
                throw new InvalidOperationException($"The worn items are drawn from {source}, which the supplied corpus does not carry.");
            TextureArchive archive = TextureArchive.Parse(bytes.Span, source);
            bool clothing = number is >= 235 and <= 242;
            bool artifactTexture = ArtifactArchives.Contains(number);
            for (int record = 0; record < archive.RecordCount; record++)
            {
                TextureRecordInfo info = archive.GetRecordInfo(record);
                IndexedTextureFrame frame = archive.DecodeFrame(record, 0);
                byte[] pixels = frame.Pixels.ToArray();
                bool masked = pixels.Contains(MaskIndex);
                for (int index = 0; index < pixels.Length; index++)
                    if (pixels[index] == MaskIndex) pixels[index] = 0;

                string baseId = MediaId(number, record);
                artifacts.Add(Emit(baseId, frame.Width, frame.Height, pixels, palette));
                Dictionary<string, string> dyes = new(StringComparer.Ordinal);
                IReadOnlyDictionary<string, byte[]> variants = artifactTexture
                    ? new Dictionary<string, byte[]> { [UnchangedDye] = pixels }
                    : DyeVariants(pixels, clothing);
                foreach ((string dye, byte[] dyed) in variants)
                {
                    if (ReferenceEquals(dyed, pixels))
                    {
                        dyes[dye] = baseId;
                        continue;
                    }

                    string id = MediaId(number, record, dye);
                    artifacts.Add(Emit(id, frame.Width, frame.Height, dyed, palette));
                    dyes[dye] = id;
                }

                string? maskId = null;
                if (masked)
                {
                    maskId = MediaId(number, record, "mask");
                    byte[] rgba = new byte[checked(frame.Pixels.Length * 4)];
                    ReadOnlySpan<byte> source_ = frame.Pixels.Span;
                    for (int index = 0; index < source_.Length; index++)
                        if (source_[index] == MaskIndex) rgba.AsSpan(index * 4, 4).Fill(byte.MaxValue);
                    artifacts.Add(new(maskId, Path(maskId), DeterministicPngEncoder.EncodeRgba8(frame.Width, frame.Height, rgba), frame.Width, frame.Height));
                }

                // The donor's ItemHelper corrects these two records ("Short shirt" variants for a human female),
                // whose headers place them wrongly.
                (int x, int y) = number == 237 && record is 52 or 54 ? (237, 43) : (info.XOffset, info.YOffset);
                images.Add(new PaperDollItemImage(number, record, dyes, DaggerfallPaperDollPlacement.FromScreenOffset(x, y, frame.Width, frame.Height), maskId));
            }
        }

        return new PaperDollItemPass(images, artifacts) { Icons = PublishIcons(archives, palette, artifacts) };
    }

    /// <summary>Publishes every record of the inventory image archives once, as stored (frame 0, the mask colour transparent).</summary>
    private static string[] PublishIcons(IReadOnlyDictionary<int, ReadOnlyMemory<byte>> archives, Arena2Palette palette, List<PaperDollArtifact> artifacts)
    {
        List<string> icons = [];
        foreach (int number in IconArchives)
        {
            string source = $"arena2/TEXTURE.{number:000}";
            if (!archives.TryGetValue(number, out ReadOnlyMemory<byte> bytes))
                throw new InvalidOperationException($"The items' inventory images are drawn from {source}, which the supplied corpus does not carry.");
            TextureArchive archive = TextureArchive.Parse(bytes.Span, source);
            for (int record = 0; record < archive.RecordCount; record++)
            {
                string id = IconMediaId(number, record);
                IndexedTextureFrame frame = archive.DecodeFrame(record, 0);
                byte[] pixels = frame.Pixels.ToArray();
                for (int index = 0; index < pixels.Length; index++)
                    if (pixels[index] == MaskIndex) pixels[index] = 0;
                artifacts.Add(Emit(id, frame.Width, frame.Height, pixels, palette));
                icons.Add(id);
            }
        }

        return [.. icons];
    }

    /// <summary>
    /// Classic clothing or metal palette swaps, shared by paper-doll publication and reference exports.
    /// A dye that changes no pixel returns the supplied array; callers can keep its aliases together.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> DyeVariants(byte[] pixels, bool clothing)
    {
        IEnumerable<(string Dye, byte[]? Table)> swaps = clothing
            ? ClothingDyes.Select(dye => (dye.Dye, (byte[]?)[.. Enumerable.Range(dye.Start, 16).Select(value => (byte)value)]))
            : MetalDyes;
        return swaps.ToDictionary(swap => swap.Dye,
            swap => swap.Table is null ? pixels : Dye(pixels, clothing ? ClothingDyeStart : MetalDyeStart, swap.Table) ?? pixels,
            StringComparer.Ordinal);
    }

    /// <summary>The record in one dye, or null when the dye changes no pixel of it.</summary>
    private static byte[]? Dye(byte[] pixels, int start, byte[] table)
    {
        byte[]? dyed = null;
        for (int index = 0; index < pixels.Length; index++)
        {
            int offset = pixels[index] - start;
            if (offset is < 0 or > 15 || table[offset] == pixels[index]) continue;
            dyed ??= (byte[])pixels.Clone();
            dyed[index] = table[offset];
        }

        return dyed;
    }

    private static PaperDollArtifact Emit(string mediaId, int width, int height, byte[] pixels, Arena2Palette palette) =>
        new(mediaId, Path(mediaId), DeterministicPngEncoder.EncodeRgba8(width, height, palette.ToRgbaBytes(pixels, PaletteAlphaMode.IndexZeroTransparent)), width, height);

    private static string Path(string mediaId) => $"media/character/paper-doll/{PublishedIds.FileSlug(mediaId)}.png";

    /// <summary>The generated index, in the inventory shape every media group writes.</summary>
    public static byte[] WriteIndex(PaperDollItemPass pass, string contentGroup)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentGroup);
        return ArtifactInventory.Write(IndexGenerator, pass.Artifacts.OrderBy(artifact => artifact.MediaId, StringComparer.Ordinal).Select(artifact =>
        {
            JsonObject entry = ArtifactInventory.Entry($"{contentGroup}/{artifact.RelativePath}", artifact.Bytes, artifact.MediaId);
            entry["width"] = artifact.Width;
            entry["height"] = artifact.Height;
            return entry;
        }));
    }

    /// <summary>The payload section the ruleset resolves a worn item through.</summary>
    public static JsonObject Section(PaperDollItemPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        JsonArray images = [];
        foreach (PaperDollItemImage image in pass.Images)
        {
            JsonObject dyes = [];
            foreach ((string dye, string id) in image.Dyes.OrderBy(entry => entry.Key, StringComparer.Ordinal)) dyes[dye] = id;
            images.Add(new JsonObject
            {
                ["archive"] = image.Archive, ["record"] = image.Record, ["dyes"] = dyes,
                ["x"] = image.Placement.X, ["y"] = image.Placement.Y, ["width"] = image.Placement.Width, ["height"] = image.Placement.Height,
                ["maskMediaId"] = image.MaskMediaId,
            });
        }

        JsonArray icons = [.. pass.Icons.Select(id => (JsonNode)id)];
        return new JsonObject { ["items"] = images, ["icons"] = icons };
    }
}
