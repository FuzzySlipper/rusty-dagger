using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>
/// The product-wide media a site closure references instead of carrying a copy: the world media publication
/// (meshes, materials, billboard and actor atlases, terrain, the classic sidecar, audio clips and world
/// visuals) and the classic media group (the classic images it already publishes). Both use the closure's own
/// relative path vocabulary, so a site names a shared artifact by exactly the path it would have written.
/// </summary>
/// <remarks>
/// A site closure keeps what is its own: the normalized document, the spatial artifacts, the geometry index,
/// its material-slot bindings and any sidecar entry that differs from the product-wide one (an authored
/// overlay). Every other artifact must be one the product-wide media carries with the same bytes: a body the
/// product-wide media lacks means the world media publication does not close over the site, and a body that
/// differs would make one relative path name two artifacts, so both are refused rather than copied.
/// </remarks>
public sealed class ProductWorldMedia
{
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string? sharedDirectory;
    private readonly string? classicGroupRoot;
    private readonly IReadOnlyDictionary<string, NormalizedMediaDescriptor> descriptors;
    private readonly IReadOnlyDictionary<string, DungeonBillboardMediaManifest> billboards;
    private readonly IReadOnlyDictionary<string, DungeonActorMediaManifest> actors;

    private ProductWorldMedia(
        CanonicalImportManifest manifest,
        IReadOnlyDictionary<string, ImportPublicationManifestArtifact> classicGroup,
        DungeonMediaManifestSidecar dungeon,
        ClassicMediaManifestSidecar classic,
        string? sharedDirectory,
        string? classicGroupRoot)
    {
        manifest.Validate();
        if (dungeon.Shared is not null || dungeon.Materials.Count != 0)
        {
            throw new InvalidOperationException("The product-wide dungeon sidecar must carry its entries itself and bind no site material slot.");
        }

        Arena2MediaBundlePublication.ValidatePersistedSidecars(dungeon, classic);
        Manifest = manifest;
        SharedArtifacts = manifest.Artifacts.ToDictionary(artifact => artifact.RelativePath, StringComparer.Ordinal);
        ClassicGroupArtifacts = classicGroup;
        if (SharedArtifacts.Keys.FirstOrDefault(classicGroup.ContainsKey) is { } collision)
        {
            throw new InvalidOperationException($"'{collision}' is published by both the world media publication and the classic media group.");
        }

        Dungeon = dungeon;
        Classic = classic;
        this.sharedDirectory = sharedDirectory;
        this.classicGroupRoot = classicGroupRoot;
        descriptors = dungeon.Media.Resources.ToDictionary(resource => resource.Id, StringComparer.Ordinal);
        billboards = dungeon.Billboards.ToDictionary(billboard => billboard.SpriteResourceId, StringComparer.Ordinal);
        actors = dungeon.Actors.ToDictionary(actor => actor.ActorResourceId, StringComparer.Ordinal);
    }

    /// <summary>The world media publication's own manifest.</summary>
    public CanonicalImportManifest Manifest { get; }

    /// <summary>The world media publication's artifacts by closure-relative path.</summary>
    public IReadOnlyDictionary<string, ImportPublicationManifestArtifact> SharedArtifacts { get; }

    /// <summary>The classic media group's artifacts by group-relative path, which is the closure-relative path.</summary>
    public IReadOnlyDictionary<string, ImportPublicationManifestArtifact> ClassicGroupArtifacts { get; }

    /// <summary>The product-wide dungeon sidecar: every descriptor, billboard and actor, and no material slot.</summary>
    public DungeonMediaManifestSidecar Dungeon { get; }

    /// <summary>The product-wide classic sidecar every site presents unless it carries its own.</summary>
    public ClassicMediaManifestSidecar Classic { get; }

