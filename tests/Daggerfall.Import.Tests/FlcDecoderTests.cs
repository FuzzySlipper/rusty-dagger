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
        }
    }

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
