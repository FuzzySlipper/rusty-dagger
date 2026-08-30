using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>A single final artifact in a portable import publication closure.</summary>
public sealed class ImportPublicationArtifact
{
    private readonly byte[] bytes;

    public ImportPublicationArtifact(string relativePath, ReadOnlySpan<byte> bytes, IReadOnlyList<string>? dependsOnPaths = null)
    {
        NormalizedImportDocument.RequireLogicalPath(relativePath, nameof(relativePath));
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A published artifact cannot be empty.", nameof(bytes));
        }

        RelativePath = relativePath;
        this.bytes = bytes.ToArray();
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

    public ReadOnlyMemory<byte> Bytes => bytes;

    public IReadOnlyList<string> DependsOnPaths { get; }

    public ContentDigest ContentHash => ContentDigest.Compute(bytes);
}

/// <summary>Portable source fact retained by a publication manifest.</summary>
public sealed record ImportPublicationSource(string SourcePath, ContentDigest ContentHash, long ByteLen)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalPath(SourcePath, nameof(SourcePath));
        ContentHash.Validate();
        if (ByteLen <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteLen), ByteLen, "A source byte length must be positive.");
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
/// Canonical, host-independent statement of an import result. It intentionally
/// excludes timestamps, absolute paths, and publication-directory identity.
/// </summary>
public sealed record CanonicalImportManifest(
    int SchemaVersion,
    string ImporterId,
    int ImporterVersion,
    IReadOnlyList<ImportPublicationSource> Sources,
    IReadOnlyList<ImportPublicationManifestArtifact> Artifacts)
{
    public const int CurrentSchemaVersion = 1;

    public CanonicalImportManifest Canonicalize() => this with
    {
        Sources = Sources.OrderBy(source => source.SourcePath, StringComparer.Ordinal).ToArray(),
        Artifacts = Artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).Select(artifact => artifact.Canonicalize()).ToArray(),
    };

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only publication manifest schema version {CurrentSchemaVersion} is supported.");
        }

        NormalizedImportDocument.RequireLogicalId(ImporterId, nameof(ImporterId));
        if (ImporterVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ImporterVersion), ImporterVersion, "An importer version must be positive.");
        }

        ArgumentNullException.ThrowIfNull(Sources);
        ArgumentNullException.ThrowIfNull(Artifacts);
        ValidateUnique(Sources, source => source.SourcePath, "source path");
        ValidateUnique(Artifacts, artifact => artifact.RelativePath, "artifact path");
        foreach (ImportPublicationSource source in Sources)
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

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static byte[] Serialize(CanonicalImportManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        CanonicalImportManifest canonical = manifest.Canonicalize();
        canonical.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(canonical, Options);
        return [.. bytes, (byte)'\n'];
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

    public static ImportPublicationPlan Create(ImportProvenance provenance, IEnumerable<ImportPublicationArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(artifacts);
        provenance.Validate();

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
            provenance.ImporterVersion,
            provenance.Sources.Select(source => new ImportPublicationSource(source.SourcePath, source.ContentDigest, source.ByteLength)).ToArray(),
            orderedContent.Select(artifact => new ImportPublicationManifestArtifact(artifact.RelativePath, artifact.ContentHash, artifact.Bytes.Length, artifact.DependsOnPaths)).ToArray());
        manifest.Validate();
        byte[] manifestBytes = ImportPublicationManifestSerializer.Serialize(manifest);
        ImportPublicationArtifact manifestArtifact = new(ImportPublicationManifestSerializer.ManifestRelativePath, manifestBytes);
        ImportPublicationArtifact[] closure = [.. orderedContent, manifestArtifact];
        return new ImportPublicationPlan(manifest, Array.AsReadOnly(closure
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray()));
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
