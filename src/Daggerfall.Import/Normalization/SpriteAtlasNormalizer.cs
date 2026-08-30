using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

public enum SpriteAtlasPacking
{
    Strip,
    Grid,
}

/// <summary>Bounded layout policy for an emitted sprite atlas.</summary>
public sealed record SpriteAtlasOptions(
    SpriteAtlasPacking Packing,
    int MaxDimension,
    int? FixedCellWidth = null,
    int? FixedCellHeight = null,
    bool CropTransparentPixels = false,
    bool BottomAlign = false)
{
    public static SpriteAtlasOptions Strip(int maxDimension) => new(SpriteAtlasPacking.Strip, maxDimension);

    public static SpriteAtlasOptions Grid(int maxDimension, bool cropTransparentPixels = false, bool bottomAlign = false) =>
        new(SpriteAtlasPacking.Grid, maxDimension, null, null, cropTransparentPixels, bottomAlign);

    public static SpriteAtlasOptions FixedCellGrid(int maxDimension, int cellWidth, int cellHeight, bool bottomAlign = false) =>
        new(SpriteAtlasPacking.Grid, maxDimension, cellWidth, cellHeight, false, bottomAlign);

    internal void Validate()
    {
        if (!Enum.IsDefined(Packing) || MaxDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDimension), "Atlas packing and maximum dimension must be valid.");
        }

        if ((FixedCellWidth is null) != (FixedCellHeight is null)
            || FixedCellWidth is <= 0 || FixedCellHeight is <= 0)
        {
            throw new ArgumentException("Fixed cell layouts require positive width and height.");
        }

        if (FixedCellWidth > MaxDimension || FixedCellHeight > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(FixedCellWidth), "A fixed cell cannot exceed the atlas quota.");
        }
    }
}

/// <summary>A decoded source frame. It is deliberately free of renderer or playback state.</summary>
public sealed record DecodedSpriteFrame(string Id, int Width, int Height, byte[] Rgba, bool MirrorHorizontally = false)
{
    /// <summary>Converts one already-decoded palette-indexed frame to RGBA8 with an explicit alpha policy.</summary>
    public static DecodedSpriteFrame FromPalette(
        string id,
        int width,
        int height,
        ReadOnlySpan<byte> indexedPixels,
        Arena2Palette palette,
        PaletteAlphaMode alphaMode,
        bool mirrorHorizontally = false)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (width <= 0 || height <= 0 || indexedPixels.Length != checked(width * height))
        {
            throw new ArgumentException("Indexed input must contain exactly one palette index per positive-dimension pixel.", nameof(indexedPixels));
        }

        Rgba32[] pixels = palette.ToRgba(indexedPixels, alphaMode);
        byte[] rgba = new byte[checked(pixels.Length * 4)];
        for (int index = 0; index < pixels.Length; index++)
        {
            int offset = index * 4;
            rgba[offset] = pixels[index].Red;
            rgba[offset + 1] = pixels[index].Green;
            rgba[offset + 2] = pixels[index].Blue;
            rgba[offset + 3] = pixels[index].Alpha;
        }

        return new(id, width, height, rgba, mirrorHorizontally);
    }

    internal void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        if (Width <= 0 || Height <= 0 || Rgba is null || Rgba.Length != checked(Width * Height * 4))
        {
            throw new ArgumentException("A decoded frame must have positive dimensions and exact RGBA8 bytes.", nameof(Rgba));
        }
    }
}

/// <summary>Regenerated frame layout from a normalized atlas.</summary>
public sealed record NormalizedAtlasFrame(
    string Id,
    int FrameIndex,
    int X,
    int Y,
    int Width,
    int Height,
    int SourceWidth,
    int SourceHeight,
    bool Mirrored)
{
    public NormalizedSpriteFrame ToSpriteFrame(NormalizedVector2 pivot) =>
        new(Id, FrameIndex, X, Y, Width, Height, pivot);
}

/// <summary>Pure result of packing decoded frames into one content-addressed PNG.</summary>
public sealed record NormalizedSpriteAtlas(
    int Width,
    int Height,
    byte[] PngBytes,
    ContentDigest PngDigest,
    IReadOnlyList<NormalizedAtlasFrame> Frames)
{
    public long ByteLength => PngBytes.LongLength;
}

