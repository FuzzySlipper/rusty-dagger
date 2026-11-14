namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>The player's attack set, as presentation keys its motions.</summary>
internal static class DaggerfallAttackIds
{
    internal const string PowerIn = "powerIn";
    internal const string PowerHold = "powerHold";
    internal const string PowerOut = "powerOut";
    internal const string Kick = "kick";
    internal const string ShotDraw = "shotDraw";
    internal const string Shot = "shot";
    internal const string Cast = "cast";

    /// <summary>The fast attacks a chain cycles through.</summary>
    internal static IReadOnlyList<string> Fast { get; } = ["fastA", "fastB", "fastC", "fastD"];

    /// <summary>Every motion a weapon can play. A cast isn't one: the left hand plays it and the weapon rests.</summary>
    internal static IReadOnlyList<string> All { get; } = [.. Fast, PowerIn, PowerHold, PowerOut, Kick, ShotDraw, Shot];

    /// <summary>Every motion the held arms can have a clip for.</summary>
    internal static IReadOnlyList<string> Held { get; } = [.. All, Cast];
}
