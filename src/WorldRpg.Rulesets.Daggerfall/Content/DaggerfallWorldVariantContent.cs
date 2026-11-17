using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// One cell a location variant redraws: it places <paramref name="Block"/> where the location places another, or its own
/// block when null, and draws <paramref name="Variant"/> of it, or the block as the source places it when null.
/// </summary>
internal sealed record DaggerfallWorldVariantCell(string? Block, string? Variant);

/// <summary>One baked block variant a pack carries: its document's content path and the spatial artifact's folder.</summary>
internal sealed record DaggerfallWorldVariantDocument(string Pack, string DocumentPath, string Root);

/// <summary>One quests pack's world variants as the reader takes them: its payload, and the files in its <c>variants/</c> folder.</summary>
internal sealed record DaggerfallWorldVariantPackFiles(string PackId, ReadOnlyMemory<byte> Payload, string VariantsRoot, IReadOnlyList<ProductContentFile> Documents);

/// <summary>
/// The world variants a quest's world update selects (<c>worldupdate location … variant V</c>), as the selected quests
/// packs carry them: a payload's <c>worldVariants.locations</c> says, by region, location and variant name, which exterior
/// cells place another block or draw a block variant, and the baked documents below a pack's <c>variants/</c> folder are
/// those block variants. Each is the base block with its recipe's edits (<c>worldVariants.blocks</c>, which
/// <c>daggerfall-source-tool world-variant-bake</c> bakes with the import's own normalizer), so the location assembly
/// reads it like a published block, collision and navigation included.
/// </summary>
/// <remarks>
/// Read at composition, which checks what needs no block: the names, that each cell is one the location places, and that
/// a block variant a cell draws is baked. A swapped block's building slots are checked against the block it replaces when
/// the location is first assembled with the variant (<see cref="DaggerfallLocationAssembly"/>), since only then are the
/// blocks read.
/// </remarks>
internal sealed class DaggerfallWorldVariantContent
{
    /// <summary>The folder beside a quests pack's payload that holds its baked block variants.</summary>
    internal const string Folder = "variants";

    private readonly IReadOnlyDictionary<(DaggerfallSiteId Site, string Variant), IReadOnlyDictionary<(int X, int Y), DaggerfallWorldVariantCell>> _locations;
    private readonly IReadOnlyDictionary<(string Block, string Variant), DaggerfallWorldVariantDocument> _blocks;

    private DaggerfallWorldVariantContent(
        IReadOnlyDictionary<(DaggerfallSiteId, string), IReadOnlyDictionary<(int X, int Y), DaggerfallWorldVariantCell>> locations,
        IReadOnlyDictionary<(string, string), DaggerfallWorldVariantDocument> blocks)
    {
        _locations = locations;
        _blocks = blocks;
    }

    internal static DaggerfallWorldVariantContent None { get; } = new(
        new Dictionary<(DaggerfallSiteId, string), IReadOnlyDictionary<(int X, int Y), DaggerfallWorldVariantCell>>(),
        new Dictionary<(string, string), DaggerfallWorldVariantDocument>());

    /// <summary>A location variant's cells, or null when no selected pack carries that variant of the location.</summary>
    internal IReadOnlyDictionary<(int X, int Y), DaggerfallWorldVariantCell>? Location(DaggerfallSiteId site, string variant) =>
        _locations.GetValueOrDefault((site, variant));

    /// <summary>The baked document of a block variant, or null when no selected pack carries it.</summary>
    internal DaggerfallWorldVariantDocument? Block(string block, string variant) => _blocks.GetValueOrDefault((block, variant));

    /// <summary>A quests pack's payload and the baked block variants in the <c>variants/</c> folder beside it.</summary>
    internal static DaggerfallWorldVariantPackFiles Files(ProductContent content, string packId, string payloadPath, ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(content);
        string folder = $"{payloadPath[..(payloadPath.LastIndexOf('/') + 1)]}{Folder}";
        return new(packId, payload, folder, [.. content.ReadDirectory(folder, true).ToArray()
            .Where(file => file.RelativePath.EndsWith(".json", StringComparison.Ordinal))]);
    }

