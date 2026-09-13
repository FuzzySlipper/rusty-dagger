namespace Daggerfall.Import.Arena2;

/// <summary>One sub-chunk inside a frame: its type, its size, and where it sits.</summary>
/// <param name="Type">The chunk's type.</param>
/// <param name="Offset">The chunk's byte offset in the container.</param>
/// <param name="Size">The chunk's declared size in bytes, including its own header.</param>
public sealed record FlcChunk(ushort Type, int Offset, int Size);

/// <summary>One frame in an FLC container: where it starts, its chunk type, and its declared size.</summary>
/// <param name="Index">The frame's ordinal in the container.</param>
/// <param name="Offset">The frame's byte offset in the file.</param>
/// <param name="ChunkType">The frame's chunk type, which is the container's frame marker.</param>
/// <param name="Size">The frame's declared size in bytes, including its own header.</param>
/// <param name="ChunkCount">How many sub-chunks the frame declares.</param>
/// <param name="Chunks">The frame's sub-chunks, in order, which say what the frame is made of.</param>
public sealed record FlcFrame(int Index, int Offset, ushort ChunkType, int Size, int ChunkCount, IReadOnlyList<FlcChunk> Chunks);

/// <summary>
/// The header of an FLC container: the animation's shape, and its frames in order.
/// </summary>
/// <param name="FileSize">The container's own declared size.</param>
/// <param name="FrameCount">The frames the header declares.</param>
/// <param name="Width">The canvas width every frame shares.</param>
/// <param name="Height">The canvas height every frame shares.</param>
/// <param name="PixelDepth">The pixel depth every frame shares.</param>
/// <param name="PrefixBytes">The bytes of the optional prefix chunk between the header and the first frame, or zero.</param>
/// <param name="Frames">The frames, in container order.</param>
public sealed record FlcContainer(
    int FileSize,
    int FrameCount,
    int Width,
    int Height,
    int PixelDepth,
    int PrefixBytes,
    IReadOnlyList<FlcFrame> Frames);

/// <summary>
/// Reads the FLC container three Daggerfall class portraits are stored in.
/// </summary>
/// <remarks>
/// The layout is the donor's <c>Assets/Scripts/API/FlcFile.cs</c> header reader: a declared file
/// size, the file id, the frame count, the canvas shape, then a fixed run of creation and aspect
/// fields, ending with the first two frame offsets - 128 bytes in all. Frames follow as
/// size-prefixed chunks.
/// <para>
/// This reads the container, not an animation runtime: it answers what shape the file is and where
/// its frames are, so a publisher can decode canvases and a caller can refuse a file that only
/// looks like an FLC. Playback belongs to whoever renders, and this repository has no second
/// renderer.
/// </para>
/// </remarks>
public static class FlcDecoder
{
    /// <summary>The file id every FLC container carries.</summary>
    public const ushort Magic = 0xAF12;

    /// <summary>The header length the donor's reader consumes.</summary>
    public const int HeaderBytes = 128;

    /// <summary>The chunk type each frame starts with.</summary>
    public const ushort FrameChunkType = 0xF1FA;

    /// <summary>The length of a frame's own header: its size, type and chunk count.</summary>
    public const int FrameHeaderBytes = 16;

    /// <summary>The chunk type of the prefix chunk a container may carry before its first frame.</summary>
    public const ushort PrefixChunkType = 0xF100;

    /// <summary>A 256-colour palette chunk.</summary>
    public const ushort Color256ChunkType = 4;

    /// <summary>A 64-colour palette chunk, whose channels are scaled by four.</summary>
    public const ushort Color64ChunkType = 11;

    /// <summary>A whole-frame run-length image.</summary>
    public const ushort ByteRunChunkType = 15;

    /// <summary>A frame delta against the previous frame.</summary>
    public const ushort DeltaFlcChunkType = 7;

    /// <summary>A thumbnail the donor skips.</summary>
    public const ushort PstampChunkType = 18;

