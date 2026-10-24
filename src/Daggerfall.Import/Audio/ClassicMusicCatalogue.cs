using System.IO;

namespace Daggerfall.Import.Audio;

/// <summary>One imported music cue: the media id the runtime names, and the donor file it comes from.</summary>
/// <param name="MediaId">The published media identity a music context resolves.</param>
/// <param name="SourceFile">The donor song file the cue carries, as the source folder names it.</param>
/// <param name="Context">The donor context the cue answers, named after that playlist.</param>
public sealed record ClassicMusicCue(string MediaId, string SourceFile, string Context)
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
        new("music.dungeon", "song_dungeon.ogg", "dungeon"),
        new("music.dungeon6", "song_dungeon6.ogg", "dungeon"),
        new("music.dungeon7", "song_dungeon7.ogg", "dungeon"),
        new("music.dungeon8", "song_dungeon8.ogg", "dungeon"),
        new("music.dungeon9", "song_dungeon9.ogg", "dungeon"),
        new("music.gdngn10", "song_gdngn10.ogg", "dungeon"),
        new("music.gdngn11", "song_gdngn11.ogg", "dungeon"),
        new("music.gdungn4", "song_gdungn4.ogg", "dungeon"),
        new("music.gdungn9", "song_gdungn9.ogg", "dungeon"),
        new("music.04", "song_04.ogg", "dungeon"),
        new("music.05", "song_05.ogg", "dungeon"),
        new("music.07", "song_07.ogg", "dungeon"),
        new("music.15", "song_15.ogg", "dungeon"),
        new("music.28", "song_28.ogg", "dungeon"),
        new("music.fm_dngn1", "song_fm_dngn1.ogg", "dungeonfm"),
        new("music.fm_dngn2", "song_fm_dngn2.ogg", "dungeonfm"),
        new("music.fm_dngn3", "song_fm_dngn3.ogg", "dungeonfm"),
        new("music.fm_dngn4", "song_fm_dngn4.ogg", "dungeonfm"),
        new("music.fm_dngn5", "song_fm_dngn5.ogg", "dungeonfm"),
        new("music.fdngn10", "song_fdngn10.ogg", "dungeonfm"),
        new("music.fdngn11", "song_fdngn11.ogg", "dungeonfm"),
        new("music.fdungn4", "song_fdungn4.ogg", "dungeonfm"),
        new("music.fdungn9", "song_fdungn9.ogg", "dungeonfm"),
        new("music.04fm", "song_04fm.ogg", "dungeonfm"),
        new("music.05fm", "song_05fm.ogg", "dungeonfm"),
        new("music.07fm", "song_07fm.ogg", "dungeonfm"),
        new("music.15fm", "song_15fm.ogg", "dungeonfm"),
        new("music.sunny", "song_gday___d.ogg", "sunny"),
        new("music.swimming", "song_swimming.ogg", "sunny"),
        new("music.sunny.third", "song_gsunny2.ogg", "sunny"),
        new("music.sunny.fourth", "song_sunnyday.ogg", "sunny"),
        new("music.sunny.second", "song_02.ogg", "sunny"),
        new("music.03", "song_03.ogg", "sunny"),
        new("music.22", "song_22.ogg", "sunny"),
        new("music.sunny.fm", "song_fday___d.ogg", "sunny-fm"),
        new("music.fm_swim2", "song_fm_swim2.ogg", "sunnyfm"),
        new("music.fm_sunny", "song_fm_sunny.ogg", "sunnyfm"),
        new("music.sunny.fm.second", "song_02fm.ogg", "sunny-fm"),
        new("music.03fm", "song_03fm.ogg", "sunnyfm"),
        new("music.22fm", "song_22fm.ogg", "sunnyfm"),
        new("music.10", "song_10.ogg", "night"),
        new("music.11", "song_11.ogg", "night"),
        new("music.gcurse", "song_gcurse.ogg", "night"),
        new("music.geerie", "song_geerie.ogg", "night"),
        new("music.gruins", "song_gruins.ogg", "night"),
        new("music.18", "song_18.ogg", "night"),
        new("music.21", "song_21.ogg", "night"),
        new("music.11fm", "song_11fm.ogg", "nightfm"),
        new("music.fcurse", "song_fcurse.ogg", "nightfm"),
        new("music.feerie", "song_feerie.ogg", "nightfm"),
        new("music.fruins", "song_fruins.ogg", "nightfm"),
        new("music.18fm", "song_18fm.ogg", "nightfm"),
        new("music.21fm", "song_21fm.ogg", "nightfm"),
        new("music.square_2", "song_square_2.ogg", "tavern"),
        new("music.tavern", "song_tavern.ogg", "tavern"),
        new("music.folk1", "song_folk1.ogg", "tavern"),
        new("music.folk2", "song_folk2.ogg", "tavern"),
        new("music.folk3", "song_folk3.ogg", "tavern"),
        new("music.fm_sqr_2", "song_fm_sqr_2.ogg", "tavernfm"),
        new("music.gshop", "song_gshop.ogg", "shop"),
        new("music.gmage_3", "song_gmage_3.ogg", "magesguild"),
        new("music.magic_2", "song_magic_2.ogg", "magesguild"),
        new("music.fm_nite3", "song_fm_nite3.ogg", "magesguildfm"),
        new("music.ggood", "song_ggood.ogg", "temple"),
        new("music.fgood", "song_fgood.ogg", "templefm"),
        new("music.gneut", "song_gneut.ogg", "templeneutral"),
        new("music.fneut", "song_fneut.ogg", "templeneutralfm"),
        new("music.gbad", "song_gbad.ogg", "templebad"),
        new("music.fbad", "song_fbad.ogg", "templebadfm"),
        new("music.17", "song_17.ogg", "knight"),
        new("music.17fm", "song_17fm.ogg", "knightfm"),
        new("music.29", "song_29.ogg", "cloudy"),
        new("music.12", "song_12.ogg", "cloudy"),
        new("music.29fm", "song_29fm.ogg", "cloudyfm"),
        new("music.12fm", "song_12fm.ogg", "cloudyfm"),
        new("music.13", "song_13.ogg", "overcast"),
        new("music.gpalac", "song_gpalac.ogg", "overcast"),
        new("music.overcast", "song_overcast.ogg", "overcast"),
        new("music.13fm", "song_13fm.ogg", "overcastfm"),
        new("music.fpalac", "song_fpalac.ogg", "overcastfm"),
        new("music.fmover_c", "song_fmover_c.ogg", "overcastfm"),
        new("music.overlong", "song_overlong.ogg", "rain"),
        new("music.raining", "song_raining.ogg", "rain"),
        new("music.08", "song_08.ogg", "rain"),
        new("music.fm_rain", "song_fm_rain.ogg", "rainfm"),
        new("music.08fm", "song_08fm.ogg", "rainfm"),
        new("music.20", "song_20.ogg", "snow"),
        new("music.gsnow__b", "song_gsnow__b.ogg", "snow"),
        new("music.oversnow", "song_oversnow.ogg", "snow"),
        new("music.snowing", "song_snowing.ogg", "snow"),
        new("music.20fm", "song_20fm.ogg", "snowfm"),
        new("music.fsnow__b", "song_fsnow__b.ogg", "snowfm"),
        new("music.fmover_s", "song_fmover_s.ogg", "snowfm"),
        new("music.23", "song_23.ogg", "interior"),
        new("music.23fm", "song_23fm.ogg", "interiorfm"),
        new("music.06", "song_06.ogg", "palace"),
        new("music.06fm", "song_06fm.ogg", "palacefm"),
    ];
}
