using System.Diagnostics;
using System.Reflection;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class CinematicVidConversionTests
{
    [Fact]
    public void Dag2_png_timeline_is_accepted_by_ffmpeg()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dagger-vid-" + Guid.NewGuid().ToString("N"));
        try
        {
            MethodInfo decode = typeof(CinematicMediaPublisher).GetMethod("DecodeVidForConversion", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("VID conversion decoder was not found.");
            string source = Path.Combine(RepositoryRoot(), "local", "arena2", "DAG2.VID");
            Assert.True(File.Exists(source), "The supplied VID corpus is required for this conversion test.");
            _ = (VidDecodeSummary?)decode.Invoke(null, [File.ReadAllBytes(source), "DAG2.VID", directory]);

            ProcessStartInfo start = new("ffmpeg") { UseShellExecute = false, RedirectStandardError = true };
            foreach (string argument in new[] { "-nostdin", "-v", "error", "-xerror", "-fflags", "+bitexact",
                "-threads:v", "1", "-f", "concat", "-safe", "0", "-i", Path.Combine(directory, "frames.ffconcat"),
                "-map", "0:v:0", "-frames:v", "1165", "-f", "null", "-" })
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start ffmpeg.");
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, stderr);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
