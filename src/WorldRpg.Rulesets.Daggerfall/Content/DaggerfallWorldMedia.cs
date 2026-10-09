using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The product-wide world media every site closure references: the meshes, material textures, billboard
/// and actor atlases, terrain textures, classic sidecar, audio clips and world visuals the importer publishes
/// once, and the classic images the classic media group publishes. A site closure names a shared artifact by
/// the same closure-relative path it would have carried, and <see cref="DaggerfallClosureArtifacts"/>
/// resolves that path here when the site does not carry it.
/// </summary>
/// <remarks>
/// The eager content snapshot holds every body except the audio clips, which the Engine serves through the
/// one bundle the Host declares at <see cref="AudioRoot"/>. The dungeon sidecar is held as one parsed
/// document while the selected sites are read, so each site resolves the entries it admits by identity
/// without reparsing it; dispose the instance once the sites are read.
/// </remarks>
internal sealed class DaggerfallWorldMedia : IDisposable
{
    /// <summary>The world media publication's content root.</summary>
    internal const string Root = "worldrpg/imports/shared";

    /// <summary>The bundle the Engine stages the shared audio clips into.</summary>
    internal const string AudioBundleId = "daggerfall.world-audio";

    /// <summary>The audio bundle's root inside the content store.</summary>
    internal const string AudioRoot = Root + "/media/audio/clips";

    /// <summary>The classic media group's inventory; its paths are content paths under <see cref="ClassicGroupRoot"/>.</summary>
    internal const string ClassicInventoryPath = DaggerfallPublishedClassicMedia.InventoryPath;

    /// <summary>The content directory the classic media group's relative paths are rooted at.</summary>
    internal const string ClassicGroupRoot = "worldrpg";

    internal const string DungeonSidecarRelativePath = "media/dungeon/manifest.json";
    internal const string ClassicSidecarRelativePath = "media/classic/manifest.json";

    private readonly JsonDocument? _dungeon;
    private readonly IReadOnlyDictionary<string, JsonElement> _resources;
    private readonly IReadOnlyDictionary<string, JsonElement> _billboards;
    private readonly IReadOnlyDictionary<string, JsonElement> _actors;

    private DaggerfallWorldMedia(
        IReadOnlyDictionary<string, ContentSha256> shared,
        IReadOnlyDictionary<string, ContentSha256> classicGroup,
        JsonDocument? dungeon,
        ReadOnlyMemory<byte>? classicSidecar)
    {
        Shared = shared;
        ClassicGroup = classicGroup;
        _dungeon = dungeon;
        ClassicSidecar = classicSidecar;
        Dictionary<string, JsonElement> resources = new(StringComparer.Ordinal);
        Dictionary<string, JsonElement> billboards = new(StringComparer.Ordinal);
        Dictionary<string, JsonElement> actors = new(StringComparer.Ordinal);
        if (dungeon is not null)
        {
            JsonElement root = dungeon.RootElement;
            foreach (JsonElement resource in root.GetProperty("media").GetProperty("resources").EnumerateArray())
                resources.TryAdd(resource.GetProperty("id").GetString()!, resource);
            foreach (JsonElement billboard in root.GetProperty("billboards").EnumerateArray())
                billboards.TryAdd(billboard.GetProperty("spriteResourceId").GetString()!, billboard);
            foreach (JsonElement actor in root.GetProperty("actors").EnumerateArray())
                actors.TryAdd(actor.GetProperty("actorResourceId").GetString()!, actor);
        }

        _resources = resources;
        _billboards = billboards;
        _actors = actors;
    }

    /// <summary>The world media publication's artifacts by content path.</summary>
    internal IReadOnlyDictionary<string, ContentSha256> Shared { get; }

    /// <summary>The classic media group's artifacts by content path.</summary>
    internal IReadOnlyDictionary<string, ContentSha256> ClassicGroup { get; }

    /// <summary>Whether the content carries the world media publication at all.</summary>
    internal bool IsPublished => _dungeon is not null;

    /// <summary>The product-wide classic sidecar a site without its own presents.</summary>
    internal ReadOnlyMemory<byte>? ClassicSidecar { get; }

    /// <summary>
    /// Reads the product-wide media from admitted content. Content without the world media publication reads
    /// as an empty instance, so a site that carries everything it names still reads; one that references the
    /// publication then reports each missing reference by name.
    /// </summary>
    internal static DaggerfallWorldMedia Read(ProductContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        AdmittedFiles files = AdmittedFiles.From(content);
        DaggerfallContentDiagnostics diagnostics = new();
        Dictionary<string, ContentSha256> classicGroup = ReadClassicGroup(files, diagnostics);
        if (Optional(files, $"{Root}/import-manifest.json") is not ReadOnlyMemory<byte> manifest)
        {
            diagnostics.ThrowIfAny();
            return new(new Dictionary<string, ContentSha256>(), classicGroup, null, null);
        }

        Dictionary<string, ContentSha256> shared = new(StringComparer.Ordinal);
        JsonDocument? dungeon = null;
        try
        {
            using (JsonDocument document = JsonDocument.Parse(manifest))
            {
                foreach (JsonElement artifact in DaggerfallBaseContent.Array(DaggerfallBaseContent.Object(document.RootElement, "world media manifest", diagnostics), "artifacts", diagnostics))
                {
                    string relativePath = DaggerfallBaseContent.Text(artifact, "relativePath", diagnostics);
                    ContentSha256 hash = DaggerfallContentHash.Parse(DaggerfallBaseContent.Text(artifact, "contentHash", diagnostics), $"World media artifact '{relativePath}'");
                    if (!shared.TryAdd($"{Root}/{relativePath}", hash)) diagnostics.Add($"The world media manifest repeats artifact '{relativePath}'.");
                }
            }

            if (Optional(files, $"{Root}/{DungeonSidecarRelativePath}") is ReadOnlyMemory<byte> dungeonBytes)
            {
                dungeon = JsonDocument.Parse(dungeonBytes);
                if (dungeon.RootElement.TryGetProperty("shared", out JsonElement selection) && selection.ValueKind != JsonValueKind.Null)
                    diagnostics.Add("The world media dungeon sidecar must carry its own entries rather than reference another publication.");
            }
            else diagnostics.Add($"The world media publication carries no '{DungeonSidecarRelativePath}'.");

            ReadOnlyMemory<byte>? classic = Optional(files, $"{Root}/{ClassicSidecarRelativePath}");
            if (classic is null) diagnostics.Add($"The world media publication carries no '{ClassicSidecarRelativePath}'.");
            diagnostics.ThrowIfAny();
            return new(shared, classicGroup, dungeon, classic);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException && exception is not DaggerfallContentException)
        {
            dungeon?.Dispose();
            diagnostics.Add($"The world media publication is malformed: {exception.Message}");
            throw diagnostics.Exception();
        }
        catch
        {
            dungeon?.Dispose();
            throw;
        }
    }

