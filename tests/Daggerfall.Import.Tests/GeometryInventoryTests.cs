using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The numeric mesh inventory: every record the archive declares, what its own bytes say, which numbers
/// it reuses, and which of them a published block names.
/// </summary>
public sealed class GeometryInventoryTests
{
    private static readonly Lazy<DaggerfallGeometry> Corpus = new(Supplied);

    [Fact]
    public void Enumerates_the_supplied_archive_in_the_only_stable_order_it_states()
    {
        DaggerfallGeometry geometry = Corpus.Value;

        Assert.Equal(10251, geometry.Records.Count);
        Assert.Equal(10251, geometry.Sources[0].DeclaredLength);
        Assert.Equal(27143532L, geometry.Sources[0].ByteLength);
        Assert.Equal(0, geometry.Records.Count(record => record.State == DaggerfallGeometryState.Malformed));

        // A record's identity is its directory ordinal: the archive is not sorted by number, so an
        // inventory that ordered records by number would be stating an order the file does not have.
        Assert.Equal(Enumerable.Range(0, 10251), geometry.Records.Select(record => record.Ordinal));
        byte[] bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/ARCH3D.BSA"));
        long expected = Arena2FormatConstants.BsaHeaderBytes;
        foreach (DaggerfallGeometryRecord record in geometry.Records)
        {
            Assert.Equal(expected, record.Offset);
            expected += record.ByteLength;
        }

        Assert.Equal(27061524L, expected);
        Assert.Equal(1840, geometry.Records.Zip(geometry.Records.Skip(1)).Count(pair => pair.First.RecordId > pair.Second.RecordId));

        // Each number is the archive's own: reading it back from the directory entry that carries it has
        // to give the number the inventory published for that ordinal.
        foreach (DaggerfallGeometryRecord record in geometry.Records.Take(64))
        {
            int directory = 27061524 + (record.Ordinal * Arena2FormatConstants.NumericBsaDirectoryEntryBytes);
            Assert.Equal(record.RecordId, BitConverter.ToUInt32(bytes, directory));
            Assert.Equal(record.ByteLength, BitConverter.ToInt32(bytes, directory + 4));
        }
    }

    [Fact]
    public void Publishes_every_identity_whether_or_not_a_block_names_it()
    {
        // The clause the task states: a mesh nothing references keeps its identity, because whether the
        // corpus is closed is a decision someone has to be able to make from the inventory.
        DaggerfallGeometry geometry = Corpus.Value;

        Assert.Equal(10237, geometry.Records.Select(record => record.RecordId).Distinct().Count());
        Assert.Equal(1043, geometry.Records.Count(record => record.Disposition == DaggerfallGeometryDisposition.Referenced));
        Assert.Equal(9194, geometry.Records.Count(record => record.Disposition == DaggerfallGeometryDisposition.Unused));
        Assert.Equal(14, geometry.Records.Count(record => record.Disposition == DaggerfallGeometryDisposition.Duplicate));
        Assert.Equal(10251, geometry.Records.Count);
        Assert.Empty(geometry.UnresolvedUseSites);

        // Nothing is dropped and nothing is invented: the numbers the inventory carries are the numbers
        // the archive's own directory states, as a multiset, so a reused number is carried as often as the
        // file states it rather than collapsed to one record.
        byte[] bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/ARCH3D.BSA"));
        uint[] published = [.. geometry.Records.Select(record => (uint)record.RecordId).Order()];
        uint[] stated = [.. Enumerable.Range(0, 10251).Select(ordinal => BitConverter.ToUInt32(bytes, 27061524 + (ordinal * Arena2FormatConstants.NumericBsaDirectoryEntryBytes))).Order()];
        Assert.Equal(stated, published);
    }

