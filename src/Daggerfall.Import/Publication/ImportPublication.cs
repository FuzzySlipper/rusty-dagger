using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>A single final artifact in a portable import publication closure.</summary>
public sealed class ImportPublicationArtifact
{
    private readonly byte[] bytes;

    public ImportPublicationArtifact(string relativePath, ReadOnlySpan<byte> bytes, IReadOnlyList<string>? dependsOnPaths = null, string? mediaId = null)
    {
        NormalizedImportDocument.RequireLogicalPath(relativePath, nameof(relativePath));
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A published artifact cannot be empty.", nameof(bytes));
        }

        if (mediaId is not null)
        {
            NormalizedImportDocument.RequireLogicalId(mediaId, nameof(mediaId));
        }

        RelativePath = relativePath;
        this.bytes = bytes.ToArray();
        MediaId = mediaId;
        DependsOnPaths = (dependsOnPaths ?? [])
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        NormalizedImportDocument.ValidateUnique(DependsOnPaths, path => path, "published artifact dependency");
        foreach (string dependency in DependsOnPaths)
        {
            NormalizedImportDocument.RequireLogicalPath(dependency, nameof(dependsOnPaths));
        }
    }

    public string RelativePath { get; }

    /// <summary>
    /// The media identity this artifact carries, when it is one a consumer asks for by name rather
    /// than a file it discovers. Placement belongs to the publication; the identity is the pack's.
    /// </summary>
    public string? MediaId { get; }

    public ReadOnlyMemory<byte> Bytes => bytes;

    public IReadOnlyList<string> DependsOnPaths { get; }

    public ContentDigest ContentHash => ContentDigest.Compute(bytes);
}

/// <summary>
/// One source file a publication or a published section was read from: its logical path in the
/// <see cref="PublishedSourcePath"/> vocabulary, the digest of the bytes read and their length. Every
/// section, closure manifest and applied overlay states its sources in this one shape.
/// </summary>
public sealed record PublishedSource(string Path, ContentDigest ContentDigest, long ByteLength)
{
    /// <summary>The source fact for bytes read under a logical path.</summary>
    public static PublishedSource Of(string path, ReadOnlySpan<byte> bytes) => new(path, ContentDigest.Compute(bytes), bytes.Length);

    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalPath(Path, nameof(Path));
        ContentDigest.Validate();
        if (ByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteLength), ByteLength, $"Source '{Path}' states no bytes.");
        }
    }
}

/// <summary>One deterministic artifact entry in the canonical publication manifest.</summary>
public sealed record ImportPublicationManifestArtifact(string RelativePath, ContentDigest ContentHash, long ByteLen, IReadOnlyList<string> DependsOnPaths)
{
    public ImportPublicationManifestArtifact Canonicalize() => this with
    {
        DependsOnPaths = DependsOnPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
    };

    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalPath(RelativePath, nameof(RelativePath));
        ContentHash.Validate();
        if (ByteLen < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteLen), ByteLen, "An artifact byte length cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(DependsOnPaths);
        NormalizedImportDocument.ValidateUnique(DependsOnPaths, path => path, "publication manifest artifact dependency");
        foreach (string dependency in DependsOnPaths)
        {
            NormalizedImportDocument.RequireLogicalPath(dependency, nameof(DependsOnPaths));
        }
    }
}

/// <summary>
/// How a publication was produced: the command line that writes it and the authored overlay documents it
/// applied. The overlays are recorded beside the sources rather than among them, because the sprite
/// authoring basis an overlay is written against is computed from the sources.
/// </summary>
public sealed record ImportInvocation(IReadOnlyList<string> Command, IReadOnlyList<PublishedSource> AuthoredOverlays)
{
    public static ImportInvocation None { get; } = new([], []);

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Command);
        ArgumentNullException.ThrowIfNull(AuthoredOverlays);
        if (Command.Any(argument => argument is null || argument.Any(char.IsControl)))
        {
            throw new ArgumentException("A recorded command argument must be plain text.", nameof(Command));
        }

        foreach (PublishedSource overlay in AuthoredOverlays)
        {
            ArgumentNullException.ThrowIfNull(overlay);
            overlay.Validate();
        }
    }
}

