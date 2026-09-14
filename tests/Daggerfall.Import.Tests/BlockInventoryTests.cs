using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The block archive inventory: every record the supplied archive declares, what its name says it is,
/// and what its own header says it places.
/// </summary>
public sealed class BlockInventoryTests
{
    [Fact]
    public void Enumerates_the_supplied_archive_with_every_record_classified()
    {
        DaggerfallBlocks blocks = Supplied();

        Assert.Equal(1295, blocks.Records.Count);
        Assert.Equal(1295, blocks.Sources[0].DeclaredLength);
        Assert.Equal(0, blocks.Records.Count(record => record.State == DaggerfallBlockState.Malformed));
        Assert.Equal(
            [920, 187, 187, 1],
            new[] { DaggerfallBlockKind.Rmb, DaggerfallBlockKind.Rdb, DaggerfallBlockKind.Rdi, DaggerfallBlockKind.Unknown }
                .Select(kind => blocks.Records.Count(record => record.Kind == kind)));

        // A record's identity is its directory ordinal, so the inventory carries every ordinal from zero
        // and no other: an ordinal that repeated would leave two records claiming one identity.
        Assert.Equal(Enumerable.Range(0, 1295), blocks.Records.Select(record => record.Ordinal));
        Assert.Equal(1295, blocks.Records.Select(record => record.SourceKey).Distinct(StringComparer.Ordinal).Count());

        // The archive stores its payloads end to end, so each record begins where the previous one ended
        // and the last one ends where the directory begins.
        long expected = Arena2FormatConstants.BsaHeaderBytes;
        foreach (DaggerfallBlockRecord record in blocks.Records)
        {
            Assert.Equal(expected, record.Offset);
            expected += record.ByteLength;
        }

        Assert.Equal(32861687L, expected);
    }

    [Fact]
    public void Keeps_every_donor_index_for_a_prefix_two_kinds_share()
    {
        // The donor's own prefix table carries TEMP twice, for the two temple kinds, so a name beginning
        // with it cannot say which of the two a block came from. Publishing one index would be a
        // classification the source does not support, so both are retained.
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord[] temples = [.. blocks.Records.Where(record => record.RmbName?.Prefix == "TEMP")];

        Assert.Equal(10, temples.Length);
        Assert.All(temples, record => Assert.Equal([13, 14], record.RmbName!.TableIndices));
        Assert.All(temples, record => Assert.True(record.RmbName!.DonorShape));

        // A prefix the table carries once keeps its one index, and the table itself is transcribed whole.
        DaggerfallBlockRecord wall = blocks.Records.Single(record => record.SourceKey == "WALLAA03.RMB");
        Assert.Equal([41], wall.RmbName!.TableIndices);
        Assert.Equal(45, BlockRecordInventoryReader.RmbBlockPrefixes.Count);
        Assert.Equal(
            [13, 14],
            BlockRecordInventoryReader.RmbBlockPrefixes.Select((prefix, index) => (prefix, index)).Where(pair => pair.prefix == "TEMP").Select(pair => pair.index));
    }

    [Fact]
    public void Classifies_names_by_the_donors_own_tables()
    {
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord[] dungeons = [.. blocks.Records.Where(record => record.Kind == DaggerfallBlockKind.Rdb)];

        Assert.Equal(
            [15, 30, 40, 9, 93],
            new[] { DaggerfallBlockRdbType.Border, DaggerfallBlockRdbType.Wet, DaggerfallBlockRdbType.Quest, DaggerfallBlockRdbType.Mausoleum, DaggerfallBlockRdbType.Normal }
                .Select(type => dungeons.Count(record => record.RdbName!.Type == type)));

        // The donor's letter table has no case for L even though the coverage inventory lists one, and the
        // corpus carries none either, so nothing here reports a type the donor cannot derive.
        Assert.DoesNotContain(dungeons, record => record.RdbName!.Type == DaggerfallBlockRdbType.Unknown);
        Assert.All(dungeons, record => Assert.Equal(record.SourceKey, record.RdbName!.Letter + record.RdbName.NumberText + ".RDB"));

        // A city block's classification has to reassemble the name it came from, and the names the donor's
        // composer could not have produced are published with their own identity and no classification.
        DaggerfallBlockRecord[] city = [.. blocks.Records.Where(record => record.Kind == DaggerfallBlockKind.Rmb)];
        DaggerfallBlockRecord[] shaped = [.. city.Where(record => record.RmbName is not null)];
        Assert.All(shaped, record => Assert.Equal(record.SourceKey[..^4], record.RmbName!.Prefix + record.RmbName.Letter1 + record.RmbName.Letter2 + record.RmbName.NumberText));
        Assert.Equal(122, city.Length - shaped.Length);
        Assert.Contains(city, record => record.SourceKey == "TEMP.RMB" && record.RmbName is null);
        Assert.Contains(city, record => record.SourceKey == "BRUCE.RMB" && record.RmbName is null);
    }

