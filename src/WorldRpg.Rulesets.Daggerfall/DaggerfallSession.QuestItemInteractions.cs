using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallQuestItemTransfer? GiveQuestItem(DaggerfallQuestRuntimeInstance instance, string symbol, string recipient, string operation, DaggerfallQuestItemTransfer? queued)
    {
        var item = instance.Resources.SingleOrDefault(resource => resource.Symbol == symbol && resource.SelectedItem is not null)
            ?? throw new NotSupportedException($"Give item requires selected Item '{symbol}'.");
        var target = instance.Resources.SingleOrDefault(resource => resource.Symbol == recipient && (resource.SelectedFoe is not null || resource.SelectedPerson is not null))
            ?? throw new NotSupportedException($"Give item requires an actor resource '{recipient}'.");
        if (target.SelectedFoe is not null)
        {
            if (queued is null)
            {
                if (State.QuestItems.Have(instance, symbol) && State.QuestItems.Take(instance, symbol) == DaggerfallQuestItemResult.Unavailable) return null;
                item = instance.Resources.Single(resource => resource.Symbol == symbol);
                queued = new(symbol, recipient, State.QuestItems.SnapshotItem(item), []);
            }
            if (item.SelectedItem!.Metadata.QuestId is null && queued.Prototype!.Metadata.QuestId is not null)
                queued = queued with { Prototype = queued.Prototype with { Metadata = queued.Prototype.Metadata with { QuestId = null, QuestItemSymbol = null } } };
            // The durable accepted queue also serves later spawn groups. Each admitted copy is
            // recorded before another can be issued; no live entity or inventory is duplicated.
            long? actorId = target.Binding.ActorIds.Where(id => !queued.Recipients.Contains(id) && !target.RemovedFoeIds.Contains(id)
                && State.Actors.TryGet(id, out _) && (_corpseLoot.Corpses.ContainsKey(id) || !State.Actors.Get(id).IsDefeated)).Select(id => (long?)id).FirstOrDefault();
            if (actorId is { } id)
            {
                var owner = _corpseLoot.Corpses.ContainsKey(id) ? DaggerfallItemOwner.Corpse(id) : DaggerfallItemOwner.Actor(id);
                void Admit() => State.QuestItems.AdmitItem(queued.Prototype!, owner, $"daggerfall.quest.gift.{Uri.EscapeDataString(instance.InstanceId)}.{Uri.EscapeDataString(operation)}.{id}");
                if (owner.Scope == "corpse") _corpseLoot.ReceiveItems(id, Admit);
                else Admit();
                queued = queued with { Recipients = [.. queued.Recipients, id] };
            }
            return queued;
        }
        if (target.IsHidden || target.IsNpcDestroyed || target.Binding.ActorIds is not [var targetId]) return null;
        if (!State.Actors.TryGet(targetId, out var actor))
        {
            if (State.Actors.Entities.TryResolve(WorldRpg.Kit.Actors.ActorsState.Identity(targetId), out _))
                throw new NotSupportedException($"Give item recipient '{recipient}' is a noncombat projection without an inventory owner.");
            return null;
        }
        if (actor.IsDefeated && !_corpseLoot.Corpses.ContainsKey(targetId)) return null;
        var destination = _corpseLoot.Corpses.ContainsKey(targetId) ? DaggerfallItemOwner.Corpse(targetId) : DaggerfallItemOwner.Actor(targetId);
        if (State.QuestItems.Have(instance, symbol) && State.QuestItems.Take(instance, symbol) == DaggerfallQuestItemResult.Unavailable) return null;
        item = instance.Resources.Single(resource => resource.Symbol == symbol);
        DaggerfallQuestResourceBinding binding = item.Binding;
        void Transfer() => binding = item.Binding.Kind == DaggerfallQuestResourceBindingKind.Pending
            ? State.QuestItems.AdmitItem(item.SelectedItem!, destination, $"daggerfall.quest.{Uri.EscapeDataString(instance.InstanceId)}.{symbol}")
            : TransferBoundQuestItem(item.Binding, (_, transfer) =>
            {
                transfer(destination);
                return item.Binding.UniqueItemIds.Length > 0 ? item.Binding : item.Binding with
                { Stacks = item.Binding.Stacks.Select(stack => stack with { Owner = new(destination.Scope, destination.Id) }).Distinct().ToArray() };
            });
        if (destination.Scope == "corpse") _corpseLoot.ReceiveItems(targetId, Transfer);
        else Transfer();
        State.Quests.SetResource(instance.InstanceId, item with { Binding = binding });
        return new(symbol, recipient, null, [targetId]);
    }

    private DaggerfallItemInstanceMetadata? QuestLootMetadata(DaggerfallItemOwner owner, InventoryContainerSelection selection)
    {
        if (selection.UniqueEntityId is { } entity)
            return State.ItemInstances.RequireUnique(State.Inventory.GetDurableItemId(new(entity)).Value);
        return selection.Stack is { } stack ? State.ItemInstances.RequireStack(owner, stack) : null;
    }
}
