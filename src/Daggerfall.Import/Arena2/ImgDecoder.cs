namespace Daggerfall.Import.Arena2;

/// <summary>Decoded indexed IMG data and source format metadata.</summary>
/// <param name="Source">Logical source identity.</param>
/// <param name="XOffset">Horizontal offset the record declares, or zero for a headerless canvas.</param>
/// <param name="YOffset">Vertical offset the record declares, or zero for a headerless canvas.</param>
/// <param name="Width">Canvas width in pixels.</param>
/// <param name="Height">Canvas height in pixels.</param>
/// <param name="Compression">Compression the record declares; a headerless canvas declares none.</param>
/// <param name="PayloadLength">
/// The payload length the record declares, or the pixel count of the shape a headerless file's
/// length establishes. A record whose declaration disagrees with its shape keeps the declared
/// value here, so the disagreement stays visible rather than being reconciled silently.
/// </param>
/// <param name="IsHeaderless">Whether the shape came from the file length rather than a record header.</param>
/// <param name="Pixels">The canvas pixels, always <c>Width * Height</c> bytes.</param>
public sealed record IndexedImg(
    string Source,
    short XOffset,
    short YOffset,
    ushort Width,
    ushort Height,
    ushort Compression,
    int PayloadLength,
    bool IsHeaderless,
    ReadOnlyMemory<byte> Pixels);

/// <summary>Decoder for supported Arena2 IMG records and the record sequences a classic CIF carries.</summary>
public static class ImgDecoder
{
    /// <summary>The compression value a classic CIF uses for a run-length encoded record.</summary>
    public const ushort RleCompressed = 2;

    /// <summary>The length whose canvas is followed by an embedded palette.</summary>
    public const int EmbeddedPaletteScreenBytes = 64768;

