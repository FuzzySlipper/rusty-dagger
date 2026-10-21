using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// What each character-sheet value means, as the sheet's tooltips say it. Attributes read the donor's own
/// descriptions (TEXT.RSC records 0-7, DFU TextProvider.GetStatDescriptionTextID) resolved for the player;
/// the vitals, resistances, guilds and legal standing are worded here, from the values the sheet shows.
/// </summary>
internal static class DaggerfallCharacterSheetText
{
    private static readonly IReadOnlyDictionary<string, string> AttributeRecords = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["strength"] = "0", ["intelligence"] = "1", ["willpower"] = "2", ["agility"] = "3",
        ["endurance"] = "4", ["personality"] = "5", ["speed"] = "6", ["luck"] = "7",
    };

    /// <summary>Adds each value's explanation to a sheet read for the player.</summary>
    internal static CharacterSheetPresentation Describe(CharacterSheetPresentation sheet, DaggerfallDefinitions definitions, DaggerfallTextContext context) => sheet with
    {
        Attributes = [.. sheet.Attributes.Select(stat => stat with { Description = stat.Id == "reflexes" ? Reflexes(stat) : Attribute(definitions, context, stat.Id) })],
        Resources = [.. sheet.Resources.Select(resource => resource with { Description = Resource(resource) })],
        Resistances = [.. sheet.Resistances.Select(stat => stat with { Description = Resistance(stat) })],
        Affiliations = [.. sheet.Affiliations.Select(affiliation => affiliation with { Description = Affiliation(affiliation) })],
        LegalStandings = sheet.LegalStandings is null ? null : [.. sheet.LegalStandings.Select(standing => standing with { Description = Legal(standing) })],
    };

    private static string? Attribute(DaggerfallDefinitions definitions, DaggerfallTextContext context, string id)
    {
        if (!AttributeRecords.TryGetValue(id, out string? record)) return null;
        string text = definitions.TextPresentation.Resolve(new(DaggerfallTextKind.Resource, record), context).Text;
        // The record opens with the attribute's name in capitals, which the row already shows.
        string[] lines = [.. text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
        return string.Join(' ', lines.Skip(lines.Length > 1 ? 1 : 0));
    }

    /// <summary>
    /// Reflexes has no TEXT.RSC description, so the sheet says what it is and what it changes, from the same
    /// rate play applies to skill use.
    /// </summary>
    private static string Reflexes(CharacterStatPresentation stat)
    {
        int reflexes = checked((int)stat.Value);
        string rating = reflexes switch { 0 => "very high", 1 => "high", 2 => "average", 3 => "low", _ => "very low" };
        int learning = Policies.DaggerfallFormulaPolicy.ReflexesSkillUseScaleMilli(reflexes) / 10;
        return $"Your reflexes are {rating}, chosen when you made your character. They set how quickly the world reacts to you, "
            + $"and your skills learn from use at {learning.ToString(CultureInfo.InvariantCulture)}% of the average rate.";
    }

    private static string? Resource(CharacterResourcePresentation resource) => resource.Id switch
    {
        "health" => "You die when your health reaches zero. Rest, healing spells and potions restore it.",
        "stamina" => "Fatigue. Running, jumping and fighting spend it, and resting restores it; when it runs out you collapse.",
        "magicka" => "Spell points. Casting a spell spends its cost, and resting restores them.",
        _ => null,
    };

    private static string? Resistance(CharacterStatPresentation stat) => stat.Id switch
    {
        "immunity-paralysis" => stat.Value > 0 ? "You are immune to paralysis." : "You are not immune to paralysis.",
        "immunity-disease" => stat.Value > 0 ? "You are immune to disease." : "You are not immune to disease.",
        _ when stat.Id.StartsWith("resistance-", StringComparison.Ordinal) =>
            $"How well you resist {stat.Label["Resistance ".Length..].ToLower(CultureInfo.InvariantCulture)} spells and effects. Higher resists more often.",
        _ => null,
    };

    private static string Affiliation(CharacterAffiliationPresentation affiliation)
    {
        string rank = affiliation.RankTitle ?? $"rank {affiliation.Rank.ToString(CultureInfo.InvariantCulture)}";
        string text = $"You are {rank} of {affiliation.Faction}, with reputation {affiliation.Reputation.ToString(CultureInfo.InvariantCulture)}.";
        if (affiliation.NextRequirement is { } next)
            text += $" Promotion to rank {next.Rank.ToString(CultureInfo.InvariantCulture)} needs reputation {next.Reputation.ToString(CultureInfo.InvariantCulture)}"
                + $" and skills of {next.HighSkill.ToString(CultureInfo.InvariantCulture)} and {next.LowSkill.ToString(CultureInfo.InvariantCulture)}.";
        if (affiliation.DaysUntilReview is int days)
            text += $" Your standing is reviewed in {days.ToString(CultureInfo.InvariantCulture)} days.";
        return text;
    }

    private static string Legal(CharacterLegalStandingPresentation standing) =>
        $"Your legal reputation in {standing.RegionName} is {standing.Reputation.ToString(CultureInfo.InvariantCulture)}, so its people know you as {standing.Standing}."
        + (standing.Banished ? " You are banished from the region." : "")
        + " Crimes there lower it.";
}
