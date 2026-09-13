namespace Daggerfall.Import.Arena2;

/// <summary>How the canvases of one supplied media file were read.</summary>
public enum Arena2CanvasKind
{
    /// <summary>One standard IMG record.</summary>
    ImgRecord,

    /// <summary>A contiguous sequence of standard IMG records, which is the classic CIF shape.</summary>
    ImgRecordSequence,

    /// <summary>A canvas with no record header, whose shape its file length establishes.</summary>
    HeaderlessCanvas,

    /// <summary>A classic GFX frame container.</summary>
    GfxFrames,

    /// <summary>A grid of equal fixed-cell canvases, which is the classic RCI shape.</summary>
    RciGrid,

    /// <summary>No reader in this repository read the file.</summary>
    Unread,

    /// <summary>An FLC animation container, whose frames are its canvases.</summary>
    FlcAnimation,
}

/// <summary>One addressable canvas inside a supplied media file.</summary>
/// <param name="Record">Record ordinal inside the file, or zero when the file has one record.</param>
/// <param name="Frame">Frame ordinal inside the record, or zero when the record has one frame.</param>
/// <param name="XOffset">Horizontal offset the record declares.</param>
/// <param name="YOffset">Vertical offset the record declares.</param>
/// <param name="Width">Canvas width in pixels.</param>
/// <param name="Height">Canvas height in pixels.</param>
public sealed record Arena2Canvas(int Record, int Frame, int XOffset, int YOffset, int Width, int Height);

/// <summary>Every canvas one supplied media file carries, and how they were read.</summary>
/// <param name="Kind">The container shape the file was read as.</param>
/// <param name="Canvases">Every addressable canvas, in file order.</param>
/// <param name="Reason">
/// Why no reader read the file when none did, or what a reader left unread when one did — an RCI
/// remainder, for instance, which is not part of any canvas but is still a source fact.
/// </param>
public sealed record Arena2CanvasSet(Arena2CanvasKind Kind, IReadOnlyList<Arena2Canvas> Canvases, string Reason)
{
    /// <summary>Whether a reader read the file.</summary>
    public bool Read => Kind != Arena2CanvasKind.Unread;

    /// <summary>How many canvases the file supplies.</summary>
    public int Count => Canvases.Count;

    /// <summary>A sentence naming what was read and what was left unread, or why nothing was read.</summary>
    public string Description => Read
        ? Reason.Length == 0 ? $"Read as {ReadShape()}." : $"Read as {ReadShape()}. {Reason}"
        : $"No reader read it: {Reason}";

    private string ReadShape()
    {
        if (Canvases.Count == 0)
        {
            throw new InvalidOperationException($"a {Kind} canvas set carries no canvas, so it has no shape to describe");
        }

        return Kind switch
        {
            Arena2CanvasKind.ImgRecord => $"one IMG record of {Shape(Canvases[0])}",
            Arena2CanvasKind.ImgRecordSequence => $"{Count} IMG records, the first of {Shape(Canvases[0])}",
            Arena2CanvasKind.HeaderlessCanvas => $"a headerless canvas of {Shape(Canvases[0])}",
            Arena2CanvasKind.GfxFrames => $"a GFX container of {Count} frames of {Shape(Canvases[0])}",
            Arena2CanvasKind.RciGrid => $"an RCI grid of {Count} canvases of {Shape(Canvases[0])}",
            Arena2CanvasKind.FlcAnimation => $"an FLC animation of {Count} frames of {Shape(Canvases[0])}",
            _ => throw new InvalidOperationException($"{Kind} names no read shape."),
        };
    }

    private static string Shape(Arena2Canvas canvas) => $"{canvas.Width} by {canvas.Height} pixels";
}

/// <summary>
/// Reads the canvases a supplied media file carries, by the repository's one probing policy.
/// </summary>
/// <remarks>
/// The policy follows the classic reader's own dispatch, which selects the container shape by
/// file name: a name the classic RCI table carries is a fixed-cell grid, <c>.GFX</c> is a frame
/// container, a non-weapon <c>.CIF</c> is a contiguous sequence of IMG records, and everything
/// else is an IMG whose length decides whether it carries a record header. This decides only
/// how a supplied file is read. Which files a publication emits stays that publication's own
/// source table.
/// </remarks>
public static class Arena2CanvasReader
{
    /// <summary>
    /// The RCI cell shapes the classic reader selects by file name, from the donor's
    /// <c>CifRciFile.ReadRci</c>.
    /// </summary>
    public static readonly (string Name, int Width, int Height)[] RciCellShapes =
    [
        ("FACES.CIF", 64, 64),
        ("CHLD00I0.RCI", 64, 64),
        ("TFAC00I0.RCI", 64, 64),
        ("BUTTONS.RCI", 32, 16),
        ("MPOP.RCI", 17, 17),
        ("NOTE.RCI", 44, 9),
        ("SPOP.RCI", 22, 22),
    ];

