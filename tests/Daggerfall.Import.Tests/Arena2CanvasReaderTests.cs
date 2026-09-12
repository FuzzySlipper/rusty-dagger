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
        Assert.Equal(720, canvas.Width * canvas.Height);
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
    public void Refuses_a_grid_that_is_not_a_whole_number_of_cells()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(
            () => RciDecoder.DecodeGrid(new byte[100], "truncated-cells.rci", 64, 64));

        Assert.Contains("whole number of 4096-byte cells", error.Message, StringComparison.Ordinal);
        Assert.Contains("supplies 100 bytes", error.Message, StringComparison.Ordinal);
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
        // A CEL and a BSS are formats the classic reader reads with readers this repository
        // does not have, and the reason names that reader instead of implying the source is bad.
        Arena2CanvasSet cel = Arena2CanvasReader.Read(File.ReadAllBytes(Corpus("MAGE.CEL")), "MAGE.CEL");
        Assert.Equal(Arena2CanvasKind.Unread, cel.Kind);
        Assert.Contains("FLC animation reader", cel.Reason, StringComparison.Ordinal);

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
