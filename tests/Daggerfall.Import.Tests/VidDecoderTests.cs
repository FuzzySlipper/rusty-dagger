using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class VidDecoderTests
{
    [Fact]
    public void Streams_palette_resolved_full_delta_and_row_offset_frames_on_the_donor_timeline()
    {
        List<VidFrame> frames = [];
        List<VidAudioBlock> audio = [];
        using MemoryStream input = new(Fixture());

        VidDecodeSummary summary = VidDecoder.Decode(input, "fixture.VID", frames.Add, audio.Add);

        Assert.Equal(new VidHeader(512, 3, 3, 2, 4, 14), summary.Header);
        Assert.Equal(3, summary.FrameCount);
        Assert.Equal(2, summary.AudioBlockCount);
        Assert.Equal(3, summary.SourceAudioSampleCount);
        Assert.Equal(1480, summary.ScheduledAudioSampleCount);
        Assert.Equal(2220, summary.TimelineSampleCount);
        Assert.Equal(2220d / VidAudioFormat.SampleRate, summary.TimelineDurationSeconds, 12);

        Assert.Collection(audio,
            block =>
            {
                Assert.Equal([0x10], block.Pcm);
                Assert.Equal(0, block.StartSampleOffset);
                Assert.Equal(740, block.ScheduledSampleCount);
                Assert.Equal(740, block.EndSampleOffset);
            },
            block =>
            {
                Assert.Equal([0x20, 0x21], block.Pcm);
                Assert.Equal(1480, block.StartSampleOffset);
                Assert.Equal(740, block.ScheduledSampleCount);
                Assert.Equal(2220, block.EndSampleOffset);
            });

        Assert.Collection(frames,
            frame =>
            {
                Assert.Equal((0L, 740, 740L), (frame.StartSampleOffset, frame.DurationSamples, frame.EndSampleOffset));
                Assert.Equal(740d / VidAudioFormat.SampleRate, frame.DurationSeconds, 12);
                Assert.Equal(Rgb(1, 2, 3, 4, 5, 6), frame.Pixels);
            },
            frame =>
            {
                // This has no preceding audio block. DaggerfallVideo advances the old cadence once,
                // so its interval starts where the first frame ends rather than doubling it.
                Assert.Equal((740L, 740, 1480L), (frame.StartSampleOffset, frame.DurationSamples, frame.EndSampleOffset));
                Assert.Equal(Rgb(1, 7, 8, 4, 5, 6), frame.Pixels);
            },
            frame =>
            {
                Assert.Equal((1480L, 740, 2220L), (frame.StartSampleOffset, frame.DurationSamples, frame.EndSampleOffset));
                Assert.Equal(Rgb(1, 7, 8, 4, 9, 10), frame.Pixels);
            });
    }

    [Fact]
    public void Refuses_truncated_overflowing_mismatched_and_trailing_streams()
    {
        byte[] valid = Fixture();
        Arena2FormatException truncated = Assert.Throws<Arena2FormatException>(() =>
            VidDecoder.Decode(new MemoryStream(valid[..(VidDecoder.HeaderBytes + 3)]), "truncated.VID", _ => { }));
        Assert.Contains("initial VID palette", truncated.Message, StringComparison.Ordinal);

        Arena2FormatException overflow = Assert.Throws<Arena2FormatException>(() =>
            VidDecoder.Decode(new MemoryStream(OverflowFixture()), "overflow.VID", _ => { }));
        Assert.Contains("beyond its 1-pixel canvas", overflow.Message, StringComparison.Ordinal);

        Arena2FormatException mismatch = Assert.Throws<Arena2FormatException>(() =>
            VidDecoder.Decode(new MemoryStream(Fixture(frameCount: 4)), "mismatch.VID", _ => { }));
        Assert.Contains("declares 4 video frame", mismatch.Message, StringComparison.Ordinal);

        byte[] trailing = [.. valid, 0];
        Arena2FormatException extra = Assert.Throws<Arena2FormatException>(() =>
            VidDecoder.Decode(new MemoryStream(trailing), "trailing.VID", _ => { }));
        Assert.Contains("after its end block", extra.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decodes_the_supplied_vid_corpus_to_each_declared_frame_and_exact_end_when_available()
    {
        string directory = Path.Combine(RepositoryRoot(), "local", "arena2");
        if (!Directory.Exists(directory)) return;

        string[] names = [.. Directory.EnumerateFiles(directory, "*.VID").Select(path => Path.GetFileName(path)!).OrderBy(name => name, StringComparer.Ordinal)];
        Assert.Equal(17, names.Length);
        foreach (string name in names)
        {
            int frames = 0;
            using FileStream input = File.OpenRead(Path.Combine(directory, name));
            VidDecodeSummary summary = VidDecoder.Decode(input, name, _ => frames++);
            Assert.Equal(summary.Header.FrameCount, frames);
            Assert.Equal(summary.Header.FrameCount, summary.FrameCount);
            Assert.True(summary.TimelineSampleCount > 0);
        }
    }

    private static Rgb24[] Rgb(params byte[] indices) => [.. indices.Select(index => new Rgb24((byte)(index * 4), (byte)(index * 4), (byte)(index * 4)))];

    private static byte[] Fixture(int frameCount = 3)
    {
        List<byte> bytes = Header(frameCount, 3, 2);
        Palette(bytes);
        AudioStart(bytes, [0x10]);
        bytes.Add(3);
        U16(bytes, 11);
        bytes.Add(6);
        bytes.AddRange([1, 2, 3, 4, 5, 6]);

        // The delta leaves its last row untouched, so its zero is a delta terminator rather than a
        // stream null block; the next audio block must begin immediately after that terminator.
        bytes.Add(1);
        U16(bytes, 12);
        bytes.Add(0x81);
        bytes.Add(2);
        bytes.AddRange([7, 8]);
        bytes.Add(0);

        AudioIncremental(bytes, [0x20, 0x21]);
        bytes.Add(4);
        U16(bytes, 13);
        U16(bytes, 1);
        bytes.Add(0x81);
        bytes.Add(2);
        bytes.AddRange([9, 10]);
        bytes.Add(0);
        bytes.Add(20);
        return [.. bytes];
    }

    private static byte[] OverflowFixture()
    {
        List<byte> bytes = Header(1, 1, 1);
        Palette(bytes);
        AudioStart(bytes, [0x10]);
        bytes.Add(3);
        U16(bytes, 0);
        bytes.Add(0x82);
        bytes.Add(1);
        bytes.Add(20);
        return [.. bytes];
    }

    private static List<byte> Header(int frames, int width, int height)
    {
        List<byte> bytes = [(byte)'V', (byte)'I', (byte)'D'];
        U16(bytes, 512);
        U16(bytes, (ushort)frames);
        U16(bytes, (ushort)width);
        U16(bytes, (ushort)height);
        U16(bytes, 4);
        U16(bytes, 14);
        return bytes;
    }

    private static void Palette(List<byte> bytes)
    {
        bytes.Add(2);
        for (int index = 0; index < 256; index++) bytes.AddRange([(byte)index, (byte)index, (byte)index]);
    }

    private static void AudioStart(List<byte> bytes, byte[] pcm)
    {
        bytes.Add(124);
        U16(bytes, 0);
        bytes.Add(166);
        U16(bytes, (ushort)pcm.Length);
        bytes.AddRange(pcm);
    }

    private static void AudioIncremental(List<byte> bytes, byte[] pcm)
    {
        bytes.Add(125);
        U16(bytes, (ushort)pcm.Length);
        bytes.AddRange(pcm);
    }

    private static void U16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