    /// <summary>Reads the canvases one supplied file carries.</summary>
    /// <param name="bytes">The whole supplied file.</param>
    /// <param name="path">The name or path the caller knows the file by; its extension selects the container shape.</param>
    public static Arena2CanvasSet Read(ReadOnlySpan<byte> bytes, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string name = System.IO.Path.GetFileName(path);
        string extension = System.IO.Path.GetExtension(name).ToUpperInvariant();
        List<string> refusals = [];
        (string Name, int Width, int Height) cell = RciCellShapes
            .FirstOrDefault(entry => StringComparer.OrdinalIgnoreCase.Equals(entry.Name, name));
        if (cell.Name is not null)
        {
            try
            {
                RciGrid grid = RciDecoder.DecodeGrid(bytes, path, cell.Width, cell.Height);
                string remainder = grid.TrailingBytes == 0
                    ? string.Empty
                    : $"{grid.TrailingBytes} byte(s) after the last whole {cell.Width}x{cell.Height} cell belong to no canvas, which is what the classic reader's whole-number division leaves behind.";
                return new(Arena2CanvasKind.RciGrid, [.. grid.Canvases], remainder);
            }
            catch (Arena2FormatException failure)
            {
                refusals.Add(failure.Message);
            }
        }
        else if (extension == ".GFX")
        {
            try
            {
                GfxArchive gfx = GfxArchive.Parse(bytes, path);
                return Read(
                    [.. gfx.Frames.Select(frame => new Arena2Canvas(0, frame.Index, 0, 0, frame.Width, frame.Height))],
                    Arena2CanvasKind.GfxFrames);
            }
            catch (Arena2FormatException failure)
            {
                refusals.Add(failure.Message);
            }
        }
        else if (extension == ".CIF" && name.Contains("WEAPO", StringComparison.OrdinalIgnoreCase))
        {
            // The substring test is the classic reader's own: CifRciFile.ReadRecords dispatches
            // on fn.Contains("WEAPO"), so matching the name exactly would leave the donor.
            refusals.Add($"'{name}' is a weapon CIF, whose frames the weapon CIF reader owns rather than this probe.");
        }
        else if (extension == ".CIF")
        {
            try
            {
                IReadOnlyList<IndexedImg> records = ImgDecoder.DecodeRecordSequence(bytes, path);
                return Read(
                    [.. records.Select((record, index) => new Arena2Canvas(index, 0, record.XOffset, record.YOffset, record.Width, record.Height))],
                    Arena2CanvasKind.ImgRecordSequence);
            }
            catch (Arena2FormatException failure)
            {
                refusals.Add(failure.Message);
            }
        }
        else if (extension == ".CEL")
        {
            // The classic reader reads a class portrait through its FLC animation reader, and the
            // container's frames are the canvases: one run-length image and a delta each after it.
            try
            {
                IReadOnlyList<FlcDecoder.FlcFrameImage> frames = FlcDecoder.DecodeFrames(bytes, path, out _);
                return Read(
                    [.. frames.Select(frame => new Arena2Canvas(frame.Index, 0, 0, 0, frame.Width, frame.Height))],
                    Arena2CanvasKind.FlcAnimation);
            }
            catch (Arena2FormatException failure)
            {
                refusals.Add(failure.Message);
            }
        }
        else if (extension == ".BSS")
        {
            refusals.Add("the classic reader reads .BSS through its BSS reader, which this repository does not have.");
        }
        else if (ImgDecoder.TryDecodeHeaderless(bytes, path, out IndexedImg? headerless, out string lengthReason))
        {
            return Read([new Arena2Canvas(0, 0, 0, 0, headerless.Width, headerless.Height)], Arena2CanvasKind.HeaderlessCanvas);
        }
        else
        {
            try
            {
                IndexedImg record = ImgDecoder.Decode(bytes, path);
                return Read([new Arena2Canvas(0, 0, record.XOffset, record.YOffset, record.Width, record.Height)], Arena2CanvasKind.ImgRecord);
            }
            catch (Arena2FormatException failure)
            {
                refusals.Add($"{lengthReason} The IMG record path also refused it: {failure.Message}");
            }
        }

        return new Arena2CanvasSet(Arena2CanvasKind.Unread, [], string.Join(" ", refusals));
    }

    private static Arena2CanvasSet Read(IReadOnlyList<Arena2Canvas> canvases, Arena2CanvasKind kind) =>
        new(kind, canvases, string.Empty);
}
