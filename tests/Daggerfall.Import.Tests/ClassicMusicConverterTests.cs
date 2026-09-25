using Daggerfall.Import.Audio;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class ClassicMusicConverterTests
{
    [Fact]
    public void A_cue_becomes_the_mono_sixteen_bit_container_the_engine_admits()
    {
        byte[] ogg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "tone.ogg"));

        (byte[] wave, int frames) = ClassicMusicConverter.Convert(ogg);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(16u, BitConverter.ToUInt32(wave, 16));      // PCM header size
        Assert.Equal((ushort)1, BitConverter.ToUInt16(wave, 20));       // PCM
        Assert.Equal((ushort)1, BitConverter.ToUInt16(wave, 22));       // mono
        Assert.Equal((uint)ClassicMusicConverter.CueSampleRate, BitConverter.ToUInt32(wave, 24));
        Assert.Equal((uint)ClassicMusicConverter.BytesPerSecond, BitConverter.ToUInt32(wave, 28));
        Assert.Equal((ushort)2, BitConverter.ToUInt16(wave, 32));       // block align
        Assert.Equal((ushort)16, BitConverter.ToUInt16(wave, 34));      // bits per sample
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wave, 36, 4));
        Assert.Equal(frames * 2, BitConverter.ToInt32(wave, 40));
        Assert.Equal(wave.Length - 44, BitConverter.ToInt32(wave, 40));
        Assert.Equal(44 + (frames * 2), wave.Length);
    }

    [Fact]
    public void A_half_second_cue_keeps_its_length_when_the_rate_changes()
    {
        // The fixture is half a second at 44.1 kHz; resampling to the cue rate must keep half a second
        // rather than keep the frame count.
        byte[] ogg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "tone.ogg"));

        (byte[] wave, int frames) = ClassicMusicConverter.Convert(ogg);

        Assert.InRange(frames, ClassicMusicConverter.CueSampleRate / 2 - 64, (ClassicMusicConverter.CueSampleRate / 2) + 64);
        Assert.InRange(wave.Length / (double)ClassicMusicConverter.BytesPerSecond, 0.45d, 0.55d);
    }

    [Fact]
    public void A_cue_that_already_carries_the_cue_rate_is_not_resampled()
    {
        byte[] ogg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "tone.ogg"));

        (_, int atCueRate) = ClassicMusicConverter.Convert(ogg, ClassicMusicConverter.CueSampleRate);
        (_, int atSameRate) = ClassicMusicConverter.Convert(ogg, ClassicMusicConverter.CueSampleRate);

        Assert.Equal(atCueRate, atSameRate);
    }

    [Fact]
    public void The_published_cue_set_stays_inside_the_engines_audio_budget()
    {
        // The Engine admits 64 clips and 32 MiB; the catalogue is deliberately a subset of the classic
        // score, so a cue added without regard to the budget fails here rather than at load.
        Assert.NotEmpty(ClassicMusicCatalogue.All);
        Assert.True(ClassicMusicCatalogue.All.Count <= ClassicMusicCatalogue.MaximumCueCount);
        int total = ClassicMusicCatalogue.TotalBytes(ClassicMusicCatalogue.All);
        Assert.True(total <= ClassicMusicCatalogue.MaximumTotalBytes,
            $"The declared cues need {total} bytes of the Engine's {ClassicMusicCatalogue.MaximumTotalBytes}.");
        Assert.Equal(ClassicMusicCatalogue.All.Count,
            ClassicMusicCatalogue.All.Select(cue => cue.MediaId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(ClassicMusicCatalogue.All, cue => Assert.EndsWith(".ogg", cue.SourceFile, StringComparison.Ordinal));
        // Every cue names the context the donor playlist it comes from answers.
        Assert.All(ClassicMusicCatalogue.All, cue => Assert.False(string.IsNullOrWhiteSpace(cue.Context)));
    }

    [Fact]
    public void An_empty_cue_is_refused_rather_than_written_as_silence()
    {
        Assert.Throws<ArgumentException>(() => ClassicMusicConverter.Convert([]));
    }
}
