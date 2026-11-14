namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Which texture archives a climate wears: its ground set and its nature set (MapsFile.GetWorldClimateSettings), each
/// with the winter and rain variants the climate has. The site readers and the exterior drawing read the same table.
/// </summary>
internal static class DaggerfallClimateArchives
{
    /// <summary>The ground archive a climate wears in a season: its rain set, and its winter set where the climate has one.</summary>
    internal static int GroundTextureArchive(int climateValue, DaggerfallExteriorSeason season)
    {
        int archive = climateValue switch
        {
            223 or 227 or 228 => 402,
            224 or 225 or 229 => 2,
            226 or 230 => 102,
            _ => 302,
        };
        return season == DaggerfallExteriorSeason.Rain ? archive + 2
            : season == DaggerfallExteriorSeason.Winter && GroundSupportsWinter(climateValue) ? archive + 1 : archive;
    }

    /// <summary>Maps MapsFile.GetWorldClimateSettings to its nature set and winter variant.</summary>
    internal static int NatureTextureArchive(int climateValue, DaggerfallExteriorSeason season)
    {
        int archive = climateValue switch
        {
            223 or 231 => 504,
            224 or 225 => 503,
            226 => 510,
            227 => 500,
            228 => 502,
            229 => 501,
            230 => 506,
            232 => 508,
            _ => 504,
        };
        return season == DaggerfallExteriorSeason.Winter && NatureSupportsWinter(climateValue)
            ? archive + 1
            : archive;
    }

    /// <summary>The desert climates, whose ground and nature have no winter set.</summary>
    internal static bool IsDesertClimate(int climateValue) => climateValue is 224 or 225 or 229;

    /// <summary>Whether a climate's ground has a winter set: every climate but the deserts.</summary>
    internal static bool GroundSupportsWinter(int climateValue) => !IsDesertClimate(climateValue);

    /// <summary>The donor nature archives carrying snow are mountains and the three woodland sets.</summary>
    internal static bool NatureSupportsWinter(int climateValue) => climateValue is 223 or 226 or 230 or 231 or 232;
}
