namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// What the quest corpus readers ask of the quest language at load, to say whether each admitted quest can run.
/// Quests implements it over the compilers its runner uses (<c>DaggerfallQuestLanguage</c>), and the ruleset passes
/// that when it composes the quests.
/// </summary>
internal interface IDaggerfallQuestLanguage
{
    /// <summary>The source's task bodies, assessed: an action no runner operation supports, or the compile's refusal.</summary>
    IReadOnlyList<DaggerfallQuestDiagnosticDefinition> Assess(DaggerfallQuestSourceDefinition source);

    /// <summary>Compiles the source's clocks, throwing <see cref="ArgumentException"/> for one the language refuses.</summary>
    void CompileClocks(DaggerfallQuestSourceDefinition source);
}
