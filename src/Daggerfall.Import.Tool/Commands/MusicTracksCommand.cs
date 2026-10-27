using Daggerfall.Import.Audio;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Stages the music container's files from a folder of recordings: each file named by the scheme
/// (<c>&lt;track&gt;.&lt;ext&gt;</c>, see <see cref="ClassicMusicTracks"/>) is copied under its scheme name, and
/// the tracks the folder lacks and the files the scheme does not name are each reported in one line.
/// </summary>
/// <remarks>
/// A missing track is not an error: its contexts play their other tracks. A folder with no recording the scheme
/// names is refused, because packing it would give an empty score that looks like a supplied one.
/// </remarks>
internal static class MusicTracksCommand
{
    public static ToolCommand Command { get; } = new("music-tracks",
        [CommandOption.Required("--music", "SOURCE_DIR"), CommandOption.Staged("--out", "TRACKS_DIR", ImportLayout.MusicTracks)], Run);

    private static int Run(CommandArguments args)
    {
        string folder = args["--music"];
        string output = args["--out"];
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"The music folder '{folder}' does not exist.");
        ClassicMusicFolder matched = ClassicMusicTracks.Match(Directory.EnumerateFiles(folder).Select(file => Path.GetFileName(file)));
        if (matched.Recordings.Count == 0)
        {
            throw new InvalidOperationException(
                $"'{folder}' holds no recording named by the music scheme (<track>.<ext>, such as song_dungeon.ogg); leave out --music to import without a score.");
        }

        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);
        foreach (ClassicMusicRecording recording in matched.Recordings)
            File.Copy(Path.Combine(folder, recording.SourceFile), Path.Combine(output, recording.PackedName));
        Console.WriteLine($"music tracks: {matched.Recordings.Count} of {ClassicMusicTracks.All.Count} tracks staged from '{folder}' into '{output}'");
        if (matched.Missing.Count != 0)
            Console.WriteLine($"music tracks: no recording of [{string.Join(", ", matched.Missing)}]; their contexts play their other tracks");
        if (matched.Unknown.Count != 0)
        {
            Console.WriteLine($"music tracks: {matched.Unknown.Count} other files in the folder name no classic track in a format the Engine plays ({string.Join(", ", ClassicMusicTracks.Extensions)}), so they are left out: [{string.Join(", ", matched.Unknown)}]");
        }

        return 0;
    }
}
