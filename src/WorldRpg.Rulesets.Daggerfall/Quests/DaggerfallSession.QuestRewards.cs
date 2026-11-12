namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private long? OfferQuestReward(DaggerfallQuestRuntimeInstance instance, string symbol)
    {
        if (State.PlayerControl.Position is not { } position) return null;
        if (State.QuestItems.Have(instance, symbol) && State.QuestItems.Take(instance, symbol) == DaggerfallQuestItemResult.Unavailable) return null;
        var resource = instance.Resources.Single(value => value.Symbol == symbol && value.SelectedItem is not null);
        var binding = resource.Binding.Kind == DaggerfallQuestResourceBindingKind.Pending
            ? _groundContainers.CreateQuestItem(resource.SelectedItem!, position, _uniqueItems)
            : PlaceBoundQuestItem(resource.Binding, position);
        State.Quests.SetResource(instance.InstanceId, resource with { Binding = binding });
        State.QuestItems.MakePermanent(instance, symbol);
        var owner = binding.UniqueItemIds.Length > 0 ? State.ItemInstances.RequireUnique(binding.UniqueItemIds[0]).Owner
            : new DaggerfallItemOwner(binding.Stacks[0].Owner.Scope, binding.Stacks[0].Owner.Id);
        if (owner.Scope != "ground") throw new InvalidOperationException("Quest reward must be admitted to its real loot owner before success.");
        return owner.Id;
    }

    private bool PresentQuestReward(long? ground)
    {
        if (ground is { } id && !_lootUi.OpenGround(id))
        {
            Presentation.SetOutcome("Return to the reward location to collect your quest reward.");
            return false;
        }
        Presentation.SetOutcome("Quest completed.");
        return true;
    }
}
