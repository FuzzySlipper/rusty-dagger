namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The last admitted static-person interaction observed by this availability action.</summary>
internal sealed record DaggerfallQuestNpcAvailability(long ActorId, bool Accepted);

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly System.Text.RegularExpressions.Regex NpcLifecycle = Header(@"^(?<verb>create|hide|restore|destroy)\s+npc\s+(?<symbol>[a-zA-Z0-9_.-]+)$|^(?<verb>destroy|hide|restore)\s+(?<symbol>[a-zA-Z0-9_.-]+)$");
    private static readonly System.Text.RegularExpressions.Regex NpcAvailable = Header(@"^when\s+(?<person>[a-zA-Z0-9_.-]+)\s+is\s+available$");
    private static DaggerfallQuestTaskOperation? CompileNpcLifecycle(string line, int sourceLine)
    {
        if (NpcAvailable.Match(line) is { Success: true } available)
            return new(DaggerfallQuestTaskOperationKind.WhenNpcAvailable, sourceLine, line, [available.Groups["person"].Value], [], null);
        if (NpcLifecycle.Match(line) is not { Success: true } match) return null;
        var kind = match.Groups["verb"].Value.ToLowerInvariant() switch
        {
            "create" => DaggerfallQuestTaskOperationKind.CreateNpc,
            "hide" => DaggerfallQuestTaskOperationKind.HideNpc,
            "restore" => DaggerfallQuestTaskOperationKind.RestoreNpc,
            _ => DaggerfallQuestTaskOperationKind.DestroyNpc,
        };
        return new(kind, sourceLine, line, [Canonical(match.Groups["symbol"].Value)], [], null);
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    /// <summary>Quest absence overlays the registry's own schedule and removal state.</summary>
    internal bool IsNpcUnavailable(long id) => _instances.Values.Any(instance => instance.Lifecycle == DaggerfallQuestLifecycle.Active
        && instance.Resources.Any(resource => resource.SelectedPerson is not null && resource.Binding.ActorIds.Contains(id)
            && (resource.IsHidden || resource.IsNpcDestroyed)));

    void IDaggerfallQuestTaskLifecycle.NpcCommand(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation, string task, int index)
    {
        var resource = instance.Resources.SingleOrDefault(value => value.Symbol == operation.Targets[0] && value.SelectedPerson is not null)
            ?? throw new ArgumentException($"Quest NPC action at line {operation.SourceLine} requires Person '{operation.Targets[0]}'.");
        switch (operation.Kind)
        {
            case DaggerfallQuestTaskOperationKind.HideNpc:
                SetResource(instance.InstanceId, resource with { IsHidden = true });
                break;
            case DaggerfallQuestTaskOperationKind.RestoreNpc:
                SetResource(instance.InstanceId, resource with { IsHidden = false });
                break;
            case DaggerfallQuestTaskOperationKind.DestroyNpc:
                SetResource(instance.InstanceId, resource with { IsNpcDestroyed = true, HasPlayerClicked = false });
                instance.Placements = instance.Placements.Where(value => value.ResourceSymbol != resource.Symbol).ToArray();
                // Only a person minted for this quest may become permanently removed in the
                // registry. A reference to an authored provider never destroys that definition.
                foreach (long id in resource.Binding.ActorIds)
                    if (_placementNpcs?.Require(id) is { Kind: DaggerfallNpcKind.Questor } npc
                        && npc.StableKey == instance.InstanceId + "/" + resource.Symbol)
                        _placementNpcs.SetPresence(id, DaggerfallNpcPresence.Removed);
                break;
            case DaggerfallQuestTaskOperationKind.CreateNpc:
                // Source CreateNpc places a generic Person at its allocated home. It cannot
                // resurrect a destroyed resource or move an individual/quest giver home.
                if (resource.IsNpcDestroyed || resource.SelectedPerson!.Individual || resource.SelectedPerson.QuestorId is not null) return;
                if (resource.SelectedPerson.Home is null)
                    throw new NotSupportedException($"Person '{resource.Symbol}' has no allocated home for create npc.");
                RequestPlacement(instance.InstanceId, $"task:{task}:{index}", resource.Symbol, resource.Symbol + ".home", reapply: true);
                break;
        }
    }

    private int AvailableNpcFaction(DaggerfallQuestTaskOperation operation)
    {
        int faction = _definitions.QuestSources.Tables.ActorItemTables.Factions.Resolve(operation.Targets[0]).P3;
        if (!_definitions.Factions.Factions.TryGetValue(faction, out var definition) || definition.Type != 0)
            throw new ArgumentException($"Quest availability at line {operation.SourceLine} requires an individual NPC.");
        return faction;
    }

    private bool ObserveAvailableNpcClick(long actor)
    {
        if (_placementNpcs?.IsStatic(actor) != true) return false;
        bool handled = false;
        int faction = _placementNpcs.Require(actor).Appearance.FactionId;
        foreach (var instance in _instances.Values.Where(value => value.Lifecycle == DaggerfallQuestLifecycle.Active))
        {
            var program = Program(instance.SourceFile);
            for (int task = 0; task < program.Tasks.Count; task++)
                for (int index = 0; index < program.Tasks[task].Operations.Count; index++)
                {
                    if (program.Tasks[task].Operations[index].Kind != DaggerfallQuestTaskOperationKind.WhenNpcAvailable) continue;
                    handled |= AvailableNpcFaction(program.Tasks[task].Operations[index]) == faction;
                    var state = instance.Tasks[task];
                    var prior = state.OperationState[index];
                    if (prior.NpcAvailability?.ActorId != actor)
                        state.OperationState[index] = prior with { NpcAvailability = new(actor, false) };
                }
        }
        return handled;
    }

    bool IDaggerfallQuestTaskLifecycle.NpcAvailable(DaggerfallQuestTaskOperation operation, DaggerfallQuestTaskRuntimeState state, int index)
    {
        int faction = AvailableNpcFaction(operation);
        var prior = state.OperationState[index];
        if (prior.NpcAvailability is not { Accepted: false } click || _placementNpcs is null
            || !_placementNpcs.IsGameplayActive(click.ActorId) || _placementNpcs.Require(click.ActorId).Appearance.FactionId != faction) return false;
        if (_instances.Values.Any(value => value.Lifecycle == DaggerfallQuestLifecycle.Active
            && value.Resources.Any(resource => resource.SelectedPerson?.FactionId == faction))) return false;
        state.OperationState[index] = prior with { NpcAvailability = click with { Accepted = true } };
        return true;
    }
}
