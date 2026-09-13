using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The one probing policy for a supplied media file: the container shape follows the classic
/// reader's own dispatch, a length in the documented headerless table decides before any
/// record header is read, and a shape no reader establishes is refused rather than assumed.
/// </summary>
public sealed class Arena2CanvasReaderTests
{
    [Fact]
    public void Reads_the_headerless_shape_the_file_length_establishes()
    {
        // 720 bytes is the documented 9x80 shape. Before the length table carried it, these
        // bytes had no reader at all, so the file was reported as unread rather than supplied.
        Arena2CanvasSet canvases = Arena2CanvasReader.Read(File.ReadAllBytes(Corpus("BANK01I1.IMG")), "BANK01I1.IMG");

        Assert.Equal(Arena2CanvasKind.HeaderlessCanvas, canvases.Kind);
        Arena2Canvas canvas = Assert.Single(canvases.Canvases);
        Assert.Equal(9, canvas.Width);
        Assert.Equal(80, canvas.Height);
        // The file is exactly its shape, which is why the whole file is the canvas.
        Assert.Equal(new FileInfo(Corpus("BANK01I1.IMG")).Length, canvas.Width * canvas.Height);
    }

    [Fact]
    public void Reports_a_headerless_canvas_at_its_own_shape_and_size()
    {
        // 112128 bytes is the documented 512x219 shape. Its pixel count exceeds a 16-bit
        // payload field, so the record has to agree with itself rather than truncating: pixels
        // of exactly width * height, and a payload that says the same.
        IndexedImg image = ImgDecoder.DecodeHeaderless(new byte[112128], "NITE00I0.IMG");
        Assert.Equal((512, 219), (image.Width, image.Height));
        Assert.Equal(512 * 219, image.PayloadLength);
        Assert.Equal(512 * 219, image.Pixels.Length);

        // 64768 bytes is a real corpus length (PRIS00I0.IMG and peers): a 320x200 shape with
        // 768 bytes after it. The classic reader reads the shape's byte count and ignores the
        // rest, so the canvas is the leading 64000 bytes rather than the whole file.
        IndexedImg padded = ImgDecoder.DecodeHeaderless(new byte[64768], "PRIS00I0.IMG");
        Assert.Equal((320, 200), (padded.Width, padded.Height));
        Assert.Equal(64000, padded.PayloadLength);
        Assert.Equal(64000, padded.Pixels.Length);

        // 44 bytes is the documented 22x22 shape, which needs 484 bytes. Padding the canvas with
        // the 440 bytes the file does not carry would be an invented image, so it is refused and
        // the reason says which part is missing.
        Arena2FormatException truncated = Assert.Throws<Arena2FormatException>(
            () => ImgDecoder.DecodeHeaderless(new byte[44], "stub.img"));
        Assert.Contains("needs 484 bytes", truncated.Message, StringComparison.Ordinal);
        Assert.Contains("44-byte file", truncated.Message, StringComparison.Ordinal);

        Arena2CanvasSet unread = Arena2CanvasReader.Read(new byte[44], "STUB.IMG");
        Assert.Equal(Arena2CanvasKind.Unread, unread.Kind);
        Assert.Contains("needs 484 bytes", unread.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_to_describe_a_read_kind_that_carries_no_canvas()
    {
        // Read never produces this state, but the set is public: describing it must fail as an
        // invalid state rather than as an index out of range.
        Arena2CanvasSet empty = new(Arena2CanvasKind.ImgRecord, [], string.Empty);

        Assert.Throws<InvalidOperationException>(() => empty.Description);
    }

    [Fact]
    public void Lets_the_documented_length_decide_before_the_record_header()
    {
        // These 720 bytes are also a well-formed IMG record: 12x59 pixels, and 12 + 708 ends
        // exactly at the end of the file. The classic reader applies the length table first, so
        // the canvas is 9x80 and the header is not consulted; reading the header instead would
        // decode the same bytes at a shape the source never declares.
        byte[] bytes = new byte[720];
        BitConverter.GetBytes((short)12).CopyTo(bytes, 4);
        BitConverter.GetBytes((short)59).CopyTo(bytes, 6);
        BitConverter.GetBytes((short)708).CopyTo(bytes, 10);

        Arena2CanvasSet canvases = Arena2CanvasReader.Read(bytes, "AMBIGUOUS.IMG");

        Assert.Equal(Arena2CanvasKind.HeaderlessCanvas, canvases.Kind);
        Assert.Equal(9, Assert.Single(canvases.Canvases).Width);
    }

    [Fact]
    public void Refuses_a_length_the_table_does_not_carry_and_names_both_paths()
    {
        Arena2CanvasSet canvases = Arena2CanvasReader.Read(new byte[700], "MYSTERY.IMG");

        Assert.Equal(Arena2CanvasKind.Unread, canvases.Kind);
        Assert.Empty(canvases.Canvases);
        Assert.Contains("700 bytes", canvases.Reason, StringComparison.Ordinal);
        Assert.Contains("IMG record path also refused it", canvases.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_one_img_record_with_the_offsets_its_header_declares()
    {
        Arena2CanvasSet canvases = Arena2CanvasReader.Read(File.ReadAllBytes(Corpus("MAIN00I0.IMG")), "MAIN00I0.IMG");

        Assert.Equal(Arena2CanvasKind.ImgRecord, canvases.Kind);
        Arena2Canvas canvas = Assert.Single(canvases.Canvases);
        Assert.Equal((0, 154, 320, 46), (canvas.XOffset, canvas.YOffset, canvas.Width, canvas.Height));
    }

    [Fact]
    public void Reads_a_cif_as_the_record_sequence_the_classic_reader_sees()
    {
        Arena2CanvasSet canvases = Arena2CanvasReader.Read(File.ReadAllBytes(Corpus("INVE16I0.CIF")), "INVE16I0.CIF");

        Assert.Equal(Arena2CanvasKind.ImgRecordSequence, canvases.Kind);
        Assert.Equal(11, canvases.Count);
        Assert.Equal(Enumerable.Range(0, 11), canvases.Canvases.Select(canvas => canvas.Record));
        // The records are different shapes, so no single canvas can stand for the file.
        Assert.True(canvases.Canvases.Select(canvas => (canvas.Width, canvas.Height)).Distinct().Count() > 1);
    }

    [Fact]
    public void Reads_the_face_cifs_as_record_sequences_and_the_face_grid_as_its_cells()
    {
        Arena2CanvasSet face = Arena2CanvasReader.Read(File.ReadAllBytes(Corpus("FACE00I0.CIF")), "FACE00I0.CIF");
        Assert.Equal(Arena2CanvasKind.ImgRecordSequence, face.Kind);
        Assert.Equal(10, face.Count);

        // FACES.CIF is the one name the classic reader treats as a fixed 64x64 grid.
        Arena2CanvasSet grid = Arena2CanvasReader.Read(File.ReadAllBytes(Corpus("FACES.CIF")), "FACES.CIF");
        Assert.Equal(Arena2CanvasKind.RciGrid, grid.Kind);
        Assert.Equal(61, grid.Count);
        Assert.All(grid.Canvases, canvas => Assert.Equal((64, 64), (canvas.Width, canvas.Height)));
    }

    [Fact]
    public void Reads_a_run_length_encoded_cif_record_sequence()
    {
        // Five residual sprite CIFs encode every record with compression 2, which the classic CIF
        // reader decodes through BaseImageFile.ReadRleData. The declared payload frames the next
        // record, so the walk has to use that rather than the encoded stream's own length.
        IReadOnlyList<IndexedImg> records = ImgDecoder.DecodeRecordSequence(File.ReadAllBytes(Corpus("FIRE00C6.CIF")), "FIRE00C6.CIF");

        Assert.Equal(6, records.Count);
        Assert.All(records, record => Assert.Equal(ImgDecoder.RleCompressed, record.Compression));
        Assert.All(records, record => Assert.Equal(record.Width * record.Height, record.Pixels.Length));

        // A repeat code of 0x82 writes the following byte three times: 130 - 127.
        byte[] repeat = [0, 0, 0, 0, 3, 0, 1, 0, 2, 0, 3, 0, 0x82, 0xAA, 0x00];
        IndexedImg decoded = Assert.Single(ImgDecoder.DecodeRecordSequence(repeat, "repeat.cif"));
        Assert.Equal(3, decoded.Width);
        Assert.Equal([0xAA, 0xAA, 0xAA], decoded.Pixels.ToArray());

        // A stream that stops inside its declared window before the shape is filled is refused
        // with the shortfall, rather than returning a partly filled canvas.
        byte[] shortStream = [0, 0, 0, 0, 4, 0, 1, 0, 2, 0, 2, 0, 0x00, 0xAA];
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(
            () => ImgDecoder.DecodeRecordSequence(shortStream, "short.cif"));
        Assert.Contains("does not encode 4 pixels", error.Message, StringComparison.Ordinal);

        // A literal that would read past the declared window is refused before the walk can lose
        // its place in the file.
        byte[] overrun = [0, 0, 0, 0, 4, 0, 1, 0, 2, 0, 2, 0, 0x01, 0xAA];
        Assert.Contains(
            "exceeds the 2-byte payload",
            Assert.Throws<Arena2FormatException>(() => ImgDecoder.DecodeRecordSequence(overrun, "overrun.cif")).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_the_palette_a_64768_byte_screen_carries_after_its_canvas()
    {
        // Six supplied screens are a 320x200 canvas plus 768 palette bytes, and the classic reader
        // reads that trailing palette for exactly those names, scaling its six-bit channels by four.
        string[] screens = ["CHGN00I0.IMG", "DIE_00I0.IMG", "PICK02I0.IMG", "PICK03I0.IMG", "PRIS00I0.IMG", "TITL00I0.IMG"];
        foreach (string screen in screens)
        {
            byte[] bytes = File.ReadAllBytes(Corpus(screen));
            Assert.Equal(ImgDecoder.EmbeddedPaletteScreenBytes, bytes.Length);
            Assert.True(ImgDecoder.TryReadEmbeddedPalette(bytes, screen, out Arena2Palette? palette, out string reason), $"{screen}: {reason}");

            // The scaled colour is the raw trailing byte times four, and the canvas is not the palette.
            Assert.Equal((byte)Math.Min(255, bytes[^768] * 4), palette!.Colors.Span[0].Red);
            Assert.Equal((byte)Math.Min(255, bytes[^767] * 4), palette.Colors.Span[0].Green);
            Assert.Contains("x4", palette.Source, StringComparison.Ordinal);

            IndexedImg canvas = ImgDecoder.DecodeHeaderless(bytes, screen);
            Assert.Equal((320, 200), (canvas.Width, canvas.Height));
            Assert.Equal(64000, canvas.Pixels.Length);
        }

        // A 320x200 canvas reached from another length carries no palette, and saying it does would
        // paint it with bytes that are not there.
        byte[] bare = new byte[Arena2FormatConstants.HeaderlessUiImgBytes];
        Assert.False(ImgDecoder.TryReadEmbeddedPalette(bare, "bare.img", out Arena2Palette? none, out string refused));
        Assert.Null(none);
        Assert.Contains("carries no 768-byte embedded palette", refused, StringComparison.Ordinal);

        // Scaling saturates rather than wrapping, so a factor that would exceed a byte clamps: the
        // first channel the art palette carries above the six-bit range proves it.
        Arena2Palette art = PaletteDecoder.Decode(File.ReadAllBytes(Corpus("ART_PAL.COL")), "ART_PAL.COL");
        int bright = art.Colors.Span.IndexOf(art.Colors.Span.ToArray().First(color => color.Red > 63));
        Assert.True(bright >= 0, "the art palette should carry a channel above the six-bit range");
        Assert.Equal(255, PaletteDecoder.ScaleChannels(art, 300).Colors.Span[bright].Red);
    }

    [Fact]
    public void Reads_an_img_record_whose_compression_field_is_not_implemented()
    {
        // The classic IMG reader reads a record's shape and never consults its compression field,
        // so a declared value this repository does not implement is still a readable image. Two
        // supplied files declare 2048 and are exactly twelve bytes plus their shape.
        IndexedImg talk = ImgDecoder.Decode(File.ReadAllBytes(Corpus("TALK00I0.IMG")), "TALK00I0.IMG");
        Assert.Equal((320, 200), (talk.Width, talk.Height));
        Assert.Equal(2048, talk.Compression);
        Assert.Equal(64000, talk.Pixels.Length);

        IndexedImg frame = ImgDecoder.Decode(File.ReadAllBytes(Corpus("FRAM00I0.IMG")), "FRAM00I0.IMG");
        Assert.Equal((96, 96), (frame.Width, frame.Height));
        Assert.Equal(9216, frame.Pixels.Length);
    }

    [Fact]
    public void Reads_the_whole_cells_of_a_grid_and_reports_the_remainder()
    {
        // The classic reader derives the cell count by whole-number division, so bytes after the
        // last whole cell belong to no canvas. They are reported rather than refilled with a
        // guessed shape or used to reject a file whose cells are all present.
        RciGrid remainder = RciDecoder.DecodeGrid(new byte[(64 * 64) + 7], "remainder.rci", 64, 64);
        Assert.Equal(1, remainder.CellCount);
        Assert.Equal(7, remainder.TrailingBytes);

        // A file that cannot fill one cell establishes no canvas at all.
        Arena2FormatException shortFile = Assert.Throws<Arena2FormatException>(
            () => RciDecoder.DecodeGrid(new byte[100], "truncated-cells.rci", 64, 64));
        Assert.Contains("at least one 4096-byte cell", shortFile.Message, StringComparison.Ordinal);
        Assert.Contains("supplies 100 bytes", shortFile.Message, StringComparison.Ordinal);

        // The corpus's own face bank is 503 cells plus seven bytes, which is the case that keeps
        // this a disclosure rather than a refusal.
        Arena2CanvasSet bank = Arena2CanvasReader.Read(File.ReadAllBytes(Corpus("TFAC00I0.RCI")), "TFAC00I0.RCI");
        Assert.Equal(Arena2CanvasKind.RciGrid, bank.Kind);
        Assert.Equal(503, bank.Count);
        Assert.Contains("7 byte(s)", bank.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_a_gfx_as_the_frames_its_header_declares_and_its_row_table_addresses()
    {
        byte[] bytes = File.ReadAllBytes(Corpus("SCRL00I0.GFX"));
        GfxArchive gfx = GfxArchive.Parse(bytes, "SCRL00I0.GFX");

        Assert.Equal(8, gfx.FrameCount);
        Assert.Equal((320, 80), (gfx.Width, gfx.Height));
        Assert.All(gfx.Frames, frame => Assert.Equal(80, frame.Rows.Count));
        Assert.All(gfx.Frames.SelectMany(frame => frame.Rows), row => Assert.InRange(row.Offset, GfxArchive.HeaderBytes, bytes.Length - 1));
        // The corpus carries both encodings, so a reader that defaulted the flag one way would
        // be contradicted by the source rather than by this test's expectation.
        Assert.Contains(gfx.Frames.SelectMany(frame => frame.Rows), row => row.IsRleEncoded);
        Assert.Contains(gfx.Frames.SelectMany(frame => frame.Rows), row => !row.IsRleEncoded);
    }

    [Fact]
    public void Refuses_a_gfx_whose_row_table_or_rows_run_past_the_file()
    {
        byte[] bytes = File.ReadAllBytes(Corpus("SCRL00I0.GFX"));

        // A row table that does not fit: the header declares 8 frames of 80 rows.
        Arena2FormatException table = Assert.Throws<Arena2FormatException>(
            () => GfxArchive.Parse(bytes[..2500], "truncated-table.gfx"));
        Assert.Contains("640-entry row table", table.Message, StringComparison.Ordinal);

        // A row table that fits, but addresses rows the file does not carry.
        Arena2FormatException rows = Assert.Throws<Arena2FormatException>(
            () => GfxArchive.Parse(bytes[..3000], "truncated-rows.gfx"));
        Assert.Contains("outside the", rows.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_the_reader_a_format_needs_rather_than_calling_it_unreadable()
    {
        // A CEL now reads: the classic reader reaches it through its FLC animation reader, and this
        // repository has one, so the portraits are canvases like any other family.
        Arena2CanvasSet cel = Arena2CanvasReader.Read(File.ReadAllBytes(Corpus("MAGE.CEL")), "MAGE.CEL");
        Assert.Equal(Arena2CanvasKind.FlcAnimation, cel.Kind);
        Assert.True(cel.Read);
        Assert.Equal(15, cel.Count);
        Assert.Equal("Read as an FLC animation of 15 frames of 110 by 119 pixels.", cel.Description);

        // A BSS is still a format whose reader this repository does not have, and the reason names
        // that reader instead of implying the source is bad.
        Arena2CanvasSet bss = Arena2CanvasReader.Read(File.ReadAllBytes(Corpus("CMPA00I0.BSS")), "CMPA00I0.BSS");
        Assert.Equal(Arena2CanvasKind.Unread, bss.Kind);
        Assert.Contains("BSS reader", bss.Reason, StringComparison.Ordinal);

        Arena2CanvasSet weapon = Arena2CanvasReader.Read(new byte[64], "WEAPON01.CIF");
        Assert.Contains("weapon CIF reader owns", weapon.Reason, StringComparison.Ordinal);
    }

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

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
