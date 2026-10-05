namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestClickResult(bool Triggered, string? Otherwise = null, int? MessageId = null);

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly System.Text.RegularExpressions.Regex ClickedActor = Header(@"^clicked\s+(?<kind>npc|foe)\s+(?<symbol>[a-zA-Z0-9_.-]+)(?:\s+(?:say|saying)\s+(?<message>[a-zA-Z0-9_.]+)|\s+and\s+at\s+least\s+(?<gold>\d+)\s+gold\s+otherwise\s+do\s+(?<task>[a-zA-Z0-9_.]+))?$");
    private static readonly System.Text.RegularExpressions.Regex ClickedPaidNpc = Header(@"^clicked\s+(?<symbol>[a-zA-Z0-9_.-]+)\s+and\s+at\s+least\s+(?<gold>\d+)\s+gold\s+otherwise\s+do\s+(?<task>[a-zA-Z0-9_.]+)$");
    private static DaggerfallQuestTaskOperation? CompileActorClick(string line, int sourceLine)
    {
        var match = ClickedActor.Match(line);
        if (!match.Success) match = ClickedPaidNpc.Match(line);
        if (!match.Success) return null;
        string text = match.Groups["message"].Value;
        int? message = int.TryParse(text, out int parsed) ? parsed : null;
        return new(match.Groups["kind"].Value.Equals("foe", StringComparison.OrdinalIgnoreCase) ? DaggerfallQuestTaskOperationKind.ClickedFoe : DaggerfallQuestTaskOperationKind.ClickedNpc,
            sourceLine, line, match.Groups["task"].Success ? [Canonical(match.Groups["symbol"].Value), Canonical(match.Groups["task"].Value)] : [Canonical(match.Groups["symbol"].Value)], [], message,
            Step: match.Groups["gold"].Success ? Step(match.Groups["gold"].Value, sourceLine) : null, MessageAlias: message is null && text.Length > 0 ? text : null);
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private Func<ulong, bool>? _spendClickGold;
    private readonly HashSet<(string Instance, string Symbol)> _pendingClickRearms = [];
    internal void BindClickGold(Func<ulong, bool> spend) => _spendClickGold = spend;
    internal bool IsClickableActor(long actor) => _instances.Values.Any(instance => instance.Lifecycle == DaggerfallQuestLifecycle.Active
        && instance.Resources.Any(resource => (resource.SelectedFoe is not null || resource.SelectedPerson is not null) && !resource.IsHidden && !resource.IsNpcDestroyed && resource.Binding.ActorIds.Contains(actor)
            && !resource.DefeatedFoeIds.Contains(actor) && !resource.RemovedFoeIds.Contains(actor)));

    /// <summary>The admitted target interaction supplies durable actor identity after live-entity validation.</summary>
    internal bool ActorClicked(long actor)
    {
        bool handled = ObserveAvailableNpcClick(actor);
        foreach (var instance in _instances.Values.Where(value => value.Lifecycle == DaggerfallQuestLifecycle.Active))
        {
            var program = Program(instance.SourceFile);
            foreach (var resource in instance.Resources.Where(value => !value.IsHidden && !value.IsNpcDestroyed && value.Binding.ActorIds.Contains(actor)
                && (value.SelectedPerson is not null || value.SelectedFoe is not null)).ToArray())
            {
                SetResource(instance.InstanceId, resource with { HasPlayerClicked = true });
                handled |= program.Tasks.Where((_, index) => !instance.Tasks[index].IsDropped)
                    .SelectMany(value => value.Operations).Any(operation =>
                        (operation.Kind == DaggerfallQuestTaskOperationKind.ClickedNpc && resource.SelectedPerson is not null
                            || operation.Kind == DaggerfallQuestTaskOperationKind.ClickedFoe && resource.SelectedFoe is not null)
                        && operation.Targets[0] == DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "clicked resource"));
            }
        }
        return handled;
    }

    DaggerfallQuestClickResult IDaggerfallQuestTaskLifecycle.ActorClick(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        var resource = instance.Resources.SingleOrDefault(value => DaggerfallQuestInstanceSave.Canonical(value.Symbol, "clicked resource") == operation.Targets[0]);
        bool correctKind = operation.Kind == DaggerfallQuestTaskOperationKind.ClickedNpc ? resource?.SelectedPerson is not null : resource?.SelectedFoe is not null;
        if (!correctKind || resource is null || resource.IsHidden || resource.IsNpcDestroyed || !resource.HasPlayerClicked) return new(false);
        int? message = null;
        if (operation.MessageId is > 0 || operation.MessageAlias is not null)
        {
            if (!Messages.TryResolveMessage(instance, operation.MessageId, operation.MessageAlias, out int resolved, out var diagnostic))
                throw new ArgumentException($"Quest click at line {operation.SourceLine}: {diagnostic}");
            message = resolved;
        }
        if (operation.Step is > 0 and var gold && !(_spendClickGold ?? throw new InvalidOperationException("Quest click currency owner has not been composed."))(checked((ulong)gold)))
            return new(false, operation.Targets[1]);
        _pendingClickRearms.Add((instance.InstanceId, resource.Symbol));
        return new(true, MessageId: message);
    }

    void IDaggerfallQuestTaskLifecycle.FinishTaskInteractions(DaggerfallQuestRuntimeInstance instance)
    {
        foreach (var pending in _pendingClickRearms.Where(value => value.Instance == instance.InstanceId).ToArray())
        {
            instance.Resources = instance.Resources.Select(value => value.Symbol == pending.Symbol ? value with { HasPlayerClicked = false } : value).ToArray();
            _pendingClickRearms.Remove(pending);
        }
    }
}
