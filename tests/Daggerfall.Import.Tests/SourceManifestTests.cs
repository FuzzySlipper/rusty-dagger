using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class SourceManifestTests : IDisposable
{
    private const string Header = "id,row_type,family_id,kind,path_or_pattern,available_count,byte_size,record_or_stem,current_scope,disposition,notes";

    private readonly string root = Path.Combine(Path.GetTempPath(), $"daggerfall-source-manifest-{Guid.NewGuid():N}");

    public SourceManifestTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_supplied_tree_and_its_inventory_reconcile_into_one_disposition_per_record()
    {
        Write("A.CIF", "alpha"u8);
        Write("B.CIF", "bravo"u8);
        Write("EXTRA.CIF", "undocumented"u8);
        Directory.CreateDirectory(Path.Combine(root, "books"));
        string inventory = Inventory(
            "CNT-001,family,CNT-001,cif,local/arena2/A.CIF,1,,A,scope,current-structural,note",
            "CNT-002,family,CNT-002,cif,local/arena2/B.CIF,1,,B,scope,current-structural,note",
            "CNT-001.file.A.CIF,file,CNT-001,source-file,local/arena2/A.CIF,1,5,A,scope,uninspected,note",
            "CNT-001.file.B.CIF,file,CNT-001,source-file,local/arena2/B.CIF,1,5,B,scope,uninspected,note",
            "CNT-001.file.GONE.CIF,file,CNT-001,source-file,local/arena2/GONE.CIF,1,5,GONE,scope,uninspected,note");

        SourceManifest manifest = SourceManifestBuilder.Build(
            new SourceManifestRequest("local/arena2", "docs/coverage/content-source-manifest.csv", root, ["A.CIF"], ["B.CIF"], []),
            Encoding.UTF8.GetBytes(inventory));

        Assert.Equal(SourceRecordDisposition.Imported, Record(manifest, "CNT-001.file.A.CIF").Disposition);
        Assert.Equal(SourceRecordDisposition.RequiredPending, Record(manifest, "CNT-001.file.B.CIF").Disposition);
        Assert.Equal(SourceRecordDisposition.SourceGap, Record(manifest, "CNT-001.file.GONE.CIF").Disposition);
        Assert.Null(Record(manifest, "CNT-001.file.GONE.CIF").Digest);
        // Supplied without a documented row: reported, not skipped.
        Assert.Equal(SourceRecordDisposition.Unresolved, Record(manifest, "scan.EXTRA.CIF").Disposition);
        // A supplied directory is a non-record, not a silent omission.
        Assert.Equal(SourceRecordDisposition.Excluded, Record(manifest, "scan.books").Disposition);
        Assert.Equal(5, manifest.Records.Count);
        Assert.All(manifest.Records, record => Assert.NotEqual(SourceRecordDisposition.None, record.Disposition));
    }

    [Fact]
    public void Family_counts_reconcile_with_the_records_they_count()
    {
        Write("A.CIF", "alpha"u8);
        string inventory = Inventory(
            "CNT-001,family,CNT-001,cif,local/arena2/A.CIF,1,,A,scope,current-structural,note",
            "CNT-009,family,CNT-009,races,donor:Assets/Scripts/Game/Entities/RaceTemplate.cs,0,,Race,scope,current-structural,note",
            "CNT-001.file.A.CIF,file,CNT-001,source-file,local/arena2/A.CIF,1,5,A,scope,uninspected,note",
            "CNT-001.file.GONE.CIF,file,CNT-001,source-file,local/arena2/GONE.CIF,1,5,GONE,scope,uninspected,note");

        SourceManifest manifest = SourceManifestBuilder.Build(
            new SourceManifestRequest("local/arena2", "docs/coverage/content-source-manifest.csv", root, ["A.CIF"], [], []),
            Encoding.UTF8.GetBytes(inventory));

        // A documented family the local tree supplies nothing for is still counted, so
        // "no records" is visible rather than absent.
        SourceManifestFamilyCount donor = Assert.Single(manifest.Families, family => family.FamilyId == "CNT-009");
        Assert.Equal(0, donor.Discovered);
        SourceManifestFamilyCount supplied = Assert.Single(manifest.Families, family => family.FamilyId == "CNT-001");
        Assert.Equal((2, 1, 0, 0, 0, 1), (supplied.Discovered, supplied.Imported, supplied.Unused, supplied.Duplicate, supplied.Excluded, supplied.SourceGap));
        manifest.Validate();
    }

    [Fact]
    public void A_repeated_scan_serializes_to_identical_bytes_and_ordering()
    {
        Write("B.CIF", "bravo"u8);
        Write("A.CIF", "alpha"u8);
        string inventory = Inventory(
            "CNT-001,family,CNT-001,cif,local/arena2/A.CIF,1,,A,scope,current-structural,note",
            "CNT-001.file.B.CIF,file,CNT-001,source-file,local/arena2/B.CIF,1,5,B,scope,uninspected,note",
            "CNT-001.file.A.CIF,file,CNT-001,source-file,local/arena2/A.CIF,1,5,A,scope,uninspected,note");
        SourceManifestRequest request = new("local/arena2", "docs/coverage/content-source-manifest.csv", root, ["B.CIF"], [], []);

        byte[] first = SourceManifestSerializer.Serialize(SourceManifestBuilder.Build(request, Encoding.UTF8.GetBytes(inventory)));
        byte[] second = SourceManifestSerializer.Serialize(SourceManifestBuilder.Build(request, Encoding.UTF8.GetBytes(inventory)));

        Assert.Equal(first, second);
        SourceManifest read = SourceManifestSerializer.Deserialize(first);
        Assert.Equal(["CNT-001.file.A.CIF", "CNT-001.file.B.CIF"], read.Records.Select(record => record.Id));
    }

    [Fact]
    public void A_casing_difference_keeps_the_casing_the_source_tree_actually_uses()
    {
        Write("Mixed.CIF", "alpha"u8);
        string inventory = Inventory(
            "CNT-001,family,CNT-001,cif,local/arena2/MIXED.CIF,1,,MIXED,scope,current-structural,note",
            "CNT-001.file.MIXED.CIF,file,CNT-001,source-file,local/arena2/MIXED.CIF,1,5,MIXED,scope,uninspected,note");

        SourceManifest manifest = SourceManifestBuilder.Build(
            new SourceManifestRequest("local/arena2", "docs/coverage/content-source-manifest.csv", root, [], [], []),
            Encoding.UTF8.GetBytes(inventory));

        SourceManifestRecord record = Record(manifest, "CNT-001.file.MIXED.CIF");
        Assert.Equal("local/arena2/Mixed.CIF", record.SourcePath);
        Assert.Contains("supplied as 'Mixed.CIF'", record.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Decoded_archive_records_carry_their_archive_identity()
    {
        byte[] archiveBytes = CreateNamedBsa(("FIRST.TXT", "one"u8.ToArray()), ("SECOND.TXT", "two"u8.ToArray()));
        Write("ARCHIVE.BSA", archiveBytes);
        string inventory = Inventory(
            "CNT-005,family,CNT-005,blocks,local/arena2/ARCHIVE.BSA,1,,A,scope,current-structural,note",
            "CNT-005.file.ARCHIVE.BSA,file,CNT-005,source-file,local/arena2/ARCHIVE.BSA,1,5,ARCHIVE,scope,uninspected,note");
        SourceManifest manifest = SourceManifestBuilder.Build(
            new SourceManifestRequest("local/arena2", "docs/coverage/content-source-manifest.csv", root, ["ARCHIVE.BSA"], [], []),
            Encoding.UTF8.GetBytes(inventory));
        SourceManifestRecord archive = Record(manifest, "CNT-005.file.ARCHIVE.BSA");

        IReadOnlyList<SourceManifestRecord> decoded = SourceManifestBuilder.DecodeArchiveRecords(archive, archiveBytes).Records;

        Assert.Equal(2, decoded.Count);
        Assert.Equal(["CNT-005.file.ARCHIVE.BSA.record.0000", "CNT-005.file.ARCHIVE.BSA.record.0001"], decoded.Select(record => record.Id));
        Assert.Equal(["FIRST.TXT", "SECOND.TXT"], decoded.Select(record => record.ArchiveKey));
        Assert.Equal([0, 1], decoded.Select(record => record.ArchiveOrdinal));
        Assert.All(decoded, record =>
        {
            Assert.Equal(SourceRecordDisposition.RequiredPending, record.Disposition);
            Assert.Equal("local/arena2/ARCHIVE.BSA", record.SourcePath);
            Assert.NotNull(record.Digest);
        });
        Assert.Equal(3L, decoded[0].ByteLength);
    }

    [Fact]
    public void A_manifest_that_does_not_add_up_is_rejected()
    {
        SourceManifestRecord good = Record("CNT-001.file.A.CIF");
        SourceManifestRecord sameIdentity = good with { Id = "CNT-001.file.A2.CIF" };
        SourceManifestFamilyCount counted = SourceManifestFamilyCount.From("CNT-001", [good]);

        // The same source record cannot appear under two identities.
        Assert.Throws<InvalidOperationException>(() => new SourceManifest(
            1, "local/arena2", "inventory.csv", [good, sameIdentity], [SourceManifestFamilyCount.From("CNT-001", [good, sameIdentity])]).Validate());
        // Family counts must reconcile with the records they count.
        Assert.Throws<InvalidOperationException>(() => new SourceManifest(
            1, "local/arena2", "inventory.csv", [good], [counted with { Discovered = 2 }]).Validate());
        // A record cannot belong to a family the manifest does not count.
        Assert.Throws<InvalidOperationException>(() => new SourceManifest(
            1, "local/arena2", "inventory.csv", [good], [SourceManifestFamilyCount.From("CNT-002", [])]).Validate());
        // Traversal, absolute paths and an unsupported schema version are refused.
        Assert.Throws<ArgumentException>(() => new SourceManifest(
            1, "local/arena2", "../escape.csv", [good], [counted]).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceManifest(
            2, "local/arena2", "inventory.csv", [good], [counted]).Validate());
    }

    [Fact]
    public void The_reader_rejects_a_manifest_that_the_writer_would_never_emit()
    {
        // The writer validates before serializing, so a hand-written manifest is the
        // only way to prove the reader refuses one it did not produce.
        string json = """
        {
          "schemaVersion": 1,
          "sourceRoot": "local/arena2",
          "inventoryPath": "inventory.csv",
          "records": [
            { "id": "CNT-001.file.A.CIF", "familyId": "CNT-001", "familyPath": "local/arena2/A.CIF", "sourcePath": "local/arena2/A.CIF", "byteLength": 5, "digest": "d970ca90f9d4b4ad1e0b3e5b3c70e0dc0d1d0d2b1d0dcbc9d3a2c6a2a5f9a6a6", "disposition": "imported", "note": "note" },
            { "id": "CNT-001.file.A.CIF", "familyId": "CNT-001", "familyPath": "local/arena2/A.CIF", "sourcePath": "local/arena2/A.CIF", "byteLength": 5, "digest": "d970ca90f9d4b4ad1e0b3e5b3c70e0dc0d1d0d2b1d0dcbc9d3a2c6a2a5f9a6a6", "disposition": "imported", "note": "note" }
          ],
          "families": [ { "familyId": "CNT-001", "discovered": 2, "imported": 2, "requiredPending": 0, "unused": 0, "duplicate": 0, "excluded": 0, "malformed": 0, "unresolved": 0, "sourceGap": 0 } ]
        }
        """;

        Assert.Throws<InvalidOperationException>(() => SourceManifestSerializer.Deserialize(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void An_undispositioned_or_malformed_record_is_rejected()
    {
        SourceManifestRecord baseRecord = new(
            "CNT-001.file.A.CIF", "CNT-001", "local/arena2/A.CIF", "local/arena2/A.CIF", 5,
            ContentDigest.Compute("alpha"u8), null, null, SourceRecordDisposition.Imported, "note");

        // An undispositioned record cannot hide behind the enum default.
        SourceManifestRecord undispositioned = baseRecord with { Disposition = SourceRecordDisposition.None };
        Assert.Throws<InvalidOperationException>(() => undispositioned.Validate());
        // A supplied record states the bytes it stands for.
        Assert.Throws<InvalidOperationException>(() => (baseRecord with { Digest = null }).Validate());
        // A gap record has no local bytes to state.
        SourceManifestRecord gap = baseRecord with { Disposition = SourceRecordDisposition.SourceGap, Digest = null, ByteLength = 0 };
        gap.Validate();
        // Traversal, absolute paths and dot segments are refused where paths are validated.
        Assert.Throws<ArgumentException>(() => (baseRecord with { SourcePath = "../escape" }).Validate());
        Assert.Throws<ArgumentException>(() => (baseRecord with { SourcePath = "/absolute" }).Validate());
        Assert.Throws<ArgumentException>(() => (baseRecord with { SourcePath = "a/./b" }).Validate());
        Assert.Throws<InvalidOperationException>(() => (baseRecord with { Disposition = (SourceRecordDisposition)99 }).Validate());
    }

    [Fact]
    public void An_inventory_row_that_cannot_be_interpreted_is_rejected_with_what_was_seen()
    {
        Assert.Throws<InvalidOperationException>(() => SourceManifestBuilder.ReadInventory(Encoding.UTF8.GetBytes("a,b,c\n")));
        Assert.Throws<InvalidOperationException>(() => SourceManifestBuilder.ReadInventory(Encoding.UTF8.GetBytes($"{Header}\nCNT-001,family,CNT-001\n")));
        Assert.Throws<InvalidOperationException>(() => SourceManifestBuilder.ReadInventory(Encoding.UTF8.GetBytes($"{Header}\nCNT-001,unknown,CNT-001,k,p,1,,r,s,d,n\n")));
        Assert.Single(SourceManifestBuilder.ReadInventory(Encoding.UTF8.GetBytes($"{Header}\nCNT-001,family,CNT-001,k,p,1,,r,s,d,n\n")));
    }

    [Fact]
    public void Inventory_reconciliation_reports_drift_and_updates_only_the_disposition_column()
    {
        string inventoryFile = Path.Combine(root, "inventory.csv");
        // CRLF on purpose: the rewrite must keep the terminator the file already uses.
        File.WriteAllText(inventoryFile, (Inventory(
            "CNT-001,family,CNT-001,cif,local/arena2/A.CIF,1,,A,scope,current-structural,keep me",
            "CNT-001.file.A.CIF,file,CNT-001,source-file,local/arena2/A.CIF,1,5,A,scope,uninspected,a note without commas",
            "CNT-001.file.GONE.CIF,file,CNT-001,source-file,local/arena2/GONE.CIF,1,5,GONE,scope,uninspected,note") + "\n").Replace("\n", "\r\n"));
        SourceManifestRecord supplied = Record("CNT-001.file.A.CIF");
        SourceManifestRecord gap = Record("CNT-001.file.GONE.CIF") with { Disposition = SourceRecordDisposition.SourceGap, Digest = null, ByteLength = 0 };

        SourceInventoryReconciliation before = SourceInventoryReconciler.Reconcile(inventoryFile, [supplied, gap], update: false);

        Assert.Equal(2, before.Drift.Count);
        Assert.False(before.IsClean);
        Assert.Contains("uninspected", File.ReadAllText(inventoryFile), StringComparison.Ordinal);
        SourceInventoryReconciliation updated = SourceInventoryReconciler.Reconcile(inventoryFile, [supplied, gap], update: true);
        Assert.Equal(before.Drift, updated.Drift);
        string text = File.ReadAllText(inventoryFile);
        Assert.Contains("\r\n", text, StringComparison.Ordinal);
        Assert.Contains("scope,imported,a note without commas", text, StringComparison.Ordinal);
        Assert.Contains("scope,source-gap,note", text, StringComparison.Ordinal);
        // Every other field survives untouched, including a family row's free text.
        Assert.Contains("current-structural,keep me", text, StringComparison.Ordinal);
        Assert.True(SourceInventoryReconciler.Reconcile(inventoryFile, [supplied, gap], update: false).IsClean);
    }

    [Fact]
    public void A_documented_file_in_a_subdirectory_is_supplied_not_a_gap()
    {
        Directory.CreateDirectory(Path.Combine(root, "books"));
        Write(Path.Combine("books", "BOK00000.TXT"), "a book"u8);
        string inventory = Inventory(
            "CNT-015,family,CNT-015,books,local/arena2/books,90,,BOK,scope,current-structural,note",
            "CNT-015.file.books/BOK00000.TXT,file,CNT-015,source-file,local/arena2/books/BOK00000.TXT,1,6,BOK,scope,uninspected,note");

        SourceManifest manifest = SourceManifestBuilder.Scan(
            new SourceManifestRequest("local/arena2", "inventory.csv", root, [], [], []),
            Encoding.UTF8.GetBytes(inventory));

        SourceManifestRecord record = Record(manifest, "CNT-015.file.books/BOK00000.TXT");
        // The documented path is relative to the source root, so a family that lives in
        // a subdirectory is supplied; reporting it as a gap would assert the opposite.
        Assert.Equal(SourceRecordDisposition.Unused, record.Disposition);
        Assert.Equal("local/arena2/books/BOK00000.TXT", record.SourcePath);
        Assert.NotNull(record.Digest);
        Assert.Equal(6L, record.ByteLength);
    }

    [Fact]
    public void An_archive_that_cannot_be_read_is_malformed_rather_than_empty()
    {
        Write("BROKEN.BSA", "not an archive at all"u8);
        string inventory = Inventory(
            "CNT-005,family,CNT-005,blocks,local/arena2/BROKEN.BSA,1,,BROKEN,scope,current-structural,note",
            "CNT-005.file.BROKEN.BSA,file,CNT-005,source-file,local/arena2/BROKEN.BSA,1,5,BROKEN,scope,uninspected,note");

        SourceManifest manifest = SourceManifestBuilder.Scan(
            new SourceManifestRequest("local/arena2", "inventory.csv", root, [], [], []),
            Encoding.UTF8.GetBytes(inventory));

        // An unreadable archive must not look like an archive with no records.
        SourceManifestRecord record = Record(manifest, "CNT-005.file.BROKEN.BSA");
        Assert.Equal(SourceRecordDisposition.Malformed, record.Disposition);
        Assert.Contains("BROKEN.BSA", record.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_documented_row_the_scan_never_resolved_is_reported_not_skipped()
    {
        string inventoryFile = Path.Combine(root, "inventory.csv");
        File.WriteAllText(inventoryFile, Inventory(
            "CNT-001,family,CNT-001,cif,local/arena2/A.CIF,1,,A,scope,current-structural,note",
            "CNT-001.file.A.CIF,file,CNT-001,source-file,local/arena2/A.CIF,1,5,A,scope,imported,note",
            "CNT-001.file.B.CIF,file,CNT-001,source-file,local/arena2/B.CIF,1,5,B,scope,uninspected,note"));

        // The scan resolved only A, so B's row must be reported as unresolved rather
        // than quietly counted as agreeing.
        SourceInventoryReconciliation reconciliation = SourceInventoryReconciler.Reconcile(
            inventoryFile, [Record("CNT-001.file.A.CIF")], update: false);

        Assert.Empty(reconciliation.Drift);
        Assert.Contains(reconciliation.Unreconciled, line => line.Contains("CNT-001.file.B.CIF", StringComparison.Ordinal));
        Assert.False(reconciliation.IsClean);
    }

    [Fact]
    public void A_malformed_manifest_read_fails_as_a_format_error()
    {
        Assert.Throws<FormatException>(() => SourceManifestSerializer.Deserialize("not json"u8));
        Assert.Throws<FormatException>(() => SourceManifestSerializer.Deserialize("[]"u8));
    }

    private static SourceManifestRecord Record(string id) => new(
        id, "CNT-001", "local/arena2/A.CIF", "local/arena2/A.CIF", 5,
        ContentDigest.Compute("alpha"u8), null, null, SourceRecordDisposition.Imported, "note");

    private SourceManifestRecord Record(SourceManifest manifest, string id) =>
        Assert.Single(manifest.Records, record => StringComparer.Ordinal.Equals(record.Id, id));

    private void Write(string name, ReadOnlySpan<byte> bytes) => File.WriteAllBytes(Path.Combine(root, name), bytes.ToArray());

    private static string Inventory(params string[] rows) => Header + "\n" + string.Join('\n', rows) + "\n";

    private static byte[] CreateNamedBsa(params (string Name, byte[] Payload)[] records)
    {
        List<byte> result = [];
        result.AddRange(BitConverter.GetBytes((short)records.Length));
        result.AddRange(BitConverter.GetBytes(Arena2FormatConstants.NamedBsaDirectoryType));
        foreach ((_, byte[] payload) in records)
        {
            result.AddRange(payload);
        }

        foreach ((string name, byte[] payload) in records)
        {
            result.AddRange(Encoding.ASCII.GetBytes(name));
            result.AddRange(new byte[14 - name.Length]);
            result.AddRange(BitConverter.GetBytes(payload.Length));
        }

        return result.ToArray();
    }
}
