using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class SourceCoverageTests
{
    [Fact]
    public void Archive_file_citation_does_not_promote_its_individual_records()
    {
        var report = SourceCoverageReconciler.Build(Manifest(File(), Child()), [Consumer()]);
        Assert.True(report.IsReconciled);
        Assert.Equal(SourceRecordDisposition.Imported, report.Records[0].Disposition);
        Assert.Equal(SourceRecordDisposition.RequiredPending, report.Records[1].Disposition);
        Assert.False(report.Records[1].HasRecordEvidence);
        Assert.Single(report.Records[1].ArchiveContext);
        Assert.False(report.RuntimeParityCertified);
    }

    [Theory]
    [InlineData("unused", SourceRecordDisposition.Unused)]
    [InlineData("duplicate", SourceRecordDisposition.Duplicate)]
    [InlineData("malformed", SourceRecordDisposition.Malformed)]
    [InlineData("referenced", SourceRecordDisposition.RequiredPending)]
    [InlineData("summarized", SourceRecordDisposition.RequiredPending)]
    [InlineData("donorUnsupported", SourceRecordDisposition.RequiredPending)]
    public void Existing_ledger_decisions_are_preserved_without_runtime_claims(string decision, SourceRecordDisposition expected)
    {
        var report = SourceCoverageReconciler.Build(Manifest(File(), Child()),
            [Consumer(), Consumer() with { ArchiveOrdinal = 0, LedgerDisposition = decision, OfflineOnly = true }]);
        Assert.True(report.IsReconciled);
        Assert.Equal(expected, report.Records[1].Disposition);
        Assert.Contains(decision, report.Records[1].Detail);
    }

    [Fact]
    public void Required_record_without_any_consumer_fails_reconciliation()
    {
        var report = SourceCoverageReconciler.Build(Manifest(Child()), []);
        Assert.False(report.IsReconciled);
        Assert.Contains(report.Errors, error => error.Contains("no consuming artifact"));
    }

    [Fact]
    public void Unknown_source_and_disagreeing_ledgers_fail_reconciliation()
    {
        var report = SourceCoverageReconciler.Build(Manifest(File(), Child()),
            [Consumer(), Consumer() with { ArchiveOrdinal = 0, LedgerDisposition = "unused" },
             Consumer() with { ArchiveOrdinal = 0, LedgerDisposition = "referenced" },
             Consumer() with { SourcePath = "arena2/MISSING.BSA" }]);
        Assert.Contains(report.Errors, error => error.Contains("disagree"));
        Assert.Contains(report.Errors, error => error.Contains("absent from"));
    }

    [Fact]
    public void MIDI_is_explicitly_excluded_and_rejects_a_consuming_artifact()
    {
        var midi = File() with { Id = "CNT-028.file.MIDI", FamilyId = "CNT-028", FamilyPath = "arena2/MIDI.BSA", SourcePath = "arena2/MIDI.BSA", Disposition = SourceRecordDisposition.Excluded };
        Assert.True(SourceCoverageReconciler.Build(Manifest(midi), []).IsReconciled);
        Assert.False(SourceCoverageReconciler.Build(Manifest(midi), [Consumer() with { SourcePath = midi.SourcePath }]).IsReconciled);
    }

    [Fact]
    public void Offline_evidence_and_aggregate_context_remain_pending()
    {
        foreach (var consumer in new[] { Consumer() with { OfflineOnly = true }, Consumer() with { ContextOnly = true } })
        {
            var report = SourceCoverageReconciler.Build(Manifest(File()), [consumer]);
            Assert.Equal(SourceRecordDisposition.RequiredPending, Assert.Single(report.Records).Disposition);
        }
    }

    [Fact]
    public void Input_order_does_not_change_report_bytes()
    {
        var a = Consumer(); var b = a with { Artifact = "content/b.json" };
        var first = SourceCoverageReconciler.Build(Manifest(File(), Child()), [a, b]);
        var second = SourceCoverageReconciler.Build(Manifest(Child(), File()), [b, a, a]);
        Assert.Equal(SourceCoverageReconciler.Serialize(first), SourceCoverageReconciler.Serialize(second));
    }

    [Fact]
    public void Source_gaps_remain_separate_and_undispositioned_discovery_fails()
    {
        var gap = File() with { Disposition = SourceRecordDisposition.SourceGap, ByteLength = 0, Digest = null };
        var report = SourceCoverageReconciler.Build(Manifest(gap), []);
        Assert.True(report.IsReconciled);
        Assert.Equal(1, Assert.Single(report.Families).SourceGap);
        Assert.False(SourceCoverageReconciler.Build(Manifest(File() with { Disposition = SourceRecordDisposition.Unresolved }), []).IsReconciled);
        Assert.Throws<InvalidOperationException>(() => SourceCoverageReconciler.Build(Manifest(File() with { Disposition = SourceRecordDisposition.None }), []));
    }

    [Fact]
    public void Donor_only_families_receive_citations_without_fake_Arena2_records()
    {
        var inventory = new[] { new SourceInventoryRow("CNT-009", "family", "CNT-009", "race", "daggerfall-unity/RaceTemplate.cs", "8", "pending", "donor") };
        var manifest = new SourceManifest("arena2", "data/inventory.csv", [], [SourceManifestFamilyCount.From("CNT-009", "pending", [])]);
        var report = SourceCoverageReconciler.Build(manifest, [new("content/catalog.json", "races", inventory[0].PathOrPattern)], inventory: inventory);
        Assert.True(report.IsReconciled);
        Assert.Empty(report.Records);
        Assert.Single(Assert.Single(report.FamilyEvidence).Consumers);
    }

    private static SourceCoverageConsumer Consumer() => new("content/a.json", "world", "arena2/A.BSA");
    private static SourceManifestRecord File() => new("CNT-001.file.A", "CNT-001", "arena2/A.BSA", "arena2/A.BSA", 1, ContentDigest.Compute("a"u8), null, null, SourceRecordDisposition.Imported, "published source");
    private static SourceManifestRecord Child() => File() with { Id = "CNT-001.file.A.record.0000", ArchiveOrdinal = 0, ArchiveKey = "child", Disposition = SourceRecordDisposition.RequiredPending };
    private static SourceManifest Manifest(params SourceManifestRecord[] records) => new("arena2", "data/inventory.csv", records,
        [.. records.GroupBy(record => record.FamilyId).Select(group => SourceManifestFamilyCount.From(group.Key, "pending", group))]);
}
