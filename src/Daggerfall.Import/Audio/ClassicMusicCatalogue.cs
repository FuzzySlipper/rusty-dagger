namespace Daggerfall.Import.Audio;

/// <summary>One imported music cue: the media id the runtime names, and the donor file it comes from.</summary>
/// <param name="MediaId">The published media identity a music context resolves.</param>
/// <param name="SourceFile">The donor song file the cue is converted from, as the source folder names it.</param>
/// <param name="Context">The donor context the cue answers, named after that playlist.</param>
/// <param name="SourceSeconds">The source track's length in whole seconds, measured from the file.</param>
public sealed record ClassicMusicCue(string MediaId, string SourceFile, string Context, int SourceSeconds);

/// <summary>
/// The music cues the offline import publishes, one whole track per context rather than an arbitrary
/// excerpt, each taken from a playlist the donor's own song manager names.
/// </summary>
/// <remarks>
/// The Engine admits at most 64 clips and 32 MiB in total, which the classic score cannot fit as a
/// whole: at the imported rate a minute of mono audio costs about 2.6 MiB, so the twenty-minute score
/// would need roughly fifty. This catalogue is therefore a deliberate subset — the three contexts a
/// session is most often in — and the remaining donor playlists stay unimported until the budget, the
/// format, or the score's own length changes. <see cref="TotalBytes"/> and the admission limits are
/// checked by a fact, so adding a cue that does not fit fails rather than silently overruns.
/// </remarks>
public static class ClassicMusicCatalogue
{
    /// <summary>The Engine's admission limits for audio resources.</summary>
    public const int MaximumCueCount = 64;
    public const int MaximumTotalBytes = 32 * 1024 * 1024;

    /// <summary>
    /// The published cues. Each is the first track of the donor playlist it answers, so a context that
    /// plays at all plays something the original game played there: the dungeon cue is the donor's
    /// dungeon playlist opener, and the two sunny cues are that playlist's General MIDI and FM openers.
    /// </summary>
    public static IReadOnlyList<ClassicMusicCue> All { get; } =
    [
        new("music.dungeon", "song_dungeon.ogg", "dungeon", 224),
        new("music.sunny", "song_gday___d.ogg", "sunny", 127),
        new("music.sunny.fm", "song_fday___d.ogg", "sunny-fm", 125),
    ];

    /// <summary>What the cues cost the Engine's budget once converted, in bytes.</summary>
    public static int TotalBytes(IEnumerable<ClassicMusicCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        return cues.Sum(cue => checked(cue.SourceSeconds * ClassicMusicConverter.BytesPerSecond));
    }
}
