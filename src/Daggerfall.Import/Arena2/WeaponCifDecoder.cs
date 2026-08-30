namespace Daggerfall.Import.Arena2;

/// <summary>Source metadata for one classic weapon CIF record.</summary>
public sealed record WeaponCifRecordInfo(ushort Width, ushort Height, short XOffset, short YOffset, ushort FrameCount);

/// <summary>One decoded, row-major, palette-indexed weapon CIF frame.</summary>
public sealed record IndexedWeaponCifFrame(string Source, int RecordOrdinal, int FrameOrdinal, WeaponCifRecordInfo Info, ReadOnlyMemory<byte> Pixels);

/// <summary>Read-only classic WEAPON*.CIF decoder with bounded PackBits-like frame decoding.</summary>
public sealed class WeaponCifArchive
{
    private const int ImageHeaderBytes = 12;
    private const int AnimationHeaderBytes = 76;
    private const int AnimationOffsetCount = 31;
    private const int MaxDimension = 4096;
    private const int MaxFramePixels = MaxDimension * MaxDimension;
    private readonly byte[] data;
    private readonly WeaponRecord[] records;

    private WeaponCifArchive(byte[] data, string source, WeaponRecord[] records)
    {
        this.data = data;
        Source = source;
        this.records = records;
    }

    /// <summary>Logical source identity supplied at parse time.</summary>
    public string Source { get; }

    /// <summary>Number of wield-image and animation records.</summary>
    public int RecordCount => records.Length;

    /// <summary>Gets record source metadata.</summary>
    public WeaponCifRecordInfo GetRecordInfo(int recordOrdinal) => GetRecord(recordOrdinal).Info;

