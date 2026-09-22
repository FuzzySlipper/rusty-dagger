using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;

namespace Daggerfall.Import.Publication;

public static partial class CinematicMediaPublisher
{
    private static VidDecodeSummary DecodeVidForConversion(byte[] bytes, string source, string directory)
    {
        Directory.CreateDirectory(directory);
        List<(string Name, long Start)> frames = [];
        using FileStream audio = File.Create(System.IO.Path.Combine(directory, "audio.pcm"));
        using MemoryStream input = new(bytes, writable: false);
        VidDecodeSummary summary = VidDecoder.Decode(input, source, frame =>
        {
            string name = $"frame-{frame.Index:D6}.png";
            using FileStream image = File.Create(System.IO.Path.Combine(directory, name));
            byte[] rgba = new byte[frame.Pixels.Length * 4];
            for (int index = 0; index < frame.Pixels.Length; index++)
            {
                rgba[index * 4] = frame.Pixels[index].Red;
                rgba[index * 4 + 1] = frame.Pixels[index].Green;
                rgba[index * 4 + 2] = frame.Pixels[index].Blue;
                rgba[index * 4 + 3] = byte.MaxValue;
            }
            image.Write(DeterministicPngEncoder.EncodeRgba8(frame.Width, frame.Height, rgba));
            frames.Add((name, frame.StartSampleOffset));
        }, block =>
        {
            PadSilence(audio, block.StartSampleOffset);
            audio.Write(block.Pcm);
            PadSilence(audio, block.EndSampleOffset);
        });
        PadSilence(audio, summary.TimelineSampleCount);
        using StreamWriter list = new(System.IO.Path.Combine(directory, "frames.ffconcat"), false, new UTF8Encoding(false));
        list.WriteLine("ffconcat version 1.0");
        for (int index = 0; index < frames.Count; index++)
        {
            long end = index + 1 < frames.Count ? frames[index + 1].Start : summary.TimelineSampleCount;
            double duration = (end - frames[index].Start) / (double)VidAudioFormat.SampleRate;
            list.WriteLine($"file '{frames[index].Name}'");
            list.WriteLine("option framerate 1000");
            list.WriteLine("duration " + duration.ToString("R", CultureInfo.InvariantCulture));
        }
        // A terminal duplicate establishes the last display duration; -t excludes that extra frame.
        list.WriteLine($"file '{frames[^1].Name}'");
        list.WriteLine("option framerate 1000");
        return summary;
    }

    private static void PadSilence(Stream output, long end)
    {
        if (output.Position > end) throw new InvalidDataException("VID audio blocks overlap their scheduled timeline.");
        Span<byte> silence = stackalloc byte[4096];
        silence.Fill(128);
        while (output.Position < end)
            output.Write(silence[..(int)Math.Min(silence.Length, end - output.Position)]);
    }
}
