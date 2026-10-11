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
/// <remarks>Published tracks are opened lazily. Only the playing music clip is retained, alongside effects.</remarks>
public static class ClassicMusicCatalogue
{
    /// <summary>Retained donor context playlists, published from operator-supplied Ogg inputs.</summary>
    public static IReadOnlyList<ClassicMusicCue> All { get; } =
    [
        new("music.dungeon", "song_dungeon.ogg", "dungeon", 5_985_696),
        new("music.dungeon6", "song_dungeon6.ogg", "dungeon", 5_089_660),
        new("music.dungeon7", "song_dungeon7.ogg", "dungeon", 6_217_184),
        new("music.dungeon8", "song_dungeon8.ogg", "dungeon", 5_925_798),
        new("music.dungeon9", "song_dungeon9.ogg", "dungeon", 2_842_103),
        new("music.gdngn10", "song_gdngn10.ogg", "dungeon", 3_461_177),
        new("music.gdngn11", "song_gdngn11.ogg", "dungeon", 3_299_468),
        new("music.gdungn4", "song_gdungn4.ogg", "dungeon", 4_443_221),
        new("music.gdungn9", "song_gdungn9.ogg", "dungeon", 3_495_153),
        new("music.04", "song_04.ogg", "dungeon", 1_842_585),
        new("music.05", "song_05.ogg", "dungeon", 3_925_985),
        new("music.07", "song_07.ogg", "dungeon", 1_493_299),
        new("music.15", "song_15.ogg", "dungeon", 2_920_553),
        new("music.28", "song_28.ogg", "dungeon", 3_945_980),
        new("music.fm_dngn1", "song_fm_dngn1.ogg", "dungeonfm", 2_736_753),
        new("music.fm_dngn2", "song_fm_dngn2.ogg", "dungeonfm", 5_089_660),
        new("music.fm_dngn3", "song_fm_dngn3.ogg", "dungeonfm", 6_217_184),
        new("music.fm_dngn4", "song_fm_dngn4.ogg", "dungeonfm", 5_925_798),
        new("music.fm_dngn5", "song_fm_dngn5.ogg", "dungeonfm", 2_808_999),
        new("music.fdngn10", "song_fdngn10.ogg", "dungeonfm", 3_461_177),
        new("music.fdngn11", "song_fdngn11.ogg", "dungeonfm", 3_299_468),
        new("music.fdungn4", "song_fdungn4.ogg", "dungeonfm", 4_443_221),
        new("music.fdungn9", "song_fdungn9.ogg", "dungeonfm", 3_495_153),
        new("music.04fm", "song_04fm.ogg", "dungeonfm", 1_842_585),
        new("music.05fm", "song_05fm.ogg", "dungeonfm", 3_925_985),
        new("music.07fm", "song_07fm.ogg", "dungeonfm", 1_493_299),
        new("music.15fm", "song_15fm.ogg", "dungeonfm", 2_920_553),
        new("music.sunny", "song_gday___d.ogg", "sunny", 3_472_545),
        new("music.swimming", "song_swimming.ogg", "sunny", 3_413_639),
        new("music.sunny.third", "song_gsunny2.ogg", "sunny", 3_904_543),
        new("music.sunny.fourth", "song_sunnyday.ogg", "sunny", 2_248_822),
        new("music.sunny.second", "song_02.ogg", "sunny", 3_768_712),
        new("music.03", "song_03.ogg", "sunny", 4_124_505),
        new("music.22", "song_22.ogg", "sunny", 4_898_026),
        new("music.sunny.fm", "song_fday___d.ogg", "sunny-fm", 1_819_234),
        new("music.fm_swim2", "song_fm_swim2.ogg", "sunnyfm", 5_400_338),
        new("music.fm_sunny", "song_fm_sunny.ogg", "sunnyfm", 5_034_656),
        new("music.sunny.fm.second", "song_02fm.ogg", "sunny-fm", 3_768_712),
        new("music.03fm", "song_03fm.ogg", "sunnyfm", 4_124_505),
        new("music.22fm", "song_22fm.ogg", "sunnyfm", 4_898_026),
        new("music.10", "song_10.ogg", "night", 2_242_694),
        new("music.11", "song_11.ogg", "night", 2_985_603),
        new("music.gcurse", "song_gcurse.ogg", "night", 4_358_113),
        new("music.geerie", "song_geerie.ogg", "night", 4_629_868),
        new("music.gruins", "song_gruins.ogg", "night", 4_790_894),
        new("music.18", "song_18.ogg", "night", 4_585_855),
        new("music.21", "song_21.ogg", "night", 2_132_956),
        new("music.11fm", "song_11fm.ogg", "nightfm", 2_985_603),
        new("music.fcurse", "song_fcurse.ogg", "nightfm", 4_358_113),
        new("music.feerie", "song_feerie.ogg", "nightfm", 4_629_868),
        new("music.fruins", "song_fruins.ogg", "nightfm", 4_790_894),
        new("music.18fm", "song_18fm.ogg", "nightfm", 4_585_855),
        new("music.21fm", "song_21fm.ogg", "nightfm", 2_132_956),
        new("music.square_2", "song_square_2.ogg", "tavern", 2_131_217),
        new("music.tavern", "song_tavern.ogg", "tavern", 1_821_306),
        new("music.folk1", "song_folk1.ogg", "tavern", 2_592_125),
        new("music.folk2", "song_folk2.ogg", "tavern", 2_263_225),
        new("music.folk3", "song_folk3.ogg", "tavern", 2_645_941),
        new("music.fm_sqr_2", "song_fm_sqr_2.ogg", "tavernfm", 2_131_217),
        new("music.gshop", "song_gshop.ogg", "shop", 4_071_501),
        new("music.gmage_3", "song_gmage_3.ogg", "magesguild", 2_963_149),
        new("music.magic_2", "song_magic_2.ogg", "magesguild", 4_303_887),
        new("music.fm_nite3", "song_fm_nite3.ogg", "magesguildfm", 6_049_006),
        new("music.ggood", "song_ggood.ogg", "temple", 2_454_565),
        new("music.fgood", "song_fgood.ogg", "templefm", 1_220_724),
        new("music.gneut", "song_gneut.ogg", "templeneutral", 2_191_700),
        new("music.fneut", "song_fneut.ogg", "templeneutralfm", 1_426_948),
        new("music.gbad", "song_gbad.ogg", "templebad", 2_164_511),
        new("music.fbad", "song_fbad.ogg", "templebadfm", 1_245_740),
        new("music.17", "song_17.ogg", "knight", 3_120_902),
        new("music.17fm", "song_17fm.ogg", "knightfm", 3_120_902),
        new("music.29", "song_29.ogg", "cloudy", 3_014_533),
        new("music.12", "song_12.ogg", "cloudy", 2_931_361),
        new("music.29fm", "song_29fm.ogg", "cloudyfm", 3_014_533),
        new("music.12fm", "song_12fm.ogg", "cloudyfm", 2_931_361),
        new("music.13", "song_13.ogg", "overcast", 3_429_181),
        new("music.gpalac", "song_gpalac.ogg", "overcast", 3_661_565),
        new("music.overcast", "song_overcast.ogg", "overcast", 2_211_155),
        new("music.13fm", "song_13fm.ogg", "overcastfm", 3_429_181),
        new("music.fpalac", "song_fpalac.ogg", "overcastfm", 3_661_565),
        new("music.fmover_c", "song_fmover_c.ogg", "overcastfm", 2_211_155),
        new("music.overlong", "song_overlong.ogg", "rain", 3_458_522),
        new("music.raining", "song_raining.ogg", "rain", 3_975_993),
        new("music.08", "song_08.ogg", "rain", 2_282_543),
        new("music.fm_rain", "song_fm_rain.ogg", "rainfm", 3_975_993),
        new("music.08fm", "song_08fm.ogg", "rainfm", 2_282_543),
        new("music.20", "song_20.ogg", "snow", 4_377_746),
        new("music.gsnow__b", "song_gsnow__b.ogg", "snow", 4_221_696),
        new("music.oversnow", "song_oversnow.ogg", "snow", 2_243_621),
        new("music.snowing", "song_snowing.ogg", "snow", 2_225_951),
        new("music.20fm", "song_20fm.ogg", "snowfm", 4_377_746),
        new("music.fsnow__b", "song_fsnow__b.ogg", "snowfm", 2_996_030),
        new("music.fmover_s", "song_fmover_s.ogg", "snowfm", 2_341_465),
        new("music.23", "song_23.ogg", "interior", 1_773_793),
        new("music.23fm", "song_23fm.ogg", "interiorfm", 1_773_793),
        new("music.06", "song_06.ogg", "palace", 4_106_318),
        new("music.06fm", "song_06fm.ogg", "palacefm", 4_106_318),
    ];

    /// <summary>Total published storage bytes; only one cue is admitted at a time.</summary>
    public static int TotalBytes(IEnumerable<ClassicMusicCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        return cues.Sum(cue => cue.SourceBytes);
    }
}
