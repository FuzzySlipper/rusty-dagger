using System.Text.RegularExpressions;

namespace WorldRpg.Rulesets.Daggerfall;

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly Regex DialogueLink = Header(@"^(?<verb>add\s+dialog|dialog\s+link)\s+for(?:\s+location\s+(?<location>[a-zA-Z0-9_.-]+))?(?:\s+person\s+(?<person>[a-zA-Z0-9_.-]+))?(?:\s+item\s+(?<item>[a-zA-Z0-9_.-]+))?$");
    private static DaggerfallQuestTaskOperation? CompileDialogueLink(string line, int sourceLine)
    {
        var match = DialogueLink.Match(line);
        if (!match.Success) return null;
        var targets = new List<string>();
        foreach (string kind in new[] { "location", "person", "item" })
            if (match.Groups[kind].Success) { targets.Add(kind); targets.Add(Canonical(match.Groups[kind].Value)); }
        return targets.Count == 0 ? null : new(match.Groups["verb"].Value.StartsWith("add", StringComparison.OrdinalIgnoreCase)
            ? DaggerfallQuestTaskOperationKind.AddDialog : DaggerfallQuestTaskOperationKind.DialogLink, sourceLine, line, targets.ToArray(), [], null);
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    void IDaggerfallQuestTaskLifecycle.DialogueLink(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        var selected = new List<DaggerfallQuestResourceState>();
        for (int index = 0; index < operation.Targets.Length; index += 2)
        {
            string symbol = operation.Targets[index + 1];
            var resource = instance.Resources.SingleOrDefault(value => value.Symbol == symbol);
            bool valid = operation.Targets[index] switch
            {
                "location" => resource?.Binding.Kind == DaggerfallQuestResourceBindingKind.Place,
                "person" => resource?.SelectedPerson is not null && !resource.IsNpcDestroyed,
                "item" => resource?.SelectedItem is not null,
                _ => false,
            };
            if (!valid) throw new NotSupportedException($"Quest dialogue link names stale or unavailable {operation.Targets[index]} '{symbol}'.");
            selected.Add(resource!);
        }
        instance.Resources = instance.Resources.Select(resource => !selected.Contains(resource) ? resource : resource with
            {
                DialogueVisible = operation.Kind == DaggerfallQuestTaskOperationKind.AddDialog,
                DialogueLinks = operation.Kind == DaggerfallQuestTaskOperationKind.DialogLink
                    ? resource.DialogueLinks.Concat(selected.Where(value => value.Symbol != resource.Symbol).Select(value => value.Symbol)).Distinct(StringComparer.Ordinal).ToArray()
                    : resource.DialogueLinks,
            }).ToArray();
        ValidateRuntime(instance);
    }

    private void RevealDialogueResource(DaggerfallQuestRuntimeInstance instance, string symbol)
    {
        if (instance.Lifecycle != DaggerfallQuestLifecycle.Active) return;
        var resource = instance.Resources.SingleOrDefault(value => value.Symbol == symbol);
        if (resource is null || resource.DialogueVisible || resource.IsNpcDestroyed
            || resource.SelectedPerson is null && resource.SelectedItem is null && resource.Binding.Kind != DaggerfallQuestResourceBindingKind.Place) return;
        SetResource(instance.InstanceId, resource with { DialogueVisible = true });
    }

    internal IReadOnlyList<DaggerfallQuestMessageDeliverySave> DialogueRumors() => Messages.Deliveries
        .Where(delivery => delivery.Delivery == DaggerfallQuestMessageDelivery.Rumor && _instances.TryGetValue(delivery.InstanceId, out var instance)
            && instance.Lifecycle == DaggerfallQuestLifecycle.Active).OrderBy(delivery => delivery.Id).ToArray();

    internal (string Text, IReadOnlyList<string> Diagnostics) ResolveDialogueRumor(DaggerfallQuestMessageDeliverySave delivery)
    {
        if (!DialogueRumors().Contains(delivery) || !_instances.TryGetValue(delivery.InstanceId, out var instance))
            return ("That quest rumor is no longer available.", ["The rumor's owning quest or delivery is no longer active."]);
        return Messages.RenderDelivery(instance, delivery, _textContext(instance));
    }
}
