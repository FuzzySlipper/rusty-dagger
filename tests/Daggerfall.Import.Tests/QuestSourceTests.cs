using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The classic quest source corpus: every supplied path with its exact casing, the stem
/// it pairs on, and the envelope facts each binary supports. Nothing here assigns quest
/// meaning; the payload semantics stay unclaimed.
/// </summary>
public sealed class QuestSourceTests
{
    [Fact]
    public void Enumerates_every_supplied_quest_source_with_its_exact_path()
    {
        QuestSourceInventory inventory = ReadInventory();

        Assert.Equal(609, inventory.Files.Count);
        Assert.Equal(306, inventory.Binaries.Count());
        Assert.Equal(303, inventory.Resources.Count());
        Assert.Equal(604, inventory.Files.Count(file => file.Pairing == QuestSourcePairing.Paired));
        // The path is the source identity, so it is stored exactly as supplied.
        Assert.All(inventory.Files, file => Assert.EndsWith(file.FileName, file.Path, StringComparison.Ordinal));
        Assert.Contains(inventory.Files, file => StringComparer.Ordinal.Equals(file.Path, "N0B20Y25.qrc"));
        Assert.DoesNotContain(inventory.Files, file => StringComparer.Ordinal.Equals(file.Path, "N0B20Y25.QRC"));
    }

    [Fact]
    public void Reports_the_four_binary_only_stems_and_the_one_resources_only_stem()
    {
        QuestSourceInventory inventory = ReadInventory();

        // The scope document names four stems whose companion resource file is not
        // supplied, and one resource file whose binary is not.
        Assert.Equal(
            ["80C00Y00.QBN", "A0C00Y04.QBN", "N0C00Y01.QBN", "R0C40Y23.QBN"],
            inventory.BinaryOnly.Select(file => file.Path).Order(StringComparer.Ordinal));
        Assert.Equal(["M0B40Y04.QRC"], inventory.ResourcesOnly.Select(file => file.Path));
    }