    /// <summary>
    /// Reads the product-wide media from disk: the world media publication directory and the classic media
    /// group root (the directory its inventory's paths are relative to the parent of).
    /// </summary>
    public static ProductWorldMedia Read(string sharedDirectory, string classicGroupRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(classicGroupRoot);
        string shared = Path.GetFullPath(sharedDirectory);
        string group = Path.GetFullPath(classicGroupRoot);
        CanonicalImportManifest manifest = ImportPublicationManifestSerializer.Deserialize(
            ReadFile(shared, ImportPublicationManifestSerializer.ManifestRelativePath, "the world media publication (run the world-media command first)"));
        byte[] dungeon = ReadVerified(shared, manifest, Arena2MediaBundlePublication.DungeonMediaManifestRelativePath);
        byte[] classic = ReadVerified(shared, manifest, Arena2MediaBundlePublication.ClassicMediaManifestRelativePath);
        return new(manifest, ReadClassicGroup(group), Deserialize<DungeonMediaManifestSidecar>(dungeon), Deserialize<ClassicMediaManifestSidecar>(classic), shared, group);
    }

    /// <summary>Builds the product-wide media from an in-memory world media plan and a classic group listing.</summary>
    public static ProductWorldMedia FromPlan(ImportPublicationPlan shared, IReadOnlyDictionary<string, ImportPublicationManifestArtifact> classicGroup)
    {
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(classicGroup);
        Dictionary<string, ImportPublicationArtifact> artifacts = shared.Artifacts.ToDictionary(artifact => artifact.RelativePath, StringComparer.Ordinal);
        return new(
            shared.Manifest,
            classicGroup,
            Deserialize<DungeonMediaManifestSidecar>(artifacts[Arena2MediaBundlePublication.DungeonMediaManifestRelativePath].Bytes.Span),
            Deserialize<ClassicMediaManifestSidecar>(artifacts[Arena2MediaBundlePublication.ClassicMediaManifestRelativePath].Bytes.Span),
            null,
            null);
    }

    /// <summary>
    /// The classic media group's artifacts by group-relative path, read from the inventory the classic-media
    /// command writes under the group root. The inventory names each artifact by its content path, which is
    /// the group's directory name joined to the group-relative path.
    /// </summary>
    public static IReadOnlyDictionary<string, ImportPublicationManifestArtifact> ReadClassicGroup(string classicGroupRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classicGroupRoot);
        string root = Path.GetFullPath(classicGroupRoot);
        string group = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        byte[] bytes = ReadFile(root, ClassicMediaGroup.InventoryRelativePath, "the classic media group (run the classic-media command first)");
        Dictionary<string, ImportPublicationManifestArtifact> artifacts = new(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("generator", out JsonElement generator) || generator.GetString() != ClassicMediaGroup.Generator
            || !document.RootElement.TryGetProperty("artifacts", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"'{ClassicMediaGroup.InventoryRelativePath}' under '{root}' is not the classic media group's inventory.");
        }

        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string path = entry.GetProperty("path").GetString() ?? string.Empty;
            if (!path.StartsWith(group + "/", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Classic media inventory entry '{path}' is not under the group '{group}'.");
            }

            string relative = path[(group.Length + 1)..];
            ImportPublicationManifestArtifact artifact = new(
                relative,
                new ContentDigest(entry.GetProperty("sha256").GetString() ?? string.Empty),
                entry.GetProperty("byteLength").GetInt64(),
                []);
            artifact.Validate();
            if (!artifacts.TryAdd(relative, artifact))
            {
                throw new InvalidDataException($"The classic media inventory names '{path}' twice.");
            }
        }

