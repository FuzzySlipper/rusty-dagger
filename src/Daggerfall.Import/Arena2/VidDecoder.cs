namespace Daggerfall.Import.Arena2;

/// <summary>The fixed unsigned-PCM format Daggerfall VID audio blocks use.</summary>
public static class VidAudioFormat
{
    /// <summary>The source playback rate used by the original VID reader.</summary>
    public const int SampleRate = 11025;

    /// <summary>The shortest audio cadence the original VID player schedules.</summary>
    public const int MinimumScheduledSamples = 740;
}

/// <summary>The fixed header at the beginning of a Daggerfall VID stream.</summary>
public sealed record VidHeader(ushort Unknown1, int FrameCount, int Width, int Height, ushort GlobalDelay, ushort Unknown2);

/// <summary>A fully resolved RGB frame, emitted in source order without retaining prior frames.</summary>
/// <param name="Index">Zero-based frame ordinal.</param>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="StartSampleOffset">The first sample on the donor player's 11,025 Hz presentation timeline.</param>
/// <param name="DurationSamples">The donor player's scheduled duration, in <see cref="VidAudioFormat.SampleRate"/> samples.</param>
/// <param name="EndSampleOffset">The exclusive end sample on the donor player's presentation timeline.</param>
/// <param name="DurationSeconds">The donor player's scheduled duration in seconds.</param>
/// <param name="Pixels">Opaque RGB source pixels, in top-down row-major order.</param>
public sealed record VidFrame(int Index, int Width, int Height, long StartSampleOffset, int DurationSamples, long EndSampleOffset,
    double DurationSeconds, Rgb24[] Pixels);

/// <summary>An unsigned 8-bit mono PCM block decoded from a VID stream.</summary>
/// <param name="Index">Zero-based audio-block ordinal.</param>
/// <param name="Pcm">The source PCM bytes, without synthetic padding.</param>
/// <param name="StartSampleOffset">The first sample on the donor player's 11,025 Hz presentation timeline.</param>
/// <param name="ScheduledSampleCount">The block's donor-player scheduling length, clamped to 740 samples.</param>
/// <param name="EndSampleOffset">The exclusive end sample on the donor player's presentation timeline.</param>
/// <param name="DurationSeconds">The block's donor-player scheduling duration in seconds.</param>
public sealed record VidAudioBlock(int Index, byte[] Pcm, long StartSampleOffset, int ScheduledSampleCount, long EndSampleOffset,
    double DurationSeconds);

/// <summary>The completed stream's source and scheduled-playback facts.</summary>
public sealed record VidDecodeSummary(VidHeader Header, int FrameCount, int AudioBlockCount, long SourceAudioSampleCount,
    long ScheduledAudioSampleCount, long TimelineSampleCount, double TimelineDurationSeconds);

/// <summary>
/// Streams Daggerfall's interleaved VID blocks into RGB snapshots and unsigned 8-bit PCM.
/// </summary>
/// <remarks>
/// This is an offline source decoder, not a playback runtime. It adopts the active timing rule in
/// Daggerfall Unity's <c>VidFile.ReadBlock</c>: every video frame uses the most recent audio block's
/// <c>max(length, 740) / 11025</c> cadence. The header/global and per-video delay fields are retained
/// as source facts, but are deliberately not used: they belong to the donor's commented-out alternate
/// formula and produce the incompatible timing FFmpeg's BethsoftVID demuxer reports for these files.
/// <para>
/// Callbacks run synchronously and receive a fresh frame or source-audio buffer. The decoder keeps only
/// the current indexed canvas, so callers can publish a frame before the next one is read. When muxing,
/// callers can use <see cref="VidAudioBlock.ScheduledSampleCount"/> to append unsigned-PCM silence for
/// a short final audio block, and to fill video-only frame intervals.
/// </para>
/// </remarks>
public static class VidDecoder
{
    /// <summary>The three-byte VID signature.</summary>
    public const string Signature = "VID";

    /// <summary>The bytes in the fixed VID header.</summary>
    public const int HeaderBytes = 15;

    /// <summary>The bytes in each six-bit RGB palette block.</summary>
    public const int PaletteBytes = 768;