    /// <summary>
    /// Reads every selected quests pack's world variants against the catalog's locations. A location variant or block
    /// variant has one home among the packs; one stated twice, or a cell the location does not place, a block variant no
    /// pack bakes or a name a quest cannot select, is refused by name.
    /// </summary>
    internal static DaggerfallWorldVariantContent Read(IReadOnlyList<DaggerfallWorldVariantPackFiles> packs, IReadOnlyList<DaggerfallSiteRecord> records)
    {
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(records);
        DaggerfallContentDiagnostics diagnostics = new();
        Dictionary<(string, string), DaggerfallWorldVariantDocument> blocks = [];
        foreach (DaggerfallWorldVariantPackFiles pack in packs)
            foreach (ProductContentFile file in pack.Documents.Where(file => file.RelativePath.EndsWith(".json", StringComparison.Ordinal)))
            {
                string owner = $"Quests pack '{pack.PackId}' world variant '{file.RelativePath}'";
                try
                {
                    using JsonDocument document = JsonDocument.Parse(file.Bytes);
                    JsonElement root = DaggerfallBaseContent.Object(document.RootElement, owner, diagnostics);
                    string block = DaggerfallBaseContent.Text(root, "sourceKey", diagnostics);
                    string? variant = DaggerfallBaseContent.OptionalText(root, "variant", diagnostics);
                    if (DaggerfallBaseContent.Text(root, "kind", diagnostics) != "rmbExterior" || variant is null)
                        diagnostics.Add($"{owner} is not a variant of an RMB exterior.");
                    else if (!blocks.TryAdd((block, variant), new(pack.PackId, file.RelativePath, pack.VariantsRoot)))
                        diagnostics.Add($"{owner} bakes variant '{variant}' of '{block}', which {blocks[(block, variant)].DocumentPath} bakes too.");
                }
                catch (JsonException exception) { diagnostics.Add($"{owner} is malformed: {exception.Message}"); }
            }

        Dictionary<DaggerfallSiteId, DaggerfallSiteRecord> sites = records.ToDictionary(record => record.Id);
        Dictionary<(DaggerfallSiteId, string), IReadOnlyDictionary<(int X, int Y), DaggerfallWorldVariantCell>> locations = [];
        foreach (DaggerfallWorldVariantPackFiles pack in packs)
        {
            using JsonDocument payload = JsonDocument.Parse(pack.Payload);
            if (!payload.RootElement.TryGetProperty("worldVariants", out JsonElement variants)) continue;
            foreach (JsonElement value in DaggerfallBaseContent.Array(variants, "locations", diagnostics))
            {
                DaggerfallSiteId site = new(DaggerfallBaseContent.Integer(value, "region", diagnostics), DaggerfallBaseContent.Integer(value, "location", diagnostics));
                string name = DaggerfallBaseContent.Text(value, "variant", diagnostics);
                string owner = $"Quests pack '{pack.PackId}' location variant '{name}' of {site}";
                if (!ValidName(name)) diagnostics.Add($"{owner} has a name a quest's world update cannot select.");
                DaggerfallSiteExterior? exterior = sites.GetValueOrDefault(site)?.Exterior;
                if (exterior is null) diagnostics.Add($"{owner} names a location the catalog places no exterior of.");
                Dictionary<(int X, int Y), DaggerfallWorldVariantCell> cells = [];
                foreach (JsonElement cellValue in DaggerfallBaseContent.Array(value, "blocks", diagnostics))
                {
                    (int X, int Y) at = (DaggerfallBaseContent.Integer(cellValue, "x", diagnostics), DaggerfallBaseContent.Integer(cellValue, "y", diagnostics));
                    DaggerfallWorldVariantCell cell = new(DaggerfallBaseContent.OptionalText(cellValue, "block", diagnostics),
                        DaggerfallBaseContent.OptionalText(cellValue, "variant", diagnostics));
                    string where = $"{owner} cell ({at.X}, {at.Y})";
                    if (!cells.TryAdd(at, cell)) diagnostics.Add($"{where} is stated twice.");
                    if (cell is { Block: null, Variant: null }) diagnostics.Add($"{where} changes nothing.");
                    if (cell.Variant is { } variant && !ValidName(variant)) diagnostics.Add($"{where} draws a block variant with a name no pack can bake.");
                    if (exterior is null) continue;
                    if (exterior.Blocks.SingleOrDefault(block => block.X == at.X && block.Y == at.Y) is not { } placed)
                    {
                        diagnostics.Add($"{where} is not one the location places a block in.");
                        continue;
                    }

                    string drawn = cell.Block ?? placed.SourceName;
                    if (cell.Block is { } swapped && !swapped.EndsWith(".RMB", StringComparison.Ordinal))
                        diagnostics.Add($"{where} places '{swapped}', which is no RMB block.");
                    if (cell.Variant is { } blockVariant && !blocks.ContainsKey((drawn, blockVariant)))
                        diagnostics.Add($"{where} draws variant '{blockVariant}' of '{drawn}', which no selected pack bakes; run daggerfall-source-tool world-variant-bake.");
                }

                if (cells.Count == 0) diagnostics.Add($"{owner} changes no cell.");
                if (!locations.TryAdd((site, name), cells)) diagnostics.Add($"{owner} is stated twice among the selected packs.");
            }
        }

        diagnostics.ThrowIfAny();
        return new(locations, blocks);
    }

    private static bool ValidName(string name) => name != "-" && DaggerfallBaseContent.ValidId(name);
}
