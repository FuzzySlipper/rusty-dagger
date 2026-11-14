namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>The donor's nature archives, 500 to 511: the climates' trees, plants and rocks and their winter sets.</summary>
internal static class DaggerfallNatureArchives
{
    /// <summary>The first nature archive.</summary>
    internal const int First = 500;
    /// <summary>How many nature archives there are.</summary>
    internal const int Count = 12;

    /// <summary>Whether a flat's archive is one of the nature archives.</summary>
    internal static bool Contains(int archive) => archive is >= First and < First + Count;
}
