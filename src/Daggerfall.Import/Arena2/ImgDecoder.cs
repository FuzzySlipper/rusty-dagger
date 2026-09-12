namespace Daggerfall.Import.Arena2;

/// <summary>Decoded indexed IMG data and source format metadata.</summary>
public sealed record IndexedImg(
    string Source,
    short XOffset,
    short YOffset,
    ushort Width,
    ushort Height,
    ushort Compression,
    ushort PayloadLength,
    bool IsHeaderless,
    ReadOnlyMemory<byte> Pixels);

/// <summary>Decoder for supported uncompressed Arena2 IMG records.</summary>
public static class ImgDecoder
{
    /// <summary>
    /// The headerless shapes the classic reader recognises by file length alone, from the
    /// donor's <c>ImgFile.GetHeaderlessFileImageDimensions</c>. A file whose length is one of
    /// these carries no IMG record header: its shape comes from the length, and the classic
    /// reader applies the table before it reads any header bytes.
    /// </summary>
    public static readonly (int Bytes, ushort Width, ushort Height)[] HeaderlessShapes =
    [
        (44, 22, 22),
        (289, 17, 17),
        (441, 49, 9),
        (512, 32, 16),
        (720, 9, 80),
        (990, 45, 22),
        (1720, 43, 40),
        (2140, 107, 20),
        (2916, 81, 36),
        (3200, 40, 80),
        (3938, 179, 22),
        (4280, 107, 40),
        (4508, 322, 14),
        (20480, 320, 64),
        (26496, 184, 144),
        (Arena2FormatConstants.HeaderlessUiImgBytes, 320, 200),
        (64768, 320, 200),
        (68800, 320, 215),
        (112128, 512, 219),
    ];

    /// <summary>Decodes a headered, uncompressed IMG record without pixel reordering.</summary>
    public static IndexedImg Decode(ReadOnlySpan<byte> bytes, string source)
    {
        CheckedLittleEndianReader reader = new(bytes, source);
        IndexedImg image = ReadRecord(ref reader, source);
        if (reader.Position != reader.Length)
        {
            throw reader.Error($"IMG has trailing bytes after its {image.PayloadLength}-byte pixel payload");
        }

        return image;
    }

    /// <summary>
    /// Decodes the contiguous sequence of single-frame IMG records a classic CIF carries.
    /// </summary>
    /// <remarks>
    /// This is the donor's non-weapon CIF path: records are read until the file ends, each one
    /// a standard IMG record, with no separate directory. A record that would run past the end
    /// of the file is refused rather than truncated, so a partial trailing record is a source
    /// error and not a shorter sequence.
    /// </remarks>
    public static IReadOnlyList<IndexedImg> DecodeRecordSequence(ReadOnlySpan<byte> bytes, string source)
    {
        CheckedLittleEndianReader reader = new(bytes, source);
        List<IndexedImg> records = [];
        while (reader.Position < reader.Length)
        {
            records.Add(ReadRecord(ref reader, source));
        }

        if (records.Count == 0)
        {
            throw reader.Error("the file carries no IMG record");
        }

        return records;
    }

    /// <summary>
    /// Decodes the headerless canvas whose shape the file length establishes.
    /// </summary>
    /// <remarks>
    /// A length the documented table does not carry has no established shape, so this refuses
    /// rather than assuming one: the same bytes read at the wrong shape would be a silently
    /// wrong image rather than a reported gap.
    /// </remarks>
    public static IndexedImg DecodeHeaderless(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!TryDecodeHeaderless(bytes, source, out IndexedImg? image, out string reason))
        {
            throw new Arena2FormatException(source, 0, reason);
        }

        return image;
    }

    /// <summary>Reads the headerless canvas whose shape the file length establishes.</summary>
    /// <param name="bytes">The whole supplied file.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    /// <param name="image">The canvas, when a documented shape carries this length.</param>
    /// <param name="reason">Why the length establishes no shape, when it does not.</param>
    public static bool TryDecodeHeaderless(
        ReadOnlySpan<byte> bytes,
        string source,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IndexedImg? image,
        out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        foreach ((int documentedBytes, ushort width, ushort height) in HeaderlessShapes)
        {
            if (bytes.Length != documentedBytes)
            {
                continue;
            }

            image = new IndexedImg(
                source,
                0,
                0,
                width,
                height,
                0,
                (ushort)documentedBytes,
                true,
                bytes.ToArray());
            reason = string.Empty;
            return true;
        }

        image = null;
        reason = $"a headerless image of {bytes.Length} bytes is not one of the {HeaderlessShapes.Length} documented shapes";
        return false;
    }

    /// <summary>Reads one headered record, with its payload, from the current position.</summary>
    private static IndexedImg ReadRecord(ref CheckedLittleEndianReader reader, string source)
    {
        short xOffset = reader.ReadInt16();
        short yOffset = reader.ReadInt16();
        ushort width = reader.ReadUInt16();
        ushort height = reader.ReadUInt16();
        ushort compression = reader.ReadUInt16();
        ushort payloadLength = reader.ReadUInt16();
        if (compression != 0)
        {
            throw reader.Error($"unsupported IMG compression {compression}");
        }

        if (width == 0 || height == 0)
        {
            throw reader.Error($"invalid IMG dimensions {width}x{height}");
        }

        int pixelsLength;
        try
        {
            pixelsLength = checked(width * height);
        }
        catch (OverflowException)
        {
            throw reader.Error($"IMG dimensions {width}x{height} overflow a 32-bit byte count");
        }

        if (pixelsLength != payloadLength)
        {
            throw reader.Error($"uncompressed IMG payload length {payloadLength} does not match {width}x{height}");
        }

        ReadOnlySpan<byte> pixels = reader.ReadBytes(pixelsLength);
        return new IndexedImg(source, xOffset, yOffset, width, height, compression, payloadLength, false, pixels.ToArray());
    }
}
