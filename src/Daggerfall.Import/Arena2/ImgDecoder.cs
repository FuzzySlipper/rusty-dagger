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
    /// <summary>Decodes a headered, uncompressed IMG record without pixel reordering.</summary>
    public static IndexedImg Decode(ReadOnlySpan<byte> bytes, string source)
    {
        CheckedLittleEndianReader reader = new(bytes, source);
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
        if (reader.Position != reader.Length)
        {
            throw reader.Error($"IMG has trailing bytes after its {pixelsLength}-byte pixel payload");
        }

        return new IndexedImg(source, xOffset, yOffset, width, height, compression, payloadLength, false, pixels.ToArray());
    }

    /// <summary>Decodes the one explicit, source-selected 320x200 headerless UI canvas shape.</summary>
    public static IndexedImg DecodeHeaderlessUiCanvas(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (bytes.Length != Arena2FormatConstants.HeaderlessUiImgBytes)
        {
            throw new Arena2FormatException(source, 0, $"headerless UI IMG must contain exactly {Arena2FormatConstants.HeaderlessUiImgBytes} bytes, got {bytes.Length}");
        }

        return new IndexedImg(
            source,
            0,
            0,
            Arena2FormatConstants.HeaderlessUiImgWidth,
            Arena2FormatConstants.HeaderlessUiImgHeight,
            0,
            (ushort)Arena2FormatConstants.HeaderlessUiImgBytes,
            true,
            bytes.ToArray());
    }
}
