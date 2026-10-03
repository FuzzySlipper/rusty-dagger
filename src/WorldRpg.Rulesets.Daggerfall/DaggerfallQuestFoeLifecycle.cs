using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Resource-owned observations and commands over canonical actors; no parallel actor lifetime.</summary>
internal sealed partial class DaggerfallQuestInstances
{
    private Func<long, bool, bool>? _applyFoeCommand;
    internal void BindFoeCommands(Func<long, bool, bool> apply) => _applyFoeCommand = apply ?? throw new ArgumentNullException(nameof(apply));

    internal void ObserveFoeFact(IProductFact fact)
    {
        long id = fact switch { ActorDamagedFact damage => damage.ActorId, ActorDiedFact death => death.ActorId, _ => 0 };
        if (id == 0) return;
        foreach (var instance in _instances.Values.Where(value => value.Lifecycle == DaggerfallQuestLifecycle.Active))
            instance.Resources = instance.Resources.Select(resource => resource.SelectedFoe is not null && resource.Binding.ActorIds.Contains(id)
                ? fact switch
                {
                    ActorDamagedFact { ActualHealthLost: > 0, TargetDefeated: false } => resource with { FoeInjured = true },
                    ActorDiedFact when !resource.RemovedFoeIds.Contains(id) && !resource.DefeatedFoeIds.Contains(id) => resource with { DefeatedFoeIds = [.. resource.DefeatedFoeIds, id] },
                    _ => resource,
                } : resource).ToArray();
    }

    internal void ObserveFoeRemoval(long id)
    {
        foreach (var instance in _instances.Values)
            instance.Resources = instance.Resources.Select(resource => resource.SelectedFoe is not null && resource.Binding.ActorIds.Contains(id) && !resource.RemovedFoeIds.Contains(id)
                ? resource with { RemovedFoeIds = [.. resource.RemovedFoeIds, id] } : resource).ToArray();
    }

    bool IDaggerfallQuestTaskLifecycle.FoeTrigger(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        var resource = FoeResource(instance, operation);
        return operation.Kind == DaggerfallQuestTaskOperationKind.InjuredFoe ? resource.FoeInjured
            : resource.DefeatedFoeIds.Length >= operation.Step!.Value;
    }

    void IDaggerfallQuestTaskLifecycle.FoeCommand(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        var resource = FoeResource(instance, operation);
        if (_applyFoeCommand is null) throw new InvalidOperationException("The session has no canonical quest foe command owner.");
        bool remove = operation.Kind == DaggerfallQuestTaskOperationKind.RemoveFoe;
        SetResource(instance.InstanceId, remove ? resource with { IsHidden = true } : resource with { FoeDeathRequested = true });
        if (remove) instance.Placements = instance.Placements.Where(value => value.ResourceSymbol != resource.Symbol).ToArray();
    }

    /// <summary>Pending command intent applies after actual actor admission, including current-schema restore.</summary>
    internal void ReconcileFoeCommands()
    {
        foreach (var instance in _instances.Values.ToArray())
            foreach (var prior in instance.Resources.Where(resource => resource.SelectedFoe is not null && (resource.IsHidden || resource.FoeDeathRequested)).ToArray())
                foreach (long id in prior.Binding.ActorIds)
                {
                    var resource = instance.Resources.Single(value => value.Symbol == prior.Symbol);
                    if (resource.RemovedFoeIds.Contains(id) || !resource.IsHidden && resource.DefeatedFoeIds.Contains(id)) continue;
                    bool applied = (_applyFoeCommand ?? throw new InvalidOperationException("The session has no canonical quest foe command owner."))(id, resource.IsHidden);
                    if (applied && resource.IsHidden)
                    {
                        resource = instance.Resources.Single(value => value.Symbol == prior.Symbol);
                        if (!resource.RemovedFoeIds.Contains(id))
                            instance.Resources = instance.Resources.Select(value => value.Symbol == resource.Symbol ? resource with { RemovedFoeIds = [.. resource.RemovedFoeIds, id] } : value).ToArray();
                    }
                }
    }

    private static DaggerfallQuestResourceState FoeResource(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) =>
        instance.Resources.SingleOrDefault(value => value.Symbol == operation.Targets[0] && value.SelectedFoe is not null)
        ?? throw new ArgumentException($"Quest foe action at line {operation.SourceLine} requires selected Foe '{operation.Targets[0]}'.");
}
