namespace Daggerfall.Import.Arena2;

/// <summary>The four climate texture bases the classic exterior texture sets are swapped between.</summary>
public enum ClassicClimateBase
{
    Desert = 0,
    Mountain = 100,
    Temperate = 300,
    Swamp = 400,
}

/// <summary>The weather variant a climate texture set is swapped to.</summary>
public enum ClassicClimateSeason
{
    Summer,
    Winter,
    Rain,
}

/// <summary>
/// The classic exterior climate and season texture swap, as Daggerfall Unity's <c>ClimateSwaps.ApplyClimate</c>
/// and <c>GetClimateTextureInfo</c> recreate it, and the climate base <c>MapsFile.GetWorldClimateSettings</c>
/// assigns each CLIMATE.PAK value. An authored mesh names a texture archive in its base set; a location in
/// another climate or season draws the same record from the archive this returns. Source-format behavior:
/// the runtime reads the published table, never this transform.
/// </summary>
public static class ClassicClimateSwaps
{
    // DFLocation.ClimateTextureSet values the swap distinguishes, as archive % 100.
    private const int ExteriorTerrain = 2;
    private const int ExteriorCastle = 9;
    private const int ExteriorMagesGuild = 35;
    private const int ExteriorDoors = 74;
    private const int InteriorMarbleFloors = 41;
    private const int FirstNatureArchive = 500;

    // GetClimateTextureInfo's exterior sets: they carry a winter variant one archive up.
    private static readonly HashSet<int> ExteriorSets = [7, 9, 12, 14, 17, 26, 29, 35, 38, 42, 58, 61, 64, 69, 79, 80, 82, 83];

    // GetClimateTextureInfo's interior sets: they swap by climate and have no weather variant.
    private static readonly HashSet<int> InteriorSets = [11, 16, 19, 20, 22, 23, 24, 25, 28, 37, 40, 41, 44, 45, 47, 48, 60, 63, 66, 68];

    // Nature sets that carry a snow variant one archive up; the others (500..503) have none.
    private static readonly HashSet<int> SnowNatureSets = [504, 506, 508, 510];

    // Archives whose winter variant is climate-specific, so the climate index is suppressed.
    private static readonly HashSet<int> ClimateSuppressed = [75, 76, 77, 79, 80, 82, 83];

    /// <summary>The highest texture archive the classic leaves number.</summary>
    public const int MaximumArchive = 511;

    /// <summary>
    /// The lowest record from which a swap no longer depends on the record: the door, castle and city-spec
    /// exceptions all test records below this.
    /// </summary>
    public const int RecordIndependentFrom = 4;

    /// <summary>The climate base <c>MapsFile.GetWorldClimateSettings</c> gives a CLIMATE.PAK value.</summary>
    public static ClassicClimateBase BaseOf(int worldClimate) => worldClimate switch
    {
        223 or 227 or 228 => ClassicClimateBase.Swamp,
        224 or 225 or 229 => ClassicClimateBase.Desert,
        226 => ClassicClimateBase.Mountain,
        _ => ClassicClimateBase.Temperate,
    };

    /// <summary>The archive a location in <paramref name="climate"/> and <paramref name="season"/> draws a mesh's texture from.</summary>
    public static int Apply(int archive, int record, ClassicClimateBase climate, ClassicClimateSeason season)
    {
        if (archive is < 0 or > MaximumArchive) throw new ArgumentOutOfRangeException(nameof(archive));
        if (record < 0) throw new ArgumentOutOfRangeException(nameof(record));

        // Door 3 keeps its archive, as classic does.
        if (archive % 100 == ExteriorDoors && record == 3) return archive;

        (bool climateSet, bool supportsWinter, bool supportsRain, int set) = Info(archive);
        if (!climateSet) return archive;
        if (climate == ClassicClimateBase.Swamp && set == InteriorMarbleFloors) return archive;
        // DFU bypasses winter swaps in desert climates: too many bad swaps, and the variant is never seen.
        if (climate == ClassicClimateBase.Desert) supportsWinter = false;
        // Swamp mages guilds and the later castle records have no winter textures (DFU's precedence).
        if (climate == ClassicClimateBase.Swamp && set == ExteriorMagesGuild || set == ExteriorCastle && record > 3) supportsWinter = false;
        if (archive == 82 && record > 1 || archive == 77) supportsWinter = false;

        if (archive < FirstNatureArchive && !ClimateSuppressed.Contains(archive))
        {
            int index = (int)climate + set;
            if (season == ClassicClimateSeason.Winter && supportsWinter) index += 1;
            else if (season == ClassicClimateSeason.Rain && supportsRain) index += 2;
            return index;
        }

        return season == ClassicClimateSeason.Winter && supportsWinter ? archive + 1 : archive;
    }

    private static (bool ClimateSet, bool SupportsWinter, bool SupportsRain, int Set) Info(int archive)
    {
        if (archive >= FirstNatureArchive)
            return archive <= 503 || SnowNatureSets.Contains(archive) ? (true, SnowNatureSets.Contains(archive), false, archive) : (false, false, false, -1);
        int set = archive % 100;
        if (set == ExteriorTerrain) return (true, true, true, set);
        if (ExteriorSets.Contains(set)) return (true, true, false, set);
        // The door set swaps by climate but has no weather variant.
        if (set == ExteriorDoors) return (true, false, false, set);
        if (InteriorSets.Contains(set)) return (true, false, false, set);
        return (false, false, false, -1);
    }
}
