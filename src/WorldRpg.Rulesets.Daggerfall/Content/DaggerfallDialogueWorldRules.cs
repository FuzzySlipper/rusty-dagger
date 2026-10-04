using WorldRpg.Rulesets.Daggerfall;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// One source-backed non-quest spoken-news rule. The rule is an authored interpretation of the
/// donor's region condition update: when the named world variable is set for the current region,
/// the talk owner admits the donor resource as ambient news.
/// </summary>
internal sealed record DaggerfallDialogueWorldNewsRule(
    int Type,
    int TextId,
    DaggerfallVariableScope Scope,
    IReadOnlyList<int> VariableKeys,
    bool RequiredValue);

/// <summary>
/// World-variable links used by social dialogue. The ruleset owns the interpretation while the
/// session's variable store owns the live values, so a conversation reads the same state that
/// quests and world systems write and save.
/// </summary>
internal sealed record DaggerfallDialogueWorldRules(
    string Source,
    IReadOnlyList<DaggerfallDialogueWorldNewsRule> News)
{
    internal const string DonorSource = "daggerfall-unity/Assets/Scripts/Game/Entities/PlayerEntity.cs";

    internal static DaggerfallDialogueWorldRules Empty { get; } = new(DonorSource, []);
}
