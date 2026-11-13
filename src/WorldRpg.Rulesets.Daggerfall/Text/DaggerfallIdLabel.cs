using System.Globalization;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The player-facing label of a content id with no authored name: its words, title-cased.</summary>
internal static class DaggerfallIdLabel
{
    internal static string For(string id) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' '));
}
