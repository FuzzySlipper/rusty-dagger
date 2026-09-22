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
        Assert.Equal(0, building.PaddingBytes);

        // A sub-record is a pair of halves. A summary that stopped at the outside half would state one
        // object for this building, which declares seven.
        Assert.Equal(new DaggerfallBlockObjectCounts(1, 0, 0, 0, 0), building.Exterior);
        Assert.Equal(new DaggerfallBlockObjectCounts(6, 7, 0, 0, 0), building.Interior);
        Assert.Equal(83 + 532, building.ByteLength);

        // Every city block's own header has to account for the bytes its record spans: the fixed header,
        // the sub-records it declares, the objects that follow them, and whatever the record carries past
        // both. Each sub-record in turn has to account for its own two halves and its padding. Those
        // identities are what make the offsets here positions rather than guesses, and the supplied corpus
        // satisfies both exactly for all 920 records and all 9005 sub-records.
        int slots = 0;
        int paddings = 0;
        foreach (DaggerfallBlockRecord record in blocks.Records.Where(record => record.Kind == DaggerfallBlockKind.Rmb))
        {
            int accounted = DaggerfallBlocks.RmbHeaderBytes + record.Rmb!.TrailingBytes
                + (record.Rmb.Misc3dObjects * RmbObjectCounts.ModelRecordBytes)
                + (record.Rmb.MiscFlatObjects * RmbObjectCounts.FlatRecordBytes)
                + record.Rmb.Buildings.Sum(value => value.ByteLength);
            Assert.Equal(record.ByteLength, accounted);
            Assert.Equal(0, record.Rmb.TrailingBytes);
            foreach (DaggerfallBlockBuilding value in record.Rmb.Buildings)
            {
                Assert.Equal(value.ByteLength, Body(value) + value.PaddingBytes);
                Assert.InRange(value.PaddingBytes, 0, 1);
                slots++;
                paddings += value.PaddingBytes;
            }
        }

        Assert.Equal(9005, slots);
        Assert.Equal(5549, paddings);

        // The header states a name for itself, and in the supplied corpus it is the archive key the record
        // is stored under, which is what establishes the header's shape in the first place.
        Assert.All(
            blocks.Records.Where(record => record.Rmb is not null),
            record => Assert.Equal(record.SourceKey, record.Rmb!.Name));
    }

    [Fact]
    public void Reads_a_buildings_slot_values_where_the_donor_reads_them()
    {
        // The building slot is twenty-six bytes and its faction sits at eighteen, after four words of an
        // uninterpreted kind. Reading it from the word before states faction zero for every building in the
        // corpus while looking like a complete answer: 460 slots name a faction, the largest of them 65535.
        byte[] bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/BLOCKS.BSA"));
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord mark = blocks.Records.Single(record => record.SourceKey == "MARKAA00.RMB");
        DaggerfallBlockBuilding slot = mark.Rmb!.Buildings[1];

        Assert.Equal(510, slot.FactionId);
        Assert.Equal(mark.Offset + 643 + 26 + 18, mark.Offset + 643 + 26 + 18);
        Assert.Equal(slot.FactionId, bytes[mark.Offset + 643 + 26 + 18] | (bytes[mark.Offset + 643 + 26 + 19] << 8));
        Assert.Equal(slot.NameSeed, bytes[mark.Offset + 643 + 26] | (bytes[mark.Offset + 643 + 26 + 1] << 8));
        Assert.Equal(slot.BuildingType, bytes[mark.Offset + 643 + 26 + 24]);
        Assert.Equal(slot.Quality, bytes[mark.Offset + 643 + 26 + 25]);

        DaggerfallBlockBuilding[] named = [.. blocks.Records
            .Where(record => record.Kind == DaggerfallBlockKind.Rmb)
            .SelectMany(record => record.Rmb!.Buildings)
            .Where(building => building.FactionId != 0)];

        Assert.Equal(460, named.Length);
        Assert.Equal(65535, named.Max(building => building.FactionId));
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

        Assert.Contains("carries ordinal 0 where the section is ordered by ordinal and the previous record of 'local/arena2/BLOCKS.BSA' was 0", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, 1, second with { Ordinal = 0 }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("carries ordinal 3 where the section is ordered by ordinal and the previous record of 'local/arena2/BLOCKS.BSA' was 0", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, 1, second with { Ordinal = 3 }) }).Validate()).Message, StringComparison.Ordinal);
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
        Assert.Contains("fewer than the two", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = wall.Rmb! with { Buildings = [wall.Rmb!.Buildings[0] with { ByteLength = 4 }], TrailingBytes = wall.Rmb!.TrailingBytes + 611 } }) }).Validate()).Message, StringComparison.Ordinal);

        // A sub-record's own size is derived from what its halves declare, so a summary whose halves do not
        // add up to the bytes it reserves describes a building that cannot be that size.
        Assert.Contains("states building 0 places", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = wall.Rmb! with { Buildings = [wall.Rmb!.Buildings[0] with { PaddingBytes = 1 }] } }) }).Validate()).Message, StringComparison.Ordinal);

        // The other direction matters as much: a sub-record whose halves account for fewer bytes than it
        // reserves is a building that cannot be that size either. MARKAA00's second sub-record reserves one
        // padding byte, so publishing none of it leaves the halves one byte short.
        DaggerfallBlockRecord mark = blocks.Records.Single(record => record.SourceKey == "MARKAA00.RMB");
        Assert.Equal(1, mark.Rmb!.Buildings[1].PaddingBytes);
        Assert.Contains("states building 1 places", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, mark.Ordinal, mark with { Rmb = mark.Rmb! with { Buildings = [.. mark.Rmb!.Buildings.Select((building, index) => index == 1 ? building with { PaddingBytes = 0 } : building)] } }) }).Validate()).Message, StringComparison.Ordinal);
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
    public void Orders_each_sources_ordinals_separately()
    {
        // A record's ordinal is its own archive's directory position, so two archives both begin at zero.
        // Reading the ordinals as one global sequence would refuse a section the producer can build.
        BlockRecordInventory first = BlockRecordInventoryReader.Read(NamedArchive(("B0000000.RDI", new byte[512]), ("B0000001.RDI", new byte[512])), "first/BLOCKS.BSA");
        BlockRecordInventory second = BlockRecordInventoryReader.Read(NamedArchive(("B0000002.RDI", new byte[512])), "second/BLOCKS.BSA");
        DaggerfallBlocks blocks = new(
            DaggerfallBlocks.CurrentSchemaVersion,
            [Source(first, "first/BLOCKS.BSA"), Source(second, "second/BLOCKS.BSA")],
            [.. first.Records.Select(record => Publish(record, "first/BLOCKS.BSA")), .. second.Records.Select(record => Publish(record, "second/BLOCKS.BSA"))]);

        blocks.Validate();
        Assert.Equal([0, 1, 0], blocks.Records.Select(record => record.Ordinal));

        // The same ordinals out of order inside one source are still refused, and so is a source that
        // reappears after another one has been published.
        DaggerfallBlocks skipped = blocks with { Records = [.. blocks.Records.Take(1), blocks.Records[1] with { Ordinal = 5 }, blocks.Records[2]] };
        Assert.Contains("carries ordinal 5 where the section is ordered by ordinal and the previous record of 'first/BLOCKS.BSA' was 0", Assert.Throws<InvalidOperationException>(() => skipped.Validate()).Message, StringComparison.Ordinal);

        DaggerfallBlocks interleaved = blocks with { Records = [.. blocks.Records, blocks.Records[0]] };
        Assert.Contains("is interleaved with another source rather than grouped", Assert.Throws<InvalidOperationException>(() => interleaved.Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_source_whose_declared_count_is_not_what_the_section_carries()
    {
        DaggerfallBlocks blocks = Supplied();

        Assert.Contains("declares 1294 records and publishes 1295, where the section carries 1295", Assert.Throws<InvalidOperationException>(() => (blocks with { Sources = [blocks.Sources[0] with { DeclaredLength = 1294 }] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("Block schema must be 2 but is 3", Assert.Throws<InvalidOperationException>(() => (blocks with { SchemaVersion = 3 }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_summary_on_a_record_the_donor_reads_as_unknown_bytes()
    {
        // A block index and a record with no extension carry no layout this product reads, so a summary
        // attached to one would describe bytes nothing decoded.
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord index = blocks.Records.First(record => record.Kind == DaggerfallBlockKind.Rdi);
        DaggerfallBlockRecord unknown = blocks.Records.First(record => record.Kind == DaggerfallBlockKind.Unknown);
        DaggerfallBlockRmbHeader header = blocks.Records.First(record => record.Rmb is not null).Rmb!;

        Assert.Contains("the donor reads as unknown bytes", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, index.Ordinal, index with { Rmb = header }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("the donor reads as unknown bytes", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, unknown.Ordinal, unknown with { Objects = blocks.Records.First(record => record.Objects is not null).Objects }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_header_whose_counts_exceed_the_widths_the_source_stores_them_in()
    {
        // The declared count, the two object counts and the other-name count all lead the header as single
        // bytes, so a wider number cannot have come from the file it claims to describe.
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord wall = blocks.Records.Single(record => record.SourceKey == "WALLAA03.RMB");

        Assert.Contains("where the header holds 32 slots", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = wall.Rmb! with { DeclaredBlocks = 33, Buildings = [.. Enumerable.Repeat(wall.Rmb!.Buildings[0], 33)] } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("negative or impossible header counts", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = wall.Rmb! with { Misc3dObjects = 65_074_648 } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("negative or impossible header counts", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = wall.Rmb! with { OtherNames = 33 } }) }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("outside the widths the source stores them in", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = wall.Rmb! with { Buildings = [wall.Rmb!.Buildings[0] with { Exterior = wall.Rmb!.Buildings[0].Exterior with { Objects = 256 } }] } }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_building_whose_halves_do_not_account_for_the_bytes_it_reserves()
    {
        // The record-level sum alone cannot catch a lie that moves bytes between a block's own object count
        // and one building's size, because the two cancel. Each sub-record's size is derived from its own
        // halves, so the same lie is refused where it is stated.
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord wall = blocks.Records.Single(record => record.SourceKey == "WALLAA03.RMB");
        DaggerfallBlockBuilding building = wall.Rmb!.Buildings[0];
        int moved = RmbObjectCounts.ModelRecordBytes;

        DaggerfallBlocks shifted = blocks with
        {
            Records = Replaced(blocks, wall.Ordinal, wall with
            {
                Rmb = wall.Rmb! with
                {
                    Misc3dObjects = 1,
                    Buildings = [building with { ByteLength = building.ByteLength - moved }],
                },
            }),
        };

        Assert.Contains("states building 0 places", Assert.Throws<InvalidOperationException>(() => shifted.Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_header_name_that_is_not_the_key_it_is_stored_under()
    {
        // The name a block states for itself is the identity a lookup inside the file uses, and in the
        // supplied corpus it is exactly the archive key.
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord wall = blocks.Records.Single(record => record.SourceKey == "WALLAA03.RMB");

        Assert.Contains("states the name 'OTHER.RMB' for itself where its own key says 'WALLAA03.RMB'", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { Rmb = wall.Rmb! with { Name = "OTHER.RMB" } }) }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_name_shape_the_donor_could_not_have_composed()
    {
        // The donor's composer writes a padded number or a temple letter and number, never a single digit,
        // so the shape claim is a function of the name rather than a fact to be trusted.
        DaggerfallBlocks blocks = Supplied();
        DaggerfallBlockRecord wall = blocks.Records.Single(record => record.SourceKey == "WALLAA03.RMB");

        Assert.Contains("is one the donor composes", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, wall with { RmbName = wall.RmbName! with { DonorShape = false } }) }).Validate()).Message, StringComparison.Ordinal);

        // A second letter the donor never writes is not one of its shapes, whatever the record claims. The
        // name, the key and the header all have to agree before the shape is the only thing wrong with it.
        DaggerfallBlockRecord other = wall with
        {
            SourceKey = "WALLAB03.RMB",
            RmbName = wall.RmbName! with { Letter2 = "B" },
            Rmb = wall.Rmb! with { Name = "WALLAB03.RMB" },
        };

        Assert.Contains("not a shape the donor composes", Assert.Throws<InvalidOperationException>(() => (blocks with { Records = Replaced(blocks, wall.Ordinal, other) }).Validate()).Message, StringComparison.Ordinal);

        // And the reader never claims it: the composer writes at least two characters for a number, so a
        // single digit is not a shape it produces, while a temple's letter-and-number is.
        Assert.True(BlockRecordInventoryReader.RmbName("WALLAA03.RMB")!.DonorShape);
        Assert.True(BlockRecordInventoryReader.RmbName("WALLAL05.RMB")!.DonorShape);
        Assert.False(BlockRecordInventoryReader.RmbName("WALLAL5.RMB")!.DonorShape);
        Assert.True(BlockRecordInventoryReader.RmbName("TEMPAAB0.RMB")!.DonorShape);

        Assert.False(BlockRecordInventoryReader.RmbName("WALLAA3.RMB")!.DonorShape);

        // A name too short to hold the composer's own shape is not classified at all rather than classified
        // and denied, because nothing says which of its letters is which.
        Assert.Null(BlockRecordInventoryReader.RmbName("WALLAA.RMB"));
    }

    [Fact]
    public void Publishes_a_record_the_archive_states_has_no_bytes()
    {
        // An archive can state a record with no bytes at all. For a kind the donor reads as unknown data
        // there is nothing to read and nothing lost, so it is published with its length rather than
        // abandoning the inventory of every other record.
        DaggerfallBlocks blocks = DaggerfallBlocksBuilder.Build(
            NamedArchive(("Z0000000.RDI", []), ("B0000000.RDI", new byte[512])),
            "local/arena2/BLOCKS.BSA",
            Inventory());

        Assert.Equal([DaggerfallBlockState.Read, DaggerfallBlockState.Read], blocks.Records.Select(record => record.State));
        Assert.Equal([0, 512], blocks.Records.Select(record => record.ByteLength));
    }

    [Fact]
    public void Refuses_a_city_block_header_it_cannot_read()
    {
        // Each of these declares a shape the bytes cannot hold: more sub-records than the header has slots,
        // a sub-record too small for its two halves, halves whose records overrun the size reserved for
        // them, and a block whose own object count runs past the record.
        DaggerfallBlocks blocks = DaggerfallBlocksBuilder.Build(
            NamedArchive(
                ("MANY.RMB", Rmb(33, new int[33])),
                ("TINY.RMB", Rmb(1, [33])),
                ("OVER.RMB", Rmb(1, [34], exteriorObjects: 1)),
                ("INSIDE.RMB", Rmb(1, [35], interiorObjects: 1)),
                ("MISC.RMB", Rmb(1, [34], misc3d: 1))),
            "local/arena2/BLOCKS.BSA",
            Inventory());

        Assert.All(blocks.Records, record => Assert.Equal(DaggerfallBlockState.Malformed, record.State));
        Assert.Contains("more than the header's 32 slots", blocks.Records[0].Reason, StringComparison.Ordinal);
        Assert.Contains("cannot hold its two 17-byte halves", blocks.Records[1].Reason, StringComparison.Ordinal);
        Assert.Contains("leaves no room for its inside half", blocks.Records[2].Reason, StringComparison.Ordinal);
        Assert.Contains("past the 35 bytes it reserves", blocks.Records[3].Reason, StringComparison.Ordinal);
        Assert.Contains("past the record's", blocks.Records[4].Reason, StringComparison.Ordinal);

        // A sub-record whose halves fill exactly what it reserves is readable, padding and all.
        DaggerfallBlocks readable = DaggerfallBlocksBuilder.Build(NamedArchive(("FINE.RMB", Rmb(1, [35], name: "FINE.RMB"))), "local/arena2/BLOCKS.BSA", Inventory());
        Assert.Equal(DaggerfallBlockState.Read, readable.Records[0].State);
        Assert.Equal(1, readable.Records[0].Rmb!.Buildings[0].PaddingBytes);
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

    private static void Write32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>
    /// Builds a city block record of the given sub-record sizes, with each sub-record's two halves declaring
    /// nothing unless a count is asked for.
    /// </summary>
    private static byte[] Rmb(byte declared, int[] sizes, string name = "", byte exteriorObjects = 0, byte interiorObjects = 0, byte misc3d = 0)
    {
        byte[] bytes = new byte[RmbBlockSummaryReader.HeaderBytes + sizes.Sum()];
        bytes[0] = declared;
        bytes[1] = misc3d;
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, RmbBlockSummaryReader.NameOffset);
        int position = RmbBlockSummaryReader.HeaderBytes;
        for (int index = 0; index < sizes.Length; index++)
        {
            Write32(bytes, 1603 + (index * 4), sizes[index]);
            if (index == 0 && sizes[index] != 0)
            {
                bytes[position] = exteriorObjects;
                if (sizes[index] >= RmbBlockSummaryReader.SubRecordHeaderBytes * 2)
                {
                    bytes[position + RmbBlockSummaryReader.SubRecordHeaderBytes] = interiorObjects;
                }
            }

            position += sizes[index];
        }

        return bytes;
    }

    /// <summary>The bytes a sub-record's two halves occupy, headers included, without its padding.</summary>
    private static int Body(DaggerfallBlockBuilding building) =>
        (DaggerfallBlocks.RmbBuildingHeaderBytes * 2)
        + Half(building.Exterior)
        + Half(building.Interior);

    private static int Half(DaggerfallBlockObjectCounts counts) =>
        (counts.Objects * RmbObjectCounts.ModelRecordBytes)
        + (counts.Flats * RmbObjectCounts.FlatRecordBytes)
        + (counts.Sections * RmbObjectCounts.SectionRecordBytes)
        + (counts.People * RmbObjectCounts.PeopleRecordBytes)
        + (counts.Doors * RmbObjectCounts.DoorRecordBytes);

    private static DaggerfallBlockSource Source(BlockRecordInventory inventory, string path) =>
        new("CNT-005", path, 4 + inventory.Records.Sum(record => record.ByteLength), inventory.DeclaredRecords, inventory.Records.Count);

    private static DaggerfallBlockRecord Publish(BlockRecord record, string path) =>
        new(
            record.Ordinal,
            record.SourceKey,
            path,
            (DaggerfallBlockKind)record.Kind,
            (DaggerfallBlockDisposition)record.Disposition,
            record.Offset,
            record.ByteLength,
            (DaggerfallBlockState)record.State,
            record.Reason,
            null,
            null,
            null,
            null);

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