public static class SpriteAtlasNormalizer
{
    public static NormalizedSpriteAtlas Normalize(IReadOnlyList<DecodedSpriteFrame> sourceFrames, SpriteAtlasOptions options)
    {
        ArgumentNullException.ThrowIfNull(sourceFrames);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (sourceFrames.Count == 0)
        {
            throw new ArgumentException("An atlas requires at least one frame.", nameof(sourceFrames));
        }

        List<WorkingFrame> frames = sourceFrames.Select((frame, index) => WorkingFrame.Create(frame, index, options.CropTransparentPixels)).ToList();
        if (frames.Select(frame => frame.Id).Distinct(StringComparer.Ordinal).Count() != frames.Count)
        {
            throw new InvalidOperationException("Atlas frame IDs must be unique.");
        }

        int cellWidth = options.FixedCellWidth ?? frames.Max(frame => frame.Width);
        int cellHeight = options.FixedCellHeight ?? frames.Max(frame => frame.Height);
        if (frames.Any(frame => frame.Width > cellWidth || frame.Height > cellHeight))
        {
            throw new InvalidOperationException("A decoded frame exceeds the requested fixed cell.");
        }

        (int columns, int rows) = ComputeGrid(frames.Count, cellWidth, cellHeight, options);
        int atlasWidth = checked(columns * cellWidth);
        int atlasHeight = checked(rows * cellHeight);
        byte[] atlas = new byte[checked(atlasWidth * atlasHeight * 4)];
        List<NormalizedAtlasFrame> normalizedFrames = new(frames.Count);
        foreach (WorkingFrame frame in frames)
        {
            int cellX = (frame.Index % columns) * cellWidth;
            int cellY = (frame.Index / columns) * cellHeight;
            int x = cellX + ((cellWidth - frame.Width) / 2);
            int y = options.BottomAlign ? cellY + cellHeight - frame.Height : cellY;
            Copy(frame.Rgba, frame.Width, frame.Height, atlas, atlasWidth, x, y);
            normalizedFrames.Add(new(frame.Id, frame.Index, x, y, frame.Width, frame.Height, frame.SourceWidth, frame.SourceHeight, frame.Mirrored));
        }

        byte[] png = DeterministicPngEncoder.EncodeRgba8(atlasWidth, atlasHeight, atlas);
        return new(atlasWidth, atlasHeight, png, ContentDigest.Compute(png), normalizedFrames);
    }

    private static (int Columns, int Rows) ComputeGrid(int frameCount, int cellWidth, int cellHeight, SpriteAtlasOptions options)
    {
        if (cellWidth > options.MaxDimension || cellHeight > options.MaxDimension)
        {
            throw new InvalidOperationException("A frame cell exceeds the atlas dimension quota.");
        }

        int maximumColumns = Math.Max(1, options.MaxDimension / cellWidth);
        int columns = options.Packing == SpriteAtlasPacking.Strip ? frameCount : Math.Min(frameCount, maximumColumns);
        if (columns > maximumColumns)
        {
            throw new InvalidOperationException("A strip atlas exceeds the dimension quota.");
        }

        int rows = checked((frameCount + columns - 1) / columns);
        if (checked(rows * cellHeight) > options.MaxDimension)
        {
            throw new InvalidOperationException("The packed atlas exceeds the dimension quota.");
        }

        return (columns, rows);
    }

    private static void Copy(byte[] source, int width, int height, byte[] target, int targetWidth, int x, int y)
    {
        for (int row = 0; row < height; row++)
        {
            source.AsSpan(row * width * 4, width * 4).CopyTo(target.AsSpan(((y + row) * targetWidth + x) * 4, width * 4));
        }
    }

    private sealed record WorkingFrame(string Id, int Index, int Width, int Height, int SourceWidth, int SourceHeight, byte[] Rgba, bool Mirrored)
    {
        public static WorkingFrame Create(DecodedSpriteFrame source, int index, bool crop)
        {
            source.Validate();
            byte[] pixels = source.Rgba.ToArray();
            if (source.MirrorHorizontally)
            {
                Mirror(pixels, source.Width, source.Height);
            }

            return crop
                ? Crop(source, index, pixels)
                : new(source.Id, index, source.Width, source.Height, source.Width, source.Height, pixels, source.MirrorHorizontally);
        }

        private static WorkingFrame Crop(DecodedSpriteFrame source, int index, byte[] pixels)
        {
            int left = source.Width;
            int top = source.Height;
            int right = -1;
            int bottom = -1;
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    if (pixels[(y * source.Width + x) * 4 + 3] == 0) continue;
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }

            if (right < 0)
            {
                return new(source.Id, index, 1, 1, source.Width, source.Height, [0, 0, 0, 0], source.MirrorHorizontally);
            }

            int width = right - left + 1;
            int height = bottom - top + 1;
            byte[] trimmed = new byte[checked(width * height * 4)];
            for (int y = 0; y < height; y++)
            {
                pixels.AsSpan(((top + y) * source.Width + left) * 4, width * 4).CopyTo(trimmed.AsSpan(y * width * 4));
            }

            return new(source.Id, index, width, height, source.Width, source.Height, trimmed, source.MirrorHorizontally);
        }

        private static void Mirror(byte[] pixels, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int left = 0, right = width - 1; left < right; left++, right--)
                {
                    int leftOffset = (y * width + left) * 4;
                    int rightOffset = (y * width + right) * 4;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        (pixels[leftOffset + channel], pixels[rightOffset + channel]) = (pixels[rightOffset + channel], pixels[leftOffset + channel]);
                    }
                }
            }
        }
    }
}
