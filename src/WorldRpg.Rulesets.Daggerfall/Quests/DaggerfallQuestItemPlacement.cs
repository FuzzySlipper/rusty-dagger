using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Where a quest's items physically go: a bound item placed again (out of an unloaded site's actors and corpses, an unloaded
/// pile or a live owner) into one ground pile, an item given to a foe or a person, and the reward set on the ground.
/// </summary>
/// <remarks>The unloaded sites' deltas (the site crossing's) and the corpses (Items' corpse loot) are read and changed through
/// plain inputs.</remarks>
internal sealed class DaggerfallQuestItemPlacement(DaggerfallQuestInstances quests, DaggerfallQuestItems questItems, DaggerfallItemInstances itemInstances,
    ActorsState actors, PlayerControlState playerControl, DaggerfallGroundContainers groundContainers, DaggerfallUniqueItemAllocator uniqueItems,
    Func<IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta>> deltas,
    Action<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> replaceDelta, Func<long, bool> hasCorpse, Action<long, Action> receiveIntoCorpse)
{
    private sealed record RetainedQuestItem(DaggerfallItemOwner Owner, DaggerfallUniqueSave? Unique, DaggerfallStackSave? Stack)
    {
        internal DaggerfallItemInstanceMetadata Metadata => Unique is { } unique
            ? DaggerfallItemInstanceMetadata.Restore(unique.ItemId, unique.Metadata)
            : DaggerfallItemInstanceMetadata.Restore(Stack!.ItemId, Stack.Metadata);
        internal InventoryContainerSeed Seed => Unique is { } unique
            ? new(new(unique.ItemId), UniqueItem: new(DurableIdentityKind.Item, unique.EntityId), CapacityCosts: DaggerfallEncumbrancePolicy.CapacityOverride(unique.Metadata.WeightClassicUnits))
            : new(new(Stack!.ItemId), Stack.Quantity, Stack: InventoryStackId.Parse(Stack.StackId));
    }

    private static HashSet<string> RemovedEffectInstances(DaggerfallSiteRuntimeDelta delta, long actorId, IReadOnlySet<ulong> items) =>
        delta.Effects.Where(effect => effect.TargetId == actorId && effect.ItemId is ulong id && items.Contains(id))
            .Select(effect => effect.Instance).ToHashSet(StringComparer.Ordinal);

    /// <summary>Places a bound quest item at a position, rejoining retained site values with canonical live containment in one Engine admission.</summary>
    internal DaggerfallQuestResourceBinding PlaceBound(DaggerfallQuestResourceBinding binding, WorldPoint position) =>
        TransferBound(binding, (owners, transfer) =>
            binding.MovedTo(groundContainers.PlaceQuestItem(binding.UniqueItemIds, binding.StackOwners, position, owners, transfer)));

    private DaggerfallQuestResourceBinding TransferBound(DaggerfallQuestResourceBinding binding,
        Func<DaggerfallItemOwner[], Action<DaggerfallItemOwner>, DaggerfallQuestResourceBinding> admit)
    {
        List<RetainedQuestItem> retained = [];
        List<DaggerfallItemOwner> owners = [];
        foreach (ulong id in binding.UniqueItemIds)
        {
            if (itemInstances.ContainsUnique(id)) { owners.Add(itemInstances.RequireUnique(id).Owner); continue; }
            var matches = deltas().Values.SelectMany(delta => delta.ActorInventories.SelectMany(value => value.Inventory.UniqueItems)
                .Concat(delta.Corpses.SelectMany(value => value.UniqueItems)))
                .Concat(groundContainers.Unloaded.SelectMany(value => value.Inventory.UniqueItems)).Where(value => value.EntityId == id).ToArray();
            if (matches.Length != 1) throw new NotSupportedException($"Quest item {id} requires one retained physical owner, found {matches.Length}.");
            var item = matches[0];
            retained.Add(new(new(item.Metadata.Owner.Scope, item.Metadata.Owner.Id), item, null));
            owners.Add(retained[^1].Owner);
        }
        foreach (var value in binding.Stacks)
        {
            var owner = new DaggerfallItemOwner(value.Owner.Scope, value.Owner.Id);
            owners.Add(owner);
            var id = InventoryStackId.Parse(value.StackId);
            if (itemInstances.ContainsStack(owner, id)) continue;
            var matches = deltas().Values.SelectMany(delta => owner.Scope == "actor"
                ? delta.ActorInventories.Where(section => section.EntityId == owner.Id).SelectMany(section => section.Inventory.Stacks)
                : delta.Corpses.Where(corpse => owner.Scope == "corpse" && corpse.ActorId == owner.Id).SelectMany(corpse => corpse.Stacks))
                .Concat(groundContainers.Unloaded.Where(pile => owner.Scope == "ground" && pile.Id == owner.Id).SelectMany(pile => pile.Inventory.Stacks))
                .Where(stack => stack.StackId == value.StackId).ToArray();
            if (matches.Length != 1) throw new NotSupportedException($"Quest stack '{value.StackId}' requires one retained physical owner, found {matches.Length}.");
            retained.Add(new(owner, null, matches[0]));
        }
        // Cross-owner equal stack IDs can combine only equal meaning. Quantities remain Engine-owned.
        foreach (var group in binding.Stacks.GroupBy(value => value.StackId))
        {
            var metadata = group.Select(value => retained.SingleOrDefault(item => item.Owner.Scope == value.Owner.Scope && item.Owner.Id == value.Owner.Id && item.Stack?.StackId == value.StackId)?.Metadata
                ?? itemInstances.RequireStack(new(value.Owner.Scope, value.Owner.Id), InventoryStackId.Parse(value.StackId))).ToArray();
            if (metadata.Skip(1).Any(value => !metadata[0].IsStackCompatibleWith(value))) throw new InvalidOperationException("Quest placement would combine distinct stack meanings.");
        }
        var uniqueIds = retained.Where(value => value.Unique is not null).Select(value => value.Unique!.EntityId).ToHashSet();
        var stackIds = retained.Where(value => value.Stack is not null).Select(value => (value.Owner, value.Stack!.StackId)).ToHashSet();
        var movedUniqueIds = binding.UniqueItemIds.ToHashSet();
        var changed = deltas().Where(entry => entry.Value.Effects.Any(effect => effect.ItemId is ulong id && movedUniqueIds.Contains(id))
            || entry.Value.ActorInventories.Any(value => value.Inventory.UniqueItems.Any(item => uniqueIds.Contains(item.EntityId))
                || value.Inventory.Stacks.Any(item => stackIds.Contains((DaggerfallItemOwner.Actor(value.EntityId), item.StackId))))
            || entry.Value.Corpses.Any(value => value.UniqueItems.Any(item => uniqueIds.Contains(item.EntityId))
                || value.Stacks.Any(item => stackIds.Contains((DaggerfallItemOwner.Corpse(value.ActorId), item.StackId)))))
            .ToDictionary(entry => entry.Key, entry => entry.Value with
        {
            Effects = entry.Value.Effects.Where(effect => effect.ItemId is not ulong id || !movedUniqueIds.Contains(id)).ToArray(),
            Actors = entry.Value.Actors.Select(actor => actor with { Stats = DaggerfallStatsSaveBoundary.WithoutEffects(actor.Stats,
                RemovedEffectInstances(entry.Value, actor.EntityId, movedUniqueIds)) }).ToArray(),
            DynamicActors = entry.Value.DynamicActors.Select(actor => actor with { Stats = DaggerfallStatsSaveBoundary.WithoutEffects(actor.Stats,
                RemovedEffectInstances(entry.Value, actor.EntityId, movedUniqueIds)) }).ToArray(),
            ActorInventories = entry.Value.ActorInventories.Select(value => value with { Inventory = value.Inventory with
            {
                UniqueItems = value.Inventory.UniqueItems.Where(item => !uniqueIds.Contains(item.EntityId)).ToArray(),
                Equipment = value.Inventory.Equipment.Where(item => !uniqueIds.Contains(item.ItemEntityId)).ToArray(),
                Stacks = value.Inventory.Stacks.Where(item => !stackIds.Contains((DaggerfallItemOwner.Actor(value.EntityId), item.StackId))).ToArray(),
            } }).ToArray(),
            Corpses = entry.Value.Corpses.Select(value => value with
            {
                UniqueItems = value.UniqueItems.Where(item => !uniqueIds.Contains(item.EntityId)).ToArray(),
                Stacks = value.Stacks.Where(item => !stackIds.Contains((DaggerfallItemOwner.Corpse(value.ActorId), item.StackId))).ToArray(),
            }).ToArray(),
        });
        var seeds = retained.Where(value => value.Unique is not null).Select(value => value.Seed)
            .Concat(retained.Where(value => value.Stack is not null).GroupBy(value => value.Stack!.StackId)
                .Select(group => group.First().Seed with { Quantity = group.Aggregate(0UL, (sum, item) => checked(sum + item.Stack!.Quantity)) })).ToArray();
        return admit(owners.Distinct().ToArray(), destination =>
        {
            questItems.MoveBoundItem(binding, destination, seeds, stackIds);
            foreach (var item in retained)
            {
                if (item.Unique is { } unique) itemInstances.AdmitRetainedUnique(unique.EntityId, item.Metadata, destination);
                else itemInstances.AdmitRetainedStack(item.Owner, destination, InventoryStackId.Parse(item.Stack!.StackId), item.Metadata);
            }
            foreach (var delta in changed) replaceDelta(delta.Key, delta.Value);
            groundContainers.RemoveRetainedContents(uniqueIds, stackIds);
        });
    }

    /// <summary>Gives a quest item to a foe resource (each admitted foe in turn, from a durable queue) or to a person.</summary>
    internal DaggerfallQuestItemTransfer? Give(DaggerfallQuestRuntimeInstance instance, string symbol, string recipient, string operation, DaggerfallQuestItemTransfer? queued)
    {
        var item = instance.Resources.SingleOrDefault(resource => resource.Symbol == symbol && resource.SelectedItem is not null)
            ?? throw new NotSupportedException($"Give item requires selected Item '{symbol}'.");
        var target = instance.Resources.SingleOrDefault(resource => resource.Symbol == recipient && (resource.SelectedFoe is not null || resource.SelectedPerson is not null))
            ?? throw new NotSupportedException($"Give item requires an actor resource '{recipient}'.");
        if (target.SelectedFoe is not null)
        {
            if (queued is null)
            {
                if (questItems.Have(instance, symbol) && questItems.Take(instance, symbol) == DaggerfallQuestItemResult.Unavailable) return null;
                item = instance.Resources.Single(resource => resource.Symbol == symbol);
                queued = new(symbol, recipient, questItems.SnapshotItem(item), []);
            }
            if (item.SelectedItem!.Metadata.QuestId is null && queued.Prototype!.Metadata.QuestId is not null)
                queued = queued with { Prototype = queued.Prototype with { Metadata = queued.Prototype.Metadata with { QuestId = null, QuestItemSymbol = null } } };
            // The durable accepted queue also serves later spawn groups. Each admitted copy is
            // recorded before another can be issued; no live entity or inventory is duplicated.
            long? actorId = target.Binding.ActorIds.Where(id => !queued.Recipients.Contains(id) && !target.RemovedFoeIds.Contains(id)
                && actors.TryGet(id, out _) && (hasCorpse(id) || !actors.Get(id).IsDefeated)).Select(id => (long?)id).FirstOrDefault();
            if (actorId is { } id)
            {
                var owner = hasCorpse(id) ? DaggerfallItemOwner.Corpse(id) : DaggerfallItemOwner.Actor(id);
                void Admit() => questItems.AdmitItem(queued.Prototype!, owner, $"daggerfall.quest.gift.{Uri.EscapeDataString(instance.InstanceId)}.{Uri.EscapeDataString(operation)}.{id}");
                if (owner.Scope == "corpse") receiveIntoCorpse(id, Admit);
                else Admit();
                queued = queued with { Recipients = [.. queued.Recipients, id] };
            }
            return queued;
        }
        if (target.IsHidden || target.IsNpcDestroyed || target.Binding.ActorIds is not [var targetId]) return null;
        if (!actors.TryGet(targetId, out var actor))
        {
            if (actors.Entities.TryResolve(WorldRpg.Kit.Actors.ActorsState.Identity(targetId), out _))
                throw new NotSupportedException($"Give item recipient '{recipient}' is a noncombat projection without an inventory owner.");
            return null;
        }
        if (actor.IsDefeated && !hasCorpse(targetId)) return null;
        var destination = hasCorpse(targetId) ? DaggerfallItemOwner.Corpse(targetId) : DaggerfallItemOwner.Actor(targetId);
        if (questItems.Have(instance, symbol) && questItems.Take(instance, symbol) == DaggerfallQuestItemResult.Unavailable) return null;
        item = instance.Resources.Single(resource => resource.Symbol == symbol);
        DaggerfallQuestResourceBinding binding = item.Binding;
        void Transfer() => binding = item.Binding.Kind == DaggerfallQuestResourceBindingKind.Pending
            ? questItems.AdmitItem(item.SelectedItem!, destination, $"daggerfall.quest.{Uri.EscapeDataString(instance.InstanceId)}.{symbol}")
            : TransferBound(item.Binding, (_, transfer) =>
            {
                transfer(destination);
                return item.Binding.UniqueItemIds.Length > 0 ? item.Binding : item.Binding with
                { Stacks = item.Binding.Stacks.Select(stack => stack with { Owner = new(destination.Scope, destination.Id) }).Distinct().ToArray() };
            });
        if (destination.Scope == "corpse") receiveIntoCorpse(targetId, Transfer);
        else Transfer();
        quests.SetResource(instance.InstanceId, item with { Binding = binding });
        return new(symbol, recipient, null, [targetId]);
    }

    /// <summary>Puts a quest reward on the ground under the player, created or moved from where it is kept, and answers its pile.</summary>
    internal long? OfferReward(DaggerfallQuestRuntimeInstance instance, string symbol)
    {
        if (playerControl.Position is not { } position) return null;
        if (questItems.Have(instance, symbol) && questItems.Take(instance, symbol) == DaggerfallQuestItemResult.Unavailable) return null;
        var resource = instance.Resources.Single(value => value.Symbol == symbol && value.SelectedItem is not null);
        var binding = resource.Binding.Kind == DaggerfallQuestResourceBindingKind.Pending
            ? DaggerfallQuestResourceBinding.Placed(groundContainers.CreateQuestItem(resource.SelectedItem!, position, uniqueItems))
            : PlaceBound(resource.Binding, position);
        quests.SetResource(instance.InstanceId, resource with { Binding = binding });
        questItems.MakePermanent(instance, symbol);
        var owner = binding.UniqueItemIds.Length > 0 ? itemInstances.RequireUnique(binding.UniqueItemIds[0]).Owner
            : new DaggerfallItemOwner(binding.Stacks[0].Owner.Scope, binding.Stacks[0].Owner.Id);
        if (owner.Scope != "ground") throw new InvalidOperationException("Quest reward must be admitted to its real loot owner before success.");
        return owner.Id;
    }
}
