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
/// <summary>How one UI canvas was read, or that neither path read it.</summary>
public enum UiMediaDecode
{
    /// <summary>Read as a standard IMG record.</summary>
    Header,

    /// <summary>Read as a headerless UI canvas.</summary>
    Headerless,

    /// <summary>Neither path read it.</summary>
    Unread,
}

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
    /// <summary>
    /// Reads one UI canvas by the repository's single probing policy: the standard IMG
    /// record first, then the headerless canvas the classic UI publication reads.
    /// </summary>
    /// <remarks>
    /// This exists so the surveys and any other caller probe the same way. The publication
    /// that emits the canvases still dispatches per file from its own source table; that
    /// table decides which files it publishes, while this decides only how a supplied file
    /// is read when a caller asks.
    /// </remarks>
    public static bool TryDecodeUi(
        ReadOnlySpan<byte> bytes,
        string source,
        out IndexedImg? image,
        out UiMediaDecode decode,
        out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        try
        {
            image = Decode(bytes, source);
            decode = UiMediaDecode.Header;
            reason = string.Empty;
            return true;
        }
        catch (Arena2FormatException headerFailure)
        {
            try
            {
                image = DecodeHeaderlessUiCanvas(bytes, source);
                decode = UiMediaDecode.Headerless;
                reason = string.Empty;
                return true;
            }
            catch (Arena2FormatException headerlessFailure)
            {
                image = null;
                decode = UiMediaDecode.Unread;
                reason = $"{headerFailure.Message} The headerless UI canvas path also refused it: {headerlessFailure.Message}";
                return false;
            }
        }
    }

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