    [Fact]
    public void Classifies_a_reused_number_by_the_lookup_the_donor_makes()
    {
        // A numeric directory may reuse a number, and the donor's lookup answers with the first record that
        // carries it. The later record is therefore unreachable by its number, and the blocks that name
        // that number belong to the record a lookup actually finds.
        DaggerfallGeometry geometry = Corpus.Value;
        DaggerfallGeometryRecord[] duplicates = [.. geometry.Records.Where(record => record.DuplicateOf is not null)];

        Assert.Equal(14, duplicates.Length);
        Assert.All(duplicates, record => Assert.Equal(DaggerfallGeometryDisposition.Duplicate, record.Disposition));
        Assert.All(duplicates, record => Assert.Empty(record.UseSites));
        Assert.All(duplicates, record => Assert.Equal(record.RecordId, geometry.Records[record.DuplicateOf!.Value].RecordId));
        Assert.All(duplicates, record => Assert.True(record.DuplicateOf < record.Ordinal));

        // The one number the corpus carries six times is the sharpest case: five records are unreachable by
        // it and every one of them names the same first record.
        DaggerfallGeometryRecord[] six = [.. geometry.Records.Where(record => record.RecordId == 5090)];
        Assert.Equal(6, six.Length);
        Assert.Equal(1, six.Count(record => record.DuplicateOf is null));
        Assert.All(six.Where(record => record.DuplicateOf is not null), record => Assert.Equal(six[0].Ordinal, record.DuplicateOf));
    }

    [Fact]
    public void Retains_the_version_counts_and_textures_of_every_readable_record()
    {
        DaggerfallGeometry geometry = Corpus.Value;

        Assert.Equal(10109, geometry.Records.Count(record => record.Facts?.Version == "v2.7"));
        Assert.Equal(134, geometry.Records.Count(record => record.Facts?.Version == "v2.6"));
        Assert.Equal(8, geometry.Records.Count(record => record.Facts?.Version == "v2.5"));
        Assert.Equal(204479, geometry.Records.Sum(record => record.Facts?.Planes ?? 0));
        Assert.Equal(367262, geometry.Records.Sum(record => record.Facts?.DeclaredPoints ?? 0));

        // Only source facts: a texture reference is an archive and a record, never a mesh or an asset.
        Assert.All(geometry.Records, record =>
        {
            Assert.NotNull(record.Facts);
            Assert.True(record.Facts!.Textures.Count <= record.Facts.Planes);
            Assert.Equal(record.Facts.Textures.Count, record.Facts.Textures.Distinct().Count());
        });
    }

    [Fact]
    public void Finds_the_records_whose_bytes_repeat_an_earlier_records()
    {
        // Two numbers carrying the same geometry is the corpus's own answer to duplication, and it is not
        // the same question as a reused number: 1909 records repeat bytes while only 14 reuse a number.
        DaggerfallGeometry geometry = Corpus.Value;
        DaggerfallGeometryRecord[] repeats = [.. geometry.Records.Where(record => record.PayloadDuplicateOf is not null)];

        Assert.Equal(1909, repeats.Length);
        Assert.Equal(8342, geometry.Records.Count - repeats.Length);
        Assert.All(repeats, record =>
        {
            Assert.True(record.PayloadDuplicateOf < record.Ordinal);
            Assert.Equal(record.ByteLength, geometry.Records[record.PayloadDuplicateOf!.Value].ByteLength);
        });
    }

    [Fact]
    public void Links_a_blocks_mesh_number_to_the_record_that_answers_it()
    {
        // The dungeon source stores a mesh number as five characters of text, so mesh 9004 is named
        // "09004" there, while this archive stores the number itself. Joining the two by spelling would
        // report the number missing from the archive that carries it.
        DaggerfallGeometry geometry = WithUseSites([new DaggerfallGeometryUseSite("09004", "B0000000.RDB"), new DaggerfallGeometryUseSite("09005", "B0000001.RDB")]);

        Assert.Empty(geometry.UnresolvedUseSites);
        DaggerfallGeometryRecord fourth = geometry.Records.Single(record => record.RecordId == 9004);
        Assert.Equal(DaggerfallGeometryDisposition.Referenced, fourth.Disposition);
        Assert.Equal(["B0000000.RDB"], fourth.UseSites);
        Assert.Equal(DaggerfallGeometryDisposition.Referenced, geometry.Records.Single(record => record.RecordId == 9005).Disposition);
    }

