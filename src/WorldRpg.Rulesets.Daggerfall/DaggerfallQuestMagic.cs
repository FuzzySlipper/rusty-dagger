using System.Text.RegularExpressions;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestFoeSpell(string Key, int SourceLine, long[] DeliveredActors);

internal static partial class DaggerfallQuestTaskCompiler
{
    private static DaggerfallQuestTaskOperation? CompileMagic(string line, int sourceLine)
    {
        if (Regex.Match(line, @"^cast\s+([a-zA-Z0-9'_.-]+)\s+(spell|effect)\s+do\s+([a-zA-Z0-9_.-]+)$", RegexOptions.IgnoreCase) is { Success: true } watch)
            return new(watch.Groups[2].Value.Equals("effect", StringComparison.OrdinalIgnoreCase) ? DaggerfallQuestTaskOperationKind.CastEffectDo : DaggerfallQuestTaskOperationKind.CastSpellDo,
                sourceLine, line, [watch.Groups[1].Value, Canonical(watch.Groups[3].Value)], [], null);
        if (Regex.Match(line, @"^cast\s+([a-zA-Z0-9'_.-]+)\s+(custom\s+)?spell\s+on\s+([a-zA-Z0-9_.-]+)$", RegexOptions.IgnoreCase) is { Success: true } foe)
            return new(DaggerfallQuestTaskOperationKind.CastSpellOnFoe, sourceLine, line,
                [foe.Groups[1].Value, Canonical(foe.Groups[3].Value), foe.Groups[2].Success ? "custom" : "classic"], [], null);
        return null;
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private DaggerfallCasting? _casting;
    private DaggerfallEffectCatalog? _effectCatalog;
    internal void BindCasting(DaggerfallCasting casting, DaggerfallEffectCatalog effects) { _casting = casting; _effectCatalog = effects; }

    private string QuestSpell(DaggerfallQuestTaskOperation operation)
    {
        string name = operation.Targets[0];
        DaggerfallSpellDefinition? spell;
        if (operation.Targets.Length > 2 && operation.Targets[2] == "custom")
            spell = _definitions.Magic.Spells.GetValueOrDefault(name) is { IsCustom: true } custom ? custom : null;
        else
        {
            if (!_definitions.QuestSources.Tables.Spells.Lookup.TryGetValue(name, out int id))
                throw new NotSupportedException($"Unknown quest spell '{name}'.");
            spell = _definitions.Magic.Spells.Values.FirstOrDefault(value => !value.IsCustom && value.Identity == id);
        }
        if (spell is null || _casting?.CanCastQuestSpell(spell.Key) != true)
            throw new NotSupportedException($"Quest spell '{name}' has no admitted casting/effect owner.");
        return spell.Key;
    }

    private string QuestEffect(string name)
    {
        static string Normalize(string key) => new(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var matches = _effectCatalog?.SpellDefinitions.Where(value => Normalize(value.Key) == Normalize(name)).ToArray() ?? [];
        if (matches.Length != 1) throw new NotSupportedException($"Quest effect '{name}' has no admitted effect identity.");
        return matches[0].Key;
    }

    internal void ObserveQuestCast(DaggerfallCastResult result)
    {
        if (result.Outcome != DaggerfallCastOutcome.DeliveryCompleted || result.Bundle is not { CasterId: DaggerfallActorIdentity.PlayerEntityId, Source: DaggerfallCastSource.Spell } bundle) return;
        bool Applied(DaggerfallCastEffectResult effect) => effect.Outcome is DaggerfallCastOutcome.Applied or DaggerfallCastOutcome.Refreshed or DaggerfallCastOutcome.Replaced;
        if (!bundle.Results.Any(Applied)) return;
        foreach (var instance in _instances.Values.Where(value => value.Lifecycle == DaggerfallQuestLifecycle.Active))
        {
            var program = Program(instance.SourceFile);
            for (int t = 0; t < program.Tasks.Count; t++)
            {
                var state = instance.Tasks[t];
                if (!state.IsSet || state.IsDropped) continue;
                for (int index = 0; index < program.Tasks[t].Operations.Count; index++)
                {
                    var operation = program.Tasks[t].Operations[index];
                    if (state.OperationCompleted[index]) continue;
                    bool match;
                    try
                    {
                        match = operation.Kind switch {
                            DaggerfallQuestTaskOperationKind.CastSpellDo => QuestSpell(operation) == bundle.Spell.Key,
                            DaggerfallQuestTaskOperationKind.CastEffectDo => bundle.Results.Any(effect => Applied(effect)
                                && bundle.Definitions[effect.EffectIndex].Key == QuestEffect(operation.Targets[0])),
                            _ => false };
                    }
                    catch (NotSupportedException) { continue; } // The runner publishes its source-line diagnostic.
                    if (match) state.OperationState[index] = state.OperationState[index] with { ObservedCastSequence = bundle.Sequence };
                }
            }
        }
    }

    bool IDaggerfallQuestTaskLifecycle.MagicAction(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation,
        DaggerfallQuestTaskRuntimeState task, int index)
    {
        if (operation.Kind == DaggerfallQuestTaskOperationKind.CastEffectDo) _ = QuestEffect(operation.Targets[0]);
        else _ = QuestSpell(operation);
        if (operation.Kind != DaggerfallQuestTaskOperationKind.CastSpellOnFoe) return task.OperationState[index].ObservedCastSequence is not null;
        var resource = instance.Resources.SingleOrDefault(value => value.Symbol == operation.Targets[1] && value.SelectedFoe is not null)
            ?? throw new NotSupportedException($"Quest spell target '{operation.Targets[1]}' is not a bound Foe resource.");
        int queued = task.OperationState[index].FoeSpellIndex ?? resource.FoeSpells.Length;
        if (task.OperationState[index].FoeSpellIndex is null)
        {
            resource = resource with { FoeSpells = [.. resource.FoeSpells, new(QuestSpell(operation), operation.SourceLine, [])] };
            SetResource(instance.InstanceId, resource);
            task.OperationState[index] = task.OperationState[index] with { FoeSpellIndex = queued };
        }
        AdmitQueuedFoeSpells(instance);
        resource = instance.Resources.Single(value => value.Symbol == resource.Symbol);
        return resource.FoeSpells[queued].DeliveredActors.Length > 0
            && resource.Binding.ActorIds.Where(id => !resource.DefeatedFoeIds.Contains(id) && !resource.RemovedFoeIds.Contains(id))
                .All(resource.FoeSpells[queued].DeliveredActors.Contains);
    }

    private void AdmitQueuedFoeSpells(DaggerfallQuestRuntimeInstance instance)
    {
        foreach (var original in instance.Resources.Where(value => value.FoeSpells.Length > 0).ToArray())
        {
            var resource = original;
            for (int i = 0; i < resource.FoeSpells.Length; i++)
            {
                var queued = resource.FoeSpells[i];
                foreach (long actor in resource.Binding.ActorIds.Where(id => !queued.DeliveredActors.Contains(id)
                    && !resource.DefeatedFoeIds.Contains(id) && !resource.RemovedFoeIds.Contains(id)))
                {
                    var result = _casting?.TriggerQuestSpell(actor, queued.Key);
                    if (result?.Outcome != DaggerfallCastOutcome.DeliveryCompleted) continue;
                    queued = queued with { DeliveredActors = [.. queued.DeliveredActors, actor] };
                    var spells = resource.FoeSpells.ToArray(); spells[i] = queued;
                    resource = resource with { FoeSpells = spells };
                    SetResource(instance.InstanceId, resource);
                }
            }
        }
    }
}
