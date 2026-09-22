using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Buffers.Binary;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>Converted media only; source identities and narrative bindings remain in the cinematic catalog.</summary>
public sealed record CinematicMediaArtifact(string Path, string MimeType, long ByteLength, string Sha256,
    int Width, int Height, long FrameCount, double DurationSeconds, bool HasAudio);

/// <summary>Offline VID decoding and FFmpeg Flic conversion into packaged WebM. No source decoder ships in the product.</summary>
public static partial class CinematicMediaPublisher
{
    public const string VideoMimeType = "video/webm";
    public const string VideoCodec = "vp9";
    public const string AudioCodec = "opus";

    public static string ArtifactName(string sourceFile)
    {
        if (sourceFile != System.IO.Path.GetFileName(sourceFile) || sourceFile.Contains('\\') ||
            !(sourceFile.EndsWith(".VID", StringComparison.OrdinalIgnoreCase) || sourceFile.EndsWith(".FLC", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Unsupported cinematic source identity '{sourceFile}'; expected one VID or FLC file name.");
        return System.IO.Path.GetFileNameWithoutExtension(sourceFile).ToLowerInvariant() + ".webm";
    }

    public static CinematicMediaArtifact Publish(string sourcePath, DaggerfallCinematicRecord source,
        string outputDirectory, string logicalRoot, string ffmpeg = "ffmpeg", string ffprobe = "ffprobe")
    {
        source.Validate();
        string name = ArtifactName(source.FileName);
        NormalizedImportDocument.RequireLogicalPath(logicalRoot, nameof(logicalRoot));
        byte[] bytes = File.ReadAllBytes(sourcePath);
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        if (bytes.LongLength != source.ByteLength || !string.Equals(digest, source.Digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Cinematic '{source.FileName}' no longer matches its source publication; regenerate source provenance first.");
        bool signature = source.Kind == DaggerfallCinematicKind.Vid
            ? bytes.Length >= 15 && bytes[0] == 'V' && bytes[1] == 'I' && bytes[2] == 'D'
            : bytes.Length >= 128 && bytes[4] == 0x12 && bytes[5] == 0xAF;
        if (!signature) throw new InvalidDataException($"Cinematic '{source.FileName}' is not a supported {source.Kind} container.");
        Directory.CreateDirectory(outputDirectory);
        string destination = System.IO.Path.Combine(outputDirectory, name);
        string workDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rusty-dagger-cinematic-" + Guid.NewGuid().ToString("N"));
        string temporary = System.IO.Path.Combine(workDirectory, name);
        string decodeDirectory = System.IO.Path.Combine(workDirectory, "decoded");
        try
        {
            Directory.CreateDirectory(workDirectory);
            List<string> inputs;
            VidDecodeSummary? vid = null;
            int flcFrames = 0;
            double flcDuration = 0;
            if (source.Kind == DaggerfallCinematicKind.Vid)
            {
                vid = DecodeVidForConversion(bytes, source.FileName, decodeDirectory);
                inputs = ["-f", "concat", "-safe", "0", "-i", System.IO.Path.Combine(decodeDirectory, "frames.ffconcat"),
                    "-f", "u8", "-ar", VidAudioFormat.SampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "1", "-i", System.IO.Path.Combine(decodeDirectory, "audio.pcm"),
                    "-map", "0:v:0", "-map", "1:a:0", "-vf", "trim=end_frame=" + vid.FrameCount.ToString(CultureInfo.InvariantCulture),
                    "-t", vid.TimelineDurationSeconds.ToString("R", CultureInfo.InvariantCulture)];
            }
            else
            {
                flcFrames = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));
                uint delayMilliseconds = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4));
                if (flcFrames == 0 || delayMilliseconds == 0)
                    throw new InvalidDataException($"Cinematic '{source.FileName}' has no positive FLC frame count or delay.");
                flcDuration = flcFrames * (double)delayMilliseconds / 1000d;
                // The final FLC ring frame reconstructs frame zero for looping; it is not
                // another display interval. Publish one cycle with the declared frame count.
                inputs = ["-i", sourcePath, "-map", "0:v:0", "-map", "0:a?",
                    "-vf", "trim=end_frame=" + flcFrames.ToString(CultureInfo.InvariantCulture),
                    "-t", flcDuration.ToString("R", CultureInfo.InvariantCulture)];
            }
            // Decode proprietary source offline, then let FFmpeg encode the accepted VP9/Opus container.
            // Bitexact muxing and one encoder thread make regeneration deterministic.
            Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-y", "-fflags", "+bitexact", .. inputs,
                "-map_metadata", "-1", "-c:v", "libvpx-vp9", "-lossless", "1",
                "-pix_fmt", "yuv420p", "-threads", "1", "-row-mt", "0", "-fps_mode", "passthrough",
                "-c:a", "libopus", "-b:a", "64k", "-flags:v", "+bitexact", "-flags:a", "+bitexact",
                "-fflags", "+bitexact", "-f", "webm", temporary], source.FileName);
            using JsonDocument probe = JsonDocument.Parse(Run(ffprobe,
                ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,codec_name,width,height,nb_read_frames:format=duration", "-of", "json", temporary], source.FileName));
            JsonElement[] streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            JsonElement video = streams.Single(value => value.GetProperty("codec_type").GetString() == "video");
            bool audio = streams.Any(value => value.GetProperty("codec_type").GetString() == "audio");
            if (video.GetProperty("codec_name").GetString() != VideoCodec ||
                streams.Any(value => value.GetProperty("codec_type").GetString() == "audio" && value.GetProperty("codec_name").GetString() != AudioCodec))
                throw new InvalidDataException($"Cinematic '{source.FileName}' conversion produced unsupported codecs.");
            int width = video.GetProperty("width").GetInt32(), height = video.GetProperty("height").GetInt32();
            long frames = long.Parse(video.GetProperty("nb_read_frames").GetString()!, CultureInfo.InvariantCulture);
            double duration = double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            if (width <= 0 || height <= 0 || frames <= 0 || !double.IsFinite(duration) || duration <= 0)
                throw new InvalidDataException($"Cinematic '{source.FileName}' conversion produced an empty or invalid stream.");
            if (vid is not null && (frames != vid.FrameCount || Math.Abs(duration - vid.TimelineDurationSeconds) > 0.1))
                throw new InvalidDataException($"Cinematic '{source.FileName}' conversion lost source frames or timing: {frames}/{vid.FrameCount} frames, {duration}/{vid.TimelineDurationSeconds} seconds.");
            if (vid is null && (frames != flcFrames || Math.Abs(duration - flcDuration) > 0.1))
                throw new InvalidDataException($"Cinematic '{source.FileName}' conversion lost declared FLC frames or timing: {frames}/{flcFrames} frames, {duration}/{flcDuration} seconds.");
            byte[] artifact = File.ReadAllBytes(temporary);
            File.Move(temporary, destination, overwrite: true);
            return new(logicalRoot.TrimEnd('/') + "/" + name, VideoMimeType, artifact.LongLength,
                Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant(), width, height, frames, duration, audio);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, recursive: true);
        }
    }

    private static string Run(string executable, IReadOnlyList<string> arguments, string source)
    {
        ProcessStartInfo start = new(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{executable}' for '{source}'.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0) throw new InvalidDataException($"Cinematic '{source}' conversion by '{executable}' failed ({process.ExitCode}): {stderr.Result.Trim()}");
        return stdout.Result;
    }
}
