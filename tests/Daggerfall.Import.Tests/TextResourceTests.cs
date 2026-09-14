using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The classic text resource as a normalized pack section: which records it declares, how their bytes
/// tokenize, and what the corpus's own macro usage is. The fixtures are built here rather than taken
/// from the corpus, because a malformed record is a case the supplied file does not contain and the
/// behaviour it needs - a key that resolves to a stated reason instead of to nothing - is the whole
/// point of the disposition.
/// </summary>
public sealed class TextResourceTests
{
    [Fact]
    public void Reads_records_tokens_variants_and_macros_from_a_resource()
    {
        Arena2TextCatalog catalog = TextResourceReader.Read(
            Resource(
                (7, [.. "Hello %cn. "u8, 0xfc, .. "Second"u8]),
                (9, [.. "One"u8, 0xff, .. "Two"u8]),
                (11, [.. "at "u8, 0xfb, 0x05, .. "x"u8])),
            "fixture/TEXT.RSC");

        Assert.Equal(24, catalog.HeaderLength);
        Assert.Equal(26, catalog.DataStart);
        Assert.Equal([7, 9, 11], catalog.Records.Select(record => record.Id));
        Assert.All(catalog.Records, record => Assert.Equal(Arena2TextState.Read, record.State));
        Assert.All(catalog.Records, record => Assert.Empty(record.Reason));

        // The record's first tokens: the characters up to the justification code, the code itself, and
        // the text after it. A reader that dropped the codes would leave the text run together, and one
        // that dropped the runs would lose the words between them.
        Arena2TextRecord first = catalog.Records[0];
        Assert.Equal([Arena2TextCode.Text, Arena2TextCode.JustifyLeft, Arena2TextCode.Text], first.Tokens.Select(token => token.Code));
        Assert.Equal("Hello %cn. ", first.Tokens[0].Text);
        Assert.Equal("Second", first.Tokens[2].Text);
        Assert.Equal((int)Arena2TextCode.JustifyLeft, first.Tokens[1].Value);
        Assert.Equal(["%cn"], first.Macros);
        Assert.Equal(1, first.Subrecords);

        // A separator divides the record into variants the donor selects between when it answers with a
        // random record, so the count is what a consumer needs to see rather than an implementation note.
        Assert.Equal(2, catalog.Records[1].Subrecords);
        Assert.Equal([Arena2TextCode.Text, Arena2TextCode.SubrecordSeparator, Arena2TextCode.Text], catalog.Records[1].Tokens.Select(token => token.Code));
        Assert.Empty(catalog.Records[1].Macros);

        // A position prefix states where the text after it sits, and the byte it takes is the payload
        // rather than text.
        Arena2TextRecord third = catalog.Records[2];
        Assert.Equal([Arena2TextCode.Text, Arena2TextCode.PositionPrefix, Arena2TextCode.Text], third.Tokens.Select(token => token.Code));
        Assert.Equal(5, third.Tokens[1].X);
        Assert.Equal((int)Arena2TextCode.PositionPrefix, third.Tokens[1].Value);

        // Each record's span includes its terminator, which is what makes the length a property of the
        // bytes: the reader does not infer it from the next record's offset.
        Assert.Equal(19, first.ByteLength);
        Assert.Equal(26, first.Offset);
        Assert.Equal(26 + first.ByteLength, catalog.Records[1].Offset);
    }

    [Fact]
    public void The_position_prefix_takes_the_byte_after_it_out_of_the_text()
    {
        // The donor's own reader consumes the byte after a position prefix without asking whether it is
        // printable, so "c" here is the payload and only "d" is text. A reader that treated the payload
        // as text would publish a position of zero and a word the donor never produces.
        Arena2TextCatalog catalog = TextResourceReader.Read(
            Resource(
                (1, [.. "ab"u8, 0xfb, .. "cd"u8]),
                (2, [.. "ab"u8, 0xfb, 0x05, 0xfc])),
            "fixture/TEXT.RSC");

        Arena2TextRecord record = catalog.Records[0];
        Assert.Equal([Arena2TextCode.Text, Arena2TextCode.PositionPrefix, Arena2TextCode.Text], record.Tokens.Select(token => token.Code));
        Assert.Equal("ab", record.Tokens[0].Text);
        Assert.Equal('c', record.Tokens[1].X);
        Assert.Equal("d", record.Tokens[2].Text);
        Assert.Equal(0, record.Tokens[2].X);

        // The payload belongs to the prefix that stated it, so the code after one carries none of its
        // own: a tokenizer that kept one payload for the whole record would give every later code a
        // position the source never stated, which only a code can show because a run is always zero.
        Arena2TextRecord preceded = catalog.Records[1];
        Assert.Equal([Arena2TextCode.Text, Arena2TextCode.PositionPrefix, Arena2TextCode.JustifyLeft], preceded.Tokens.Select(token => token.Code));
        Assert.Equal(5, preceded.Tokens[1].X);
        Assert.Equal(0, preceded.Tokens[2].X);
    }

