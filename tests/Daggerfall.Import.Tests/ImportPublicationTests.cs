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
        ImportPublicationSource source = Assert.Single(first.Manifest.Sources);
        Assert.Equal("arena2/MAPS.BSA", source.SourcePath);
        Assert.Equal(4, source.ByteLen);
        Assert.Contains("\"sourcePath\"", Encoding.UTF8.GetString(ImportPublicationManifestSerializer.Serialize(first.Manifest)), StringComparison.Ordinal);
        Assert.Equal(4, first.Manifest.Artifacts.Single(artifact => artifact.RelativePath == "zeta.bin").ByteLen);
        Assert.Contains("import-manifest.json", first.Artifacts.Select(artifact => artifact.RelativePath));
    }

    [Fact]
    public void PlanCarriesValidatedArtifactDependenciesAlongsideExactBytes()
    {
        ImportPublicationPlan plan = CreatePlan(
            new ImportPublicationArtifact("spatial/static-mesh.json", "mesh"u8),
            new ImportPublicationArtifact("spatial/collision-navigation.json", "spatial"u8, ["spatial/static-mesh.json"]),
            new ImportPublicationArtifact("normalized.json", "normalized"u8, ["spatial/collision-navigation.json", "spatial/static-mesh.json"]));

        ImportPublicationManifestArtifact spatial = plan.Manifest.Artifacts.Single(artifact => artifact.RelativePath == "spatial/collision-navigation.json");
        Assert.Equal(["spatial/static-mesh.json"], spatial.DependsOnPaths);
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
            ImportProvenance.CurrentSchemaVersion,
            "daggerfall-import/test",
            1,
            [new LogicalSourceRecord(LogicalSourceRecord.CurrentSchemaVersion, "arena2/MAPS.BSA", new ContentDigest("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"), 4, 1)]),
        artifacts);
}
