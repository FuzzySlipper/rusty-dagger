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
}