    [Fact]
    public void A_font_prefix_at_the_end_of_a_record_takes_the_terminator_rather_than_reading_past_it()
    {
        // The terminator is part of the buffer the donor tokenizes, so a font prefix may take it as its
        // payload and end the record. Reading the byte after the record instead would take the next
        // record's first character and silently change both records.
        Arena2TextCatalog catalog = TextResourceReader.Read(
            Resource(
                (1, [.. "x"u8, 0xf9]),
                (2, "second"u8.ToArray())),
            "fixture/TEXT.RSC");

        Arena2TextRecord first = catalog.Records[0];
        Assert.Equal([Arena2TextCode.Text, Arena2TextCode.FontPrefix], first.Tokens.Select(token => token.Code));
        Assert.Equal((int)TextResourceReader.Terminator, first.Tokens[1].X);
        Assert.Equal("second", catalog.Records[1].Tokens[0].Text);
    }

    [Fact]
    public void A_run_covers_the_donors_own_character_range_and_nothing_below_it()
    {
        // The donor's range is 0x20 through 0x7f inclusive, so 0x7f stays inside a run even though no
        // supplied record carries one, and every byte below the range is a code. The boundary is the
        // donor's own and is pinned here rather than left to whatever the corpus happens to contain.
        Arena2TextCatalog catalog = TextResourceReader.Read(
            Resource(
                (1, [.. "a"u8, 0x7f, .. "b"u8]),
                (2, [.. "a"u8, 0x1f, .. "b"u8])),
            "fixture/TEXT.RSC");

        Assert.Equal([Arena2TextCode.Text], catalog.Records[0].Tokens.Select(token => token.Code));
        Assert.Equal("a\u007fb", catalog.Records[0].Tokens[0].Text);
        Assert.Equal([Arena2TextCode.Text, Arena2TextCode.Unknown, Arena2TextCode.Text], catalog.Records[1].Tokens.Select(token => token.Code));
        Assert.Equal(0x1f, catalog.Records[1].Tokens[1].Value);
    }

    [Fact]
    public void An_unnamed_code_keeps_the_byte_it_came_from()
    {
        // The supplied corpus carries seven bytes 0x14 that the donor's own enum does not name. Dropping
        // them would leave the surrounding runs joined, and naming them would claim a meaning the donor
        // does not state, so the byte is what survives.
        Arena2TextCatalog catalog = TextResourceReader.Read(Resource((1, [.. "a"u8, 0x14, .. "b"u8])), "fixture/TEXT.RSC");

        Arena2TextToken unnamed = catalog.Records[0].Tokens[1];
        Assert.Equal(Arena2TextCode.Unknown, unnamed.Code);
        Assert.Equal(0x14, unnamed.Value);
        Assert.Empty(unnamed.Text);
        Assert.Equal(["a", "b"], catalog.Records[0].Tokens.Where(token => token.Code == Arena2TextCode.Text).Select(token => token.Text));
    }

    [Fact]
    public void A_macro_runs_to_the_first_terminator_and_glued_text_stays_in_the_run()
    {
        Arena2TextCatalog catalog = TextResourceReader.Read(
            Resource((1, "%di|ern %cn, %key? %a%b"u8.ToArray())),
            "fixture/TEXT.RSC");

        // The donor's own terminators decide where a symbol ends, so the bar ends one, the comma ends the
        // next, and a second marker ends a symbol with an empty name rather than starting a new scan.
        Assert.Equal(["%di", "%cn", "%key", "%a", "%b"], catalog.Records[0].Macros);

        // The text itself is untouched. "ern" is what makes "southern" out of a direction, so a reader
        // that stripped the glue would leave the expander nothing to attach the suffix to.
        Assert.Equal("%di|ern %cn, %key? %a%b", catalog.Records[0].Tokens[0].Text);
    }

