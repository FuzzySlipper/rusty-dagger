using System.Globalization;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;

namespace Daggerfall.Import.Publication;

/// <summary>
/// The product-wide world media publication: one closure of the material textures, billboard and actor
/// atlases, terrain textures, meshes, classic sidecar and audio clips every site closure references, with
/// the selection facts that say which texture records it carries and which it cannot.
/// </summary>
/// <param name="Plan">The publication closure, written once under the product's content root.</param>
/// <param name="Textures">Which texture records became materials and billboards, and why the others did not.</param>
/// <param name="Geometry">How the published meshes relate to every record the mesh archive declares.</param>
public sealed record Arena2WorldMediaPublication(
    ImportPublicationPlan Plan,
    Arena2WorldTextureSelection Textures,
    GeometryPublicationSummary Geometry);

/// <summary>One texture record the product-wide publication does not carry in a form, and why.</summary>
/// <param name="Archive">The texture leaf.</param>
/// <param name="Record">The record within it.</param>
/// <param name="Form">"material" or "billboard".</param>
/// <param name="Reason">Why the record cannot be published in that form.</param>
public sealed record Arena2WorldTextureRefusal(ushort Archive, ushort Record, string Form, string Reason);

/// <summary>
/// The texture records the product-wide publication carries. A block's climate and dungeon texture tables
/// remap the archives its meshes name, and a flat may sit in any archive, so every record a material or a
/// billboard atlas can be cut from is selected rather than the records one site happens to use. A record
/// that cannot be cut in a form is stated with its reason instead of being dropped silently; a site that
/// needs it then fails naming it, exactly as it would have when it cut the record itself.
/// </summary>
public sealed class Arena2WorldTextureSelection
{
    private Arena2WorldTextureSelection(
        IReadOnlyList<string> materials,
        IReadOnlyList<string> billboards,
        IReadOnlyList<ushort> archives,
        IReadOnlyList<Arena2WorldTextureRefusal> refused)
    {
        Materials = materials;
        Billboards = billboards;
        Archives = archives;
        Refused = refused;
    }

    /// <summary>Material texture handles, <c>texture/&lt;archive&gt;-&lt;record&gt;</c>.</summary>
    public IReadOnlyList<string> Materials { get; }

    /// <summary>Billboard sprite handles, <c>sprite/texture-&lt;archive&gt;-&lt;record&gt;</c>.</summary>
    public IReadOnlyList<string> Billboards { get; }

    /// <summary>The leaves a selected record lives in, ascending.</summary>
    public IReadOnlyList<ushort> Archives { get; }

    /// <summary>The records a form could not be cut from, with the reason.</summary>
    public IReadOnlyList<Arena2WorldTextureRefusal> Refused { get; }

    /// <summary>
    /// Selects every publishable record of every leaf the inventory decoded. A material is the record's
    /// first frame; a billboard is every frame in one strip, so the frames must fit the atlas quota side by
    /// side, exactly as a site's billboard atlas is packed.
    /// </summary>
    public static Arena2WorldTextureSelection Select(
        TextureLeafInventory inventory,
        Func<ushort, TextureArchive> parse,
        Arena2DungeonMediaQuotas quotas)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(quotas);
        quotas.Validate();
        List<string> materials = [];
        List<string> billboards = [];
        SortedSet<ushort> archives = [];
        List<Arena2WorldTextureRefusal> refused = [];
        foreach (TextureLeafRecord leaf in inventory.Decoded.OrderBy(leaf => leaf.Id))
        {
            ushort archiveId = checked((ushort)leaf.Id);
            TextureArchive archive = parse(archiveId);
            for (int index = 0; index < archive.RecordCount; index++)
            {
                ushort record = checked((ushort)index);
                if (TryMaterial(archive, index, out string? materialReason))
                {
                    materials.Add(string.Create(CultureInfo.InvariantCulture, $"texture/{archiveId}-{record}"));
                    archives.Add(archiveId);
                }
                else
                {
                    refused.Add(new(archiveId, record, "material", materialReason!));
                    refused.Add(new(archiveId, record, "billboard", materialReason!));
                    continue;
                }

                if (TryBillboard(archive, index, quotas, out string? billboardReason))
                {
                    billboards.Add(string.Create(CultureInfo.InvariantCulture, $"sprite/texture-{archiveId}-{record}"));
                }
                else
                {
                    refused.Add(new(archiveId, record, "billboard", billboardReason!));
                }
            }
        }

        return new(materials, billboards, [.. archives], refused);
    }

    private static bool TryMaterial(TextureArchive archive, int record, out string? reason)
    {
        try
        {
            TextureRecordInfo info = archive.GetRecordInfo(record);
            if (info.FrameCount == 0 || info.Width <= 0 || info.Height <= 0)
            {
                reason = $"the record declares {info.FrameCount} frames of {info.Width}x{info.Height}, nothing a texture can be cut from";
                return false;
            }

            _ = archive.DecodeFrame(record, 0);
            reason = null;
            return true;
        }
        catch (Exception exception) when (exception is Arena2FormatException or ArgumentException)
        {
            reason = exception.Message;
            return false;
        }
    }

    private static bool TryBillboard(TextureArchive archive, int record, Arena2DungeonMediaQuotas quotas, out string? reason)
    {
        try
        {
            int frames = archive.GetRecordInfo(record).FrameCount;
            if (frames > quotas.MaximumFramesPerAtlas)
            {
                reason = $"its {frames} frames exceed the {quotas.MaximumFramesPerAtlas}-frame atlas quota";
                return false;
            }

            int cellWidth = 0;
            int cellHeight = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                IndexedTextureFrame decoded = archive.DecodeFrame(record, frame);
                cellWidth = Math.Max(cellWidth, decoded.Width);
                cellHeight = Math.Max(cellHeight, decoded.Height);
            }

            if (cellHeight > quotas.MaximumAtlasDimension || (long)cellWidth * frames > quotas.MaximumAtlasDimension)
            {
                reason = $"its {frames} frames of {cellWidth}x{cellHeight} do not fit one {quotas.MaximumAtlasDimension}-pixel atlas strip";
                return false;
            }

            reason = null;
            return true;
        }
        catch (Exception exception) when (exception is Arena2FormatException or ArgumentException)
        {
            reason = exception.Message;
            return false;
        }
    }
}
