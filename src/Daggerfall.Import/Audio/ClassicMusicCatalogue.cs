using System.IO;

namespace Daggerfall.Import.Audio;

/// <summary>One imported music cue: the media id the runtime names, and the donor file it comes from.</summary>
/// <param name="MediaId">The published media identity a music context resolves.</param>
/// <param name="SourceFile">The donor song file the cue carries, as the source folder names it.</param>
/// <param name="Context">The donor context the cue answers, named after that playlist.</param>
/// <param name="SourceBytes">The source file's length in bytes, measured from the file.</param>
public sealed record ClassicMusicCue(string MediaId, string SourceFile, string Context, int SourceBytes)
{
    /// <summary>
    /// The donor song identity the cue carries: the source file's name without its container.
    /// </summary>
    /// <remarks>
    /// The runtime names a track rather than a file — <c>song_dungeon</c>, not <c>song_dungeon.ogg</c> —
    /// because the donor's own song manager names tracks, and the container is a property of how this
    /// publication carries them. Deriving it here keeps one statement of which donor track a cue is.
    /// </remarks>
    public string Track => Path.GetFileNameWithoutExtension(SourceFile);
}

/// <summary>
/// The music cues the offline import publishes, taken from the playlists the donor's own song manager
/// names and carried in the container the Engine admits without decoding.
/// </summary>
/// <remarks>
/// The Engine admits compressed containers now, so a cue is the donor's own file rather than a
/// downsampled conversion of it, and the budget counts the bytes that are actually admitted. That buys
/// this set roughly three times the music the earlier conversion could fit: seven whole tracks cost
/// 23.8 MiB, where three cost 20 MiB as PCM. The set still leaves headroom, because the music shares the
/// Engine's 32 MiB with the imported effect clips — <see cref="EffectHeadroomBytes"/> is the part reserved
/// for them, and a fact fails if a new cue eats into it. The remaining donor playlists stay unimported
/// until a later change widens the budget or shortens what needs to fit.
/// </remarks>
public static class ClassicMusicCatalogue
{
    /// <summary>The Engine's admission limits for audio resources.</summary>
    public const int MaximumCueCount = 64;
    public const int MaximumTotalBytes = 32 * 1024 * 1024;

    /// <summary>The part of the budget the imported effect clips need, which music must not consume.</summary>
    public const int EffectHeadroomBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The published cues: the donor's dungeon playlist opener and six tracks from the sunny playlists in
    /// both of their versions, so a context that plays at all plays something the original game played
    /// there. Each entry's length is the source file's own, measured when the file was read.
    /// </summary>
    public static IReadOnlyList<ClassicMusicCue> All { get; } =
    [
        new("music.dungeon", "song_dungeon.ogg", "dungeon", 5_985_696),
        new("music.sunny", "song_gday___d.ogg", "sunny", 3_472_545),
        new("music.sunny.second", "song_02.ogg", "sunny", 3_768_712),
        new("music.sunny.third", "song_gsunny2.ogg", "sunny", 3_904_543),
        new("music.sunny.fourth", "song_sunnyday.ogg", "sunny", 2_248_822),
        new("music.sunny.fm", "song_fday___d.ogg", "sunny-fm", 1_819_234),
        new("music.sunny.fm.second", "song_02fm.ogg", "sunny-fm", 3_768_712),
    ];

    /// <summary>What the cues cost the Engine's budget, in the bytes it admits.</summary>
    public static int TotalBytes(IEnumerable<ClassicMusicCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        return cues.Sum(cue => cue.SourceBytes);
    }
}
