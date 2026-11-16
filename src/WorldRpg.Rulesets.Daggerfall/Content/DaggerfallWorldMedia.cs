using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The product-wide world media every assembled location draws from: the meshes, material textures, billboard
/// and actor atlases, terrain textures, classic sidecar, audio clips and world visuals the importer publishes
/// once, and the classic images the classic media group publishes. Their sidecars name each artifact by its
/// publication-relative path, which <see cref="Resolve"/> turns into a content path.
/// </summary>
/// <remarks>
/// Every body is read from the data container when first needed; the audio clips below
/// <see cref="AudioRoot"/> are opened as Engine references when a cue first plays. The dungeon sidecar is held as one
/// parsed document while the product media is read from it; dispose the instance once that is done.
/// </remarks>
internal sealed class DaggerfallWorldMedia : IDisposable
{
    /// <summary>The world media publication's content root.</summary>
    internal const string Root = "worldrpg/imports/shared";


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
    private readonly IReadOnlyDictionary<string, JsonElement> _animatedMaterials;

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
        Dictionary<string, JsonElement> animatedMaterials = new(StringComparer.Ordinal);
        if (dungeon is not null)
        {
            JsonElement root = dungeon.RootElement;
            foreach (JsonElement resource in root.GetProperty("media").GetProperty("resources").EnumerateArray())
                resources.TryAdd(resource.GetProperty("id").GetString()!, resource);
            foreach (JsonElement billboard in root.GetProperty("billboards").EnumerateArray())
                billboards.TryAdd(billboard.GetProperty("spriteResourceId").GetString()!, billboard);
            foreach (JsonElement actor in root.GetProperty("actors").EnumerateArray())
                actors.TryAdd(actor.GetProperty("actorResourceId").GetString()!, actor);
            foreach (JsonElement animation in root.GetProperty("animatedMaterials").EnumerateArray())
                animatedMaterials.TryAdd(animation.GetProperty("mediaId").GetString()!, animation);
        }

        _resources = resources;
        _billboards = billboards;
        _actors = actors;
        _animatedMaterials = animatedMaterials;
    }

    /// <summary>The world media publication's artifacts by content path.</summary>
    internal IReadOnlyDictionary<string, ContentSha256> Shared { get; }

    /// <summary>The classic media group's artifacts by content path.</summary>
    internal IReadOnlyDictionary<string, ContentSha256> ClassicGroup { get; }

    /// <summary>Whether the content carries the world media publication at all.</summary>
    internal bool IsPublished => _dungeon is not null;

    /// <summary>The product-wide classic sidecar.</summary>
    internal ReadOnlyMemory<byte>? ClassicSidecar { get; }

    /// <summary>
    /// Reads the product-wide media from admitted content. Content without the world media publication reads
    /// as an empty instance, so content that names none of it still reads, and a location that draws from it
    /// reports each missing artifact by name.
    /// </summary>
    internal static DaggerfallWorldMedia Read(DaggerfallData content)
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

            if (Optional(files, $"{Root}/{DungeonSidecarRelativePath}") is ReadOnlyMemory<byte> dungeonBytes) dungeon = JsonDocument.Parse(dungeonBytes);
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

    /// <summary>Every product-wide dungeon descriptor, in sidecar order.</summary>
    internal IEnumerable<JsonElement> Resources => _resources.Values;

    /// <summary>Every product-wide billboard entry, in sidecar order.</summary>
    internal IEnumerable<JsonElement> Billboards => _billboards.Values;

    /// <summary>Every product-wide actor entry, in sidecar order.</summary>
    internal IEnumerable<JsonElement> Actors => _actors.Values;

    /// <summary>A material whose texture cycles through several frames, by the material's media id.</summary>
    internal bool TryGetAnimation(string id, out JsonElement animation) => _animatedMaterials.TryGetValue(id, out animation);

    /// <summary>A product-wide dungeon descriptor by media id.</summary>
    internal bool TryGetResource(string id, out JsonElement resource) => _resources.TryGetValue(id, out resource);

    /// <summary>
    /// The content path a publication-relative path names: the world media publication's when it carries it, else the
    /// classic media group's, else the world media path, which then fails its manifest check by name.
    /// </summary>
    internal string Resolve(string relativePath)
    {
        string shared = $"{Root}/{relativePath}";
        if (Shared.ContainsKey(shared)) return shared;
        string classic = $"{ClassicGroupRoot}/{relativePath}";
        return ClassicGroup.ContainsKey(classic) ? classic : shared;
    }

    /// <summary>The admitted digest of a resolved content path.</summary>
    internal bool TryGetArtifact(string contentPath, out ContentSha256 hash) =>
        Shared.TryGetValue(contentPath, out hash) || ClassicGroup.TryGetValue(contentPath, out hash);

    public void Dispose() => _dungeon?.Dispose();

    /// <summary>An admitted file's bytes, or null when content does not carry it.</summary>
    private static ReadOnlyMemory<byte>? Optional(AdmittedFiles files, string path) =>
        files.ContainsExactlyOne(path) ? files.GetExactlyOne(path) : (ReadOnlyMemory<byte>?)null;

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
