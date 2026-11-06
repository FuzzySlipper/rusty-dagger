using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class CfaDecoderTests
{
    private static byte[] File(short width, short height, byte frames, params byte[] data)
    {
        byte[] header = new byte[14];
        BitConverter.TryWriteBytes(header.AsSpan(0), width);
        BitConverter.TryWriteBytes(header.AsSpan(2), height);
        header[10] = 8;
        header[11] = frames;
        BitConverter.TryWriteBytes(header.AsSpan(12), (short)14);
        return [.. header, .. data];
    }

    [Fact]
    public void Runs_and_literals_unpack_into_frames()
    {
        // 130 repeats the next byte three times; 0 copies one byte.
        CfaFrames frames = CfaDecoder.Decode(File(2, 1, 2, 130, 7, 0, 9), "test.cfa");

        Assert.Equal(2, frames.FrameCount);
        Assert.Equal(new byte[] { 7, 7 }, frames.Frame(0).ToArray());
        Assert.Equal(new byte[] { 7, 9 }, frames.Frame(1).ToArray());
    }

    [Fact]
    public void Data_ending_early_is_refused_rather_than_read_as_black()
    {
        Assert.Throws<Arena2FormatException>(() => CfaDecoder.Decode(File(2, 2, 1, 128, 7), "test.cfa"));
    }

    [Fact]
    public void A_run_past_its_frames_is_refused()
    {
        Assert.Throws<Arena2FormatException>(() => CfaDecoder.Decode(File(1, 1, 1, 200, 7), "test.cfa"));
    }

    [Theory]
    [InlineData("MOON00I0.CFA", 36, 28)]
    [InlineData("MOON01I0.CFA", 58, 46)]
    public void Each_moon_has_a_frame_for_every_day_of_the_cycle(string file, int width, int height)
    {
        if (!System.IO.File.Exists(TestData.Corpus(file))) return;
        CfaFrames frames = CfaDecoder.Decode(System.IO.File.ReadAllBytes(TestData.Corpus(file)), file);

        Assert.Equal((width, height, 32), (frames.Width, frames.Height, frames.FrameCount));
    }
}
