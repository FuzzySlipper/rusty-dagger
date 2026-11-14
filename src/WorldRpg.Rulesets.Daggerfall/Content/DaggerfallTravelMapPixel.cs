namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>A Daggerfall world-map pixel used as a travel route endpoint.</summary>
internal readonly record struct DaggerfallTravelMapPixel(int X, int Y)
{
    internal const int Width = 1000;
    internal const int Height = 500;

    internal bool IsInBounds => X >= 0 && X < Width && Y >= 0 && Y < Height;

    internal void RequireInBounds(string parameterName)
    {
        if (!IsInBounds)
            throw new ArgumentOutOfRangeException(parameterName, this,
                $"Travel map pixels must be inside the {Width} by {Height} world map.");
    }
}
