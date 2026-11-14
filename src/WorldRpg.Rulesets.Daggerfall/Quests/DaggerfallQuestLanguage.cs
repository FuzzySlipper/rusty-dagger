using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The quest language the corpus readers assess sources with: the task and clock compilers the runner uses.</summary>
internal sealed class DaggerfallQuestLanguage : IDaggerfallQuestLanguage
{
    public IReadOnlyList<DaggerfallQuestDiagnosticDefinition> Assess(DaggerfallQuestSourceDefinition source) =>
        DaggerfallQuestTaskCompiler.Assess(source);

    public void CompileClocks(DaggerfallQuestSourceDefinition source) => _ = DaggerfallQuestClockCompiler.Compile(source);
}
