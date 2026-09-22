using Daggerfall.Import.Arena2;
using System.Buffers.Binary;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class BssDecoderTests
{
    [Theory]
    [InlineData("CMPA00I0.BSS", 272, 157, 48, 40, 32)]
    [InlineData("CMPA01I0.BSS", 279, 163, 34, 28, 32)]
    [InlineData("CMPA02I0.BSS", 281, 165, 30, 25, 32)]
    public void Reads_donor_header_positions_dimensions_counts_and_complete_frames(
        string name, int x, int y, int width, int height, int count)
    {
        if (!File.Exists(Corpus(name))) return;
        byte[] bytes = File.ReadAllBytes(Corpus(name));

        Assert.True(BssDecoder.TryRead(bytes, name, out BssContainer? container, out string reason), reason);
        Assert.Equal((x, y, width, height, count),
            (container!.XOffset, container.YOffset, container.Width, container.Height, container.FrameCount));
        Assert.Equal(bytes.Length, BssDecoder.HeaderBytes + (width * height * count));

        IReadOnlyList<BssFrameImage> frames = BssDecoder.DecodeFrames(bytes, name);
        Assert.Equal(count, frames.Count);
        Assert.Equal(Enumerable.Range(0, count), frames.Select(frame => frame.Index));
        Assert.All(frames, frame => Assert.Equal((x, y, width, height, width * height),
            (frame.XOffset, frame.YOffset, frame.Width, frame.Height, frame.Pixels.Length)));
        Assert.Contains(frames.SelectMany(frame => frame.Pixels), pixel => pixel != 0);
    }

    [Fact]
    public void Refuses_magic_from_an_flc_and_a_header_count_that_does_not_fit()
    {
        byte[] cel = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(cel.AsSpan(4), 0xaf12);
        Assert.False(BssDecoder.TryRead(cel, "MAGE.CEL", out _, out string magic));
        Assert.Contains("MAGE.CEL", magic, StringComparison.Ordinal);
        Assert.Contains("FLC magic", magic, StringComparison.Ordinal);

        byte[] malformed = Fixture();
        BinaryPrimitives.WriteInt16LittleEndian(malformed.AsSpan(8), 3);
        Assert.False(BssDecoder.TryRead(malformed, "count-mismatch.BSS", out _, out string count));
        Assert.Contains("count-mismatch.BSS", count, StringComparison.Ordinal);
        Assert.Contains("3 frame", count, StringComparison.Ordinal);
    }

    [Fact]
    public void Decodes_distinct_frames_with_signed_offsets_through_the_canonical_canvas_reader()
    {
        byte[] bytes = Fixture();
        Arena2CanvasSet canvases = Arena2CanvasReader.Read(bytes, "fixture.BSS");
        Assert.Equal(Arena2CanvasKind.BssFrames, canvases.Kind);
        Assert.Equal([0, 1], canvases.Canvases.Select(canvas => canvas.Record));
        Assert.All(canvases.Canvases, canvas => Assert.Equal((-2, 3, 2, 1),
            (canvas.XOffset, canvas.YOffset, canvas.Width, canvas.Height)));
        IReadOnlyList<BssFrameImage> frames = BssDecoder.DecodeFrames(bytes, "fixture.BSS");
        Assert.Equal(new byte[] { 1, 2 }, frames[0].Pixels);
        Assert.Equal(new byte[] { 3, 4 }, frames[1].Pixels);
    }

    private static byte[] Fixture()
    {
        byte[] bytes = new byte[14];
        short[] header = [-2, 3, 2, 1, 2];
        for (int index = 0; index < header.Length; index++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2), header[index]);
        new byte[] { 1, 2, 3, 4 }.CopyTo(bytes, 10);
        return bytes;
    }

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
