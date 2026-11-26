using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The locations the base pack lays out anew in place of the imported ones. Its <c>locationOverrides</c> section says
/// which, and whether each is <see cref="DaggerfallSiteRecord.Reserved"/>; <c>daggerfall-source-tool location-bake</c>
/// bakes each with the import's own normalizer into the pack's <c>locations/</c> folder as a record, a detail and a
/// ground file in the shapes the import publishes. A baked location keeps the classic one's identity, kind, climate and
/// map pixel, and takes its name and exterior: block grid, buildings, building references and ground.
/// </summary>
internal sealed class DaggerfallLocationOverrides
{
    /// <summary>The folder beside the base pack's payload that holds the baked locations.</summary>
    internal const string Folder = "locations";

    private readonly IReadOnlyDictionary<DaggerfallSiteId, (string Name, DaggerfallSiteExterior Exterior, bool Reserved)> _sites;

    private DaggerfallLocationOverrides(IReadOnlyDictionary<DaggerfallSiteId, (string, DaggerfallSiteExterior, bool)> sites) => _sites = sites;

    internal static DaggerfallLocationOverrides None { get; } = new(new Dictionary<DaggerfallSiteId, (string, DaggerfallSiteExterior, bool)>());

    internal int Count => _sites.Count;

    /// <summary>The base pack's overrides: its assembled authored sections and the files in the <see cref="Folder"/> beside its payload.</summary>
    internal static DaggerfallLocationOverrides Read(ProductContent content, string payloadPath, ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(content);
        string folder = $"{payloadPath[..(payloadPath.LastIndexOf('/') + 1)]}{Folder}";
        // The content names each file by its path from the content root; the overrides name them from the folder.
        Dictionary<string, ReadOnlyMemory<byte>> files = content.ReadDirectory(folder, true).ToArray()
            .ToDictionary(file => file.RelativePath[(folder.Length + 1)..], file => file.Bytes, StringComparer.Ordinal);
        return Read(payload, files, folder);
    }

    /// <summary>
    /// Reads each override against the baked files, by path below the folder: one the payload names that no file bakes,
    /// or a baked file no override names, is refused, since the location would be laid out from a recipe it no longer
    /// follows.
    /// </summary>
    internal static DaggerfallLocationOverrides Read(ReadOnlyMemory<byte> payload, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files, string folder)
    {
        ArgumentNullException.ThrowIfNull(files);
        DaggerfallContentDiagnostics diagnostics = new();
        Dictionary<DaggerfallSiteId, (string, DaggerfallSiteExterior, bool)> sites = [];
        HashSet<string> used = new(StringComparer.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(payload))
        {
            if (document.RootElement.TryGetProperty("locationOverrides", out JsonElement section))
            {
                foreach (JsonElement value in section.EnumerateArray())
                {
                    DaggerfallSiteId site = new(DaggerfallBaseContent.Integer(value, "region", diagnostics), DaggerfallBaseContent.Integer(value, "index", diagnostics));
                    bool reserved = DaggerfallBaseContent.Boolean(value, "reserved", diagnostics);
                    string owner = $"Base pack location override {site}";
                    string record = $"{site.Region}/{site.Index}.location.json", detail = $"{site.Region}/{site.Index}.json", ground = $"{site.Region}/{site.Index}.ground";
                    if (!files.ContainsKey(record) || !files.ContainsKey(detail) || !files.ContainsKey(ground))
                    {
                        diagnostics.Add($"{owner} is not baked below '{folder}'; run daggerfall-source-tool location-bake.");
                        continue;
                    }

                    used.UnionWith([record, detail, ground]);
                    if (ReadSite(site, files[record], files[detail], files[ground], owner, diagnostics) is { } baked
                        && !sites.TryAdd(site, (baked.Name, baked.Exterior, reserved)))
                        diagnostics.Add($"{owner} is stated twice.");
                }
            }
        }

        foreach (string stale in files.Keys.Where(path => !used.Contains(path)).Order(StringComparer.Ordinal))
            diagnostics.Add($"Base pack location file '{folder}/{stale}' is baked for no location override; run daggerfall-source-tool location-bake.");
        diagnostics.ThrowIfAny();
        return new(sites);
    }

    /// <summary><paramref name="records"/> with each overridden location's name and exterior in place of the imported ones.</summary>
    internal IReadOnlyList<DaggerfallSiteRecord> Apply(IReadOnlyList<DaggerfallSiteRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        DaggerfallContentDiagnostics diagnostics = new();
        foreach (DaggerfallSiteId site in _sites.Keys.Where(site => !records.Any(record => record.Id == site && record.Exterior is not null)))
            diagnostics.Add($"Base pack location override {site} names a location the catalog places no exterior of.");
        diagnostics.ThrowIfAny();
        return [.. records.Select(record => _sites.TryGetValue(record.Id, out var baked)
            ? record with { Name = baked.Name, Exterior = baked.Exterior, Reserved = baked.Reserved }
            : record)];
    }

    private static (string Name, DaggerfallSiteExterior Exterior)? ReadSite(DaggerfallSiteId site, ReadOnlyMemory<byte> recordBytes,
        ReadOnlyMemory<byte> detailBytes, ReadOnlyMemory<byte> groundBytes, string owner, DaggerfallContentDiagnostics diagnostics)
    {
        try
        {
            using JsonDocument record = JsonDocument.Parse(recordBytes);
            using JsonDocument detail = JsonDocument.Parse(detailBytes);
            JsonElement root = DaggerfallBaseContent.Object(record.RootElement, owner, diagnostics);
            if (DaggerfallBaseContent.Integer(root, "region", diagnostics) != site.Region || DaggerfallBaseContent.Integer(root, "index", diagnostics) != site.Index)
            {
                diagnostics.Add($"{owner} is baked as another location.");
                return null;
            }

            string name = DaggerfallBaseContent.Text(root, "name", diagnostics);
            DaggerfallSiteExterior? exterior = DaggerfallBaseContent.ReadSiteExterior(root, site.Region, site.Index,
                DaggerfallBaseContent.Integer(root, "longitude", diagnostics), DaggerfallBaseContent.Integer(root, "latitude", diagnostics), null, diagnostics);
            if (exterior is null)
            {
                diagnostics.Add($"{owner} is baked with no exterior.");
                return null;
            }

            JsonElement details = DaggerfallBaseContent.Object(detail.RootElement, $"{owner} detail", diagnostics);
            if (groundBytes.Length != DaggerfallGroundTileGrid.CellCount)
            {
                diagnostics.Add($"{owner} ground carries {groundBytes.Length} bytes instead of the 128-by-128 grid.");
                return null;
            }

            IReadOnlyList<int> types = exterior.BuildingTypes;
            return (name, exterior with
            {
                Detail = null,
                Buildings = DaggerfallBaseContent.ReadSiteBuildings(details, site.Region, site.Index, exterior.Width, exterior.Height, diagnostics),
                BuildingReferences = DaggerfallBaseContent.ReadBuildingReferences(details, diagnostics),
                BuildingTypes = types,
                GroundTiles = DaggerfallGroundTileGrid.FromBytes(groundBytes.Span, $"{owner} ground"),
            });
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            diagnostics.Add($"{owner} is malformed: {exception.Message}");
            return null;
        }
    }
}
