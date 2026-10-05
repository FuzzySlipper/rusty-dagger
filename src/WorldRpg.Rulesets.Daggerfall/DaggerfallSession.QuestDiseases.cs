namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    // Quests-Diseases numeric IDs are source identities, not the runtime enum's ordering.
    private static readonly DaggerfallClassicDisease[] QuestDiseases = [
        DaggerfallClassicDisease.WitchesPox, DaggerfallClassicDisease.Plague, DaggerfallClassicDisease.YellowFever,
        DaggerfallClassicDisease.StomachRot, DaggerfallClassicDisease.Consumption, DaggerfallClassicDisease.BrainFever,
        DaggerfallClassicDisease.SwampRot, DaggerfallClassicDisease.CalironsCurse, DaggerfallClassicDisease.Cholera,
        DaggerfallClassicDisease.Leprosy, DaggerfallClassicDisease.WoundRot, DaggerfallClassicDisease.RedDeath,
        DaggerfallClassicDisease.BloodRot, DaggerfallClassicDisease.TyphoidFever, DaggerfallClassicDisease.Dementia,
        DaggerfallClassicDisease.Chrondiasis, DaggerfallClassicDisease.WizardFever];

    private void QuestDiseaseAction(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        string name = operation.Targets.Single();
        if (!_definitions.QuestSources.Tables.Diseases.Lookup.TryGetValue(name, out int id) || id < 0 || id >= QuestDiseases.Length)
            throw new NotSupportedException($"Quest disease '{name}' has no admitted disease identity.");
        var disease = QuestDiseases[id];
        if (operation.Kind == DaggerfallQuestTaskOperationKind.CurePcDisease) { CureDisease(disease); return; }
        // Source quest exposure bypasses saving throws, while the disease owner retains level-one immunity.
        string prefix = $"quest:{instance.InstanceId}:disease:{operation.SourceLine}";
        int occurrence = 0;
        while (State.Effects.Active.Any(effect => effect.Context.Instance.Value == $"{prefix}:{occurrence}")) occurrence++;
        InflictDisease(new($"{prefix}:{occurrence}", instance.SourceFile, null,
            State.Actors.Player.DurableId, [disease], BypassSavingThrows: true));
    }
}