/// <summary>
/// Canonical statement of an import result: the importer revision and command line that produced it, the
/// sources and authored overlays it read, and the artifacts it wrote. It excludes timestamps; the command is
/// recorded as the caller spelled it, so a caller that passes repository-relative paths keeps the manifest
/// host-independent.
/// </summary>
public sealed record CanonicalImportManifest(
    int SchemaVersion,
    string ImporterId,
    string ImporterRevision,
    IReadOnlyList<string> Command,
    IReadOnlyList<PublishedSource> Sources,
    IReadOnlyList<PublishedSource> AuthoredOverlays,
    IReadOnlyList<ImportPublicationManifestArtifact> Artifacts)
{
    public const int CurrentSchemaVersion = 1;

    public CanonicalImportManifest Canonicalize() => this with
    {
        Sources = Sources.OrderBy(source => source.Path, StringComparer.Ordinal).ToArray(),
        AuthoredOverlays = AuthoredOverlays.OrderBy(source => source.Path, StringComparer.Ordinal).ToArray(),
        Artifacts = Artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).Select(artifact => artifact.Canonicalize()).ToArray(),
    };

    /// <summary>The invocation this manifest records, as a view of its own fields rather than another one.</summary>
    [JsonIgnore]
    public ImportInvocation Invocation => new(Command, AuthoredOverlays);

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only publication manifest schema version {CurrentSchemaVersion} is supported.");
        }

        NormalizedImportDocument.RequireLogicalId(ImporterId, nameof(ImporterId));
        NormalizedImportDocument.RequireLogicalId(ImporterRevision, nameof(ImporterRevision));
        ArgumentNullException.ThrowIfNull(Sources);
        ArgumentNullException.ThrowIfNull(Artifacts);
        Invocation.Validate();
        ValidateUnique(Sources, source => source.Path, "source path");
        ValidateUnique(AuthoredOverlays, source => source.Path, "authored overlay path");
        ValidateUnique(Artifacts, artifact => artifact.RelativePath, "artifact path");
        foreach (PublishedSource source in Sources)
        {
            source.Validate();
        }

        foreach (ImportPublicationManifestArtifact artifact in Artifacts)
        {
            artifact.Validate();
        }

        HashSet<string> paths = Artifacts.Select(artifact => artifact.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (ImportPublicationManifestArtifact artifact in Artifacts)
        {
            foreach (string dependency in artifact.DependsOnPaths)
            {
                if (StringComparer.Ordinal.Equals(dependency, artifact.RelativePath) || !paths.Contains(dependency))
                {
                    throw new InvalidOperationException($"Publication artifact '{artifact.RelativePath}' has an unresolved or self dependency '{dependency}'.");
                }
            }
        }
        ValidateAcyclicArtifactDependencies(Artifacts);
    }

    private static void ValidateUnique<T>(IEnumerable<T> values, Func<T, string> value, string kind)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (T entry in values)
        {
            if (!seen.Add(value(entry)))
            {
                throw new InvalidOperationException($"The publication manifest contains a duplicate {kind} '{value(entry)}'.");
            }
        }
    }

    private static void ValidateAcyclicArtifactDependencies(IReadOnlyList<ImportPublicationManifestArtifact> artifacts)
    {
        Dictionary<string, ImportPublicationManifestArtifact> byPath = artifacts.ToDictionary(artifact => artifact.RelativePath, StringComparer.Ordinal);
        Dictionary<string, VisitState> states = [];
        foreach (ImportPublicationManifestArtifact artifact in artifacts)
        {
            Visit(artifact.RelativePath);
        }

        return;

        void Visit(string path)
        {
            if (states.TryGetValue(path, out VisitState state))
            {
                if (state == VisitState.Visiting)
                {
                    throw new InvalidOperationException($"Publication artifact dependency graph contains a cycle at '{path}'.");
                }

                return;
            }

            states[path] = VisitState.Visiting;
            foreach (string dependency in byPath[path].DependsOnPaths)
            {
                Visit(dependency);
            }

            states[path] = VisitState.Visited;
        }
    }

    private enum VisitState
    {
        Visiting,
        Visited,
    }
}