    /// <summary>
    /// Decodes a complete VID stream and sends each resolved frame and audio block to its callback.
    /// </summary>
    /// <exception cref="Arena2FormatException">The source is truncated, malformed, unsupported, or has inconsistent frame/end markers.</exception>
    public static VidDecodeSummary Decode(Stream input, string source, Action<VidFrame> onFrame, Action<VidAudioBlock>? onAudio = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(onFrame);
        if (!input.CanRead) throw new ArgumentException("VID input stream must be readable.", nameof(input));

        Reader reader = new(input, source);
        VidHeader header = ReadHeader(reader);
        Rgb24[] palette = ReadInitialPalette(reader);
        int pixels = PixelCount(header, reader);
        byte[] canvas = new byte[pixels];
        int frames = 0;
        int audioBlocks = 0;
        long sourceAudioSamples = 0;
        long scheduledAudioSamples = 0;
        long timelineSamples = 0;
        int currentDurationSamples = 0;
        bool lastBlockWasAudio = false;

        while (true)
        {
            int blockOffset = reader.Offset;
            byte type = reader.ReadByte("a VID block type");
            switch (type)
            {
                case 0:
                    break;
                case 1:
                    ReadVideoDelay(reader);
                    ReadDelta(reader, canvas, 0, source, blockOffset);
                    EmitFrame(blockOffset);
                    break;
                case 2:
                    // VidFile deliberately skips palette updates. The first palette is the colour map
                    // every frame uses, and preserving that donor behavior avoids a second visual rule.
                    _ = reader.ReadBytes(PaletteBytes, "a VID palette block");
                    break;
                case 3:
                    ReadVideoDelay(reader);
                    ReadFullFrame(reader, canvas, source, blockOffset);
                    EmitFrame(blockOffset);
                    break;
                case 4:
                    ReadVideoDelay(reader);
                    int row = reader.ReadUInt16("a VID row-offset frame row");
                    long rowStart = (long)row * header.Width;
                    if (rowStart > canvas.Length)
                    {
                        throw reader.ErrorAt(blockOffset, $"row-offset frame starts at pixel {rowStart}, beyond its {canvas.Length}-pixel canvas");
                    }

                    ReadDelta(reader, canvas, (int)rowStart, source, blockOffset);
                    EmitFrame(blockOffset);
                    break;
                case 20:
                    if (frames != header.FrameCount)
                    {
                        throw reader.ErrorAt(blockOffset, $"declares {header.FrameCount} video frame(s) but contains {frames} before its end block");
                    }

                    reader.RequireEnd();
                    return new(header, frames, audioBlocks, sourceAudioSamples, scheduledAudioSamples, timelineSamples,
                        DurationSeconds(timelineSamples));
                case 124:
                    _ = reader.ReadUInt16("an audio-start unknown field");
                    byte rate = reader.ReadByte("an audio-start playback rate");
                    if (rate != 166)
                    {
                        throw reader.ErrorAt(blockOffset, $"audio-start playback rate {rate} is unsupported; Daggerfall VID requires 166 for {VidAudioFormat.SampleRate} Hz PCM");
                    }

                    EmitAudio(reader.ReadBytes(reader.ReadUInt16("an audio-start byte length"), "an audio-start PCM payload"));
                    break;
                case 125:
                    EmitAudio(reader.ReadBytes(reader.ReadUInt16("an incremental-audio byte length"), "an incremental-audio PCM payload"));
                    break;
                default:
                    throw reader.ErrorAt(blockOffset, $"unsupported VID block type {type}");
            }
        }

        void EmitAudio(byte[] pcm)
        {
            int scheduled = Math.Max(pcm.Length, VidAudioFormat.MinimumScheduledSamples);
            double duration = DurationSeconds(scheduled);
            long start = timelineSamples;
            long end = checked(start + scheduled);
            onAudio?.Invoke(new VidAudioBlock(audioBlocks++, pcm, start, scheduled, end, duration));
            sourceAudioSamples += pcm.Length;
            scheduledAudioSamples += scheduled;
            currentDurationSamples = scheduled;
            timelineSamples = end;
            lastBlockWasAudio = true;
        }

        void EmitFrame(int blockOffset)
        {
            if (currentDurationSamples == 0)
            {
                throw reader.ErrorAt(blockOffset, "video frame appears before an audio block establishes the donor playback cadence");
            }

            if (frames >= header.FrameCount)
            {
                throw reader.ErrorAt(blockOffset, $"declares {header.FrameCount} video frame(s) but contains another frame at ordinal {frames}");
            }

            long start = lastBlockWasAudio ? timelineSamples - currentDurationSamples : timelineSamples;
            long end = checked(start + currentDurationSamples);
            if (!lastBlockWasAudio) timelineSamples = end;
            Rgb24[] rgb = new Rgb24[canvas.Length];
            for (int index = 0; index < canvas.Length; index++) rgb[index] = palette[canvas[index]];
            double duration = DurationSeconds(currentDurationSamples);
            onFrame(new VidFrame(frames++, header.Width, header.Height, start, currentDurationSamples, end, duration, rgb));
            lastBlockWasAudio = false;
        }
    }