    [Fact]
    public void Reports_a_number_a_block_names_that_the_archive_cannot_answer()
    {
        // Both ways a lookup can fail: a number no record carries, and a number whose only record could not
        // be decoded. Either one leaves a block naming a mesh nothing can serve.
        byte[] bytes = NamedArchive(("PLACE", new byte[8]));
        DaggerfallGeometry geometry = DaggerfallGeometryBuilder.Build(
            NumericArchive((9004, Mesh()), (9005, new byte[32]), (9006, Mesh())),
            "local/arena2/ARCH3D.BSA",
            Inventory(),
            [new DaggerfallGeometryUseSite("09004", "B0000000.RDB"), new DaggerfallGeometryUseSite("09005", "B0000001.RDB"), new DaggerfallGeometryUseSite("09999", "B0000002.RDB")]);
        Assert.Equal(3, geometry.Records.Count);
        Assert.Equal(1, geometry.Records.Count(record => record.State == DaggerfallGeometryState.Malformed));
        Assert.Equal(2, geometry.UnresolvedUseSites.Count);
        Assert.Equal("09005", geometry.UnresolvedUseSites[0].MeshId);
        Assert.Contains("could not be decoded", geometry.UnresolvedUseSites[0].Reason, StringComparison.Ordinal);
        Assert.Equal("09999", geometry.UnresolvedUseSites[1].MeshId);
        Assert.Contains("carries no record numbered 9999", geometry.UnresolvedUseSites[1].Reason, StringComparison.Ordinal);

        Assert.Equal(DaggerfallGeometryState.Read, geometry.Records.Single(record => record.RecordId == 9004).State);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Publishes_a_record_the_archive_states_has_no_bytes()
    {
        // A numeric directory can state a record of no bytes. Nothing decodes from it, so it is malformed
        // with that reason and keeps its number, rather than abandoning the inventory around it.
        DaggerfallGeometry geometry = DaggerfallGeometryBuilder.Build(
            NumericArchive((9004, []), (9005, Mesh())),
            "local/arena2/ARCH3D.BSA",
            Inventory(),
            []);

        Assert.Equal(DaggerfallGeometryState.Malformed, geometry.Records[0].State);
        Assert.Equal(0, geometry.Records[0].ByteLength);
        Assert.Equal(9004, geometry.Records[0].RecordId);
        Assert.Contains("requires at least 64 bytes, got 0", geometry.Records[0].Reason, StringComparison.Ordinal);
        Assert.Equal(DaggerfallGeometryDisposition.Unused, geometry.Records[0].Disposition);
        Assert.Equal(DaggerfallGeometryState.Read, geometry.Records[1].State);
    }

    [Fact]
    public void Refuses_a_named_archive_because_its_records_carry_no_numbers()
    {
        // ARCH3D.BSA is the numeric variant. A named archive has no number to look a mesh up by, so
        // publishing one as a mesh inventory would file every record under an identity it does not have.
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => Arch3dInventoryReader.Read(NamedArchive(("MESH", Mesh())), "local/arena2/ARCH3D.BSA"));

        Assert.Contains("a mesh archive is the numeric variant whose records carry numbers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Publishes_a_record_whose_bytes_cannot_be_decoded_as_malformed()
    {
        // A record exists whatever its bytes say, and a block may name it, so it is published with the
        // reason rather than dropped from the inventory.
        Arch3dMeshInventory inventory = Arch3dInventoryReader.Read(
            NumericArchive((1, new byte[32]), (2, Mesh()), (3, [.. "v9.9"u8, .. new byte[60]])),
            "local/arena2/ARCH3D.BSA");

        Assert.Equal(
            [Arch3dRecordState.Malformed, Arch3dRecordState.Read, Arch3dRecordState.Malformed],
            inventory.Records.Select(record => record.State));
        Assert.Contains("requires at least 64 bytes", inventory.Records[0].Reason, StringComparison.Ordinal);
        Assert.Contains("unsupported ARCH3D version v9.9", inventory.Records[2].Reason, StringComparison.Ordinal);
        Assert.Null(inventory.Records[0].Facts);
        Assert.Equal("v2.7", inventory.Records[1].Facts!.Version);
    }

    [Fact]
    public void Refuses_a_duplicate_column_that_does_not_match_where_a_number_first_appears()
    {
        DaggerfallGeometry geometry = Corpus.Value;
        DaggerfallGeometryRecord duplicate = geometry.Records.First(record => record.DuplicateOf is not null);

        Assert.Contains("where number", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, duplicate.Ordinal, duplicate with { DuplicateOf = null, Disposition = DaggerfallGeometryDisposition.Unused }) }).Validate()).Message, StringComparison.Ordinal);
        DaggerfallGeometryRecord predecessor = geometry.Records[duplicate.Ordinal - 1];
        Assert.NotEqual(predecessor.RecordId, duplicate.RecordId);
        Assert.Contains("where number", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, duplicate.Ordinal, duplicate with { DuplicateOf = duplicate.Ordinal - 1 }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("not an earlier ordinal", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, duplicate.Ordinal, duplicate with { DuplicateOf = duplicate.Ordinal + 1 }) }).Validate()).Message, StringComparison.Ordinal);

        // The byte column is checked the same way round, and the two checks are not the same question: the
        // record's own rule refuses a pointer that does not come before it, and the section's rule refuses
        // one that does but whose bytes cannot be the same because their lengths differ.
        DaggerfallGeometryRecord repeat = geometry.Records.First(record => record.PayloadDuplicateOf is not null);
        Assert.Contains("not an earlier ordinal", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, repeat.Ordinal, repeat with { PayloadDuplicateOf = repeat.Ordinal + 1 }) }).Validate()).Message, StringComparison.Ordinal);

        DaggerfallGeometryRecord shorter = geometry.Records.First(record => record.Ordinal > 0 && record.ByteLength != geometry.Records[0].ByteLength);
        Assert.Contains("not an earlier record of the same length", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, shorter.Ordinal, shorter with { PayloadDuplicateOf = 0 }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_disposition_that_disagrees_with_the_duplicate_column_and_use_sites()
    {
        DaggerfallGeometry geometry = Corpus.Value;
        DaggerfallGeometryRecord referenced = geometry.Records.First(record => record.Disposition == DaggerfallGeometryDisposition.Referenced);
        DaggerfallGeometryRecord unused = geometry.Records.First(record => record.Disposition == DaggerfallGeometryDisposition.Unused);

        Assert.Contains("make it", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, referenced.Ordinal, referenced with { Disposition = DaggerfallGeometryDisposition.Unused }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("make it", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, unused.Ordinal, unused with { UseSites = ["B0000001.RDB"] }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_state_that_disagrees_with_the_facts_it_publishes()
    {
        DaggerfallGeometry geometry = Corpus.Value;
        DaggerfallGeometryRecord record = geometry.Records.First(value => value.State == DaggerfallGeometryState.Read && value.Facts is not null);

        Assert.Contains("is readable", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, record.Ordinal, record with { Facts = null }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("is readable", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, record.Ordinal, record with { Reason = "the fixture says so" }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("is malformed", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, record.Ordinal, record with { State = DaggerfallGeometryState.Malformed }) }).Validate()).Message, StringComparison.Ordinal);

        // The version set is the format's, so a record stating another one cannot have come from it.
        Assert.Contains("the format does not declare", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, record.Ordinal, record with { Facts = record.Facts! with { Version = "v9.9" } }) }).Validate()).Message, StringComparison.Ordinal);

        // A record that states why it is malformed cannot also state what it read: both halves of the rule
        // are checked, so a reason beside facts is refused as well as facts beside no reason.
        Assert.Contains("facts it could not read", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, record.Ordinal, record with { State = DaggerfallGeometryState.Malformed, Reason = "the fixture says so" }) }).Validate()).Message, StringComparison.Ordinal);

        // A number is four bytes in the source, so a wider one cannot have come from the file.
        Assert.Contains("four-byte number cannot state", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, record.Ordinal, record with { RecordId = 4_294_967_296 }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_use_sites_that_repeat_or_run_out_of_order()
    {
        DaggerfallGeometry geometry = Corpus.Value;
        DaggerfallGeometryRecord referenced = geometry.Records.First(record => record.UseSites.Count > 1);

        Assert.Contains("use site", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, referenced.Ordinal, referenced with { UseSites = [referenced.UseSites[1], referenced.UseSites[0]] }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("use site", Assert.Throws<InvalidOperationException>(() => (geometry with { Records = Replaced(geometry, referenced.Ordinal, referenced with { UseSites = [referenced.UseSites[0], referenced.UseSites[0]] }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_unresolved_number_the_archive_can_answer()
    {
        DaggerfallGeometry geometry = Corpus.Value;
        DaggerfallGeometryRecord answered = geometry.Records.First(record => record.Disposition == DaggerfallGeometryDisposition.Referenced);

        Assert.Contains("answers it", Assert.Throws<InvalidOperationException>(() => (geometry with { UnresolvedUseSites = [new DaggerfallGeometryUnresolvedRecord(answered.RecordId.ToString(), ["B0000001.RDB"], "the fixture says so")] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("no reason or no use site", Assert.Throws<InvalidOperationException>(() => (geometry with { UnresolvedUseSites = [new DaggerfallGeometryUnresolvedRecord("999999", [], "the fixture says so")] }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_source_whose_declared_count_is_not_what_the_section_carries()
    {
        DaggerfallGeometry geometry = Corpus.Value;

        Assert.Contains("declares 10250 records and publishes 10251", Assert.Throws<InvalidOperationException>(() => (geometry with { Sources = [geometry.Sources[0] with { DeclaredLength = 10250 }] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("Geometry schema must be 1 but is 2", Assert.Throws<InvalidOperationException>(() => (geometry with { SchemaVersion = 2 }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_unresolved_number_spelled_the_way_the_blocks_spell_it()
    {
        // The rule is about numbers, and a block spells a number as the dungeon source stores it. A section
        // reporting "09004" unresolved while record 9004 answers it has to be refused in that spelling too,
        // because that is the spelling the corpus actually uses.
        DaggerfallGeometry geometry = Corpus.Value;
        DaggerfallGeometryUnresolvedRecord reported = new("09004", ["B0000000.RDB"], "the fixture says so");

        Assert.Equal(DaggerfallGeometryDisposition.Referenced, geometry.Records.Single(record => record.RecordId == 9004).Disposition);
        Assert.Contains("answers it", Assert.Throws<InvalidOperationException>(() => (geometry with { UnresolvedUseSites = [reported] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("answers it", Assert.Throws<InvalidOperationException>(() => (geometry with { UnresolvedUseSites = [reported with { MeshId = "9004" }] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("not a mesh number", Assert.Throws<InvalidOperationException>(() => (geometry with { UnresolvedUseSites = [reported with { MeshId = "nine thousand" }] }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_blocks_section_whose_members_are_not_the_ones_it_declares()
    {
        // A section read with its unknown members ignored answers a different question than it states: a
        // renamed model list reads as an empty one, and the geometry folded from it reports every mesh as
        // unused — the opposite of the closure set this inventory exists to publish.
        System.Text.Json.Nodes.JsonObject pack = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")))!.AsObject();
        System.Text.Json.Nodes.JsonArray records = pack["blocks"]!["records"]!.AsArray();
        System.Text.Json.Nodes.JsonObject objects = records
            .Select(record => record!["objects"] as System.Text.Json.Nodes.JsonObject)
            .First(value => value is not null && value.ContainsKey("modelIds"))!;
        objects["modelIdsX"] = objects["modelIds"]!.DeepClone();
        objects.Remove("modelIds");
        string json = pack["blocks"]!.ToJsonString();

        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<DaggerfallBlocks>(json, PublishedJson.SectionRead));
        Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<DaggerfallBlocks>(json, PublishedJson.Section));
    }

    [Fact]
    public void The_builder_cites_the_archive_to_the_documented_inventory()
    {
        byte[] bytes = NumericArchive((9004, Mesh()));

        Assert.Contains("but the documented inventory places CNT-006 at", Assert.Throws<InvalidOperationException>(() => DaggerfallGeometryBuilder.Build(bytes, "elsewhere/ARCH3D.BSA", Inventory(), [])).Message, StringComparison.Ordinal);
        Assert.Contains("does not carry family 'CNT-006'", Assert.Throws<InvalidOperationException>(() => DaggerfallGeometryBuilder.Build(bytes, "local/arena2/ARCH3D.BSA", [], [])).Message, StringComparison.Ordinal);
        Assert.Equal("local/arena2/ARCH3D.BSA", DaggerfallGeometryBuilder.Build(bytes, "local/arena2/ARCH3D.BSA", Inventory(), []).Sources[0].Path);
    }

    /// <summary>Builds the corpus inventory once, with the use sites the published block section states.</summary>
    private static DaggerfallGeometry Supplied() => WithUseSites(BlockUseSites());

    private static DaggerfallGeometry WithUseSites(IReadOnlyList<DaggerfallGeometryUseSite> useSites) =>
        DaggerfallGeometryBuilder.Build(
            File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/ARCH3D.BSA")),
            "local/arena2/ARCH3D.BSA",
            Inventory(),
            useSites);

    /// <summary>
    /// The mesh numbers the published block section names, read from the pack exactly as the tool reads
    /// them, so this test answers the same question the product does.
    /// </summary>
    private static IReadOnlyList<DaggerfallGeometryUseSite> BlockUseSites()
    {
        List<DaggerfallGeometryUseSite> useSites = [];
        using System.Text.Json.JsonDocument pack = System.Text.Json.JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        foreach (System.Text.Json.JsonElement block in pack.RootElement.GetProperty("blocks").GetProperty("records").EnumerateArray())
        {
            if (!block.TryGetProperty("objects", out System.Text.Json.JsonElement objects) || objects.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                continue;
            }

            foreach (System.Text.Json.JsonElement model in objects.GetProperty("modelIds").EnumerateArray())
            {
                useSites.Add(new DaggerfallGeometryUseSite(model.GetString()!, block.GetProperty("sourceKey").GetString()!));
            }
        }

        return useSites;
    }

    private static IReadOnlyList<DaggerfallGeometryRecord> Replaced(DaggerfallGeometry geometry, int ordinal, DaggerfallGeometryRecord record) =>
        [.. geometry.Records.Select(value => value.Ordinal == ordinal ? record : value)];

    private static IReadOnlyList<SourceInventoryRow> Inventory() => SourceManifestBuilder.ReadInventory(
        File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));

    /// <summary>A minimal v2.7 mesh record: a version, no planes, and a point list that starts after it.</summary>
    private static byte[] Mesh()
    {
        byte[] bytes = new byte[64];
        "v2.7"u8.CopyTo(bytes);
        return bytes;
    }

    /// <summary>Builds the numeric BSA variant: a header, contiguous payloads, and eight-byte directory entries.</summary>
    private static byte[] NumericArchive(params (uint Id, byte[] Payload)[] records)
    {
        int payloadBytes = records.Sum(record => record.Payload.Length);
        byte[] bytes = new byte[Arena2FormatConstants.BsaHeaderBytes + payloadBytes + (records.Length * Arena2FormatConstants.NumericBsaDirectoryEntryBytes)];
        bytes[0] = (byte)records.Length;
        bytes[1] = (byte)(records.Length >> 8);
        bytes[2] = (byte)(Arena2FormatConstants.NumericBsaDirectoryType & 0xff);
        bytes[3] = (byte)((Arena2FormatConstants.NumericBsaDirectoryType >> 8) & 0xff);
        int payload = Arena2FormatConstants.BsaHeaderBytes;
        int directory = Arena2FormatConstants.BsaHeaderBytes + payloadBytes;
        for (int index = 0; index < records.Length; index++)
        {
            records[index].Payload.CopyTo(bytes, payload);
            payload += records[index].Payload.Length;
            BitConverter.GetBytes(records[index].Id).CopyTo(bytes, directory + (index * 8));
            BitConverter.GetBytes(records[index].Payload.Length).CopyTo(bytes, directory + (index * 8) + 4);
        }

        return bytes;
    }

    /// <summary>Builds the named BSA variant, which a mesh inventory has to refuse.</summary>
    private static byte[] NamedArchive(params (string Name, byte[] Payload)[] records)
    {
        int payloadBytes = records.Sum(record => record.Payload.Length);
        byte[] bytes = new byte[Arena2FormatConstants.BsaHeaderBytes + payloadBytes + (records.Length * Arena2FormatConstants.NamedBsaDirectoryEntryBytes)];
        bytes[0] = (byte)records.Length;
        bytes[1] = (byte)(records.Length >> 8);
        bytes[2] = (byte)(Arena2FormatConstants.NamedBsaDirectoryType & 0xff);
        bytes[3] = (byte)((Arena2FormatConstants.NamedBsaDirectoryType >> 8) & 0xff);
        int payload = Arena2FormatConstants.BsaHeaderBytes;
        int directory = Arena2FormatConstants.BsaHeaderBytes + payloadBytes;
        for (int index = 0; index < records.Length; index++)
        {
            records[index].Payload.CopyTo(bytes, payload);
            payload += records[index].Payload.Length;
            System.Text.Encoding.ASCII.GetBytes(records[index].Name).CopyTo(bytes, directory + (index * Arena2FormatConstants.NamedBsaDirectoryEntryBytes));
            BitConverter.GetBytes(records[index].Payload.Length).CopyTo(bytes, directory + (index * Arena2FormatConstants.NamedBsaDirectoryEntryBytes) + 14);
        }

        return bytes;
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
