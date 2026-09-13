using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>The FLC container the three class portraits are stored in.</summary>
public sealed class FlcDecoderTests
{
    [Fact]
    public void Reads_each_class_portrait_to_the_shape_and_frames_its_own_header_declares()
    {
        // The three supplied class portraits are genuine FLC containers, and each one's header is the
        // only thing that says what shape it is: nothing here is inferred from the file name.
        (string Name, int Frames, int Width, int Height)[] portraits =
        [
            ("MAGE.CEL", 15, 110, 119),
            ("ROGUE.CEL", 10, 175, 119),
            ("WARRIOR.CEL", 15, 183, 119),
        ];
        foreach ((string name, int frames, int width, int height) in portraits)
        {
            byte[] bytes = File.ReadAllBytes(Corpus(name));
            Assert.True(FlcDecoder.TryRead(bytes, name, out FlcContainer? container, out string reason), $"{name}: {reason}");
            Assert.Equal(frames, container!.FrameCount);
            Assert.Equal((width, height), (container.Width, container.Height));
            Assert.Equal(8, container.PixelDepth);
            Assert.Equal(bytes.Length, container.FileSize);
            Assert.Equal(frames, container.Frames.Count);
            Assert.Equal(Enumerable.Range(0, frames), container.Frames.Select(frame => frame.Index));
            Assert.All(container.Frames, frame => Assert.Equal(FlcDecoder.FrameChunkType, frame.ChunkType));

            // Each container accounts for the bytes between its header and its first frame: the
            // supplied portraits carry a 72-byte prefix chunk there, and the reader checks the
            // declared offset against it rather than trusting the number.
            Assert.Equal(72, container.PrefixBytes);
            Assert.Equal(FlcDecoder.HeaderBytes + container.PrefixBytes, container.Frames[0].Offset);

            // The frames then tile the file without overlapping or leaving a gap a later frame's own
            // size would have to explain.
            for (int index = 1; index < container.Frames.Count; index++)
            {
                FlcFrame previous = container.Frames[index - 1];
                Assert.Equal(previous.Offset + previous.Size, container.Frames[index].Offset);
            }

            Assert.True(container.Frames[^1].Offset + container.Frames[^1].Size <= bytes.Length);

            // What the frames are made of, measured from the files: one thumbnail the donor skips, one
            // 256-colour palette, one full frame, and a delta for every remaining frame. That is what
            // a decoder has to read, and it is why the last frame's chunk types are not all alike.
            ushort[] chunkTypes = [.. container.Frames.SelectMany(frame => frame.Chunks).Select(chunk => chunk.Type)];
            Assert.Equal(1, chunkTypes.Count(type => type == PstampChunkType));
            Assert.Equal(1, chunkTypes.Count(type => type == Color256ChunkType));
            Assert.Equal(1, chunkTypes.Count(type => type == ByteRunChunkType));
            Assert.Equal(frames - 1, chunkTypes.Count(type => type == DeltaFlcChunkType));

            // The palette chunk measures 778 bytes: its six-byte chunk header, a four-byte prefix and
            // the 768 bytes a 256-colour palette takes. Which four bytes those are, and how the donor
            // reads around them, is for the decoding increment to read out of ReadPalette rather than
            // guess - the size is measured here and asserted only as measured.
            FlcChunk palette = container.Frames.SelectMany(frame => frame.Chunks).Single(chunk => chunk.Type == Color256ChunkType);
            Assert.Equal(778, palette.Size);
            Assert.Equal(772, palette.Size - 6);
            foreach (FlcFrame frame in container.Frames)
            {
                Assert.Equal(frame.ChunkCount, frame.Chunks.Count);
                Assert.Equal(frame.Offset + FlcDecoder.FrameHeaderBytes, frame.Chunks[0].Offset);
                for (int index = 1; index < frame.Chunks.Count; index++)
                {
                    Assert.Equal(frame.Chunks[index - 1].Offset + frame.Chunks[index - 1].Size, frame.Chunks[index].Offset);
                }

                Assert.Equal(frame.Offset + frame.Size, frame.Chunks[^1].Offset + frame.Chunks[^1].Size);
            }
        }
    }

