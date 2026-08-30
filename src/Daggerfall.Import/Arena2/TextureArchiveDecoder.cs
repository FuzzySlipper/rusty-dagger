namespace Daggerfall.Import.Arena2;

/// <summary>Known virtual solid-colour texture archive variants.</summary>
public enum TextureSolidPalette
{
    /// <summary>Record ordinal is the palette index.</summary>
    FirstHalf,

    /// <summary>Record ordinal plus 128 is the palette index.</summary>
    SecondHalf,
}

/// <summary>Source metadata for one texture record.</summary>
public sealed record TextureRecordInfo(
    short Width,
    short Height,
    ushort Compression,
    ushort FrameCount,
    short ScaleX,
    short ScaleY);

/// <summary>One decoded, row-major, palette-indexed texture frame.</summary>
public sealed record IndexedTextureFrame(
    string Source,
    int RecordOrdinal,
    int FrameOrdinal,
    ushort Width,
    ushort Height,
    ReadOnlyMemory<byte> Pixels);

/// <summary>Read-only parsed TEXTURE.nnn archive backed by caller-supplied source bytes.</summary>
public sealed class TextureArchive
{
    private const int HeaderBytes = 26;
    private const int RecordTableEntryBytes = 20;
    private const int RecordHeaderBytes = 28;
    private const int SingleFrameRowStride = 256;
    private const int VirtualSolidDimension = 32;
    private const int MaxDimension = 4096;
    private const int MaxFramePixels = MaxDimension * MaxDimension;
    private const ushort Uncompressed = 0x0000;
    private const ushort ImageRle = 0x0108;
    private const ushort RecordRle = 0x1108;

    private readonly byte[] data;
    private readonly TextureRecord[] records;
    private readonly TextureSolidPalette? solidPalette;

    internal TextureArchive(byte[] data, string source, TextureRecord[] records, TextureSolidPalette? solidPalette)
    {
        this.data = data;
        Source = source;
        this.records = records;
        this.solidPalette = solidPalette;
    }

    /// <summary>Logical source identity supplied at parse time.</summary>
    public string Source { get; }

    /// <summary>Number of source or virtual records.</summary>
    public int RecordCount => records.Length;

    /// <summary>Gets source metadata for a record.</summary>
    public TextureRecordInfo GetRecordInfo(int recordOrdinal)
    {
        return GetRecord(recordOrdinal).Info;
    }

    /// <summary>Decodes one frame without applying palette or renderer policy.</summary>
    public IndexedTextureFrame DecodeFrame(int recordOrdinal, int frameOrdinal)
    {
        TextureRecord record = GetRecord(recordOrdinal);
        ValidateFrameOrdinal(record, frameOrdinal);
        (ushort width, ushort height) = ValidateDimensions(record.Info, record.Offset);
        byte[] pixels = solidPalette.HasValue
            ? DecodeSolid(recordOrdinal, width, height)
            : record.Info.Compression switch
            {
                Uncompressed => DecodeUncompressed(record, frameOrdinal, width, height),
                ImageRle or RecordRle => DecodeRle(record, frameOrdinal, width, height),
                _ => throw new Arena2FormatException(Source, record.Offset + 8, $"unsupported texture compression 0x{record.Info.Compression:X4}"),
            };
        return new IndexedTextureFrame(Source, recordOrdinal, frameOrdinal, width, height, pixels);
    }