/// <summary>Canonical JSON for <see cref="CanonicalImportManifest"/>.</summary>
public static class ImportPublicationManifestSerializer
{
    public const string ManifestRelativePath = "import-manifest.json";

    private static readonly JsonSerializerOptions Options = PublishedJson.Section;

    public static byte[] Serialize(CanonicalImportManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        CanonicalImportManifest canonical = manifest.Canonicalize();
        canonical.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(canonical, Options);
        return [.. bytes, (byte)'\n'];
    }

    /// <summary>Reads a published manifest, so a consumer can cite what a publication actually read.</summary>
    public static CanonicalImportManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        CanonicalImportManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CanonicalImportManifest>(bytes, Options)
                ?? throw new FormatException("The publication manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("The publication manifest is not a supported strict JSON document.", exception);
        }

        manifest.Validate();
        return manifest;
    }
}

/// <summary>
/// An immutable, validated publication closure. The manifest is always emitted
/// as <c>import-manifest.json</c> and is itself part of the closure.
/// </summary>
public sealed class ImportPublicationPlan
{
    private readonly IReadOnlyList<ImportPublicationArtifact> artifacts;

    private ImportPublicationPlan(CanonicalImportManifest manifest, IReadOnlyList<ImportPublicationArtifact> artifacts)
    {
        Manifest = manifest;
        this.artifacts = artifacts;
    }

    public CanonicalImportManifest Manifest { get; }

    public IReadOnlyList<ImportPublicationArtifact> Artifacts => artifacts;

    public static ImportPublicationPlan Create(ImportProvenance provenance, IEnumerable<ImportPublicationArtifact> artifacts, ImportInvocation? invocation = null)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(artifacts);
        provenance.Validate();
        invocation ??= ImportInvocation.None;
        invocation.Validate();

        ImportPublicationArtifact[] materialized = artifacts.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("An import publication must contain at least one artifact.", nameof(artifacts));
        }

        if (materialized.Any(artifact => StringComparer.Ordinal.Equals(artifact.RelativePath, ImportPublicationManifestSerializer.ManifestRelativePath)))
        {
            throw new ArgumentException($"'{ImportPublicationManifestSerializer.ManifestRelativePath}' is reserved for the generated publication manifest.", nameof(artifacts));
        }

        ValidateUniquePaths(materialized);
        ImportPublicationArtifact[] orderedContent = materialized.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray();
        CanonicalImportManifest manifest = new(
            CanonicalImportManifest.CurrentSchemaVersion,
            provenance.ImporterId,
            provenance.ImporterRevision,
            invocation.Command.ToArray(),
            provenance.Sources.Select(source => new PublishedSource(source.SourcePath, source.ContentDigest, source.ByteLength)).ToArray(),
            invocation.AuthoredOverlays.ToArray(),
            orderedContent.Select(artifact => new ImportPublicationManifestArtifact(artifact.RelativePath, artifact.ContentHash, artifact.Bytes.Length, artifact.DependsOnPaths)).ToArray());
        manifest.Validate();
        byte[] manifestBytes = ImportPublicationManifestSerializer.Serialize(manifest);
        ImportPublicationArtifact manifestArtifact = new(ImportPublicationManifestSerializer.ManifestRelativePath, manifestBytes);
        ImportPublicationArtifact[] closure = [.. orderedContent, manifestArtifact];
        return new ImportPublicationPlan(manifest, Array.AsReadOnly(closure
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray()));
    }

    /// <summary>
    /// The same closure with the invocation that produces it recorded in its manifest. The artifacts are
    /// unchanged; only the manifest is rebuilt.
    /// </summary>
    public ImportPublicationPlan WithInvocation(ImportInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return Create(
            new ImportProvenance(
                ImportProvenance.CurrentSchemaVersion,
                Manifest.ImporterId,
                Manifest.ImporterRevision,
                Manifest.Sources.Select(source => new LogicalSourceRecord(
                    LogicalSourceRecord.CurrentSchemaVersion,
                    source.Path,
                    source.ContentDigest,
                    source.ByteLength,
                    NormalizedImportDocument.CurrentSchemaVersion)).ToArray()),
            artifacts.Where(artifact => artifact.RelativePath != ImportPublicationManifestSerializer.ManifestRelativePath),
            invocation);
    }

    /// <summary>Compares this exact closure with a target directory without mutating it.</summary>
    public ImportPublicationComparison Compare(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (!Directory.Exists(outputDirectory))
        {
            return new(false, artifacts.Select(artifact => artifact.RelativePath).ToArray(), [], []);
        }

        Dictionary<string, ImportPublicationArtifact> expected = artifacts.ToDictionary(artifact => artifact.RelativePath, StringComparer.Ordinal);
        List<string> missing = [];
        List<string> changed = [];
        foreach (ImportPublicationArtifact artifact in artifacts)
        {
            string path = ToOutputPath(outputDirectory, artifact.RelativePath);
            if (!File.Exists(path))
            {
                missing.Add(artifact.RelativePath);
            }
            else if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(artifact.Bytes.Span))
            {
                changed.Add(artifact.RelativePath);
            }
        }

        List<string> unexpected = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(outputDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => !expected.ContainsKey(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        return new(missing.Count == 0 && changed.Count == 0 && unexpected.Count == 0, missing, changed, unexpected);
    }

    private static void ValidateUniquePaths(IEnumerable<ImportPublicationArtifact> artifacts)
    {
        HashSet<string> paths = new(StringComparer.Ordinal);
        foreach (ImportPublicationArtifact artifact in artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            if (!paths.Add(artifact.RelativePath))
            {
                throw new ArgumentException($"The publication contains duplicate relative path '{artifact.RelativePath}'.", nameof(artifacts));
            }
        }
    }

    internal static string ToOutputPath(string outputDirectory, string relativePath)
    {
        string candidate = Path.GetFullPath(Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string root = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A publication artifact escaped its output directory.");
        }

        return candidate;
    }
}

