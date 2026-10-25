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

    /// <summary>
    /// Every classic track the ruleset's playlists name, in ordinal order: the standard and alternate (FM)
    /// renders of the dungeon, weather, night, tavern, shop, guild, temple, knight, interior and palace playlists
    /// the donor's song manager plays.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "song_02", "song_02fm", "song_03", "song_03fm", "song_04", "song_04fm", "song_05", "song_05fm", "song_06",
        "song_06fm", "song_07", "song_07fm", "song_08", "song_08fm", "song_10", "song_11", "song_11fm", "song_12",
        "song_12fm", "song_13", "song_13fm", "song_15", "song_15fm", "song_17", "song_17fm", "song_18", "song_18fm",
        "song_20", "song_20fm", "song_21", "song_21fm", "song_22", "song_22fm", "song_23", "song_23fm", "song_28",
        "song_29", "song_29fm", "song_dungeon", "song_dungeon6", "song_dungeon7", "song_dungeon8", "song_dungeon9",
        "song_fbad", "song_fcurse", "song_fday___d", "song_fdngn10", "song_fdngn11", "song_fdungn4", "song_fdungn9",
        "song_feerie", "song_fgood", "song_fm_dngn1", "song_fm_dngn2", "song_fm_dngn3", "song_fm_dngn4",
        "song_fm_dngn5", "song_fm_nite3", "song_fm_rain", "song_fm_sqr_2", "song_fm_sunny", "song_fm_swim2",
        "song_fmover_c", "song_fmover_s", "song_fneut", "song_folk1", "song_folk2", "song_folk3", "song_fpalac",
        "song_fruins", "song_fsnow__b", "song_gbad", "song_gcurse", "song_gday___d", "song_gdngn10", "song_gdngn11",
        "song_gdungn4", "song_gdungn9", "song_geerie", "song_ggood", "song_gmage_3", "song_gneut", "song_gpalac",
        "song_gruins", "song_gshop", "song_gsnow__b", "song_gsunny2", "song_magic_2", "song_overcast", "song_overlong",
        "song_oversnow", "song_raining", "song_snowing", "song_square_2", "song_sunnyday", "song_swimming",
        "song_tavern"
    ];

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