    [Fact]
    public void Pairs_by_case_insensitive_stem_without_rewriting_the_stored_path()
    {
        QuestSourceInventory inventory = ReadInventory();

        // Six companions are stored in lower case. Pairing must match them and must not
        // normalize the identity it matched: both halves keep the casing they were read
        // with, so a consumer citing either path cites what is on disk.
        Assert.True(inventory.TryGetByPath("N0B20Y25.qrc", out QuestSourceFile? lower));
        Assert.Equal("N0B20Y25.QBN", lower!.CompanionPath);
        Assert.Equal(QuestSourcePairing.Paired, lower.Pairing);
        Assert.True(inventory.TryGetByPath("N0B20Y25.QBN", out QuestSourceFile? upper));
        Assert.Equal("N0B20Y25.qrc", upper!.CompanionPath);
        Assert.Equal(6, inventory.Files.Count(file => file.Path.EndsWith(".qrc", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_documented_inventory_carries_exactly_the_supplied_quest_paths()
    {
        // The inventory is the authority for which source paths exist, so the corpus and
        // the document are checked against each other: a supplied file the inventory does
        // not carry, or a documented one that is not supplied, is drift either way.
        string[] supplied = [.. ReadInventory().Files.Select(file => file.Path)];
        HashSet<string> documented = [.. Daggerfall.Import.Publication.SourceManifestBuilder
            .ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")))
            .Where(row => row.RowType == "file" && StringComparer.Ordinal.Equals(row.FamilyId, "CNT-017"))
            .Select(row => Path.GetFileName(row.PathOrPattern))];

        Assert.Equal(609, documented.Count);
        Assert.DoesNotContain(supplied, path => !documented.Contains(path));
        Assert.DoesNotContain(documented, path => !supplied.Contains(path, StringComparer.Ordinal));
    }

    [Fact]
    public void Rejects_two_files_claiming_one_stem()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() =>
            QuestSourceInventory.Enumerate(["A.QBN", "a.qbn"], "fixture"));

        Assert.Contains("claim the same quest stem", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_path_in_neither_family()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() =>
            QuestSourceInventory.Enumerate(["A.TXT"], "fixture"));

        Assert.Contains("neither", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decodes_every_supplied_binary_envelope_without_claiming_its_payload()
    {
        string root = RepositoryRoot();
        QuestSourceInventory inventory = ReadInventory();

        int marked = 0;
        foreach (QuestSourceFile file in inventory.Binaries)
        {
            QuestBinaryEnvelope envelope = QuestBinaryEnvelope.Decode(File.ReadAllBytes(Path.Combine(root, "local/arena2", file.Path)), file.Path);
            Assert.Equal(QuestBinaryEnvelopeDisposition.WellFormed, envelope.Disposition);
            Assert.InRange(envelope.HeaderVariant, (byte)0, (byte)1);
            Assert.True(envelope.Length > QuestBinaryEnvelope.HeaderLength);
            if (envelope.HasTerminalMarker)
            {
                marked++;
            }
        }

        // 272 of the 306 carry the terminal marker; the other 34 end without one, which
        // the envelope reports as an observed variant rather than a defect.
        Assert.Equal(272, marked);
        Assert.Equal(34, inventory.Binaries.Count() - marked);
        Assert.Equal(294, CountVariant(inventory, 0));
        Assert.Equal(12, CountVariant(inventory, 1));
    }

    [Fact]
    public void Refuses_a_truncated_binary_envelope()
    {
        QuestBinaryEnvelope envelope = QuestBinaryEnvelope.Decode(new byte[QuestBinaryEnvelope.HeaderLength - 1], "fixture.QBN");

        Assert.Equal(QuestBinaryEnvelopeDisposition.Truncated, envelope.Disposition);
        Assert.Contains("envelope header", envelope.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_binary_envelope_whose_header_is_not_the_observed_shape()
    {
        byte[] bytes = new byte[64];
        bytes[7] = 0x20;

        QuestBinaryEnvelope envelope = QuestBinaryEnvelope.Decode(bytes, "fixture.QBN");

        Assert.Equal(QuestBinaryEnvelopeDisposition.Malformed, envelope.Disposition);
        Assert.Contains("byte 7", envelope.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_a_binary_without_the_terminal_marker_as_a_variant()
    {
        byte[] bytes = new byte[64];
        bytes[15] = 1;

        QuestBinaryEnvelope envelope = QuestBinaryEnvelope.Decode(bytes, "fixture.QBN");

        Assert.Equal(QuestBinaryEnvelopeDisposition.WellFormed, envelope.Disposition);
        Assert.False(envelope.HasTerminalMarker);
        Assert.Contains("no terminal marker", envelope.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Decodes_every_supplied_resource_envelope_into_raw_records()
    {
        string root = RepositoryRoot();
        QuestSourceInventory inventory = ReadInventory();
        int records = 0;
        foreach (QuestSourceFile file in inventory.Resources)
        {
            QuestResourceEnvelope envelope = QuestResourceEnvelope.Decode(File.ReadAllBytes(Path.Combine(root, "local/arena2", file.Path)), file.Path);
            Assert.Equal(QuestResourceEnvelopeDisposition.Decoded, envelope.Disposition);
            records += envelope.Records.Count;
            Assert.All(envelope.Records, record =>
            {
                Assert.Equal(record.Length, record.Payload.Length);
                Assert.NotEqual(QuestResourceEnvelope.SentinelId, record.Id);
            });
        }

        Assert.Equal(4588, records);
    }

    [Fact]
    public void Delivers_resource_text_as_bytes_without_interpreting_tokens()
    {
        string root = RepositoryRoot();
        QuestResourceEnvelope envelope = QuestResourceEnvelope.Decode(File.ReadAllBytes(Path.Combine(root, "local/arena2/S0000001.QRC")), "S0000001.QRC");

        QuestResourceRecord greeting = envelope.Records.Single(record => record.Id == 1000);
        string text = System.Text.Encoding.Latin1.GetString(greeting.Payload.Span);
        // The record is delivered whole, macros and separators included: a token's meaning
        // belongs to the quest worker, not to the envelope.
        Assert.StartsWith("Welcome to Sentinel, %pcf.", text, StringComparison.Ordinal);
        Assert.Contains("%oth", System.Text.Encoding.Latin1.GetString(envelope.Records.Single(record => record.Id == 1001).Payload.Span), StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_resource_envelope_whose_directory_runs_past_the_file()
    {
        byte[] bytes = new byte[8];
        bytes[0] = 60;

        QuestResourceEnvelope envelope = QuestResourceEnvelope.Decode(bytes, "fixture.QRC");

        Assert.Equal(QuestResourceEnvelopeDisposition.Truncated, envelope.Disposition);
        Assert.Contains("in a file of 8 bytes", envelope.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_resource_envelope_whose_directory_size_is_not_whole_entries()
    {
        byte[] bytes = new byte[32];
        bytes[0] = 7;

        QuestResourceEnvelope envelope = QuestResourceEnvelope.Decode(bytes, "fixture.QRC");

        Assert.Equal(QuestResourceEnvelopeDisposition.Malformed, envelope.Disposition);
        Assert.Contains("multiple of 6", envelope.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_resource_envelope_whose_entries_do_not_ascend()
    {
        byte[] bytes = BuildResourceEnvelope([(1000, 24), (1001, 20)]);

        QuestResourceEnvelope envelope = QuestResourceEnvelope.Decode(bytes, "fixture.QRC");

        Assert.Equal(QuestResourceEnvelopeDisposition.Malformed, envelope.Disposition);
        Assert.Contains("before the previous record", envelope.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_resource_envelope_without_the_terminal_sentinel()
    {
        byte[] bytes = BuildResourceEnvelope([(1000, 20)], sentinel: false);

        QuestResourceEnvelope envelope = QuestResourceEnvelope.Decode(bytes, "fixture.QRC");

        Assert.Equal(QuestResourceEnvelopeDisposition.Malformed, envelope.Disposition);
        Assert.Contains("sentinel", envelope.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Decodes_a_well_formed_synthetic_resource_envelope()
    {
        byte[] bytes = BuildResourceEnvelope([(7, 20), (8, 24)]);

        QuestResourceEnvelope envelope = QuestResourceEnvelope.Decode(bytes, "fixture.QRC");

        Assert.Equal(QuestResourceEnvelopeDisposition.Decoded, envelope.Disposition);
        Assert.Equal(2, envelope.Records.Count);
        Assert.Equal([7, 8], envelope.Records.Select(record => (int)record.Id));
        Assert.Equal([4, 4], envelope.Records.Select(record => record.Length));
        Assert.Equal(20, envelope.Records[0].Offset);
        Assert.Equal([0xaa, 0xaa, 0xaa, 0xaa], envelope.Records[0].Payload.ToArray());
    }

    /// <summary>Builds a resource envelope: size field, entries, records, sentinel.</summary>
    private static byte[] BuildResourceEnvelope((ushort Id, int Offset)[] entries, bool sentinel = true)
    {
        int entryCount = entries.Length + (sentinel ? 1 : 0);
        int directoryBytes = entryCount * QuestResourceEnvelope.DirectoryEntryBytes;
        int end = entries.Length == 0 ? 2 + directoryBytes : Math.Max(entries[^1].Offset, 2 + directoryBytes) + 4;
        byte[] bytes = new byte[end];
        bytes[0] = (byte)directoryBytes;
        bytes[1] = (byte)(directoryBytes >> 8);
        int at = 2;
        foreach ((ushort id, int offset) in entries)
        {
            Write(bytes, ref at, id, offset);
        }

        if (sentinel)
        {
            Write(bytes, ref at, QuestResourceEnvelope.SentinelId, bytes.Length);
        }

        for (int index = 2 + directoryBytes; index < bytes.Length; index++)
        {
            bytes[index] = 0xaa;
        }

        return bytes;
    }

    private static void Write(byte[] bytes, ref int at, ushort id, int offset)
    {
        bytes[at] = (byte)id;
        bytes[at + 1] = (byte)(id >> 8);
        bytes[at + 2] = (byte)offset;
        bytes[at + 3] = (byte)(offset >> 8);
        bytes[at + 4] = (byte)(offset >> 16);
        bytes[at + 5] = (byte)(offset >> 24);
        at += QuestResourceEnvelope.DirectoryEntryBytes;
    }

    private static int CountVariant(QuestSourceInventory inventory, byte variant)
    {
        string root = RepositoryRoot();
        return inventory.Binaries.Count(file => QuestBinaryEnvelope.Decode(File.ReadAllBytes(Path.Combine(root, "local/arena2", file.Path)), file.Path).HeaderVariant == variant);
    }

    private static QuestSourceInventory ReadInventory()
    {
        string root = RepositoryRoot();
        string[] paths = [.. Directory.GetFiles(Path.Combine(root, "local/arena2"))
            .Where(path => path.EndsWith(QuestSourceInventory.BinaryExtension, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(QuestSourceInventory.ResourcesExtension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)!];
        return QuestSourceInventory.Enumerate(paths, "local/arena2");
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