        return artifacts;
    }

    /// <summary>The product-wide artifact a closure-relative path names, from either publication.</summary>
    public bool TryGetArtifact(string relativePath, out ImportPublicationManifestArtifact artifact) =>
        SharedArtifacts.TryGetValue(relativePath, out artifact!) || ClassicGroupArtifacts.TryGetValue(relativePath, out artifact!);

    /// <summary>Reads one product-wide body from disk; only a product-wide media read from disk can.</summary>
    public byte[] ReadBody(string relativePath)
    {
        if (SharedArtifacts.ContainsKey(relativePath) && sharedDirectory is not null)
        {
            return ReadFile(sharedDirectory, relativePath, "the world media publication");
        }

        if (ClassicGroupArtifacts.ContainsKey(relativePath) && classicGroupRoot is not null)
        {
            return ReadFile(classicGroupRoot, relativePath, "the classic media group");
        }

        throw new InvalidOperationException($"The product-wide media carries no readable '{relativePath}'.");
    }

    /// <summary>
    /// Reduces a complete site closure to what is its own and references the rest. Bodies the product-wide
    /// media carries with the same bytes are dropped; the dungeon sidecar keeps its slot bindings and every
    /// entry that differs from the product-wide one and names the rest in <see cref="DungeonMediaManifestSidecar.Shared"/>;
    /// a classic sidecar identical to the product-wide one is dropped. Dependencies on dropped artifacts are
    /// dropped with them, because the product-wide publication states its own.
    /// </summary>
    public ImportPublicationPlan Partition(ImportPublicationPlan site)
    {
        ArgumentNullException.ThrowIfNull(site);
        List<(ImportPublicationArtifact Artifact, byte[]? Replacement)> kept = [];
        List<string> missing = [];
        foreach (ImportPublicationArtifact artifact in site.Artifacts)
        {
            string path = artifact.RelativePath;
            if (path == ImportPublicationManifestSerializer.ManifestRelativePath) continue;
            if (path == Arena2MediaBundlePublication.DungeonMediaManifestRelativePath)
            {
                kept.Add((artifact, Arena2MediaBundlePublication.SerializeSidecar(Reference(Deserialize<DungeonMediaManifestSidecar>(artifact.Bytes.Span)))));
                continue;
            }

            if (path == Arena2MediaBundlePublication.ClassicMediaManifestRelativePath)
            {
                if (!SameDocument(Deserialize<ClassicMediaManifestSidecar>(artifact.Bytes.Span), Classic)) kept.Add((artifact, null));
                continue;
            }

            // A site's structural artifacts are its own even where the product-wide publication carries
            // one at the same path (its geometry index lists every mesh, a site's only the ones it names).
            if (IsSiteStructure(path))
            {
                kept.Add((artifact, null));
                continue;
            }

            if (TryGetArtifact(path, out ImportPublicationManifestArtifact? shared))
            {
                if (shared.ContentHash != artifact.ContentHash || shared.ByteLen != artifact.Bytes.Length)
                {
                    throw new InvalidOperationException($"Site artifact '{path}' differs from the product-wide artifact at the same path; regenerate the world media publication from the same inputs before the site.");
                }

                continue;
            }

            missing.Add(path);
        }

        if (missing.Count != 0)
        {
            throw new InvalidOperationException($"The product-wide world media does not carry {missing.Count} artifact(s) this site needs, so it cannot be referenced: {string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? ", ..." : string.Empty)}.");
        }

        HashSet<string> paths = kept.Select(entry => entry.Artifact.RelativePath).ToHashSet(StringComparer.Ordinal);
        ImportPublicationArtifact[] artifacts = [.. kept.Select(entry => new ImportPublicationArtifact(
            entry.Artifact.RelativePath,
            entry.Replacement ?? entry.Artifact.Bytes.ToArray(),
            [.. entry.Artifact.DependsOnPaths.Where(paths.Contains)],
            entry.Artifact.MediaId))];
        return ImportPublicationPlan.Create(
            new ImportProvenance(site.Manifest.ImporterId, site.Manifest.ImporterRevision,
                [.. site.Manifest.Sources.Select(source => new LogicalSourceRecord(source.Path, source.ContentDigest, source.ByteLength))]),
            artifacts,
            site.Manifest.Invocation);
    }

    /// <summary>
    /// The complete sidecar a site closure's own sidecar stands for: its own entries and the product-wide
    /// entries it admits, in the canonical order a complete sidecar is written in.
    /// </summary>
    public DungeonMediaManifestSidecar Rehydrate(DungeonMediaManifestSidecar site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (site.Shared is not { } shared) return site;
        return new DungeonMediaManifestSidecar(
            new NormalizedMediaManifest([.. site.Media.Resources
                .Concat(shared.Resources.Select(id => Require(descriptors, id, "descriptor")))
                .OrderBy(resource => resource.Id, StringComparer.Ordinal)]),
            site.Materials,
            [.. site.Billboards.Concat(shared.Billboards.Select(id => Require(billboards, id, "billboard"))).OrderBy(value => value.SpriteResourceId, StringComparer.Ordinal)],
            [.. site.Actors.Concat(shared.Actors.Select(id => Require(actors, id, "actor"))).OrderBy(value => value.ActorResourceId, StringComparer.Ordinal)]);
    }

    /// <summary>The site's classic sidecar if it carries one, otherwise the product-wide one.</summary>
    public ClassicMediaManifestSidecar ClassicFor(ClassicMediaManifestSidecar? site) => site ?? Classic;

    private DungeonMediaManifestSidecar Reference(DungeonMediaManifestSidecar site)
    {
        if (site.Shared is not null) throw new InvalidOperationException("A site dungeon sidecar is already written against the product-wide media.");
        List<string> sharedResources = [];
        List<NormalizedMediaDescriptor> ownResources = [];
        foreach (NormalizedMediaDescriptor resource in site.Media.Resources)
        {
            if (descriptors.TryGetValue(resource.Id, out NormalizedMediaDescriptor? product) && SameDocument(resource, product)) sharedResources.Add(resource.Id);
            else ownResources.Add(resource);
        }

        List<string> sharedBillboards = [];
        List<DungeonBillboardMediaManifest> ownBillboards = [];
        foreach (DungeonBillboardMediaManifest billboard in site.Billboards)
        {
            if (billboards.TryGetValue(billboard.SpriteResourceId, out DungeonBillboardMediaManifest? product) && SameDocument(billboard, product)) sharedBillboards.Add(billboard.SpriteResourceId);
            else ownBillboards.Add(billboard);
        }

        List<string> sharedActors = [];
        List<DungeonActorMediaManifest> ownActors = [];
        foreach (DungeonActorMediaManifest actor in site.Actors)
        {
            if (actors.TryGetValue(actor.ActorResourceId, out DungeonActorMediaManifest? product) && SameDocument(actor, product)) sharedActors.Add(actor.ActorResourceId);
            else ownActors.Add(actor);
        }

        DungeonMediaManifestSidecar reduced = new(new NormalizedMediaManifest(ownResources), site.Materials, ownBillboards, ownActors)
        {
            Shared = new DungeonSharedMediaSelection(sharedResources, sharedBillboards, sharedActors),
        };
        if (!SameDocument(Rehydrate(reduced), site))
        {
            throw new InvalidOperationException("The site dungeon sidecar does not round-trip through the product-wide media it references.");
        }

        return reduced;
    }

    /// <summary>
    /// The artifacts that are a site's own: its normalized document, its two sidecars, its spatial and
    /// resource artifacts and the geometry index that lists the meshes it references.
    /// </summary>
    private static bool IsSiteStructure(string path) =>
        path == Arena2MediaBundlePublication.NormalizedDocumentRelativePath
        || path == GeometryPublication.IndexRelativePath
        || path.StartsWith("spatial/", StringComparison.Ordinal)
        || path.StartsWith("resources/", StringComparison.Ordinal);

    private static T Require<T>(IReadOnlyDictionary<string, T> entries, string id, string kind) =>
        entries.TryGetValue(id, out T? value)
            ? value
            : throw new InvalidOperationException($"A site closure admits product-wide {kind} '{id}', which the world media publication does not carry.");

    private static bool SameDocument<T>(T left, T right) =>
        JsonSerializer.SerializeToUtf8Bytes(left, PublishedJson.SectionCompact).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, PublishedJson.SectionCompact));

    private static T Deserialize<T>(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, StrictJson) ?? throw new InvalidDataException($"The {typeof(T).Name} document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The {typeof(T).Name} document is not the generated shape: {exception.Message}", exception);
        }
    }

    private static byte[] ReadVerified(string root, CanonicalImportManifest manifest, string relativePath)
    {
        byte[] bytes = ReadFile(root, relativePath, "the world media publication");
        ImportPublicationManifestArtifact artifact = manifest.Artifacts.SingleOrDefault(value => value.RelativePath == relativePath)
            ?? throw new InvalidDataException($"The world media manifest does not list '{relativePath}'.");
        if (artifact.ByteLen != bytes.Length || artifact.ContentHash != ContentDigest.Compute(bytes))
        {
            throw new InvalidDataException($"'{relativePath}' in the world media publication does not match its manifest digest.");
        }

        return bytes;
    }

    private static byte[] ReadFile(string root, string relativePath, string owner)
    {
        string path = ImportPublicationPlan.ToOutputPath(root, relativePath);
        return File.Exists(path)
            ? File.ReadAllBytes(path)
            : throw new FileNotFoundException($"'{relativePath}' is absent from {owner} at '{root}'.", path);
    }
}
