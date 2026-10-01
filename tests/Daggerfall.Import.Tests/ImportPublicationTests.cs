using System.Text;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class ImportPublicationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"daggerfall-import-publication-{Guid.NewGuid():N}");

    [Fact]
    public void PlanCanonicalizesPortablePathsManifestOrderAndHashes()
    {
        ImportPublicationPlan first = CreatePlan(
            new ImportPublicationArtifact("zeta.bin", "zeta"u8),
            new ImportPublicationArtifact("nested/alpha.bin", "alpha"u8));
        ImportPublicationPlan second = CreatePlan(
            new ImportPublicationArtifact("nested/alpha.bin", "alpha"u8),
            new ImportPublicationArtifact("zeta.bin", "zeta"u8));

        Assert.Equal(first.Artifacts.Select(artifact => artifact.RelativePath), second.Artifacts.Select(artifact => artifact.RelativePath));
        Assert.Equal(first.Artifacts.Select(artifact => artifact.Bytes.ToArray()), second.Artifacts.Select(artifact => artifact.Bytes.ToArray()));
        PublishedSource source = Assert.Single(first.Manifest.Sources);
        Assert.Equal("arena2/MAPS.BSA", source.Path);
        Assert.Equal(4, source.ByteLength);
        Assert.Contains("\"contentDigest\"", Encoding.UTF8.GetString(ImportPublicationManifestSerializer.Serialize(first.Manifest)), StringComparison.Ordinal);
        Assert.Equal(4, first.Manifest.Artifacts.Single(artifact => artifact.RelativePath == "zeta.bin").ByteLen);
        Assert.Contains("import-manifest.json", first.Artifacts.Select(artifact => artifact.RelativePath));
    }

    [Fact]
    public void ManifestRecordsTheImporterRevisionCommandAndAuthoredOverlays()
    {
        ImportPublicationPlan plan = CreatePlan(new ImportPublicationArtifact("value.bin", "value"u8));
        PublishedSource overlay = new("sprites/site.json", ContentDigest.Compute("overlay"u8), 7);
        ImportPublicationPlan invoked = plan.WithInvocation(new ImportInvocation(["daggerfall-import-tool", "write", "--arena2", "arena2"], [overlay]));

        // Only the manifest changes: the recorded invocation is provenance, not another artifact.
        Assert.Equal(
            plan.Artifacts.Where(artifact => artifact.RelativePath != "import-manifest.json").Select(artifact => artifact.ContentHash),
            invoked.Artifacts.Where(artifact => artifact.RelativePath != "import-manifest.json").Select(artifact => artifact.ContentHash));
        ReadOnlyMemory<byte> manifestBytes = invoked.Artifacts.Single(artifact => artifact.RelativePath == "import-manifest.json").Bytes;
        CanonicalImportManifest reopened = ImportPublicationManifestSerializer.Deserialize(manifestBytes.Span);
        // The command and overlays are stated once, as top-level fields.
        Assert.DoesNotContain("\"invocation\"", Encoding.UTF8.GetString(manifestBytes.Span), StringComparison.Ordinal);
        Assert.Equal("test-revision", reopened.ImporterRevision);
        Assert.Equal(["daggerfall-import-tool", "write", "--arena2", "arena2"], reopened.Command);
        Assert.Equal(overlay, Assert.Single(reopened.AuthoredOverlays));
        // An overlay is recorded beside the sources, never among them: the sprite authoring basis is
        // computed from the sources, and an overlay cannot be part of the basis it is written against.
        Assert.DoesNotContain(reopened.Sources, source => source.Path == overlay.Path);
        Assert.Empty(plan.Manifest.Command);
        Assert.Throws<ArgumentException>(() => plan.WithInvocation(new ImportInvocation(["write\n"], [])));
    }

    [Fact]
    public void PlanCarriesValidatedArtifactDependenciesAlongsideExactBytes()
    {
        ImportPublicationPlan plan = CreatePlan(
            new ImportPublicationArtifact("spatial/static-mesh.rstatmsh", "mesh"u8),
            new ImportPublicationArtifact("spatial/collision-navigation.rspatial", "spatial"u8, ["spatial/static-mesh.rstatmsh"]),
            new ImportPublicationArtifact("normalized.json", "normalized"u8, ["spatial/collision-navigation.rspatial", "spatial/static-mesh.rstatmsh"]));

        ImportPublicationManifestArtifact spatial = plan.Manifest.Artifacts.Single(artifact => artifact.RelativePath == "spatial/collision-navigation.rspatial");
        Assert.Equal(["spatial/static-mesh.rstatmsh"], spatial.DependsOnPaths);
        Assert.Equal(7, spatial.ByteLen);
        Assert.Contains("\"dependsOnPaths\"", Encoding.UTF8.GetString(ImportPublicationManifestSerializer.Serialize(plan.Manifest)), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => CreatePlan(new ImportPublicationArtifact("only.json", "only"u8, ["missing.json"])));
        Assert.Throws<InvalidOperationException>(() => CreatePlan(new ImportPublicationArtifact("self.json", "self"u8, ["self.json"])));
        Assert.Throws<InvalidOperationException>(() => CreatePlan(
            new ImportPublicationArtifact("first.json", "first"u8, ["second.json"]),
            new ImportPublicationArtifact("second.json", "second"u8, ["first.json"])));
    }

    [Fact]
    public void CompareSeparatesMissingChangedAndUnexpectedClosureFiles()
    {
        ImportPublicationPlan plan = CreatePlan(new ImportPublicationArtifact("nested/value.bin", "expected"u8));
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(Path.Combine(output, "nested"));
        File.WriteAllText(Path.Combine(output, "nested", "value.bin"), "wrong", Encoding.UTF8);
        File.WriteAllText(Path.Combine(output, "leftover.bin"), "legacy", Encoding.UTF8);

        ImportPublicationComparison comparison = plan.Compare(output);

        Assert.False(comparison.IsNoOp);
        Assert.Contains("nested/value.bin", comparison.Changed);
        Assert.Contains("import-manifest.json", comparison.Missing);
        Assert.Contains("leftover.bin", comparison.Unexpected);
    }

    [Fact]
    public void WriterPublishesExactClosureAndThenReportsNoOp()
    {
        ImportPublicationPlan plan = CreatePlan(new ImportPublicationArtifact("nested/value.bin", "expected"u8));
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "legacy.bin"), "legacy", Encoding.UTF8);

        ImportPublicationComparison write = ImportPublicationWriter.Write(plan, output);

        Assert.False(write.IsNoOp);
        Assert.Equal(plan.Artifacts.Select(artifact => artifact.RelativePath), Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(output, path).Replace(Path.DirectorySeparatorChar, '/')).OrderBy(path => path, StringComparer.Ordinal));
        Assert.True(plan.Compare(output).IsNoOp);
        Assert.True(ImportPublicationWriter.Write(plan, output).IsNoOp);
    }

    [Fact]
    public void WriterLeavesExistingOutputUntouchedWhenStagingFails()
    {
        ImportPublicationPlan plan = CreatePlan(
            new ImportPublicationArtifact("conflict", "file"u8),
            new ImportPublicationArtifact("conflict/child.bin", "child"u8));
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "keep.bin"), "keep", Encoding.UTF8);

        Assert.ThrowsAny<IOException>(() => ImportPublicationWriter.Write(plan, output));

        Assert.Equal("keep", File.ReadAllText(Path.Combine(output, "keep.bin"), Encoding.UTF8));
        Assert.False(File.Exists(Path.Combine(output, "conflict")));
    }

    [Fact]
    public void PlanRejectsTraversalDuplicateAndReservedManifestPaths()
    {
        Assert.Throws<ArgumentException>(() => new ImportPublicationArtifact("../escape.bin", "x"u8));
        Assert.Throws<ArgumentException>(() => CreatePlan(
            new ImportPublicationArtifact("same.bin", "one"u8),
            new ImportPublicationArtifact("same.bin", "two"u8)));
        Assert.Throws<ArgumentException>(() => CreatePlan(new ImportPublicationArtifact("import-manifest.json", "spoof"u8)));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ImportPublicationPlan CreatePlan(params ImportPublicationArtifact[] artifacts) => ImportPublicationPlan.Create(
        new ImportProvenance(
            "daggerfall-import/test",
            "test-revision",
            [new LogicalSourceRecord("arena2/MAPS.BSA", new ContentDigest("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"), 4)]),
        artifacts);
}
