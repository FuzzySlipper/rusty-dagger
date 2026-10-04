namespace Daggerfall.Import.Arena2;

/// <summary>One source SKY image: one east or west record and one animation frame.</summary>
public sealed record SkyFrameSource(
    int Record,
    int Frame,
    ReadOnlyMemory<byte> IndexedPixels)
{
    public void Validate()
    {
        if (Record is < 0 or > 1 || Frame is < 0 or >= SkyFileDecoder.FramesPerRecord)
        {
            throw new ArgumentOutOfRangeException(nameof(Record), "A SKY frame must identify record zero or one and a frame from zero through thirty-one.");
        }

        if (IndexedPixels.Length != SkyFileDecoder.FrameBytes)
        {
            throw new ArgumentException($"A SKY frame must carry exactly {SkyFileDecoder.FrameBytes} indexed pixels.", nameof(IndexedPixels));
        }
    }
}

/// <summary>One decoded SKY##.DAT file and its per-frame palettes.</summary>
public sealed record SkyFileDocument(
    int SkyIndex,
    string Source,
    IReadOnlyList<Arena2Palette> Palettes,
    IReadOnlyList<SkyFrameSource> Frames)
{
    public SkyFrameSource Frame(int record, int frame) => Frames.Single(candidate => candidate.Record == record && candidate.Frame == frame);

    public void Validate()
    {
        if (SkyIndex is < 0 or >= SkyFileDecoder.FileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(SkyIndex));
        }

        SkyFileDecoder.ValidateSource(Source, nameof(Source));
        if (Palettes.Count != SkyFileDecoder.PaletteCount)
        {
            throw new InvalidOperationException($"SKY source '{Source}' carries {Palettes.Count} palettes instead of {SkyFileDecoder.PaletteCount}.");
        }

        if (Frames.Count != SkyFileDecoder.RecordCount * SkyFileDecoder.FramesPerRecord)
        {
            throw new InvalidOperationException($"SKY source '{Source}' carries {Frames.Count} frames instead of {SkyFileDecoder.RecordCount * SkyFileDecoder.FramesPerRecord}.");
        }

        HashSet<(int Record, int Frame)> keys = [];
        foreach (SkyFrameSource frame in Frames)
        {
            frame.Validate();
            if (!keys.Add((frame.Record, frame.Frame)))
            {
                throw new InvalidOperationException($"SKY source '{Source}' repeats record {frame.Record}, frame {frame.Frame}.");
            }
        }
    }
}

/// <summary>Source-format decoder for the donor's SKY##.DAT day-sky files.</summary>
public static class SkyFileDecoder
{
    public const int FileCount = 32;
    public const int RecordCount = 2;
    public const int PaletteCount = 32;
    public const int FramesPerRecord = 32;
    public const int FrameWidth = 512;
    public const int FrameHeight = 220;
    public const int FrameBytes = FrameWidth * FrameHeight;
    public const int PaletteBytes = Arena2FormatConstants.PaletteHeaderedBytes;
    public const int ImageDataOffset = 549120;
    public const int FileBytes = ImageDataOffset + (RecordCount * FramesPerRecord * FrameBytes);

    /// <summary>Decodes all palettes and both east/west frame records from one SKY source.</summary>
    public static SkyFileDocument Decode(int skyIndex, ReadOnlySpan<byte> bytes, string source)
    {
        if (skyIndex is < 0 or >= FileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(skyIndex), skyIndex, $"SKY index must be within 0..{FileCount - 1}.");
        }

        ValidateSource(source, nameof(source));
        if (bytes.Length != FileBytes)
        {
            throw new Arena2FormatException(source, 0, $"SKY source must be exactly {FileBytes} bytes, got {bytes.Length}");
        }

        byte[] owned = bytes.ToArray();
        Arena2Palette[] palettes = new Arena2Palette[PaletteCount];
        for (int paletteIndex = 0; paletteIndex < PaletteCount; paletteIndex++)
        {
            int offset = checked(paletteIndex * PaletteBytes);
            palettes[paletteIndex] = PaletteDecoder.Decode(owned.AsSpan(offset, PaletteBytes), $"{source} palette {paletteIndex}");
        }

        List<SkyFrameSource> frames = new(RecordCount * FramesPerRecord);
        for (int record = 0; record < RecordCount; record++)
        {
            for (int frame = 0; frame < FramesPerRecord; frame++)
            {
                int index = checked((record * FramesPerRecord) + frame);
                int offset = checked(ImageDataOffset + (index * FrameBytes));
                frames.Add(new(record, frame, owned.AsMemory(offset, FrameBytes)));
            }
        }

        SkyFileDocument document = new(skyIndex, source, palettes, frames);
        document.Validate();
        return document;
    }

    internal static void ValidateSource(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith('\\') || value.Contains('\\')
            || value.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("A SKY source must be a relative slash-separated logical path.", name);
        }
    }
}
