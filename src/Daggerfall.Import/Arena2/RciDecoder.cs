namespace Daggerfall.Import.Arena2;

/// <summary>The fixed-cell canvases of one RCI file and any bytes after the last whole cell.</summary>
/// <param name="Canvases">Every whole cell, in file order.</param>
/// <param name="TrailingBytes">Bytes after the last whole cell, which no cell claims.</param>
public sealed record RciGrid(IReadOnlyList<Arena2Canvas> Canvases, int TrailingBytes)
{
    /// <summary>How many whole cells the file supplies.</summary>
    public int CellCount => Canvases.Count;
}

/// <summary>The classic RCI canvas grid: equal fixed-cell canvases laid end to end.</summary>
/// <remarks>
/// The donor's <c>CifRciFile.ReadRci</c> derives the canvas count from the file length and a
/// cell shape it selects by file name, and gives every canvas zero offsets, because the format
/// carries no directory and no record header. Its count is a whole-number division, so a file
/// whose length is not a whole number of cells yields the whole cells and leaves the remainder
/// unread. That remainder is reported here rather than refused: the corpus's own
/// <c>TFAC00I0.RCI</c> is 503 cells plus seven bytes, and treating a 2 MB face bank as unreadable
/// over a seven-byte tail would lose the whole file to protect nothing.
/// </remarks>
public static class RciDecoder
{
    /// <summary>Enumerates the cells of a fixed-shape RCI grid.</summary>
    /// <param name="bytes">The whole supplied RCI file.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    /// <param name="cellWidth">Cell width in pixels.</param>
    /// <param name="cellHeight">Cell height in pixels.</param>
    public static RciGrid DecodeGrid(ReadOnlySpan<byte> bytes, string source, int cellWidth, int cellHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (cellWidth <= 0 || cellHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellWidth), $"an RCI cell must have positive dimensions, got {cellWidth}x{cellHeight}");
        }

        int cellBytes;
        try
        {
            cellBytes = checked(cellWidth * cellHeight);
        }
        catch (OverflowException)
        {
            throw new Arena2FormatException(source, 0, $"RCI cell {cellWidth}x{cellHeight} overflows a 32-bit byte count");
        }

        if (bytes.Length < cellBytes)
        {
            throw new Arena2FormatException(
                source,
                0,
                $"an RCI grid of {cellWidth}x{cellHeight} cells needs at least one {cellBytes}-byte cell, but the file supplies {bytes.Length} bytes");
        }

        int cells = bytes.Length / cellBytes;
        List<Arena2Canvas> canvases = [];
        for (int index = 0; index < cells; index++)
        {
            canvases.Add(new Arena2Canvas(index, 0, 0, 0, cellWidth, cellHeight));
        }

        return new RciGrid(canvases, bytes.Length - (cells * cellBytes));
    }
}