    /// <summary>Every product-wide dungeon descriptor's media id.</summary>
    internal IEnumerable<string> ResourceIds => _resources.Keys;

    /// <summary>Every product-wide billboard's sprite resource id.</summary>
    internal IEnumerable<string> BillboardIds => _billboards.Keys;

    /// <summary>Every product-wide actor's resource id.</summary>
    internal IEnumerable<string> ActorIds => _actors.Keys;

    /// <summary>A product-wide dungeon descriptor by media id.</summary>
    internal bool TryGetResource(string id, out JsonElement resource) => _resources.TryGetValue(id, out resource);

    /// <summary>A product-wide billboard entry by sprite resource id.</summary>
    internal bool TryGetBillboard(string id, out JsonElement billboard) => _billboards.TryGetValue(id, out billboard);

    /// <summary>A product-wide actor entry by actor resource id.</summary>
    internal bool TryGetActor(string id, out JsonElement actor) => _actors.TryGetValue(id, out actor);

    public void Dispose() => _dungeon?.Dispose();

    /// <summary>An admitted file's bytes, or null when content does not carry it.</summary>
    private static ReadOnlyMemory<byte>? Optional(AdmittedFiles files, string path) =>
        files.ContainsExactlyOne(path) ? files.GetExactlyOne(path) : null;

    private static Dictionary<string, ContentSha256> ReadClassicGroup(AdmittedFiles files, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, ContentSha256> artifacts = new(StringComparer.Ordinal);
        if (Optional(files, ClassicInventoryPath) is not ReadOnlyMemory<byte> bytes) return artifacts;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            foreach (JsonElement artifact in DaggerfallBaseContent.Array(DaggerfallBaseContent.Object(document.RootElement, "classic media inventory", diagnostics), "artifacts", diagnostics))
            {
                string path = DaggerfallBaseContent.Text(artifact, "path", diagnostics);
                if (!path.StartsWith(ClassicGroupRoot + "/", StringComparison.Ordinal)
                    || !artifacts.TryAdd(path, DaggerfallContentHash.Parse(DaggerfallBaseContent.Text(artifact, "sha256", diagnostics), $"Classic media artifact '{path}'")))
                    diagnostics.Add($"The classic media inventory names '{path}' outside its group or more than once.");
            }
        }
        catch (JsonException exception) { diagnostics.Add($"The classic media inventory is not valid JSON: {exception.Message}"); }
        return artifacts;
    }
}

/// <summary>
/// One site closure's artifacts and the product-wide artifacts it references, as one lookup. A closure-relative
/// path resolves to the site's own artifact when the site carries it, then to the world media publication,
/// then to the classic media group; the hash lookup answers for any of the three by content path.
/// </summary>
internal sealed class DaggerfallClosureArtifacts
{
    private readonly string _root;
    private readonly IReadOnlyDictionary<string, ContentSha256> _site;
    private readonly DaggerfallWorldMedia _product;

    internal DaggerfallClosureArtifacts(string publicationRoot, IReadOnlyDictionary<string, ContentSha256> site, DaggerfallWorldMedia product)
    {
        _root = publicationRoot.TrimEnd('/');
        _site = site;
        _product = product;
    }

    /// <summary>Whether the site itself carries the artifact at a closure-relative path.</summary>
    internal bool SiteCarries(string relativePath) => _site.ContainsKey($"{_root}/{relativePath}");

    /// <summary>The content path a closure-relative path names; the site's own path when nothing carries it.</summary>
    internal string Resolve(string relativePath)
    {
        string site = $"{_root}/{relativePath}";
        if (_site.ContainsKey(site)) return site;
        string shared = $"{DaggerfallWorldMedia.Root}/{relativePath}";
        if (_product.Shared.ContainsKey(shared)) return shared;
        string classic = $"{DaggerfallWorldMedia.ClassicGroupRoot}/{relativePath}";
        return _product.ClassicGroup.ContainsKey(classic) ? classic : site;
    }

    /// <summary>The admitted digest of a resolved content path.</summary>
    internal bool TryGetValue(string contentPath, out ContentSha256 hash) =>
        _site.TryGetValue(contentPath, out hash)
        || _product.Shared.TryGetValue(contentPath, out hash)
        || _product.ClassicGroup.TryGetValue(contentPath, out hash);
}