    private TextureRecord GetRecord(int ordinal)
    {
        if ((uint)ordinal >= (uint)records.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), $"Texture record ordinal must be within 0..{records.Length - 1}.");
        }

        return records[ordinal];
    }

    private static void ValidateFrameOrdinal(TextureRecord record, int frameOrdinal)
    {
        if ((uint)frameOrdinal >= record.Info.FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frameOrdinal), $"Texture frame ordinal must be within 0..{record.Info.FrameCount - 1}.");
        }
    }

    private (ushort Width, ushort Height) ValidateDimensions(TextureRecordInfo info, int offset)
    {
        if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxDimension || info.Height > MaxDimension)
        {
            throw new Arena2FormatException(Source, offset + 4, $"texture dimensions {info.Width}x{info.Height} exceed supported bounds");
        }

        int pixelCount;
        try
        {
            pixelCount = checked(info.Width * info.Height);
        }
        catch (OverflowException)
        {
            throw new Arena2FormatException(Source, offset + 4, "texture dimensions overflow a 32-bit pixel count");
        }

        if (pixelCount > MaxFramePixels)
        {
            throw new Arena2FormatException(Source, offset + 4, $"texture frame requires {pixelCount} pixels, above the supported limit");
        }

        return ((ushort)info.Width, (ushort)info.Height);
    }

    private byte[] DecodeSolid(int recordOrdinal, ushort width, ushort height)
    {
        int pixels = checked(width * height);
        byte paletteIndex = solidPalette == TextureSolidPalette.FirstHalf
            ? checked((byte)recordOrdinal)
            : unchecked((byte)(128 + recordOrdinal));
        return Enumerable.Repeat(paletteIndex, pixels).ToArray();
    }

    private byte[] DecodeUncompressed(TextureRecord record, int frameOrdinal, ushort width, ushort height)
    {
        int pixelCount = checked(width * height);
        byte[] pixels = new byte[pixelCount];
        int dataStart = CheckedAdd(record.Offset, record.DataOffset, "texture image data position");
        if (record.Info.FrameCount == 1)
        {
            for (int row = 0; row < height; row++)
            {
                int sourceOffset = CheckedAdd(dataStart, checked(row * SingleFrameRowStride), "texture row position");
                RequireRange(sourceOffset, width, "texture uncompressed row");
                data.AsSpan(sourceOffset, width).CopyTo(pixels.AsSpan(row * width, width));
            }

            return pixels;
        }

        int tableEntry = CheckedAdd(dataStart, checked(frameOrdinal * sizeof(int)), "texture frame table entry");
        int frameOffset = ReadInt32At(tableEntry, "texture frame table entry");
        if (frameOffset < 0)
        {
            throw new Arena2FormatException(Source, tableEntry, $"texture frame {frameOrdinal} has negative offset {frameOffset}");
        }

        int frameStart = CheckedAdd(dataStart, frameOffset, "texture frame position");
        CheckedLittleEndianReader frame = ReaderAt(frameStart);
        short frameWidth = frame.ReadInt16();
        short frameHeight = frame.ReadInt16();
        if (frameWidth < 0 || frameHeight < 0 || frameWidth > width || frameHeight > height)
        {
            throw frame.Error($"texture frame dimensions {frameWidth}x{frameHeight} exceed record {width}x{height}");
        }

        int destination = 0;
        for (int row = 0; row < frameHeight; row++)
        {
            int x = 0;
            while (x < frameWidth)
            {
                int transparent = frame.ReadByte();
                if (transparent > frameWidth - x)
                {
                    throw frame.Error("texture transparent run exceeds frame row");
                }

                x += transparent;
                destination = CheckedAdd(destination, transparent, "texture transparent output position");
                int opaque = frame.ReadByte();
                if (transparent == 0 && opaque == 0)
                {
                    throw frame.Error("texture frame RLE contains a zero-progress run");
                }

                if (opaque > frameWidth - x || opaque > pixels.Length - destination)
                {
                    throw frame.Error("texture opaque run exceeds frame bounds");
                }

                frame.ReadBytes(opaque).CopyTo(pixels.AsSpan(destination, opaque));
                x += opaque;
                destination = CheckedAdd(destination, opaque, "texture opaque output position");
            }
        }

        return pixels;
    }

    private byte[] DecodeRle(TextureRecord record, int frameOrdinal, ushort width, ushort height)
    {
        int pixelCount = checked(width * height);
        byte[] pixels = new byte[pixelCount];
        int frameTableOffset = checked(height * frameOrdinal * sizeof(int));
        int rowTable = CheckedAdd(CheckedAdd(record.Offset, record.DataOffset, "texture RLE data position"), frameTableOffset, "texture RLE row table");
        RequireRange(rowTable, checked(height * sizeof(int)), "texture RLE row table");

        int destination = 0;
        for (int row = 0; row < height; row++)
        {
            CheckedLittleEndianReader tableEntry = ReaderAt(CheckedAdd(rowTable, checked(row * sizeof(int)), "texture RLE row table entry"));
            short relativeOffset = tableEntry.ReadInt16();
            ushort rowEncoding = tableEntry.ReadUInt16();
            if (relativeOffset < 0)
            {
                throw tableEntry.Error($"texture RLE row {row} has negative offset {relativeOffset}");
            }

            CheckedLittleEndianReader encoded = ReaderAt(CheckedAdd(record.Offset, relativeOffset, "texture RLE row position"));
            if (rowEncoding != 0x8000)
            {
                RequireRange(encoded.Position, width, "texture RLE raw row");
                data.AsSpan(encoded.Position, width).CopyTo(pixels.AsSpan(destination, width));
                destination = CheckedAdd(destination, width, "texture RLE raw output position");
                continue;
            }

            int rowWidth = encoded.ReadUInt16();
            if (rowWidth > width)
            {
                throw encoded.Error($"texture RLE row width {rowWidth} exceeds record width {width}");
            }

            int rowPosition = 0;
            while (rowPosition < rowWidth)
            {
                short probe = encoded.ReadInt16();
                if (probe == 0)
                {
                    break;
                }

                int count = probe < 0 ? -probe : probe;
                if (count > rowWidth - rowPosition || count > pixels.Length - destination)
                {
                    throw encoded.Error("texture RLE run exceeds row or output bounds");
                }

                if (probe < 0)
                {
                    pixels.AsSpan(destination, count).Fill(encoded.ReadByte());
                }
                else
                {
                    encoded.ReadBytes(count).CopyTo(pixels.AsSpan(destination, count));
                }

                rowPosition += count;
                destination = CheckedAdd(destination, count, "texture RLE output position");
            }
        }

        return pixels;
    }

    private CheckedLittleEndianReader ReaderAt(int offset)
    {
        CheckedLittleEndianReader reader = new(data, Source);
        reader.Seek(offset);
        return reader;
    }

    private int ReadInt32At(int offset, string context)
    {
        RequireRange(offset, sizeof(int), context);
        return ReaderAt(offset).ReadInt32();
    }

    private void RequireRange(int offset, int length, string context)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new Arena2FormatException(Source, Math.Max(0, offset), $"{context} requires {length} bytes within source length {data.Length}");
        }
    }

    private int CheckedAdd(int offset, int length, string context)
    {
        try
        {
            return checked(offset + length);
        }
        catch (OverflowException)
        {
            throw new Arena2FormatException(Source, offset, $"{context} overflows a 32-bit source offset");
        }
    }

    internal sealed record TextureRecord(int Offset, int DataOffset, TextureRecordInfo Info);

    /// <summary>Parses an archive from immutable caller-owned bytes.</summary>
    public static TextureArchive Parse(ReadOnlySpan<byte> bytes, string source, TextureSolidPalette? solidPalette = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        byte[] ownedData = bytes.ToArray();
        CheckedLittleEndianReader header = new(ownedData, source);
        header.ReadBytes(HeaderBytes);
        header.Seek(0);
        short signedCount = header.ReadInt16();
        if (signedCount < 0)
        {
            throw header.Error($"negative texture record count {signedCount}");
        }

        int sourceCount = signedCount;
        if (solidPalette.HasValue)
        {
            int virtualCount = Math.Max(1, sourceCount);
            TextureRecordInfo info = new(VirtualSolidDimension, VirtualSolidDimension, Uncompressed, 1, 0, 0);
            TextureRecord[] virtualRecords = Enumerable.Range(0, virtualCount).Select(_ => new TextureRecord(0, 0, info)).ToArray();
            return new TextureArchive(ownedData, source, virtualRecords, solidPalette);
        }

        int tableBytes;
        try
        {
            tableBytes = checked(sourceCount * RecordTableEntryBytes);
        }
        catch (OverflowException)
        {
            throw new Arena2FormatException(source, HeaderBytes, "texture record table size overflows a 32-bit byte count");
        }

        if (tableBytes > ownedData.Length - HeaderBytes)
        {
            throw new Arena2FormatException(source, HeaderBytes, $"texture record table requires {tableBytes} bytes within source length {ownedData.Length}");
        }

        TextureRecord[] records = new TextureRecord[sourceCount];
        for (int ordinal = 0; ordinal < records.Length; ordinal++)
        {
            CheckedLittleEndianReader entry = new(ownedData, source);
            entry.Seek(HeaderBytes + (ordinal * RecordTableEntryBytes));
            entry.ReadInt16();
            int recordOffset = entry.ReadInt32();
            if (recordOffset < 0)
            {
                throw entry.Error($"texture record {ordinal} has negative position {recordOffset}");
            }

            if (recordOffset > ownedData.Length - RecordHeaderBytes)
            {
                throw new Arena2FormatException(source, recordOffset, $"texture record {ordinal} header exceeds source length {ownedData.Length}");
            }

            CheckedLittleEndianReader record = new(ownedData, source);
            record.Seek(recordOffset);
            record.ReadInt16();
            record.ReadInt16();
            short width = record.ReadInt16();
            short height = record.ReadInt16();
            ushort compression = record.ReadUInt16();
            record.ReadUInt32();
            uint unsignedDataOffset = record.ReadUInt32();
            record.ReadInt16();
            ushort frameCount = record.ReadUInt16();
            record.ReadInt16();
            short scaleX = record.ReadInt16();
            short scaleY = record.ReadInt16();
            if (unsignedDataOffset > int.MaxValue)
            {
                throw record.Error($"texture record {ordinal} data offset {unsignedDataOffset} exceeds a supported source offset");
            }

            if (frameCount == 0)
            {
                throw record.Error($"texture record {ordinal} has no frames");
            }

            int dataOffset = (int)unsignedDataOffset;
            try
            {
                _ = checked(recordOffset + dataOffset);
            }
            catch (OverflowException)
            {
                throw record.Error($"texture record {ordinal} data offset overflows a 32-bit source position");
            }

            records[ordinal] = new TextureRecord(recordOffset, dataOffset, new TextureRecordInfo(width, height, compression, frameCount, scaleX, scaleY));
        }

        return new TextureArchive(ownedData, source, records, null);
    }
}
