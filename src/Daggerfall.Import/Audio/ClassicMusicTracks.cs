namespace Daggerfall.Import.Audio;

/// <summary>One recording a music folder supplies for a track: the folder's file and the name it is packed as.</summary>
/// <param name="Track">The classic track identifier, such as <c>song_dungeon</c>.</param>
/// <param name="SourceFile">The file's name in the supplied folder.</param>
/// <param name="PackedName">The name the music container carries it as: the track and the file's own extension, in lower case.</param>
public sealed record ClassicMusicRecording(string Track, string SourceFile, string PackedName);

/// <summary>What a music folder supplies, measured against the track list.</summary>
/// <param name="Recordings">One recording per supplied track, ordered by track.</param>
/// <param name="Missing">Tracks the folder carries no recording of, ordered.</param>
/// <param name="Unknown">Files the scheme does not name: no known track, or a format the Engine's audio does not admit.</param>
public sealed record ClassicMusicFolder(
    IReadOnlyList<ClassicMusicRecording> Recordings,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Unknown);

/// <summary>
/// The music container's naming scheme: one audio file per classic track at the container's root, named
/// <c>&lt;track&gt;.&lt;ext&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The track is the identifier the classic song manager names (<c>song_dungeon</c>, <c>song_04</c>,
/// <c>song_fm_dngn1</c>, ...), and the extension is any format the Engine's audio admits. So the container can
/// be packed from any source of recordings: the Daggerfall Unity song pack, the player's own renders of the
/// classic MIDI, or anything else named this way. Nothing pins the bytes of a particular recording.
/// </para>
/// <para>
/// A file is matched to its track without regard to case, because a Windows folder does not keep it, and is
/// packed under the lower-case name. Two recordings of one track are refused by name: packing either would
/// silently drop the other.
/// </para>
/// </remarks>
public static class ClassicMusicTracks
{
    /// <summary>The audio formats the Engine opens, by file extension.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".ogg", ".opus", ".mp3", ".flac", ".wav"];

    /// <summary>Every classic track the ruleset's playlists name, ordered.</summary>
    public static IReadOnlyList<string> All { get; } =
        [.. ClassicMusicCatalogue.All.Select(cue => cue.Track).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Matches a folder's file names to the tracks they record.</summary>
    /// <exception cref="InvalidOperationException">The folder carries two recordings of one track.</exception>
    public static ClassicMusicFolder Match(IEnumerable<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        Dictionary<string, string> known = All.ToDictionary(track => track, track => track, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ClassicMusicRecording> recordings = new(StringComparer.Ordinal);
        List<string> unknown = [];
        foreach (string fileName in fileNames.Order(StringComparer.Ordinal))
        {
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (!Extensions.Contains(extension, StringComparer.Ordinal) || !known.TryGetValue(Path.GetFileNameWithoutExtension(fileName), out string? track))
            {
                unknown.Add(fileName);
                continue;
            }

            if (recordings.TryGetValue(track, out ClassicMusicRecording? earlier))
            {
                throw new InvalidOperationException($"The music folder carries two recordings of track '{track}': '{earlier.SourceFile}' and '{fileName}'. Keep one.");
            }

            recordings.Add(track, new ClassicMusicRecording(track, fileName, track + extension));
        }

        return new ClassicMusicFolder(
            [.. recordings.Values.OrderBy(recording => recording.Track, StringComparer.Ordinal)],
            [.. All.Where(track => !recordings.ContainsKey(track))],
            unknown);
    }
}