    [Fact]
    public void Refuses_a_directory_that_does_not_end_with_the_formats_sentinel()
    {
        // The record count is one less than the directory declares, so the slot it stops short of has to be
        // the sentinel. A directory ending another way has not described its own extent, and reading one
        // record fewer than it declares would drop its last record without saying so.
        byte[] bytes = Resource((1, "one"u8.ToArray()), (2, "two"u8.ToArray()));
        int sentinel = TextResourceReader.HeaderLengthBytes + (2 * TextResourceReader.DirectoryEntryBytes);
        Write16(bytes, sentinel, 3);
        Write32(bytes, sentinel + 2, 0);

        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => TextResourceReader.Read(bytes, "fixture/TEXT.RSC"));

        Assert.Contains($"ends its directory at id 3 pointing at 0 where the format ends it with the sentinel {Arena2FormatConstants.ClassicDirectorySentinelId}", error.Message, StringComparison.Ordinal);
        Assert.Contains("so the record it describes would be dropped", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_resource_that_declares_one_key_twice()
    {
        // Two directory entries claiming one key leave one of them unreachable through every lookup the
        // contract offers, so the file is refused where the donor's own key dictionary would refuse it.
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => TextResourceReader.Read(
            Resource((5, "one"u8.ToArray()), (6, "two"u8.ToArray()), (5, "three"u8.ToArray())),
            "fixture/TEXT.RSC"));

        Assert.Contains("declares record 5 twice", error.Message, StringComparison.Ordinal);
        Assert.Contains("index 0 and index 2", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(13, "not a whole number")]
    [InlineData(6, "carries no text")]
    public void Refuses_a_directory_it_cannot_read(int headerLength, string expected)
    {
        byte[] bytes = Resource((1, "text"u8.ToArray()));
        bytes[0] = (byte)(headerLength & 0xff);
        bytes[1] = (byte)(headerLength >> 8);

        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => TextResourceReader.Read(bytes, "fixture/TEXT.RSC"));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_directory_that_runs_past_the_file()
    {
        // A directory declaring more than the file holds cannot be walked without inventing bytes, and a
        // partial read would look exactly like a complete one.
        byte[] bytes = new byte[14];
        Write16(bytes, 0, 18);
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => TextResourceReader.Read(bytes, "fixture/TEXT.RSC"));

        Assert.Contains("past its 14 bytes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_whose_offset_points_inside_the_directory_is_published_as_malformed()
    {
        byte[] bytes = Resource((4, "readable"u8.ToArray()));
        bytes[4] = 0x02;

        Arena2TextRecord record = TextResourceReader.Read(bytes, "fixture/TEXT.RSC").Records[0];

        // The key exists in the source either way, so it is published with the reason rather than
        // dropped: a lookup answering "no such text" would report a source defect as an absence.
        Assert.Equal(4, record.Id);
        Assert.Equal(Arena2TextState.Malformed, record.State);
        Assert.Contains("inside the directory", record.Reason, StringComparison.Ordinal);
        Assert.Empty(record.Tokens);
        Assert.Equal(0, record.ByteLength);
        Assert.Equal(0, record.Subrecords);
    }

    [Fact]
    public void A_record_with_no_terminator_is_published_as_malformed()
    {
        // A record whose text runs to the end of the file declares no end, so its length would be the
        // rest of the file and its last byte would be text the source never terminated.
        byte[] bytes = Resource((1, "one"u8.ToArray()), (2, "two"u8.ToArray()));
        bytes[^1] = 0x20;

        IReadOnlyList<Arena2TextRecord> records = TextResourceReader.Read(bytes, "fixture/TEXT.RSC").Records;

        Assert.Equal(Arena2TextState.Read, records[0].State);
        Assert.Equal(Arena2TextState.Malformed, records[1].State);
        Assert.Contains("carries no FE terminator", records[1].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_that_names_another_records_terminator_reads_as_a_record_with_no_words()
    {
        // The donor reads each entry from its own offset to the next terminator, so an entry naming the
        // terminator another record ends at reads length one and no tokens. Refusing it as an overlap
        // would publish text the donor reads as unreadable, and inventing a rule here is exactly what
        // the reader must not do.
        byte[] bytes = Resource((1, "abcdefgh"u8.ToArray()), (2, "second"u8.ToArray()));
        Write32(bytes, OffsetField(1), DataStart(2) + 8);

        IReadOnlyList<Arena2TextRecord> records = TextResourceReader.Read(bytes, "fixture/TEXT.RSC").Records;

        Assert.Equal([Arena2TextState.Read, Arena2TextState.Read], records.Select(record => record.State));
        Assert.Equal(1, records[1].ByteLength);
        Assert.Empty(records[1].Tokens);
        Assert.Empty(records[1].Macros);
        Assert.Equal(1, records[1].Subrecords);

        // The same shape survives publication: a value the source leaves empty is readable and carries
        // no words, which is the source's fact rather than a value that could not be read.
        DaggerfallText text = DaggerfallTextBuilder.Build(bytes, "local/arena2/TEXT.RSC", Inventory(), "en");
        DaggerfallTextRecord published = text.Records[1];
        Assert.Equal(Arena2TextState.Read, published.State);
        Assert.Empty(published.Tokens);
        Assert.Equal(1, published.ByteLength);
    }

    [Fact]
    public void A_record_that_names_a_byte_inside_another_records_run_reads_its_own_suffix()
    {
        // An entry may name a byte a neighbour's run passes through. The directory is the authority on
        // where a record's text is, so the record reads from there to the next terminator; the donor
        // does the same, and both records stay addressable.
        byte[] bytes = Resource((1, "abcdefgh"u8.ToArray()), (2, "second"u8.ToArray()));
        Write32(bytes, OffsetField(1), DataStart(2) + 4);

        IReadOnlyList<Arena2TextRecord> records = TextResourceReader.Read(bytes, "fixture/TEXT.RSC").Records;

        Assert.Equal([Arena2TextState.Read, Arena2TextState.Read], records.Select(record => record.State));
        Assert.Equal("efgh", records[1].Tokens[0].Text);
        Assert.Equal(5, records[1].ByteLength);
    }

    [Fact]
    public void Two_keys_may_read_one_region_of_the_resource()
    {
        // The corpus shares twelve regions between keys, and the donor reads each entry's own bytes, so
        // both keys address the same text. Treating that as an overlap would publish readable text as
        // malformed and collapse two keys the source keeps apart.
        byte[] bytes = Resource((1, "shared text"u8.ToArray()), (2, "second"u8.ToArray()));
        Write32(bytes, OffsetField(1), DataStart(2));

        IReadOnlyList<Arena2TextRecord> records = TextResourceReader.Read(bytes, "fixture/TEXT.RSC").Records;

        Assert.Equal([Arena2TextState.Read, Arena2TextState.Read], records.Select(record => record.State));
        Assert.Equal(records[0].Offset, records[1].Offset);
        Assert.Equal(records[0].ByteLength, records[1].ByteLength);
        Assert.Equal("shared text", records[0].Tokens[0].Text);
        Assert.Equal("shared text", records[1].Tokens[0].Text);
    }

    [Fact]
    public void Publishes_the_supplied_corpus_with_every_record_accounted_for()
    {
        DaggerfallText text = Supplied();

        DaggerfallTextSource source = Assert.Single(text.Sources);
        Assert.Equal(DaggerfallTextKind.Resource, source.Kind);
        Assert.Equal("CNT-016", source.RecordId);
        Assert.Equal("local/arena2/TEXT.RSC", source.Path);
        Assert.Equal("en", source.Language);
        Assert.Equal(353393, source.ByteLength);
        Assert.Equal(8454, source.DeclaredLength);
        Assert.Equal(1408, source.Records);
        Assert.Equal(1408, text.Records.Count);
        Assert.All(text.Records, record => Assert.Equal(Arena2TextState.Read, record.State));
        Assert.Equal(Enumerable.Range(0, 1408), text.Records.Select(record => record.Index));

        // The directory covers the record region exactly: 1396 distinct offsets for 1408 entries, so
        // twelve entries share another key's region, and the last record ends at the file's last byte.
        Assert.Equal(1396, text.Records.Select(record => record.Offset).Distinct().Count());
        IReadOnlyList<DaggerfallTextRecord> byOffset = [.. text.Records.OrderBy(record => record.Offset)];
        Assert.Equal(source.ByteLength, byOffset[^1].Offset + byOffset[^1].ByteLength);
        Assert.All(byOffset.Zip(byOffset.Skip(1)).Where(pair => pair.First.Offset != pair.Second.Offset), pair => Assert.Equal(pair.First.Offset + pair.First.ByteLength, pair.Second.Offset));

        // The keys are the source's own, so the two records that share a region stay two keys.
        Assert.Equal("0", text.Records[0].Key.Id);
        Assert.Equal("Paralysis", text.Records.Single(record => record.Key.Id == "1201").Tokens[0].Text);
        Assert.Equal("Paralysis", text.Records.Single(record => record.Key.Id == "1202").Tokens[0].Text);
        Assert.Equal(text.Records.Single(record => record.Key.Id == "1201").Offset, text.Records.Single(record => record.Key.Id == "1202").Offset);
        Assert.Equal(5, text.Records.Single(record => record.Key.Id == "11").Subrecords);

        // The macro index is the corpus's own usage: every symbol accounted for, and the three outcomes
        // the donor's table distinguishes kept apart rather than flattened into "unresolved".
        Assert.Equal(168, text.Macros.Count);
        Assert.Equal(141, text.Macros.Count(macro => macro.Disposition == TextMacroDisposition.Handled));
        Assert.Equal(18, text.Macros.Count(macro => macro.Disposition == TextMacroDisposition.DonorUnresolved));
        Assert.Equal(9, text.Macros.Count(macro => macro.Disposition == TextMacroDisposition.Unrecognised));
        Assert.Equal(3339, Occurrences(text));
        Assert.Equal(448, text.Records.Count(record => record.Macros.Count != 0));
        Assert.Equal(TextMacroDisposition.Handled, text.Macros.Single(macro => macro.Symbol == "%str").Disposition);
        Assert.Equal(TextMacroDisposition.DonorUnresolved, text.Macros.Single(macro => macro.Symbol == "%hol").Disposition);
        Assert.Equal(TextMacroDisposition.Unrecognised, text.Macros.Single(macro => macro.Symbol == "%pc").Disposition);

        // The families this contract declares and does not fill name the tasks that supply them, so a
        // reference into a book or a biography is a legal key with a known owner rather than a gap.
        Assert.Equal(
            ["biography", "book", "name", "rumor"],
            text.PendingKinds.Select(pending => pending.Kind.ToString().ToLowerInvariant()).Order());
        Assert.All(text.PendingKinds, pending => Assert.True(pending.OwnerTask is 7951 or 7941));
    }

    [Fact]
    public void Refuses_a_variant_count_that_disagrees_with_the_separators()
    {
        // The separators a value publishes are what divide it into variants a consumer selects between,
        // so a count that disagrees with them describes a value nobody can divide.
        DaggerfallTextRecord record = Supplied().Records.Single(value => value.Key.Id == "11");

        Assert.Contains("variants where its separators divide it into", Assert.Throws<InvalidOperationException>(() => (record with { Subrecords = 99 }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_symbol_list_that_does_not_match_the_text_it_describes()
    {
        // The list is derived from the text, so it is checked against the text rather than trusted: a
        // value whose list omitted a symbol it spells would leave a resolver expanding macros nothing
        // told it to expect, and the published index is derived from these lists in turn.
        DaggerfallTextRecord record = Supplied().Records[0];

        Assert.Contains("where its text carries", Assert.Throws<InvalidOperationException>(() => (record with { Macros = [] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("where its text carries", Assert.Throws<InvalidOperationException>(() => (record with { Macros = [.. record.Macros, "%zzz"] }).Validate()).Message, StringComparison.Ordinal);

        // The list is in first-appearance order, which is what a resolver expanding in place reads, so a
        // reordered list is a different claim rather than the same one.
        Assert.Contains("where its text carries", Assert.Throws<InvalidOperationException>(() => (record with { Macros = [.. record.Macros.Reverse()] }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Publishes_a_corrupt_offset_as_the_unsigned_byte_it_declares()
    {
        // The directory states an unsigned offset, so a corrupt entry is published as the byte it
        // declares. Wrapping it into a negative one would state a byte the file never named, and the
        // published record's own validation would then refuse the value instead of the reader reporting
        // the source defect.
        foreach ((byte[] declared, long expected) in new[]
        {
            (new byte[] { 0xff, 0xff, 0xff, 0xff }, 4294967295L),
            (new byte[] { 0x00, 0x00, 0x00, 0x80 }, 2147483648L),
        })
        {
            byte[] bytes = Resource((1, "text"u8.ToArray()));
            for (int index = 0; index < declared.Length; index++)
            {
                bytes[OffsetField(0) + index] = declared[index];
            }

            DaggerfallText text = DaggerfallTextBuilder.Build(bytes, "local/arena2/TEXT.RSC", Inventory(), "en");

            Assert.Equal(expected, text.Records[0].Offset);
            Assert.Equal(Arena2TextState.Malformed, text.Records[0].State);
            Assert.Contains($"names byte {expected} for its text, past the file's", text.Records[0].Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Refuses_a_language_that_is_not_a_tag()
    {
        // The language is the caller's assertion about bytes that declare none, so a failure names the
        // source and the value it was given rather than a parameter name the caller never wrote.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DaggerfallTextBuilder.Build(
            Resource((1, "text"u8.ToArray())),
            "local/arena2/TEXT.RSC",
            Inventory(),
            "not a tag"));

        Assert.Contains("must state a language tag such as 'en'; it states 'not a tag'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_builder_refuses_bytes_that_did_not_come_from_the_documented_source()
    {
        // The inventory decides the logical source identity, so a pack cannot cite text to a file the
        // repository does not document as carrying it.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DaggerfallTextBuilder.Build(
            Resource((1, "text"u8.ToArray())),
            "elsewhere/TEXT.RSC",
            Inventory(),
            "en"));

        Assert.Contains("places CNT-016 at 'local/arena2/TEXT.RSC'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_builder_refuses_an_inventory_that_does_not_document_the_family()
    {
        byte[] csv = Encoding.UTF8.GetBytes("id,row_type,family_id,kind,path_or_pattern,available_count,byte_size,record_or_stem,current_scope,disposition,notes\n");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DaggerfallTextBuilder.Build(
            Resource((1, "text"u8.ToArray())),
            "local/arena2/TEXT.RSC",
            SourceManifestBuilder.ReadInventory(csv),
            "en"));

        Assert.Contains("does not carry family 'CNT-016'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_section_that_publishes_one_key_twice()
    {
        DaggerfallText text = Supplied();
        DaggerfallTextRecord duplicate = text.Records[1] with { Key = text.Records[0].Key };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => (text with { Records = [text.Records[0], duplicate, .. text.Records.Skip(2)] }).Validate());

        Assert.Contains("twice, so one of them is unreachable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_record_whose_source_the_section_does_not_carry()
    {
        DaggerfallText text = Supplied();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => (text with { Records = [text.Records[0] with { Source = "elsewhere/TEXT.RSC" }, .. text.Records.Skip(1)] }).Validate());

        Assert.Contains("which the section does not carry", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_malformed_record_that_still_carries_tokens_or_states_no_reason()
    {
        DaggerfallText text = Supplied();
        DaggerfallTextRecord record = text.Records[0];

        // A malformed record carrying tokens would publish bytes the reader says it could not read, and
        // one with no reason would leave a consumer with an absence and nothing to report.
        Assert.Contains("states no reason", Assert.Throws<InvalidOperationException>(() => (record with { State = Arena2TextState.Malformed, ByteLength = 0, Subrecords = 0 }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("is readable but spans", Assert.Throws<InvalidOperationException>(() => (record with { ByteLength = 0 }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("states a reason it is not", Assert.Throws<InvalidOperationException>(() => (record with { Reason = "because" }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_source_byte_the_source_cannot_state()
    {
        // The directory's offset is unsigned, so no source can name a negative byte: a published record
        // that did would be describing a position nothing could have read.
        DaggerfallTextRecord record = Supplied().Records[0];

        Assert.Contains("cannot begin at a negative source byte", Assert.Throws<ArgumentOutOfRangeException>(() => (record with { Offset = -1 }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_token_whose_members_disagree_with_its_kind()
    {
        DaggerfallText text = Supplied();
        DaggerfallTextRecord record = text.Records[0];

        // A run states its characters and nothing else, a named code states no byte, and only the two
        // prefixes state a payload: any other combination would be read as a different element than the
        // one it names.
        Assert.Contains("holds no text", Assert.Throws<InvalidOperationException>(() => (record with { Tokens = [new DaggerfallTextToken(Arena2TextCode.Text)] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("states byte 252", Assert.Throws<InvalidOperationException>(() => (record with { Tokens = [new DaggerfallTextToken(Arena2TextCode.JustifyLeft, Value: 252)] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("payload it does not take", Assert.Throws<InvalidOperationException>(() => (record with { Tokens = [new DaggerfallTextToken(Arena2TextCode.JustifyLeft, X: 4)] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("payload it does not take", Assert.Throws<InvalidOperationException>(() => (record with { Tokens = [new DaggerfallTextToken(Arena2TextCode.PositionPrefix)] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("no byte", Assert.Throws<InvalidOperationException>(() => (record with { Tokens = [new DaggerfallTextToken(Arena2TextCode.Unknown)] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("a name covers", Assert.Throws<InvalidOperationException>(() => (record with { Tokens = [new DaggerfallTextToken(Arena2TextCode.Unknown, Value: 0xfc)] }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_macro_index_that_disagrees_with_the_records()
    {
        DaggerfallText text = Supplied();
        DaggerfallTextMacro macro = text.Macros.Single(value => value.Symbol == "%str");

        // The index is derived from the records, so a consumer expanding text with a symbol the index
        // does not account for, or reading a count the records do not support, is a defect either way.
        Assert.Contains("index does not account for", Assert.Throws<InvalidOperationException>(() => (text with { Macros = [.. text.Macros.Where(value => value.Symbol != "%str")] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("where the records carry it in", Assert.Throws<InvalidOperationException>(() => (text with { Macros = [.. text.Macros.Where(value => value.Symbol != "%str"), macro with { Records = macro.Records + 1 }] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("carried by no published value", Assert.Throws<InvalidOperationException>(() => (text with { Macros = [.. text.Macros, new DaggerfallTextMacro("%zzz", 1, TextMacroDisposition.Unrecognised)] }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_macro_index_entry_nothing_carries_or_carries_twice()
    {
        // An entry for a symbol no value carries would report a symbol the corpus lacks, and a symbol
        // indexed twice would leave a consumer reading one of the two rows and never the other.
        DaggerfallText text = Supplied();
        DaggerfallTextMacro first = text.Macros[0];

        Assert.Contains("carried by no published value", Assert.Throws<InvalidOperationException>(() => (text with { Macros = [.. text.Macros, new DaggerfallTextMacro("%zzz", 1, TextMacroDisposition.Unrecognised)] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("is carried by 0 values", Assert.Throws<ArgumentOutOfRangeException>(() => (text with { Macros = [.. text.Macros, new DaggerfallTextMacro("%zzz", 0, TextMacroDisposition.Unrecognised)] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => (text with { Macros = [.. text.Macros, first] }).Validate());
    }

    [Fact]
    public void Refuses_a_macro_whose_disposition_is_not_how_the_donor_table_accounts_for_it()
    {
        DaggerfallText text = Supplied();
        DaggerfallTextMacro macro = text.Macros.Single(value => value.Symbol == "%pc");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => (text with { Macros = [.. text.Macros.Where(value => value.Symbol != "%pc"), macro with { Disposition = TextMacroDisposition.Handled }] }).Validate());

        Assert.Contains("not how the donor's macro table accounts for it", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_family_that_is_both_pending_and_carried()
    {
        DaggerfallText text = Supplied();
        DaggerfallTextSource source = text.Sources[0];

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => (text with
        {
            PendingKinds = [.. text.PendingKinds, new DaggerfallTextPendingKind(source.Kind, 7941, "supplied here as well")],
        }).Validate());

        Assert.Contains("published as pending and carried by a source", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_record_that_is_not_in_its_source_order()
    {
        DaggerfallText text = Supplied();

        // Out of order and claiming another record's ordinal are different defects: the first leaves a
        // reader's position meaningless, and the second leaves one of two values unaddressable inside the
        // group. Only the strict comparison refuses both.
        Assert.Contains("not in source order", Assert.Throws<InvalidOperationException>(() => (text with { Records = [text.Records[1], text.Records[0], .. text.Records.Skip(2)] }).Validate()).Message, StringComparison.Ordinal);
        Assert.Contains("not in source order", Assert.Throws<InvalidOperationException>(() => (text with { Records = [text.Records[0], text.Records[1] with { Index = text.Records[0].Index }, .. text.Records.Skip(2)] }).Validate()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_donor_macro_table_keeps_handled_and_unresolved_symbols_apart()
    {
        // The distinction is the donor's own: a symbol its table names with no handler is a decision it
        // made, and reporting it as unrecognised would hide that behind a gap in this table.
        Assert.Equal(217, Arena2TextMacroSymbols.SymbolCount);
        Assert.Equal(TextMacroDisposition.Handled, Arena2TextMacroSymbols.Classify("%fx1"));
        Assert.Equal(TextMacroDisposition.Handled, Arena2TextMacroSymbols.Classify("%mpw"));
        Assert.Equal(TextMacroDisposition.DonorUnresolved, Arena2TextMacroSymbols.Classify("%htwn"));
        Assert.Equal(TextMacroDisposition.Unrecognised, Arena2TextMacroSymbols.Classify("%pc"));
        Assert.Equal(TextMacroDisposition.Unrecognised, Arena2TextMacroSymbols.Classify("%notasymbol"));
    }

    /// <summary>
    /// How many times the published values spell a macro symbol.
    /// </summary>
    /// <remarks>
    /// The scan is written here rather than read from a published total: the section says which values
    /// carry a symbol, and how often the corpus spells one is a fact about a value's text that only a
    /// scanner over that text can produce. Re-deriving it in the test is what makes the corpus's own
    /// frequency an independent fact rather than a number the reader also computed.
    /// </remarks>
    private static int Occurrences(DaggerfallText text)
    {
        const string Terminators = " %.,'?!/(){}[]\";:|";
        int total = 0;
        foreach (DaggerfallTextToken token in text.Records.SelectMany(record => record.Tokens).Where(token => token.Code == Arena2TextCode.Text))
        {
            string run = token.Text!;
            int position = 0;
            while (position < run.Length)
            {
                int marker = run.IndexOf('%', position);
                if (marker < 0)
                {
                    break;
                }

                int end = marker + 1;
                while (end < run.Length && Terminators.IndexOf(run[end], StringComparison.Ordinal) < 0)
                {
                    end++;
                }

                total++;
                position = end;
            }
        }

        return total;
    }

    private static DaggerfallText Supplied() => DaggerfallTextBuilder.Build(
        File.ReadAllBytes(Corpus(TextResourceReader.FileName)),
        "local/arena2/TEXT.RSC",
        Inventory(),
        "en");

    private static IReadOnlyList<SourceInventoryRow> Inventory() =>
        SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

    /// <summary>
    /// A text resource carrying the supplied records, laid out the way the file does: a declared
    /// directory length, one entry per record, then each record's bytes and its terminator.
    /// </summary>
    private static byte[] Resource(params (int Id, byte[] Text)[] records)
    {
        int headerLength = TextResourceReader.DirectoryEntryBytes * (records.Length + TextResourceReader.DirectoryExtraEntries);
        int dataStart = DataStart(records.Length);
        byte[] bytes = new byte[dataStart + records.Sum(record => record.Text.Length + 1)];
        Write16(bytes, 0, headerLength);
        int text = dataStart;
        for (int index = 0; index < records.Length; index++)
        {
            int entry = TextResourceReader.HeaderLengthBytes + (index * TextResourceReader.DirectoryEntryBytes);
            Write16(bytes, entry, records[index].Id);
            Write32(bytes, entry + 2, text);
            records[index].Text.CopyTo(bytes, text);
            text += records[index].Text.Length;
            bytes[text++] = TextResourceReader.Terminator;
        }

        // The slot the record count stops short of is the format's sentinel: the reserved id pointing at
        // the end of the file, which is what tells a reader the directory described its own extent.
        int sentinel = TextResourceReader.HeaderLengthBytes + (records.Length * TextResourceReader.DirectoryEntryBytes);
        Write16(bytes, sentinel, Arena2FormatConstants.ClassicDirectorySentinelId);
        Write32(bytes, sentinel + 2, bytes.Length);
        return bytes;
    }

    private static int DataStart(int records) =>
        TextResourceReader.HeaderLengthBytes + (TextResourceReader.DirectoryEntryBytes * (records + TextResourceReader.DirectoryExtraEntries));

    private static int OffsetField(int index) =>
        TextResourceReader.HeaderLengthBytes + (index * TextResourceReader.DirectoryEntryBytes) + 2;

    private static void Write16(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value & 0xff);
        bytes[offset + 1] = (byte)((value >> 8) & 0xff);
    }

    private static void Write32(byte[] bytes, int offset, int value)
    {
        Write16(bytes, offset, value);
        Write16(bytes, offset + 2, value >> 16);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
