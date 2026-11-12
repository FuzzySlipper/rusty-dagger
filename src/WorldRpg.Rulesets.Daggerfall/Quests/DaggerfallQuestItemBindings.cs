namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallQuestInstances
{
    /// <summary>Follows completed Engine-backed metadata changes, including partial moves and splits.</summary>
    internal void ObserveStackChange(DaggerfallStackChange change)
    {
        var source = new DaggerfallQuestStackBinding(new(change.SourceOwner.Scope, change.SourceOwner.Id), change.Source.Value);
        foreach (var instance in _instances.Values)
            instance.Resources = instance.Resources.Select(resource =>
            {
                var binding = resource.Binding;
                if (binding.Kind != DaggerfallQuestResourceBindingKind.Item || !binding.Stacks.Contains(source)) return resource;
                var stacks = binding.Stacks.Where(value => !change.SourceRetired || value != source).ToList();
                if (change.DestinationOwner is { } owner && change.Destination is { } destination)
                {
                    var target = new DaggerfallQuestStackBinding(new(owner.Scope, owner.Id), destination.Value);
                    if (!stacks.Contains(target)) stacks.Add(target);
                }
                return resource with { Binding = binding with { Stacks = stacks.ToArray() } };
            }).ToArray();
    }
}