    /// <summary>
    /// Reads the container header and walks its frames.
    /// </summary>
    /// <param name="bytes">The container's bytes.</param>
    /// <param name="source">Logical source identity, for diagnostics.</param>
    /// <param name="container">The container, when it is one.</param>
    /// <param name="reason">Why the bytes are not a container this reader accepts.</param>
    public static bool TryRead(ReadOnlySpan<byte> bytes, string source, out FlcContainer? container, out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        container = null;
        if (bytes.Length < HeaderBytes)
        {
            reason = $"an FLC container needs at least its {HeaderBytes}-byte header, and '{source}' has {bytes.Length} bytes";
            return false;
        }

        CheckedLittleEndianReader reader = new(bytes[..HeaderBytes].ToArray(), source);
        int fileSize = reader.ReadInt32();
        int magic = reader.ReadUInt16();
        if (magic != Magic)
        {
            reason = $"'{source}' declares file id 0x{magic:X4} rather than an FLC container's 0x{Magic:X4}";
            return false;
        }

        int frames = reader.ReadInt16();
        int width = reader.ReadInt16();
        int height = reader.ReadInt16();
        int depth = reader.ReadInt16();
        if (frames <= 0 || width <= 0 || height <= 0)
        {
            reason = $"'{source}' declares {frames} frames of {width}x{height}, which is not a container this reader can decode";
            return false;
        }

        // The first frame offset sits after the fixed header run, which is where the donor seeks to.
        reader.Seek(FirstFrameOffsetBytes);
        int offset = reader.ReadInt32();
        if (offset < HeaderBytes || offset > bytes.Length - FrameHeaderBytes)
        {
            reason = $"'{source}' declares its first frame at byte {offset}, outside its {bytes.Length} bytes";
            return false;
        }

        // A container may carry a prefix chunk between the header and its frames, and the supplied
        // class portraits all do. Reading it is what makes the declared first frame offset a fact to
        // check rather than a number to trust: with a prefix the frames begin after it, and without
        // one they begin at the header.
        int prefix = 0;
        if (offset > HeaderBytes)
        {
            CheckedLittleEndianReader candidate = new(bytes[HeaderBytes..(HeaderBytes + 8)].ToArray(), source);
            int prefixSize = candidate.ReadInt32();
            int prefixType = candidate.ReadUInt16();
            if (prefixType != PrefixChunkType || prefixSize != offset - HeaderBytes)
            {
                reason = $"'{source}' declares its first frame at byte {offset} but carries no {offset - HeaderBytes}-byte prefix chunk there";
                return false;
            }

            prefix = prefixSize;
        }

        List<FlcFrame> framesRead = [];
        int position = offset;
        while (framesRead.Count < frames)
        {
            if (position > bytes.Length - FrameHeaderBytes)
            {
                reason = $"'{source}' declares {frames} frames but its {framesRead.Count + 1}th frame header does not fit at byte {position} of {bytes.Length}";
                return false;
            }

            CheckedLittleEndianReader frame = new(bytes[position..(position + FrameHeaderBytes)].ToArray(), source);
            int size = frame.ReadInt32();
            int type = frame.ReadUInt16();
            int chunks = frame.ReadUInt16();
            if (type != FrameChunkType)
            {
                reason = $"'{source}' frame {framesRead.Count} at byte {position} declares chunk type 0x{type:X4} rather than a frame's 0x{FrameChunkType:X4}";
                return false;
            }

            if (size < FrameHeaderBytes || position + size > bytes.Length)
            {
                reason = $"'{source}' frame {framesRead.Count} at byte {position} declares {size} bytes, which does not fit its {bytes.Length} bytes";
                return false;
            }

            // A frame's own header is followed by its sub-chunks, which are what a decoder actually
            // reads: the frame marker says where a frame is, not what it contains.
            List<FlcChunk> subChunks = [];
            int chunkPosition = position + FrameHeaderBytes;
            int frameEnd = position + size;
            for (int index = 0; index < chunks; index++)
            {
                if (chunkPosition > frameEnd - 6)
                {
                    reason = $"'{source}' frame {framesRead.Count} declares {chunks} chunks but its {index + 1}th chunk header does not fit before byte {frameEnd}";
                    return false;
                }

                CheckedLittleEndianReader chunk = new(bytes[chunkPosition..(chunkPosition + 6)].ToArray(), source);
                int chunkSize = chunk.ReadInt32();
                int chunkType = chunk.ReadUInt16();
                if (chunkSize < 6 || chunkPosition + chunkSize > frameEnd)
                {
                    reason = $"'{source}' frame {framesRead.Count} chunk {index} at byte {chunkPosition} declares {chunkSize} bytes, which does not fit the frame ending at {frameEnd}";
                    return false;
                }

                subChunks.Add(new FlcChunk((ushort)chunkType, chunkPosition, chunkSize));
                chunkPosition += chunkSize;
            }

            if (chunkPosition != frameEnd)
            {
                reason = $"'{source}' frame {framesRead.Count} ends at {frameEnd} but its chunks end at {chunkPosition}, so {frameEnd - chunkPosition} bytes belong to no chunk";
                return false;
            }

            framesRead.Add(new FlcFrame(framesRead.Count, position, (ushort)type, size, chunks, subChunks));
            position += size;
        }

        container = new FlcContainer(fileSize, frames, width, height, depth, prefix, framesRead);
        reason = string.Empty;
        return true;
    }

    /// <summary>Where the first frame offset sits, after the header fields the donor reads.</summary>
    private const int FirstFrameOffsetBytes = 80;