    /// <summary>How many palette bytes follow that canvas.</summary>
    public const int EmbeddedPaletteBytes = 768;

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
        (EmbeddedPaletteScreenBytes, 320, 200),
        (68800, 320, 215),
        (112128, 512, 219),
    ];

    /// <summary>
    /// Decodes the single record an IMG file carries, without pixel reordering.
    /// </summary>
    /// <remarks>
    /// The record's compression field does not decide how its pixels are read: the classic
    /// reader's <c>ImgFile.ReadImage</c> reads <c>Width * Height</c> bytes and never consults the
    /// field, so a declared value this repository does not implement is still a readable image.
    /// Two supplied files declare 2048 and are exactly twelve bytes plus their shape —
    /// <c>TALK00I0.IMG</c> at 320x200 and <c>FRAM00I0.IMG</c> at 96x96 — and refusing them would
    /// refuse images the donor reads. The declared value travels in <see cref="IndexedImg.Compression"/>
    /// so a caller that cares can still see it.
    /// </remarks>
    public static IndexedImg Decode(ReadOnlySpan<byte> bytes, string source)
    {
        CheckedLittleEndianReader reader = new(bytes, source);
        RecordHeader header = ReadHeader(ref reader, source);
        ReadOnlySpan<byte> pixels = reader.ReadBytes(header.Pixels);
        if (reader.Position != reader.Length)
        {
            throw reader.Error($"IMG has trailing bytes after its {header.Pixels}-byte pixel payload");
        }

        return header.ToImage(source, header.PayloadLength, pixels);
    }

    /// <summary>
    /// Decodes the contiguous sequence of single-frame IMG records a classic CIF carries.
    /// </summary>
    /// <remarks>
    /// This is the donor's non-weapon CIF path: records are read until the file ends, each one a
    /// standard IMG record, with no separate directory. A record is framed by the payload length
    /// it declares, which is why a declaration that does not match the record's shape is refused
    /// here rather than tolerated — the walk would otherwise lose its place in the file. Pixels
    /// are read by the record's compression: uncompressed records carry their shape directly, and
    /// compressed records are run-length decoded exactly as the donor's <c>BaseImageFile.ReadRleData</c>
    /// does. A compression this repository does not implement is refused by name.
    /// </remarks>
    public static IReadOnlyList<IndexedImg> DecodeRecordSequence(ReadOnlySpan<byte> bytes, string source)
    {
        CheckedLittleEndianReader reader = new(bytes, source);
        List<IndexedImg> records = [];
        while (reader.Position < reader.Length)
        {
            records.Add(ReadSequenceRecord(ref reader, source));
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
    /// wrong image rather than a reported gap. A documented shape whose file is shorter than
    /// the shape is refused for the same reason — the classic reader would pad the canvas with
    /// the bytes that are missing. A file longer than its shape yields the shape's leading
    /// bytes; the remainder is not part of the canvas and this decoder does not read it, but it
    /// can carry meaning: the 64768-byte length is a 320x200 canvas followed by a 768-byte
    /// palette, which the classic reader reads for exactly `CHGN00I0.IMG`, `DIE_00I0.IMG`,
    /// `PICK02I0.IMG`, `PICK03I0.IMG`, `PRIS00I0.IMG` and `TITL00I0.IMG` (donor
    /// <c>ImgFile.ReadPalette</c>, which also scales that palette's RGB by four because it is
    /// otherwise very dark). Those six files are `CNT-027` source, and reading their palette
    /// belongs to the publication that emits them rather than to this shape table.
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
    /// <param name="reason">Why the length establishes no canvas, when it does not.</param>
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

            int pixels = width * height;
            if (bytes.Length < pixels)
            {
                image = null;
                reason = $"the documented {width}x{height} headerless shape needs {pixels} bytes, but this {bytes.Length}-byte file supplies fewer, so the missing bytes would have to be invented";
                return false;
            }

            image = new IndexedImg(source, 0, 0, width, height, 0, pixels, true, bytes[..pixels].ToArray());
            reason = string.Empty;
            return true;
        }

        image = null;
        reason = $"a headerless image of {bytes.Length} bytes is not one of the {HeaderlessShapes.Length} documented shapes";
        return false;
    }

    /// <summary>
    /// Reads the palette a 64768-byte screen carries after its canvas.
    /// </summary>
    /// <remarks>
    /// Six supplied screens are exactly this length — a 320x200 canvas followed by 768 palette bytes
    /// — and the classic reader reads that trailing palette for exactly those names, scaling its
    /// channels by four. The shape table already stops at the canvas, so the trailing bytes are left
    /// to whoever publishes the screen; this reads them. A screen reached from any other length
    /// carries no palette, which is why the check is on the length the shape table establishes
    /// rather than on a file name: the corpus's six are the only files of this length.
    /// </remarks>
    public static bool TryReadEmbeddedPalette(
        ReadOnlySpan<byte> bytes,
        string source,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Arena2Palette? palette,
        out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (bytes.Length != EmbeddedPaletteScreenBytes)
        {
            palette = null;
            reason = $"a screen of {bytes.Length} bytes carries no {EmbeddedPaletteBytes}-byte embedded palette; only the {EmbeddedPaletteScreenBytes}-byte shape does";
            return false;
        }

        if (!TryDecodeHeaderless(bytes, source, out IndexedImg? image, out reason))
        {
            palette = null;
            return false;
        }

        if (image.Width * image.Height + EmbeddedPaletteBytes != bytes.Length)
        {
            palette = null;
            reason = $"the {image.Width}x{image.Height} canvas of {source} leaves {bytes.Length - (image.Width * image.Height)} bytes, not the {EmbeddedPaletteBytes} an embedded palette needs";
            return false;
        }

        // Six-bit, like the map palette: the classic reader scales it by four for the same reason.
        palette = PaletteDecoder.ScaleChannels(
            PaletteDecoder.Decode(bytes[^EmbeddedPaletteBytes..], $"{source} embedded palette"),
            4);
        reason = string.Empty;
        return true;
    }

    /// <summary>Reads one record of a CIF sequence, framed by the payload length it declares.</summary>
    private static IndexedImg ReadSequenceRecord(ref CheckedLittleEndianReader reader, string source)
    {
        int recordStart = reader.Position;
        RecordHeader header = ReadHeader(ref reader, source);
        if (header.Compression == 0)
        {
            if (header.PayloadLength != header.Pixels)
            {
                throw reader.Error($"uncompressed IMG payload length {header.PayloadLength} does not match {header.Width}x{header.Height}, so the record does not frame the next one");
            }

            ReadOnlySpan<byte> raw = reader.ReadBytes(header.Pixels);
            return header.ToImage(source, header.PayloadLength, raw);
        }

        if (header.Compression != RleCompressed)
        {
            throw reader.Error($"unsupported IMG compression {header.Compression}");
        }

        byte[] pixels = DecodeRunLength(ref reader, header, source);
        // The donor walks records by the declared payload length, so the stream's own length is
        // not what decides where the next record begins.
        reader.Seek(checked(recordStart + Arena2FormatConstants.ImgHeaderBytes + header.PayloadLength));
        return header.ToImage(source, header.PayloadLength, pixels);
    }

    /// <summary>Run-length decodes one record within the payload window the record declares.</summary>
    private static byte[] DecodeRunLength(ref CheckedLittleEndianReader reader, RecordHeader header, string source)
    {
        int windowEnd = checked(reader.Position + header.PayloadLength);
        byte[] pixels = new byte[header.Pixels];
        int written = 0;
        while (written < header.Pixels)
        {
            if (reader.Position >= windowEnd)
            {
                throw reader.Error($"run-length record declares {header.PayloadLength} bytes but its stream does not encode {header.Pixels} pixels within them");
            }

            byte code = reader.ReadByte();
            if (code > 127)
            {
                if (reader.Position >= windowEnd)
                {
                    throw reader.Error("run-length repeat is missing its pixel");
                }

                byte pixel = reader.ReadByte();
                int repeat = code - 127;
                if (written + repeat > header.Pixels)
                {
                    throw reader.Error($"run-length repeat of {repeat} exceeds the {header.Pixels} pixels the record declares");
                }

                pixels.AsSpan(written, repeat).Fill(pixel);
                written += repeat;
            }
            else
            {
                int literal = code + 1;
                if (reader.Position + literal > windowEnd)
                {
                    throw reader.Error($"run-length literal of {literal} bytes exceeds the {header.PayloadLength}-byte payload the record declares");
                }

                if (written + literal > header.Pixels)
                {
                    throw reader.Error($"run-length literal of {literal} bytes exceeds the {header.Pixels} pixels the record declares");
                }

                reader.ReadBytes(literal).CopyTo(pixels.AsSpan(written));
                written += literal;
            }
        }

        return pixels;
    }

    /// <summary>Reads the twelve-byte record header every IMG record and CIF record carries.</summary>
    private static RecordHeader ReadHeader(ref CheckedLittleEndianReader reader, string source)
    {
        short xOffset = reader.ReadInt16();
        short yOffset = reader.ReadInt16();
        ushort width = reader.ReadUInt16();
        ushort height = reader.ReadUInt16();
        ushort compression = reader.ReadUInt16();
        ushort payloadLength = reader.ReadUInt16();
        if (width == 0 || height == 0)
        {
            throw reader.Error($"invalid IMG dimensions {width}x{height}");
        }

        int pixels;
        try
        {
            pixels = checked(width * height);
        }
        catch (OverflowException)
        {
            throw reader.Error($"IMG dimensions {width}x{height} overflow a 32-bit byte count");
        }

        return new RecordHeader(xOffset, yOffset, width, height, compression, payloadLength, pixels);
    }

    /// <summary>One record's declared header, with its shape already checked.</summary>
    private readonly record struct RecordHeader(
        short XOffset,
        short YOffset,
        ushort Width,
        ushort Height,
        ushort Compression,
        ushort PayloadLength,
        int Pixels)
    {
        internal IndexedImg ToImage(string source, int declaredPayloadLength, ReadOnlySpan<byte> pixels) =>
            new(source, XOffset, YOffset, Width, Height, Compression, declaredPayloadLength, false, pixels.ToArray());
    }
}
