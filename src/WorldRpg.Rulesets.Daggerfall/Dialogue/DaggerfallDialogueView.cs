namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallDialogueTone { Polite, Normal, Blunt }
internal enum DaggerfallDialogueTopic { Directions, News, Work, QuestInfo, Donate, Cure, RankReview, Armor, House, Join, Teleport, Spymaster, RestoreAttributes }

/// <param name="Group">The heading the conversation lists the topic under (<see cref="DaggerfallDialogueTopicGroups"/>).</param>
/// <param name="Category">The list inside that heading the topic belongs to, such as a kind of building; null when the heading has no lists.</param>
internal sealed record DaggerfallDialogueTopicOption(string Id, string Label, string? Key = null, string Group = DaggerfallDialogueTopicGroups.Services,
    string? Category = null);

/// <summary>The headings a conversation groups its topics under, in the order it shows them.</summary>
internal static class DaggerfallDialogueTopicGroups
{
    internal const string Talk = "talk";
    internal const string Places = "places";
    internal const string Quests = "quests";
    internal const string Services = "services";
    internal static readonly (string Id, string Label)[] All =
        [(Talk, "Ask about"), (Quests, "Your business"), (Places, "Where is…"), (Services, "Services")];

    /// <summary>
    /// The kinds of building the "Where is" directory lists, in the donor's order, each under its own list
    /// (DFU TalkManager.AssembleTopicListLocation and BuildingTypeToGroupString). Every other kind - houses,
    /// walls, the palace, ships, furniture stores and the specials - is on the donor's skip list.
    /// </summary>
    internal static readonly IReadOnlyDictionary<int, (int Order, string Label)> BuildingCategories = new Dictionary<int, (int, string)>
    {
        [0] = (0, "Alchemists"), [2] = (1, "Armorers"), [3] = (2, "Banks"), [5] = (3, "Bookstores"),
        [6] = (4, "Clothing stores"), [8] = (5, "Gem stores"), [9] = (6, "General stores"), [10] = (7, "Libraries"),
        [11] = (8, "Guilds"), [12] = (9, "Pawn shops"), [13] = (10, "Weaponsmiths"), [14] = (11, "Temples"), [15] = (12, "Taverns"),
    };
    internal const string People = "People";
    internal const string Elsewhere = "Elsewhere in the region";
    internal const string QuestPlaces = "Your business";
}
/// <param name="Known">Whether the map already discloses this destination; subject knowledge is resolved separately.</param>
internal sealed record DaggerfallDialogueDestination(string Id, string Name, string Hint, bool Known = true)
{
    /// <summary>Whether the speaking NPC shares the destination's building.</summary>
    internal bool SameBuilding { get; init; }
    /// <summary>Whether the speaking NPC belongs to the destination's organization.</summary>
    internal bool SameOrganization { get; init; }
    /// <summary>Whether the destination is a current quest-local subject.</summary>
    internal bool QuestLocality { get; init; }
}
internal sealed record DaggerfallDialogueView(
    string Revision,
    string TargetLabel,
    string Greeting,
    string Tone,
    string? Question,
    string? Reply,
    IReadOnlyList<DaggerfallDialogueTopicOption> Topics,
    IReadOnlyList<string> Diagnostics)
{
    internal int ComprehendLanguagesBonus { get; init; }
    internal bool BankAvailable { get; init; }
    internal IReadOnlyList<DaggerfallQuestContact> QuestContacts { get; init; } = [];
    internal DaggerfallTavernView? Tavern { get; init; }
    internal DaggerfallSkillTrainingProviderView? Training { get; init; }
    internal DaggerfallMerchantView? Merchant { get; init; }
    /// <summary>What the person offers, in the order the person screen's chooser lists it; talk is always first.</summary>
    internal IReadOnlyList<DaggerfallPersonService> Services { get; init; } = DaggerfallPersonServices.TalkOnly;
    /// <summary>The page contact opens on: talk for a person who offers nothing else, otherwise the chooser.</summary>
    internal string OpensOn => Services.Count == 1 ? DaggerfallPersonServices.Talk : DaggerfallPersonServices.Chooser;
}

/// <summary>A quest this person is a contact in, by the quest's instance and the person's symbol in it.</summary>
internal sealed record DaggerfallQuestContact(string InstanceId, string Symbol);
