namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>The spell elements in their classic order (a spell's element value), as presentation names them.</summary>
internal static class DaggerfallSpellElements
{
    internal static IReadOnlyList<string> Names { get; } = ["fire", "cold", "poison", "shock", "magic"];

    /// <summary>The element's name, or null for a value outside the five.</summary>
    internal static string? Name(int element) => element >= 0 && element < Names.Count ? Names[element] : null;
}