    [Fact]
    public void Summarizes_a_city_block_from_its_own_header()
    {
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord wall = blocks.Records.Single(record => record.SourceKey == "WALLAA03.RMB");

        Assert.Equal(DaggerfallBlockState.Read, wall.State);
        Assert.Equal(DaggerfallBlockDisposition.Summarized, wall.Disposition);
        Assert.Equal("WALLAA03.RMB", wall.Rmb!.Name);
        Assert.Equal(1, wall.Rmb.DeclaredBlocks);
        DaggerfallBlockBuilding building = Assert.Single(wall.Rmb.Buildings);
        Assert.Equal(0, building.Index);
        Assert.Equal(615, building.ByteLength);
        Assert.Equal(23, building.BuildingType);

        // Every city block's own header has to account for the bytes its record spans: the fixed header,
        // the sub-records it declares, the objects that follow them, and whatever the record carries past
        // both. That identity is what makes the offsets in this summary positions rather than guesses, and
        // the supplied corpus satisfies it exactly for all 920 records.
        foreach (DaggerfallBlockRecord record in blocks.Records.Where(record => record.Kind == DaggerfallBlockKind.Rmb))
        {
            int accounted = DaggerfallBlocks.RmbHeaderBytes + record.Rmb!.TrailingBytes
                + (record.Rmb.Misc3dObjects * DaggerfallBlocks.RmbModelRecordBytes)
                + (record.Rmb.MiscFlatObjects * DaggerfallBlocks.RmbFlatRecordBytes)
                + record.Rmb.Buildings.Sum(value => value.ByteLength);
            Assert.Equal(record.ByteLength, accounted);
            Assert.Equal(0, record.Rmb.TrailingBytes);
        }

        // The header states a name for itself, and in the supplied corpus it is the archive key the record
        // is stored under, which is what establishes the header's shape in the first place.
        Assert.All(
            blocks.Records.Where(record => record.Rmb is not null),
            record => Assert.Equal(record.SourceKey, record.Rmb!.Name));
    }

