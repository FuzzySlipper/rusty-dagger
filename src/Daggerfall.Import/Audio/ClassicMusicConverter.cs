using NVorbis;

namespace Daggerfall.Import.Audio;

/// <summary>
/// Converts the Daggerfall Unity score into the one container the Engine admits. The score ships as OGG
/// and the Engine takes RIFF/WAVE only, so the offline import decodes it here; the runtime never sees a
/// compressed container or a decoder.
/// </summary>
/// <remarks>
/// The Engine admits at most 64 clips and 32 MiB of audio in total, which the classic score as a whole
/// cannot fit: at this rate one minute of mono 16-bit PCM costs about 2.6 MiB, so the imported set is a
/// deliberate subset and the rest of each donor playlist stays unimported until the budget or the format
/// changes. Mono at this rate is what the budget buys; the score is not a spatial cue and loses only
/// stereo width.
/// </remarks>
public static class ClassicMusicConverter
{
    /// <summary>The rate imported cues carry, chosen against the Engine's total audio budget.</summary>
    public const int CueSampleRate = 22_050;

    /// <summary>Bytes one second of an imported cue occupies: one mono 16-bit sample per frame.</summary>
    public const int BytesPerSecond = CueSampleRate * 2;

    /// <summary>Decodes one OGG file into a mono 16-bit PCM WAV container, and reports its frame count.</summary>
    public static (byte[] Wave, int SampleCount) Convert(ReadOnlySpan<byte> ogg, int sampleRate = CueSampleRate)
    {
        if (ogg.IsEmpty) throw new ArgumentException("The cue has no bytes to convert.", nameof(ogg));
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8_000);
        (float[] mono, int sourceRate) = DecodeMono(ogg);
        float[] resampled = Resample(mono, sourceRate, sampleRate);
        return (WriteWave(resampled, sampleRate), resampled.Length);
    }

    /// <summary>Decodes every frame to one channel, mixing the source's channels evenly.</summary>
    private static (float[] Mono, int SourceRate) DecodeMono(ReadOnlySpan<byte> ogg)
    {
        using MemoryStream stream = new(ogg.ToArray(), writable: false);
        using VorbisReader reader = new(stream);
        if (reader.Channels <= 0) throw new InvalidOperationException("The cue declares no channels.");
        List<float> mono = [];
        float[] block = new float[reader.Channels * 4_096];
        int read;
        while ((read = reader.ReadSamples(block, 0, block.Length)) > 0)
        {
            int frames = read / reader.Channels;
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                for (int channel = 0; channel < reader.Channels; channel++) sum += block[(frame * reader.Channels) + channel];
                mono.Add(sum / reader.Channels);
            }
        }
        if (mono.Count == 0) throw new InvalidOperationException("The cue decoded to no frames.");
        return ([.. mono], reader.SampleRate);
    }

    /// <summary>Resamples by linear interpolation, which is enough for a score that is not a sampled effect.</summary>
    private static float[] Resample(float[] mono, int sourceRate, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceRate, 8_000);
        if (sourceRate == sampleRate) return mono;
        int frames = checked((int)((long)mono.Length * sampleRate / sourceRate));
        float[] resampled = new float[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            double position = (double)frame * sourceRate / sampleRate;
            int left = (int)position;
            int right = Math.Min(left + 1, mono.Length - 1);
            float fraction = (float)(position - left);
            resampled[frame] = (mono[left] * (1f - fraction)) + (mono[right] * fraction);
        }
        return resampled;
    }

    /// <summary>Writes a canonical PCM RIFF/WAVE container around 16-bit mono samples.</summary>
    private static byte[] WriteWave(float[] samples, int sampleRate)
    {
        int dataLength = checked(samples.Length * 2);
        byte[] wave = new byte[checked(dataLength + 44)];
        WriteAscii(wave, 0, "RIFF");
        WriteUInt32(wave, 4, (uint)checked(dataLength + 36));
        WriteAscii(wave, 8, "WAVEfmt ");
        WriteUInt32(wave, 16, 16);
        WriteUInt16(wave, 20, 1);
        WriteUInt16(wave, 22, 1);
        WriteUInt32(wave, 24, (uint)sampleRate);
        WriteUInt32(wave, 28, (uint)(sampleRate * 2));
        WriteUInt16(wave, 32, 2);
        WriteUInt16(wave, 34, 16);
        WriteAscii(wave, 36, "data");
        WriteUInt32(wave, 40, (uint)dataLength);
        for (int sample = 0; sample < samples.Length; sample++)
        {
            float value = Math.Clamp(samples[sample], -1f, 1f);
            short pcm = (short)Math.Round(value * short.MaxValue);
            wave[44 + (sample * 2)] = (byte)(pcm & 0xFF);
            wave[45 + (sample * 2)] = (byte)((pcm >> 8) & 0xFF);
        }
        return wave;
    }

    private static void WriteAscii(byte[] target, int offset, string value)
    {
        for (int index = 0; index < value.Length; index++) target[offset + index] = (byte)value[index];
    }

    private static void WriteUInt16(byte[] target, int offset, ushort value)
    {
        target[offset] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
        target[offset + 2] = (byte)((value >> 16) & 0xFF);
        target[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
