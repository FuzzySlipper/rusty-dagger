namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>The eight compass words a bearing reads as, for the map, the HUD and directions.</summary>
internal static class DaggerfallCompass
{
    private static readonly string[] Words = ["north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"];

    /// <summary>
    /// The compass word for a bearing in radians, clockwise from north (map up, world -Z), the way the
    /// player's yaw and a direction hint both read it.
    /// </summary>
    internal static string Word(float radians)
    {
        float turn = MathF.PI * 2;
        float bearing = ((radians % turn) + turn) % turn;
        return Words[(int)MathF.Round(bearing / (MathF.PI / 4)) % 8];
    }
}