    /// <summary>One decoded frame: the canvas indices a frame holds, in top-down order.</summary>
    /// <param name="Index">The frame's ordinal.</param>
    /// <param name="Width">The frame's width.</param>
    /// <param name="Height">The frame's height.</param>
    /// <param name="Pixels">One palette index per pixel, row by row from the top.</param>
    /// <param name="FullFrame">Whether this frame carried the run-length image rather than a delta.</param>
    public sealed record FlcFrameImage(int Index, int Width, int Height, byte[] Pixels, bool FullFrame);

    /// <summary>
    /// Decodes every frame into palette indices, with the palette the container carries.
    /// </summary>
    /// <remarks>
    /// The donor's reader addresses its buffer from the bottom up; this emits rows from the top, as
    /// every other canvas in this repository does, because the flip is an artefact of the donor's
    /// drawing surface rather than part of the image.
    /// <para>
    /// Frames accumulate: the run-length frame paints the whole canvas and each delta updates it, so
    /// a delta cannot be decoded on its own and the returned frames are snapshots rather than
    /// patches. Packets address pixels in pairs, which is why a delta packet's count advances x by
    /// twice its value.
    /// </para>
    /// </remarks>
    /// <param name="bytes">The container's bytes.</param>
    /// <param name="source">Logical source identity, for diagnostics.</param>
    /// <param name="palette">The palette the container carries, when it carries one.</param>
    public static IReadOnlyList<FlcFrameImage> DecodeFrames(ReadOnlySpan<byte> bytes, string source, out Arena2Palette? palette)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!TryRead(bytes, source, out FlcContainer? container, out string reason))
        {
            throw new Arena2FormatException(source, 0, reason);
        }

        byte[] buffer = new byte[container!.Width * container.Height];
        List<FlcFrameImage> images = [];
        palette = null;
        foreach (FlcFrame frame in container.Frames)
        {
            bool full = false;
            foreach (FlcChunk chunk in frame.Chunks)
            {
                ReadOnlySpan<byte> payload = bytes[(chunk.Offset + 6)..(chunk.Offset + chunk.Size)];
                switch (chunk.Type)
                {
                    case Color256ChunkType:
                        palette = ReadPalette(payload, source, frame.Index, scale: 1);
                        break;
                    case Color64ChunkType:
                        palette = ReadPalette(payload, source, frame.Index, scale: 4);
                        break;
                    case ByteRunChunkType:
                        ReadByteRun(payload, buffer, container, source);
                        full = true;
                        break;
                    case DeltaFlcChunkType:
                        ReadDelta(payload, buffer, container, source);
                        break;
                    default:
                        // Every other chunk type either describes the frame rather than its pixels or
                        // is not one Daggerfall's portraits use; both are skipped by their own size,
                        // which the container walk already validated.
                        break;
                }
            }

            images.Add(new FlcFrameImage(frame.Index, container.Width, container.Height, [.. buffer], full));
        }

        return images;
    }

    /// <summary>Reads the palette a container carries, in the donor's packet form.</summary>
    private static Arena2Palette ReadPalette(ReadOnlySpan<byte> payload, string source, int frame, int scale)
    {
        CheckedLittleEndianReader reader = new(payload.ToArray(), source);
        int packets = reader.ReadUInt16();
        Rgb24[] colors = new Rgb24[256];
        int index = 0;
        for (int packet = 0; packet < packets; packet++)
        {
            reader.ReadBytes(reader.ReadByte() * 3);
            int count = reader.ReadByte();
            if (count == 0) count = 256;
            if (index + count > colors.Length)
            {
                throw new Arena2FormatException(source, 0,
                    $"the palette chunk of frame {frame} declares {index + count} colours, more than the {colors.Length} a palette holds");
            }

            for (int color = 0; color < count; color++)
            {
                byte red = reader.ReadByte();
                byte green = reader.ReadByte();
                byte blue = reader.ReadByte();
                colors[index++] = new Rgb24(Scaled(red), Scaled(green), Scaled(blue));
            }
        }

        return new Arena2Palette($"{source} frame {frame} COLOR_{(scale == 4 ? 64 : 256)} chunk", colors);

        byte Scaled(byte channel) => (byte)Math.Min(255, channel * scale);
    }

    /// <summary>Reads a run-length frame:each row is a run of replicated or copied pixels.</summary>
    private static void ReadByteRun(ReadOnlySpan<byte> payload, byte[] buffer, FlcContainer container, string source)
    {
        int position = 0;
        for (int y = 0; y < container.Height; y++)
        {
            position = Require(payload, position, 1, source, $"the packet count of row {y}");
            _ = payload[position++];
            int x = 0;
            int row = (container.Height - 1 - y) * container.Width;
            while (x < container.Width)
            {
                if (position >= payload.Length)
                {
                    throw new Arena2FormatException(source, 0,
                        $"a run-length frame wants another packet for row {y} but its chunk ends after {payload.Length} bytes");
                }

                position = Require(payload, position, 1, source, $"a packet header of row {y}");
                sbyte packet = (sbyte)payload[position++];
                int count = Math.Abs((int)packet);
                int painted = Math.Min(count, container.Width - x);
                if (packet < 0)
                {
                    // A negative packet copies one byte per pixel, and every byte of it belongs to the
                    // stream even when the row ends first: a packet that runs past the row is still
                    // consumed whole, or the next row starts in the middle of its pixels.
                    if (position + count > payload.Length)
                    {
                        throw new Arena2FormatException(source, 0,
                            $"a run-length packet at row {y} wants {count} bytes but its chunk has {payload.Length - position} left");
                    }

                    for (int pixel = 0; pixel < painted; pixel++)
                    {
                        buffer[row + x + pixel] = payload[position + pixel];
                    }

                    position += count;
                }
                else
                {
                    position = Require(payload, position, 1, source, $"the repeated pixel of row {y}");
                    byte value = payload[position++];
                    for (int pixel = 0; pixel < painted; pixel++)
                    {
                        buffer[row + x + pixel] = value;
                    }
                }

                x += count;
            }
        }
    }

    /// <summary>Reads a delta frame: line runs, then paired packets that update the last frame.</summary>
    private static void ReadDelta(ReadOnlySpan<byte> payload, byte[] buffer, FlcContainer container, string source)
    {
        int position = Require(payload, 0, 2, source, "a delta frame's line count");
        int lines = payload[position] | (payload[position + 1] << 8);
        position += 2;
        int y = 0;

        // The donor keeps one packet count across the frame's lines rather than resetting it per line,
        // and that is not a detail to improve on: a line ending in the last-pixel opcode reuses the
        // previous line's count, and treating it as zero walks the packet stream out of step.
        int packets = 0;
        for (int line = 0; line < lines; line++)
        {
            while (true)
            {
                position = Require(payload, position, 2, source, $"an opcode of delta line {line}");
                int opcode = payload[position] | (payload[position + 1] << 8);
                position += 2;
                if ((opcode & 0x8000) != 0)
                {
                    if ((opcode & 0x4000) != 0)
                    {
                        int row = y + Math.Abs((short)opcode);
                        if (row >= container.Height)
                        {
                            throw new Arena2FormatException(source, 0,
                                $"a delta line skip at line {line} reaches row {row}, past the frame's {container.Height} rows");
                        }

                        y = row;
                        continue;
                    }

                    break;
                }

                // The donor reads a further opcode for the reserved range rather than taking it as a
                // packet count: 0x4000 to 0x7FFF is neither a skip nor a count, and treating it as a
                // count walks the packet stream out of step.
                if ((opcode & 0x4000) == 0)
                {
                    packets = opcode;
                    break;
                }
            }

            if (y >= container.Height)
            {
                throw new Arena2FormatException(source, 0,
                    $"delta line {line} writes row {y}, past the frame's {container.Height} rows");
            }

            int x = 0;
            for (int packet = 0; packet < packets; packet++)
            {
                position = Require(payload, position, 2, source, $"a packet of delta line {line}");
                x += payload[position++];
                sbyte size = (sbyte)payload[position++];
                int count = Math.Abs((int)size);
                int bytes = size > 0 ? count * 2 : 2;
                position = Require(payload, position, bytes, source, $"the pixels of a delta packet at line {line}");
                int row = (container.Height - 1 - y) * container.Width;
                for (int index = 0; index < count * 2 && x + index < container.Width; index++)
                {
                    // A delta packet's sign is the opposite of a run-length frame's: positive copies one
                    // byte per pixel from the packet, negative repeats the two bytes its pair is made of.
                    buffer[row + x + index] = size > 0 ? payload[position + index] : payload[position + (index % 2)];
                }

                position += bytes;
                x += count * 2;
            }

            y++;
        }
    }

    /// <summary>
    /// Refuses a read that would leave the chunk, naming what was being read.
    /// </summary>
    /// <remarks>
    /// Every read of a decoded chunk goes through this rather than indexing the span directly, so a
    /// truncated frame is refused with the file and the row named instead of reaching the caller as an
    /// index error. The guards are before the read by construction: the position this returns is the
    /// only one a caller may use.
    /// </remarks>
    private static int Require(ReadOnlySpan<byte> payload, int position, int width, string source, string what)
    {
        if (width < 0 || position < 0 || position + width > payload.Length)
        {
            throw new Arena2FormatException(source, 0,
                $"a frame chunk needs {width} byte(s) for {what} at {position}, but the chunk has {payload.Length}");
        }

        return position;
    }

}
