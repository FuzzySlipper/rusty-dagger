using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class SourceCoverageInputTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "source-coverage-" + Guid.NewGuid().ToString("N"));
    public SourceCoverageInputTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void Actual_producer_shapes_link_books_original_quests_and_flat_corpus_receipts()
    {
        string pack = Write("content/pack.json", """
            {"books":{"source":{"path":"arena2/books"},"books":[
             {"fileName":"BOK00000.TXT","disposition":"read"},{"fileName":"BOK00001.TXT","disposition":"notSupplied"}]}}
            """);
        string original = Write("import-records/original.json", """
            {"questOriginalSources":{"quests":[{"stem":"Q","selection":"rewrittenText","availability":"not binary execution",
              "binaryPath":"Q.QBN","resourcePath":"Q.QRC","rewrittenSourceFile":"Q.txt"}]}}
            """);
        string corpus = Write("content/quests.json", """
            {"catalogSource":"donor/list","quests":[{"name":"Q","sourceFingerprint":"abc"}]}
            """);
        var inputs = SourceCoverageInputReader.Read(root, [pack, original, corpus],
            [Row("family", "CNT-015", "arena2/books"), Row("file", "CNT-015", "arena2/books/BOK00000.TXT"), Row("file", "CNT-017", "arena2/Q.QBN")]);
        Assert.DoesNotContain(inputs.Consumers, consumer => consumer.SourcePath.EndsWith("BOK00001.TXT", StringComparison.Ordinal));
        Assert.Contains(inputs.Consumers, consumer => consumer.SourcePath.EndsWith("BOK00000.TXT", StringComparison.Ordinal) && !consumer.ContextOnly);
        Assert.Contains(inputs.Consumers, consumer => consumer.SourcePath == "arena2/Q.QBN" && consumer.OfflineOnly);
        Assert.DoesNotContain(inputs.Consumers, consumer => consumer.SourcePath == "arena2/Q.QRC");
        Assert.Equal("content/quests.json", Assert.Single(Assert.Single(inputs.Quests).CorpusArtifacts));
    }

    [Fact]
    public void Snapshot_reports_cannot_turn_their_own_inventory_into_consumer_evidence()
    {
        string file = Write("import-records/snapshot.json", """
            {"families":[],"records":[{"sourcePath":"arena2/A.BSA"}]}
            """);
        Assert.Empty(SourceCoverageInputReader.Read(root, [file], [Row("file", "CNT-001", "arena2/A.BSA")]).Consumers);
    }

    [Fact]
    public void Archive_ledgers_and_unsupported_media_keep_their_actual_dispositions()
    {
        string ledger = Write("import-records/geometry.json", """
            {"geometry":{"records":[{"source":"arena2/ARCH3D.BSA","ordinal":42,"state":"read","disposition":"unused"}]}}
            """);
        string media = Write("content/media.json", """
            {"unreadableFamilies":[{"family":".CEL","files":["MAGE.CEL"],"reason":"donor unsupported"}]}
            """);
        var inputs = SourceCoverageInputReader.Read(root, [ledger, media], [Row("file", "CNT-006", "arena2/ARCH3D.BSA")]);
        Assert.Contains(inputs.Consumers, consumer => consumer.ArchiveOrdinal == 42 && consumer.LedgerDisposition == "unused" && consumer.OfflineOnly);
        Assert.Equal("donor unsupported", Assert.Single(inputs.Unsupported).Reason);
    }

    [Fact]
    public void Donor_family_citations_remain_visible_and_aggregate_citations_stay_context()
    {
        string file = Write("content/catalog.json", """
            {"sources":["daggerfall-unity/RaceTemplate.cs","arena2/*.FLC"]}
            """);
        var inputs = SourceCoverageInputReader.Read(root, [file],
            [Row("family", "CNT-009", "daggerfall-unity/RaceTemplate.cs"), Row("family", "CNT-026", "arena2/*.FLC"), Row("file", "CNT-026", "arena2/A.FLC")]);
        Assert.Contains(inputs.Consumers, consumer => consumer.SourcePath == "daggerfall-unity/RaceTemplate.cs");
        Assert.True(Assert.Single(inputs.Consumers, consumer => consumer.SourcePath == "arena2/A.FLC").ContextOnly);
    }

    [Fact]
    public void Character_and_site_media_sourceFile_leaves_resolve_without_claiming_donor_quest_text()
    {
        string character = Write("content/character.json", """
            {"artifacts":[{"sourceFile":"BODY10I1.IMG"},{"sourceFile":"FACES.CIF"}]}
            """);
        string site = Write("content/site.json", """
            {"artifacts":[{"sourceFile":"TALK00I0.IMG"},{"sourceFile":"Q.txt"}]}
            """);
        var inputs = SourceCoverageInputReader.Read(root, [character, site],
            [Row("file", "CNT-021", "arena2/BODY10I1.IMG"), Row("file", "CNT-021", "arena2/FACES.CIF"), Row("file", "CNT-020", "arena2/TALK00I0.IMG")]);
        Assert.Equal(new[] { "arena2/BODY10I1.IMG", "arena2/FACES.CIF", "arena2/TALK00I0.IMG" }, inputs.Consumers.Select(consumer => consumer.SourcePath));
        Assert.All(inputs.Consumers, consumer => Assert.False(consumer.ContextOnly));
    }

    [Fact]
    public void Bare_sourceFile_cannot_guess_between_colliding_admitted_paths()
    {
        string file = Write("content/a.json", """{"sourceFile":"A.IMG"}""");
        Assert.Throws<InvalidOperationException>(() => SourceCoverageInputReader.Read(root, [file],
            [Row("file", "CNT-021", "arena2/A.IMG"), Row("file", "CNT-021", "arena2/nested/A.IMG")]));
    }

    private string Write(string path, string content)
    {
        string file = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, content); return file;
    }
    private static SourceInventoryRow Row(string type, string family, string path) => new(family + "." + type + "." + path, type, family, "source", path, "", "pending", "test");
}
