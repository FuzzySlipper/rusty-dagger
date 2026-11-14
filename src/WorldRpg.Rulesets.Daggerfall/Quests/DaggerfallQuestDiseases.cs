using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>A quest giving the player a disease or curing one, by the quest sources' disease ids.</summary>
internal sealed class DaggerfallQuestDiseases(DaggerfallDefinitions definitions, DaggerfallDiseases diseases, DaggerfallEffectLifecycle effects, long player)
{
    // Quests-Diseases numeric IDs are source identities, not the runtime enum's ordering.
    private static readonly DaggerfallClassicDisease[] QuestDiseases = [
        DaggerfallClassicDisease.WitchesPox, DaggerfallClassicDisease.Plague, DaggerfallClassicDisease.YellowFever,
        DaggerfallClassicDisease.StomachRot, DaggerfallClassicDisease.Consumption, DaggerfallClassicDisease.BrainFever,
        DaggerfallClassicDisease.SwampRot, DaggerfallClassicDisease.CalironsCurse, DaggerfallClassicDisease.Cholera,
        DaggerfallClassicDisease.Leprosy, DaggerfallClassicDisease.WoundRot, DaggerfallClassicDisease.RedDeath,
        DaggerfallClassicDisease.BloodRot, DaggerfallClassicDisease.TyphoidFever, DaggerfallClassicDisease.Dementia,
        DaggerfallClassicDisease.Chrondiasis, DaggerfallClassicDisease.WizardFever];

    /// <summary>Whether the quest sources name this disease with an id the game knows.</summary>
    internal bool Knows(string name) => definitions.QuestSources.Tables.Diseases.Lookup.TryGetValue(name, out int disease)
        && disease >= 0 && disease < QuestDiseases.Length;

    internal void Apply(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        string name = operation.Targets.Single();
        if (!definitions.QuestSources.Tables.Diseases.Lookup.TryGetValue(name, out int id) || id < 0 || id >= QuestDiseases.Length)
            throw new NotSupportedException($"Quest disease '{name}' has no admitted disease identity.");
        var disease = QuestDiseases[id];
        if (operation.Kind == DaggerfallQuestTaskOperationKind.CurePcDisease) { diseases.Cure(disease); return; }
        // Source quest exposure bypasses saving throws, while the disease owner retains level-one immunity.
        string prefix = $"quest:{instance.InstanceId}:disease:{operation.SourceLine}";
        int occurrence = 0;
        while (effects.Active.Any(effect => effect.Context.Instance.Value == $"{prefix}:{occurrence}")) occurrence++;
        diseases.Inflict(new($"{prefix}:{occurrence}", instance.SourceFile, null,
            player, [disease], BypassSavingThrows: true));
    }
}
