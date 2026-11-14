namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>Which part of the year a date falls in, as the donor's season table divides it.</summary>
public enum DaggerfallSeason
{
    /// <summary>Frost Fall through First Seed: months 11, 0 and 1.</summary>
    Winter,

    /// <summary>Rain's Hand through Mid Year: months 2, 3 and 4.</summary>
    Spring,

    /// <summary>Sun's Height through Last Seed: months 5, 6 and 7.</summary>
    Summer,

    /// <summary>Hearth Fire through Frost Fall's eve: months 8, 9 and 10.</summary>
    Autumn,
}

/// <summary>
/// Where the day and the year stand, as the sky, the lights and the moons show them: the hour and the second of the day,
/// the day of the year, the season, and each moon's day of its 32-day cycle, counted from the full moon. The calendar
/// hands it over; nothing here advances time.
/// </summary>
internal readonly record struct DaggerfallSkyTime(int Hour, int SecondOfDay, int DayOfYear, DaggerfallSeason Season, int MasserCycleDay, int SecundaCycleDay)
{
    /// <summary>The hour dawn begins, which is when the world's day changes character.</summary>
    internal const int DawnHour = 6;

    /// <summary>The hour dusk begins.</summary>
    internal const int DuskHour = 18;

    /// <summary>Whether it is day: dawn to dusk, as the world's own hours divide it.</summary>
    internal bool IsDay => Hour >= DawnHour && Hour < DuskHour;

    /// <summary>A moon's day of its cycle, counted from the full moon.</summary>
    internal int LunarCycleDay(bool masser) => masser ? MasserCycleDay : SecundaCycleDay;
}