    [Fact]
    public void Summarizes_a_dungeon_block_from_its_placement_list()
    {
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord record = blocks.Records.Single(value => value.SourceKey == "B0000000.RDB");

        Assert.Equal(DaggerfallBlockState.Read, record.State);
        Assert.Equal(36, record.Objects!.Models);
        Assert.Equal(17, record.Objects.Flats);
        Assert.Equal(12, record.Objects.Lights);
        Assert.Equal(4, record.Objects.Doors);
        Assert.Equal(17, record.Objects.ModelIds.Count);
        Assert.True(record.Objects.ModelIds.Count < record.Objects.Models);

        // What a block places names models and textures, and those identities are what the geometry
        // inventory enumerates usage from: each list is a set, and none of it exceeds what the block says
        // it places.
        foreach (DaggerfallBlockRecord dungeon in blocks.Records.Where(value => value.Kind == DaggerfallBlockKind.Rdb))
        {
            DaggerfallBlockObjects objects = dungeon.Objects!;
            Assert.True(objects.Doors <= objects.Models);
            Assert.True(objects.ModelIds.Count <= objects.Models);
            Assert.True(objects.Textures.Count <= objects.Flats);
            Assert.Equal(objects.ModelIds.Count, objects.ModelIds.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(objects.Textures.Count, objects.Textures.Select(texture => (texture.Archive, texture.Record)).Distinct().Count());
            Assert.True(objects.StartMarkers + objects.EnterMarkers + objects.TreasureMarkers + objects.FixedMobiles <= objects.Flats);
        }
    }

    [Fact]
    public void Classifies_the_index_records_the_donor_reads_and_ignores()
    {
        // The donor's own block descriptor calls these five hundred and twelve bytes of unknown data that
        // should be ignored, so they are published as a disposition rather than decoded into facts no
        // consumer could check.
        DaggerfallBlockRecord[] indexes = [.. Supplied().Records.Where(record => record.Kind == DaggerfallBlockKind.Rdi)];

        Assert.Equal(187, indexes.Length);
        Assert.All(indexes, record => Assert.Equal(512, record.ByteLength));
        Assert.All(indexes, record => Assert.Equal(DaggerfallBlockDisposition.DonorUnsupported, record.Disposition));
        Assert.All(indexes, record => Assert.Null(record.Rmb));
        Assert.All(indexes, record => Assert.Null(record.Objects));
    }

    [Fact]
    public void Publishes_a_record_whose_own_header_cannot_be_read_as_malformed()
    {
        // A record exists in the archive whatever its bytes say, so a header that cannot be read is a
        // disposition with a reason rather than a record missing from the inventory. Both kinds of record
        // are exercised here, since the two read their bytes differently.
        DaggerfallBlocks blocks = DaggerfallBlocksBuilder.Build(
            NamedArchive(("SHORT.RMB", new byte[64]), ("SHORT.RDB", new byte[64]), ("NOISE.RDB", new byte[10928])),
            "local/arena2/BLOCKS.BSA",
            Inventory());

        Assert.Equal(
            [DaggerfallBlockState.Malformed, DaggerfallBlockState.Malformed, DaggerfallBlockState.Malformed],
            blocks.Records.Select(record => record.State));
        Assert.Contains("too short to carry the 6776-byte block header", blocks.Records[0].Reason, StringComparison.Ordinal);
        Assert.Contains("RDB requires at least 6020 bytes", blocks.Records[1].Reason, StringComparison.Ordinal);
        Assert.NotEmpty(blocks.Records[2].Reason);
        Assert.Equal([64, 64, 10928], blocks.Records.Select(record => record.ByteLength));
    }

    [Fact]
    public void Refuses_a_record_whose_kind_or_disposition_disagrees_with_its_name()
    {
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord wall = blocks.Records.Single(record => record.SourceKey == "WALLAA03.RMB");

        Assert.Contains("which its own name does not carry", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Kind = DaggerfallBlockKind.Rdb, RdbName = null, Rmb = null, Objects = wall.Objects }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("where its kind Rmb is Summarized", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Disposition = DaggerfallBlockDisposition.UnknownKind }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_ordinal_that_repeats_or_skips()
    {
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord first = blocks.Records[0];
        DaggerfallBlockRecord second = blocks.Records[1];

        Assert.Contains("carries ordinal 0 where the section is ordered by ordinal and the previous record was 0", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, 1, second with { Ordinal = 0 }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("carries ordinal 3 where the section is ordered by ordinal and the previous record was 0", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, 1, second with { Ordinal = 3 }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Equal(0, first.Ordinal);
    }

    [Fact]
    public void Refuses_a_summary_a_malformed_record_could_not_have_and_a_readable_one_without()
    {
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord wall = blocks.Records.Single(record => record.SourceKey == "WALLAA03.RMB");
        DaggerfallBlockRecord dungeon = blocks.Records.Single(record => record.SourceKey == "B0000000.RDB");

        Assert.Contains("is a readable city block with no header summary", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = null }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("is a readable dungeon block with no object summary", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, dungeon.Ordinal, dungeon with { Objects = null }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("is malformed and still summarizes bytes it could not read", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { State = DaggerfallBlockState.Malformed, Reason = "the fixture says so" }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("is malformed and states no reason", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { State = DaggerfallBlockState.Malformed, Reason = " ", Rmb = null }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_city_block_whose_own_arithmetic_does_not_add_up()
    {
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord wall = blocks.Records.Single(record => record.SourceKey == "WALLAA03.RMB");

        // The header's own counts are what the summary is read through, so a summary that does not account
        // for the record's bytes describes offsets that land in the middle of something else.
        Assert.Contains("accounts for", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = wall.Rmb! with { TrailingBytes = 4 } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("declares 2 sub-records and carries 1", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = wall.Rmb! with { DeclaredBlocks = 2 } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("fewer than the 5-byte header", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = wall.Rmb! with { Buildings = [wall.Rmb!.Buildings[0] with { ByteLength = 4 }], TrailingBytes = wall.Rmb!.TrailingBytes + 611 } }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_name_classification_that_does_not_reassemble_its_name()
    {
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord wall = blocks.Records.Single(record => record.SourceKey == "WALLAA03.RMB");
        DaggerfallBlockRecord dungeon = blocks.Records.Single(record => record.SourceKey == "B0000000.RDB");

        Assert.Contains("does not reassemble its name", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { RmbName = wall.RmbName! with { NumberText = "04" } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("occupies [13, 14]", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { RmbName = wall.RmbName! with { Prefix = "TEMP", TableIndices = [13] } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("which the donor's table does not carry", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { RmbName = wall.RmbName! with { Prefix = "ZZZZ" } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("does not reassemble its name", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, dungeon.Ordinal, dungeon with { RdbName = dungeon.RdbName! with { NumberText = "0000001" } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("which the letter 'B' does not select", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, dungeon.Ordinal, dungeon with { RdbName = dungeon.RdbName! with { Type = DaggerfallBlockRdbType.Wet } }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_object_summary_that_claims_more_than_the_block_places()
    {
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord dungeon = blocks.Records.Single(record => record.SourceKey == "B0000000.RDB");
        DaggerfallBlockObjects objects = dungeon.Objects!;

        Assert.Contains("door models", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, dungeon.Ordinal, dungeon with { Objects = objects with { Doors = objects.Models + 1 } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("marker flats", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, dungeon.Ordinal, dungeon with { Objects = objects with { StartMarkers = objects.Flats + 1 } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("distinct models", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, dungeon.Ordinal, dungeon with { Objects = objects with { ModelIds = [.. Enumerable.Range(0, objects.Models + 1).Select(index => index.ToString(CultureInfo.InvariantCulture))] } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, dungeon.Ordinal, dungeon with { Objects = objects with { ModelIds = [objects.ModelIds[0], objects.ModelIds[0]] } }) }).Validate());
    }

    [Fact]
    public void Refuses_a_source_whose_declared_count_is_not_what_the_section_carries()
    {
        DaggerfallBlocks blocks = Supplied();

        Assert.Contains("declares 1294 records and publishes 1295, where the section carries 1295", Assert.Throws<InvalidOperationException>(() => (blocks with { Sources = [blocks.Sources[0] with { DeclaredLength = 1294 }] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("Block schema must be 1 but is 2", Assert.Throws<InvalidOperationException>(() => (blocks with { SchemaVersion = 2 }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_duplicate_archive_key()
    {
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord first = blocks.Records[0];

        Assert.Contains("twice, so one of them is unreachable", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = [first, first with { Ordinal = 1 }, .. blocks.Records.Skip(2)] }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_builder_cites_the_archive_to_the_documented_inventory()
    {
        byte[] bytes = NamedArchive(("B0000000.RDI", new byte[512]));

        Assert.Contains("but the documented inventory places CNT-005 at", Assert.Throws<InvalidOperationException>(() => DaggerfallBlocksBuilder.Build(bytes, "elsewhere/BLOCKS.BSA", Inventory())).Message, StringComparison.Ordinal);
        Assert.Contains("does not carry family 'CNT-005'", Assert.Throws<InvalidOperationException>(() => DaggerfallBlocksBuilder.Build(bytes, "local/arena2/BLOCKS.BSA", [])).Message, StringComparison.Ordinal);
        Assert.Equal("local/arena2/BLOCKS.BSA", DaggerfallBlocksBuilder.Build(bytes, "local/arena2/BLOCKS.BSA", Inventory()).Sources[0].Path);
    }

    [Fact]
    public void Reads_a_named_archive_whose_directory_lists_its_records()
    {
        // The archive reader is the existing owner of this container, so the inventory is built on it
        // rather than on a second directory walk: a record's offset is where the payloads before it end.
        byte[] bytes = NamedArchive(("B0000000.RDI", new byte[512]), ("FOO", [1, 2, 3]), ("B0000001.RDI", new byte[512]));

        BlockRecordInventory inventory = BlockRecordInventoryReader.Read(bytes, "local/arena2/BLOCKS.BSA");

        Assert.Equal(3, inventory.DeclaredRecords);
        Assert.Equal([4L, 516L, 519L], inventory.Records.Select(record => record.Offset));
        Assert.Equal([512, 3, 512], inventory.Records.Select(record => record.ByteLength));
        Assert.Equal([BlockRecordKind.Rdi, BlockRecordKind.Unknown, BlockRecordKind.Rdi], inventory.Records.Select(record => record.Kind));
        Assert.Equal([BlockRecordDisposition.DonorUnsupported, BlockRecordDisposition.UnknownKind, BlockRecordDisposition.DonorUnsupported], inventory.Records.Select(record => record.Disposition));
    }

    private static DaggerfallBlocks Supplied() => DaggerfallBlocksBuilder.Build(
        File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/BLOCKS.BSA")),
        "local/arena2/BLOCKS.BSA",
        Inventory());

    private static IReadOnlyList<SourceInventoryRow> Inventory() => SourceManifestBuilder.ReadInventory(
        File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));

    private static IReadOnlyList<DaggerfallBlockRecord> Replaced(DaggerfallBlocks blocks, int ordinal, DaggerfallBlockRecord record) =>
        [.. blocks.Records.Select(value => value.Ordinal == ordinal ? record : value)];

    /// <summary>
    /// Builds the named BSA variant: a header, the payloads end to end, and a directory of fourteen-byte
    /// names and four-byte lengths at the end of the file.
    /// </summary>
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
            byte[] name = Encoding.ASCII.GetBytes(records[index].Name);
            name.CopyTo(bytes, directory + (index * Arena2FormatConstants.NamedBsaDirectoryEntryBytes));
            int length = records[index].Payload.Length;
            int offset = directory + (index * Arena2FormatConstants.NamedBsaDirectoryEntryBytes) + 14;
            bytes[offset] = (byte)length;
            bytes[offset + 1] = (byte)(length >> 8);
            bytes[offset + 2] = (byte)(length >> 16);
            bytes[offset + 3] = (byte)(length >> 24);
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
