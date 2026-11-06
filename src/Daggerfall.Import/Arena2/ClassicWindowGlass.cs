namespace Daggerfall.Import.Arena2;

/// <summary>
/// The glass of the classic exterior window textures, as a mask a renderer can light at night. Most windows paint their
/// glass in palette index 255, which the classic swaps by the hour and Daggerfall Unity's <c>GetWindowColors32</c> reads.
/// A few paint it in an ordinary colour the donor leaves dark, so those records name their glass here: its index, and the
/// rectangle holding it, since the same colour also falls in the wall around it. Source-format behavior: the runtime
/// reads the published mask, never this table.
/// </summary>
public static class ClassicWindowGlass
{
    private const byte GlassIndex = 255;

    private readonly record struct PaintedGlass(byte Index, int Left, int Top, int Right, int Bottom);

    private static readonly Dictionary<(int Archive, int Record), PaintedGlass> Painted = new()
    {
        [(42, 3)] = new(97, 9, 38, 56, 70),
        [(61, 3)] = new(190, 16, 11, 46, 63),
        [(109, 3)] = new(127, 22, 19, 43, 70),
        [(110, 3)] = new(127, 22, 20, 43, 69),
        [(151, 3)] = new(126, 26, 39, 39, 89),
        [(154, 3)] = new(138, 7, 19, 44, 61),
        [(171, 2)] = new(105, 7, 38, 57, 71),
        [(172, 3)] = new(131, 7, 37, 57, 71),
        [(173, 3)] = new(131, 7, 37, 57, 71),
        [(309, 3)] = new(127, 22, 19, 43, 70),
        [(310, 3)] = new(127, 22, 20, 43, 69),
        [(351, 3)] = new(126, 26, 39, 39, 89),
    };

    /// <summary>
    /// Whether a record is a window, as Daggerfall Unity's <c>ClimateSwaps.IsExteriorWindow</c> lists them: record 3 of a
    /// general wall set, every gable, and the windows of the two special city sets, in any climate's archive.
    /// </summary>
    public static bool IsExteriorWindow(int archive, int record)
    {
        if (archive is >= 375 and <= 379 or 210 or 280) return false;
        if ((archive, record) is (36, 2) or (151, 3) or (154, 3) or (351, 3) or (171, 2) or (172, 3) or (173, 3)) return true;
        return (archive % 100) switch
        {
            9 or 10 or 12 or 13 or 14 or 15 or 26 or 27 or 35 or 36 or 38 or 39 or 42 or 43
                or 58 or 59 or 61 or 62 or 64 or 65 => record == 3,
            75 or 76 or 77 => true,
            79 or 80 => record is 0 or 2,
            82 or 83 => record == 0,
            _ => false,
        };
    }

    /// <summary>
    /// A frame's glass as row-major RGBA, white over opaque black, or null where the record isn't a window or shows no glass.
    /// </summary>
    public static byte[]? Mask(int archive, int record, IndexedTextureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!IsExteriorWindow(archive, record)) return null;
        PaintedGlass glass = Painted.TryGetValue((archive, record), out PaintedGlass painted)
            ? painted : new(GlassIndex, 0, 0, frame.Width, frame.Height);
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        byte[] rgba = new byte[pixels.Length * 4];
        bool any = false;
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                int pixel = y * frame.Width + x;
                bool lit = pixels[pixel] == glass.Index && x >= glass.Left && x < glass.Right && y >= glass.Top && y < glass.Bottom;
                any |= lit;
                byte value = lit ? byte.MaxValue : (byte)0;
                rgba[pixel * 4] = rgba[pixel * 4 + 1] = rgba[pixel * 4 + 2] = value;
                rgba[pixel * 4 + 3] = byte.MaxValue;
            }
        return any ? rgba : null;
    }
}