    private static VidHeader ReadHeader(Reader reader)
    {
        byte first = reader.ReadByte("the VID signature");
        byte second = reader.ReadByte("the VID signature");
        byte third = reader.ReadByte("the VID signature");
        if (first != 'V' || second != 'I' || third != 'D')
        {
            throw reader.ErrorAt(0, "does not begin with the VID signature");
        }

        ushort unknown1 = reader.ReadUInt16("VID header field one");
        int frames = reader.ReadUInt16("VID frame count");
        int width = reader.ReadUInt16("VID frame width");
        int height = reader.ReadUInt16("VID frame height");
        ushort delay = reader.ReadUInt16("VID global delay");
        ushort unknown2 = reader.ReadUInt16("VID header field two");
        if (frames <= 0 || width <= 0 || height <= 0)
        {
            throw reader.ErrorAt(0, $"declares {frames} frame(s) of {width}x{height}, which cannot form a VID canvas");
        }

        return new(unknown1, frames, width, height, delay, unknown2);
    }

    private static Rgb24[] ReadInitialPalette(Reader reader)
    {
        int offset = reader.Offset;
        if (reader.ReadByte("the initial VID palette marker") != 2)
        {
            throw reader.ErrorAt(offset, "does not carry a palette block immediately after its header");
        }

        byte[] bytes = reader.ReadBytes(PaletteBytes, "the initial VID palette");
        Rgb24[] palette = new Rgb24[256];
        for (int index = 0; index < palette.Length; index++)
        {
            int source = index * 3;
            // VID stores six-bit channels. The donor's byte cast is intentionally retained, including
            // its wrap behavior for malformed out-of-range channel values.
            palette[index] = new((byte)(bytes[source] * 4), (byte)(bytes[source + 1] * 4), (byte)(bytes[source + 2] * 4));
        }

        return palette;
    }

    private static int PixelCount(VidHeader header, Reader reader)
    {
        try
        {
            return checked(header.Width * header.Height);
        }
        catch (OverflowException)
        {
            throw reader.ErrorAt(0, $"canvas {header.Width}x{header.Height} overflows the decoder's pixel count");
        }
    }

    private static void ReadVideoDelay(Reader reader) => _ = reader.ReadUInt16("a video-frame delay");

    private static void ReadFullFrame(Reader reader, byte[] canvas, string source, int blockOffset)
    {
        int position = 0;
        while (position < canvas.Length)
        {
            byte code = reader.ReadByte("a full-frame run-length code");
            if (code == 0) return;
            if (code >= 128)
            {
                int count = code - 128;
                byte pixel = reader.ReadByte("a full-frame repeated palette index");
                RequireWrite(reader, blockOffset, position, count, canvas.Length, "full-frame repeated run");
                canvas.AsSpan(position, count).Fill(pixel);
                position += count;
            }
            else
            {
                int count = code;
                RequireWrite(reader, blockOffset, position, count, canvas.Length, "full-frame literal run");
                reader.ReadBytes(count, "a full-frame literal run").CopyTo(canvas.AsSpan(position));
                position += count;
            }
        }
    }

    private static void ReadDelta(Reader reader, byte[] canvas, int initialPosition, string source, int blockOffset)
    {
        int position = initialPosition;
        while (position < canvas.Length)
        {
            byte code = reader.ReadByte("a delta-frame run-length code");
            if (code == 0) return;
            if (code >= 128)
            {
                int count = code - 128;
                RequireWrite(reader, blockOffset, position, count, canvas.Length, "delta-frame skip");
                position += count;
            }
            else
            {
                int count = code;
                RequireWrite(reader, blockOffset, position, count, canvas.Length, "delta-frame literal run");
                reader.ReadBytes(count, "a delta-frame literal run").CopyTo(canvas.AsSpan(position));
                position += count;
            }
        }
    }

    private static void RequireWrite(Reader reader, int blockOffset, int position, int count, int length, string what)
    {
        if (count > length - position)
        {
            throw reader.ErrorAt(blockOffset, $"{what} writes pixels {position} through {position + count - 1}, beyond its {length}-pixel canvas");
        }
    }

    private static double DurationSeconds(long samples) => (double)samples / VidAudioFormat.SampleRate;

    private sealed class Reader(Stream input, string source)
    {
        private readonly Stream input = input;
        private readonly string source = source;

        public int Offset { get; private set; }

        public byte ReadByte(string what)
        {
            int value = input.ReadByte();
            if (value < 0) throw Error($"requires one byte for {what}, but the stream ends");
            Offset++;
            return (byte)value;
        }

        public ushort ReadUInt16(string what)
        {
            byte low = ReadByte(what);
            byte high = ReadByte(what);
            return (ushort)(low | high << 8);
        }

        public byte[] ReadBytes(int count, string what)
        {
            byte[] bytes = new byte[count];
            int position = 0;
            while (position < bytes.Length)
            {
                int read = input.Read(bytes, position, bytes.Length - position);
                if (read == 0) throw Error($"requires {count} byte(s) for {what}, but only {position} remain");
                position += read;
                Offset += read;
            }

            return bytes;
        }

        public void RequireEnd()
        {
            if (input.ReadByte() >= 0) throw Error("contains bytes after its end block");
        }

        public Arena2FormatException Error(string message) => new(source, Offset, message);

        public Arena2FormatException ErrorAt(int offset, string message) => new(source, offset, message);
    }
}
