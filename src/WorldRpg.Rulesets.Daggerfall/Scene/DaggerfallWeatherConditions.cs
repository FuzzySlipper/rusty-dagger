namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The weather's facts over a climate: what is falling and how hard, the wind, and what it has left on the ground. Wet
/// and snow build and fade with world time, so they outlast the day that brought them. Presentation draws these, and
/// they are what any rule that cares about the weather should read.
/// </summary>
/// <param name="Kind">The day's weather.</param>
/// <param name="Falling">How hard rain or snow is falling, from 0 to 1.</param>
/// <param name="Wind">How hard the wind blows, from 0 to 1.</param>
/// <param name="WindDegrees">Where the wind blows toward, clockwise from north.</param>
/// <param name="Wet">How wet open ground is, from 0 to 1.</param>
/// <param name="Snow">How much snow lies on open ground, from 0 to 1.</param>
internal readonly record struct DaggerfallWeatherConditions(DaggerfallWeatherKind Kind, float Falling, float Wind, float WindDegrees, float Wet, float Snow);
