namespace Daggerfall.Import.Arena2;

/// <summary>The classic RCI canvas grid: equal fixed-cell canvases laid end to end.</summary>
/// <remarks>
/// The donor's <c>CifRciFile.ReadRci</c> derives the canvas count from the file length and a
/// cell shape it selects by file name, and gives every canvas zero offsets, because the format
/// carries no directory and no record header. A length that is not a whole number of cells
/// establishes no canvas set, so it is refused rather than truncated to the whole cells.
/// </remarks>
public static class RciDecoder
{
    /// <summary>Enumerates the cells of a fixed-shape RCI grid.</summary>
    /// <param name="bytes">The whole supplied RCI file.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    /// <param name="cellWidth">Cell width in pixels.</param>
    /// <param name="cellHeight">Cell height in pixels.</param>
    public static IReadOnlyList<Arena2Canvas> DecodeGrid(ReadOnlySpan<byte> bytes, string source, int cellWidth, int cellHeight)
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

        if (bytes.Length == 0 || bytes.Length % cellBytes != 0)
        {
            throw new Arena2FormatException(
                source,
                0,
                $"an RCI grid of {cellWidth}x{cellHeight} cells needs a whole number of {cellBytes}-byte cells, but the file supplies {bytes.Length} bytes");
        }

        List<Arena2Canvas> canvases = [];
        for (int index = 0; index < bytes.Length / cellBytes; index++)
        {
            canvases.Add(new Arena2Canvas(index, 0, 0, 0, cellWidth, cellHeight));
        }

        return canvases;
    }
}