/// <summary>Exact closure differences detected by <see cref="ImportPublicationPlan.Compare"/>.</summary>
public sealed record ImportPublicationComparison(bool IsNoOp, IReadOnlyList<string> Missing, IReadOnlyList<string> Changed, IReadOnlyList<string> Unexpected);

/// <summary>Safe filesystem owner for atomic publication of an import plan.</summary>
public static class ImportPublicationWriter
{
    public static ImportPublicationComparison Write(ImportPublicationPlan plan, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ImportPublicationComparison comparison = plan.Compare(outputDirectory);
        if (comparison.IsNoOp)
        {
            return comparison;
        }

        string target = Path.GetFullPath(outputDirectory);
        string? parent = Path.GetDirectoryName(target);
        string name = Path.GetFileName(target);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("The publication output directory must name a child directory.", nameof(outputDirectory));
        }

        if (File.Exists(target))
        {
            throw new IOException($"Publication target '{target}' is a file, not a directory.");
        }

        Directory.CreateDirectory(parent);
        string nonce = Guid.NewGuid().ToString("N");
        string staging = Path.Combine(parent, $".{name}.staging-{nonce}");
        string backup = Path.Combine(parent, $".{name}.backup-{nonce}");
        bool originalMoved = false;
        bool published = false;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (ImportPublicationArtifact artifact in plan.Artifacts)
            {
                string artifactPath = ImportPublicationPlan.ToOutputPath(staging, artifact.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
                File.WriteAllBytes(artifactPath, artifact.Bytes.ToArray());
            }

            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
                originalMoved = true;
            }

            Directory.Move(staging, target);
            published = true;
            if (originalMoved)
            {
                Directory.Delete(backup, recursive: true);
            }

            return comparison;
        }
        catch
        {
            if (originalMoved && !published && Directory.Exists(backup) && !Directory.Exists(target))
            {
                Directory.Move(backup, target);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            if (Directory.Exists(backup) && published)
            {
                Directory.Delete(backup, recursive: true);
            }
        }
    }
}
