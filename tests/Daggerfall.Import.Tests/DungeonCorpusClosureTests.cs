using Daggerfall.Import.Normalization;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>Whole-corpus closure proof for the published MAPS, BLOCKS, ARCH3D and texture sources.</summary>
public sealed class DungeonCorpusClosureTests
{
    [Fact]
    public void Reads_every_region_and_reachable_geometry_texture_link_in_the_local_corpus()
    {
        DungeonCorpusClosureReport report = DungeonCorpusClosureBuilder.Build(new(BuildSources()));

        Assert.Equal(62, report.RegionCount);
        Assert.Equal(15_251, report.LocationCount);
        Assert.Equal(3_959, report.DungeonLocationCount);
        Assert.Equal(15_251, report.ExteriorLocationCount);
        Assert.Equal(658, report.ReferencedRmbCount);
        Assert.Equal(179, report.ReferencedRdbCount);

        // Every reachable block, mesh and texture link closes against the supplied source archives.
        Assert.DoesNotContain(report.Entries, entry =>
            (entry.Kind is DungeonCorpusClosureKind.ExteriorBlockLink
                or DungeonCorpusClosureKind.DungeonBlockLink
                or DungeonCorpusClosureKind.MeshLink
                or DungeonCorpusClosureKind.TextureLink)
            && (entry.Disposition is DungeonCorpusClosureDisposition.Unresolved or DungeonCorpusClosureDisposition.Malformed));
        Assert.DoesNotContain(report.Entries, entry => entry.Kind == DungeonCorpusClosureKind.Mesh
            && entry.Disposition == DungeonCorpusClosureDisposition.Malformed);

        // These two high-bit RMB names exercise the byte-wrapped second-letter resolver used by DFU.
        Assert.Equal(DungeonCorpusClosureDisposition.Valid, Entry(report, "maps/region/1/location/669/exterior").Disposition);
        Assert.Equal(DungeonCorpusClosureDisposition.Valid, Entry(report, "maps/region/33/location/198/exterior").Disposition);

        // MAPNAMES repeats names in a region; closure must retain the index identity instead of
        // resolving the later Old Elara's Shack through the first matching name.
        Assert.Contains("location index 196", Entry(report, "maps/region/17/location/196/exterior").Reason, StringComparison.Ordinal);

        Assert.Equal(14, report.Count(DungeonCorpusClosureDisposition.Duplicate));
        Assert.Equal(3, report.Entries.Count(entry => entry.Kind == DungeonCorpusClosureKind.TextureLeaf
            && entry.Disposition == DungeonCorpusClosureDisposition.Malformed));
        Assert.Equal(40, report.Entries.Count(entry => entry.Kind == DungeonCorpusClosureKind.TextureLeaf
            && entry.Disposition == DungeonCorpusClosureDisposition.Unresolved));
        Assert.Equal(43, report.Entries.Count(entry => entry.Kind == DungeonCorpusClosureKind.ActionLink
            && entry.Disposition == DungeonCorpusClosureDisposition.Unresolved));

        string first = report.ToMarkdown();
        Assert.Contains("# Daggerfall world corpus closure", first, StringComparison.Ordinal);
        Assert.Contains("dungeon-corpus-closure.tsv", first, StringComparison.Ordinal);
        Assert.Equal(first, report.ToMarkdown());

        string manifest = report.ToTsv();
        Assert.Contains("maps/region/1/location/669/exterior", manifest, StringComparison.Ordinal);
        Assert.Equal(report.Entries.Count + 1, manifest.Count(character => character == '\n'));
    }

    private static DungeonLogicalSourceSet BuildSources()
    {
        string root = Path.Combine(RepositoryRoot(), "local/arena2");
        IEnumerable<string> paths = Directory.EnumerateFiles(root)
            .Where(path => Path.GetFileName(path) is "MAPS.BSA" or "BLOCKS.BSA" or "ARCH3D.BSA" or "CLIMATE.PAK"
                || Path.GetFileName(path).StartsWith("TEXTURE.", StringComparison.OrdinalIgnoreCase));
        return new DungeonLogicalSourceSet(paths.Select(path => new DungeonLogicalSource(Path.GetFileName(path), File.ReadAllBytes(path))));
    }

    private static DungeonCorpusClosureEntry Entry(DungeonCorpusClosureReport report, string sourceId) =>
        Assert.Single(report.Entries, entry => entry.SourceId == sourceId);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
