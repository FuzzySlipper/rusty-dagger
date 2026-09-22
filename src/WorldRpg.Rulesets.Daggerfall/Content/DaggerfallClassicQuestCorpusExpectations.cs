namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Ruleset-owned category policy; exact source names derive from the admitted catalog rather than being copied from import data.</summary>
internal static class DaggerfallClassicQuestCorpusExpectations
{
    private static readonly IReadOnlyDictionary<string, DaggerfallClassicQuestCorpusExpectation> Values =
        new Dictionary<string, DaggerfallClassicQuestCorpusExpectation>(StringComparer.Ordinal)
        {
            ["mages"] = new("mages", [new("mages", "MagesGuild", true, "ordinary")]),
            ["temples"] = new("temples", [new("general", "HolyOrder", true, "ordinary", "C"), new("specific", "HolyOrder", true, "ordinary", "C", ExcludePrefix: true)]),
            ["social"] = new("social", [new("populace", "GeneralPopulace", true, "ordinary"), new("brotherhood", "DarkBrotherHood", true, "ordinary"), new("knightly", "KnightlyOrder", true, "ordinary")]),
            ["witches-commoners"] = new("witches-commoners", [new("witches", "Witches", true, "ordinary"), new("commoners", "Commoners", true, "ordinary")]),
            ["merchants-vampires"] = new("merchants-vampires", [new("merchants", "Merchants", true, "ordinary"), new("vampires", "Vampires", true, "ordinary")]),
            ["disabled"] = new("disabled", [new("disabled-merchant", "Merchants", false, "notOffered"), new("disabled-nobility", "Nobility", false, "notOffered"), new("daedric-summon", "Oblivion", false, "summonOnly")]),
            ["nobility"] = new("nobility", [new("nobility", "Nobility", true, "ordinary")]),
        };

    internal static DaggerfallClassicQuestCorpusExpectation Require(string id) =>
        Values.TryGetValue(id, out DaggerfallClassicQuestCorpusExpectation? value)
        ? value
        : throw new DaggerfallContentException([$"Classic quest corpus pack '{id}' has no ruleset admission contract."]);
}

internal sealed record DaggerfallClassicQuestCorpusExpectation(string Id, IReadOnlyList<DaggerfallClassicQuestCategoryExpectation> Categories);
internal sealed record DaggerfallClassicQuestCategoryExpectation(string Id, string CatalogGroup, bool Active, string Availability, string? NamePrefix = null, bool ExcludePrefix = false);