    [Fact]
    public void Decodes_every_frame_of_each_portrait_into_palette_indices()
    {
        foreach (string name in new[] { "MAGE.CEL", "ROGUE.CEL", "WARRIOR.CEL" })
        {
            byte[] bytes = File.ReadAllBytes(Corpus(name));
            IReadOnlyList<FlcDecoder.FlcFrameImage> frames = FlcDecoder.DecodeFrames(bytes, name, out Arena2Palette? palette);

            Assert.True(FlcDecoder.TryRead(bytes, name, out FlcContainer? container, out _));
            Assert.Equal(container!.FrameCount, frames.Count);
            Assert.All(frames, frame => Assert.Equal(container.Width * container.Height, frame.Pixels.Length));

            // The container's own palette chunk is what the frames index into, at scale one: the
            // measured first colour of MAGE.CEL is (0, 0, 208), which is the palette's first triple.
            Assert.NotNull(palette);
            Assert.Equal(256, palette!.Colors.Length);
            if (name == "MAGE.CEL")
            {
                // Measured from the file: its palette chunk opens with one packet, a zero skip and a
                // count byte of zero meaning all 256 colours, so the first triple is black and the
                // second is (208, 208, 208).
                Assert.Equal(new Rgb24(0, 0, 0), palette.Colors.Span[0]);
                Assert.Equal(new Rgb24(208, 208, 208), palette.Colors.Span[1]);
            }
            else
            {
                Assert.Contains(palette.Colors.ToArray(), color => color.Red != 0 || color.Green != 0 || color.Blue != 0);
            }

            // The first frame is the run-length image and every later frame is a delta against it, so
            // only the first is a full frame and no frame is left blank.
            Assert.True(frames[0].FullFrame);
            Assert.All(frames.Skip(1), frame => Assert.False(frame.FullFrame));
            Assert.All(frames, frame => Assert.Contains(frame.Pixels, pixel => pixel != 0));

            // Decoding is deterministic: the same bytes give the same indices.
            IReadOnlyList<FlcDecoder.FlcFrameImage> again = FlcDecoder.DecodeFrames(bytes, name, out _);
            Assert.Equal(frames.Count, again.Count);
            for (int index = 0; index < frames.Count; index++)
            {
                Assert.Equal(frames[index].Pixels, again[index].Pixels);
            }

            // A delta changes the canvas it follows rather than replacing it, which is why a frame is a
            // snapshot: the frames differ from one another.
            Assert.NotEqual(frames[0].Pixels, frames[^1].Pixels);
        }
    }

    /// <summary>The chunk types the donor's reader switches on, which these files all use.</summary>
    private const ushort PstampChunkType = FlcDecoder.PstampChunkType;

    private const ushort Color256ChunkType = FlcDecoder.Color256ChunkType;

    private const ushort ByteRunChunkType = FlcDecoder.ByteRunChunkType;

    private const ushort DeltaFlcChunkType = FlcDecoder.DeltaFlcChunkType;

    [Fact]
    public void Refuses_bytes_that_only_look_like_a_container()
    {
        // A story sprite is not an FLC, and the reader says so by name rather than half-reading it.
        byte[] bss = File.ReadAllBytes(Corpus("CMPA00I0.BSS"));
        Assert.False(FlcDecoder.TryRead(bss, "CMPA00I0.BSS", out FlcContainer? none, out string wrongMagic));
        Assert.Null(none);
        Assert.Contains("declares file id", wrongMagic, StringComparison.Ordinal);

        // A real container cut short is refused where the frames stop fitting, not silently truncated
        // to the frames that happen to fit.
        byte[] mage = File.ReadAllBytes(Corpus("MAGE.CEL"));
        Assert.False(FlcDecoder.TryRead(mage[..60], "MAGE.CEL", out _, out string tooShort));
        Assert.Contains("128-byte header", tooShort, StringComparison.Ordinal);
        Assert.False(FlcDecoder.TryRead(mage[..(FlcDecoder.HeaderBytes + 20)], "MAGE.CEL", out _, out string clipped));
        Assert.Contains("declares its first frame at", clipped, StringComparison.Ordinal);

        // A container whose declared first frame offset is not backed by a prefix chunk is refused:
        // the gap would otherwise be a fact nobody accounted for.
        byte[] shifted = [.. mage];
        shifted[80] = 130;
        shifted[81] = 0;
        shifted[82] = 0;
        shifted[83] = 0;
        Assert.False(FlcDecoder.TryRead(shifted, "MAGE.CEL", out _, out string unbacked));
        Assert.Contains("no 2-byte prefix chunk", unbacked, StringComparison.Ordinal);
    }

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