    /// <summary>Decodes one source frame without palette or presentation policy.</summary>
    public IndexedWeaponCifFrame DecodeFrame(int recordOrdinal, int frameOrdinal)
    {
        WeaponRecord record = GetRecord(recordOrdinal);
        if ((uint)frameOrdinal >= record.Info.FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frameOrdinal), $"Weapon CIF frame ordinal must be within 0..{record.Info.FrameCount - 1}.");
        }

        int expected = checked(record.Info.Width * record.Info.Height);
        byte[] pixels;
        if (record is ImageRecord image)
        {
            pixels = image.Compression switch
            {
                0 when image.DataLength == expected => data.AsSpan(image.DataOffset, image.DataLength).ToArray(),
                0 => throw new Arena2FormatException(Source, image.DataOffset, $"weapon wield image has {image.DataLength} bytes, expected {expected}"),
                2 => DecodeRle(image.DataOffset, image.DataLength, expected),
                _ => throw new Arena2FormatException(Source, 8, $"unsupported wield image compression 0x{image.Compression:X4}"),
            };
        }
        else
        {
            AnimationRecord animation = (AnimationRecord)record;
            int start = checked(animation.RecordOffset + animation.FrameOffsets[frameOrdinal]);
            int end = frameOrdinal + 1 < animation.FrameOffsets.Length
                ? checked(animation.RecordOffset + animation.FrameOffsets[frameOrdinal + 1])
                : checked(animation.RecordOffset + animation.TotalSize);
            pixels = DecodeRle(start, end - start, expected);
        }

        return new IndexedWeaponCifFrame(Source, recordOrdinal, frameOrdinal, record.Info, pixels);
    }

    private WeaponRecord GetRecord(int ordinal)
    {
        if ((uint)ordinal >= (uint)records.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), $"Weapon CIF record ordinal must be within 0..{records.Length - 1}.");
        }

        return records[ordinal];
    }

    private byte[] DecodeRle(int offset, int length, int expected)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new Arena2FormatException(Source, Math.Max(0, offset), "weapon CIF RLE input exceeds source bounds");
        }

        byte[] output = new byte[expected];
        int sourcePosition = offset;
        int sourceEnd = offset + length;
        int destination = 0;
        while (destination < expected)
        {
            if (sourcePosition >= sourceEnd)
            {
                throw new Arena2FormatException(Source, sourcePosition, "weapon CIF RLE ended before frame output was complete");
            }

            byte code = data[sourcePosition++];
            if (code > 127)
            {
                int count = code - 127;
                if (sourcePosition >= sourceEnd)
                {
                    throw new Arena2FormatException(Source, sourcePosition, "weapon CIF RLE repeat is missing its pixel");
                }

                if (count > expected - destination)
                {
                    throw new Arena2FormatException(Source, sourcePosition - 1, "weapon CIF RLE repeat exceeds frame bounds");
                }

                output.AsSpan(destination, count).Fill(data[sourcePosition++]);
                destination += count;
            }
            else
            {
                int count = code + 1;
                if (count > sourceEnd - sourcePosition || count > expected - destination)
                {
                    throw new Arena2FormatException(Source, sourcePosition - 1, "weapon CIF RLE literal exceeds its input or frame bounds");
                }

                data.AsSpan(sourcePosition, count).CopyTo(output.AsSpan(destination, count));
                sourcePosition += count;
                destination += count;
            }
        }

        return output;
    }

    /// <summary>Parses a classic weapon CIF from immutable caller-owned bytes.</summary>
    public static WeaponCifArchive Parse(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        byte[] data = bytes.ToArray();
        CheckedLittleEndianReader first = new(data, source);
        short xOffset = first.ReadInt16();
        short yOffset = first.ReadInt16();
        ushort width = PositiveDimension(first.ReadInt16(), first, "wield image width");
        ushort height = PositiveDimension(first.ReadInt16(), first, "wield image height");
        EnsureFramePixels(width, height, source, 4);
        ushort compression = first.ReadUInt16();
        if (compression is not 0 and not 2)
        {
            throw first.Error($"unsupported wield image compression 0x{compression:X4}");
        }

        ushort dataLength = first.ReadUInt16();
        if (dataLength > data.Length - ImageHeaderBytes)
        {
            throw first.Error($"weapon wield image data length {dataLength} exceeds source length {data.Length}");
        }

        List<WeaponRecord> records = [new ImageRecord(new WeaponCifRecordInfo(width, height, xOffset, yOffset, 1), compression, ImageHeaderBytes, dataLength)];
        int position = checked(ImageHeaderBytes + dataLength);
        while (position < data.Length)
        {
            if (position > data.Length - AnimationHeaderBytes)
            {
                throw new Arena2FormatException(source, position, "weapon animation header exceeds source bounds");
            }

            CheckedLittleEndianReader header = new(data, source);
            header.Seek(position);
            ushort frameWidth = header.ReadUInt16();
            ushort frameHeight = header.ReadUInt16();
            EnsureFramePixels(frameWidth, frameHeight, source, position);
            header.ReadUInt16();
            short frameXOffset = header.ReadInt16();
            short lastFrameYOffset = header.ReadInt16();
            header.ReadInt16();
            List<ushort> frameOffsets = [];
            for (int index = 0; index < AnimationOffsetCount; index++)
            {
                ushort frameOffset = header.ReadUInt16();
                if (frameOffset != 0)
                {
                    frameOffsets.Add(frameOffset);
                }
            }

            ushort totalSize = header.ReadUInt16();
            if (frameOffsets.Count == 0 || totalSize < AnimationHeaderBytes || totalSize > data.Length - position)
            {
                throw header.Error($"weapon animation at {position} has invalid frame offsets or total size {totalSize}");
            }

            for (int index = 0; index < frameOffsets.Count; index++)
            {
                ushort frameOffset = frameOffsets[index];
                if (frameOffset < AnimationHeaderBytes || frameOffset >= totalSize || (index > 0 && frameOffset <= frameOffsets[index - 1]))
                {
                    throw header.Error($"weapon animation frame offset {frameOffset} is outside or not ordered within {totalSize}-byte record");
                }
            }

            records.Add(new AnimationRecord(new WeaponCifRecordInfo(frameWidth, frameHeight, frameXOffset, lastFrameYOffset, (ushort)frameOffsets.Count), position, totalSize, [.. frameOffsets]));
            position = checked(position + totalSize);
        }

        return new WeaponCifArchive(data, source, [.. records]);
    }

    private static ushort PositiveDimension(short value, CheckedLittleEndianReader reader, string label)
    {
        if (value <= 0)
        {
            throw reader.Error($"{label} must be positive, got {value}");
        }

        return (ushort)value;
    }

    private static void EnsureFramePixels(ushort width, ushort height, string source, int offset)
    {
        if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension)
        {
            throw new Arena2FormatException(source, offset, $"weapon CIF dimensions {width}x{height} exceed supported bounds");
        }

        int pixels = checked(width * height);
        if (pixels > MaxFramePixels)
        {
            throw new Arena2FormatException(source, offset, $"weapon CIF frame requires {pixels} pixels, above the supported limit");
        }
    }

    private abstract record WeaponRecord(WeaponCifRecordInfo Info);
    private sealed record ImageRecord(WeaponCifRecordInfo Info, ushort Compression, int DataOffset, int DataLength) : WeaponRecord(Info);
    private sealed record AnimationRecord(WeaponCifRecordInfo Info, int RecordOffset, int TotalSize, ushort[] FrameOffsets) : WeaponRecord(Info);
}
