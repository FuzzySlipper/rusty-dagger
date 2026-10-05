using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
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

    /// <summary>Rejoins retained site values with canonical live containment in one Engine admission.</summary>
    private DaggerfallQuestResourceBinding PlaceBoundQuestItem(DaggerfallQuestResourceBinding binding, WorldPoint position) =>
        TransferBoundQuestItem(binding, (owners, transfer) => _groundContainers.PlaceQuestItem(binding, position, owners, transfer));

    private DaggerfallQuestResourceBinding TransferBoundQuestItem(DaggerfallQuestResourceBinding binding,
        Func<DaggerfallItemOwner[], Action<DaggerfallItemOwner>, DaggerfallQuestResourceBinding> admit)
    {
        List<RetainedQuestItem> retained = [];
        List<DaggerfallItemOwner> owners = [];
        foreach (ulong id in binding.UniqueItemIds)
        {
            if (State.ItemInstances.ContainsUnique(id)) { owners.Add(State.ItemInstances.RequireUnique(id).Owner); continue; }
            var matches = _sites.Deltas.Values.SelectMany(delta => delta.ActorInventories.SelectMany(value => value.Inventory.UniqueItems)
                .Concat(delta.Corpses.SelectMany(value => value.UniqueItems)))
                .Concat(_groundContainers.Unloaded.SelectMany(value => value.Inventory.UniqueItems)).Where(value => value.EntityId == id).ToArray();
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
            if (State.ItemInstances.ContainsStack(owner, id)) continue;
            var matches = _sites.Deltas.Values.SelectMany(delta => owner.Scope == "actor"
                ? delta.ActorInventories.Where(section => section.EntityId == owner.Id).SelectMany(section => section.Inventory.Stacks)
                : delta.Corpses.Where(corpse => owner.Scope == "corpse" && corpse.ActorId == owner.Id).SelectMany(corpse => corpse.Stacks))
                .Concat(_groundContainers.Unloaded.Where(pile => owner.Scope == "ground" && pile.Id == owner.Id).SelectMany(pile => pile.Inventory.Stacks))
                .Where(stack => stack.StackId == value.StackId).ToArray();
            if (matches.Length != 1) throw new NotSupportedException($"Quest stack '{value.StackId}' requires one retained physical owner, found {matches.Length}.");
            retained.Add(new(owner, null, matches[0]));
        }
        // Cross-owner equal stack IDs can combine only equal meaning. Quantities remain Engine-owned.
        foreach (var group in binding.Stacks.GroupBy(value => value.StackId))
        {
            var metadata = group.Select(value => retained.SingleOrDefault(item => item.Owner.Scope == value.Owner.Scope && item.Owner.Id == value.Owner.Id && item.Stack?.StackId == value.StackId)?.Metadata
                ?? State.ItemInstances.RequireStack(new(value.Owner.Scope, value.Owner.Id), InventoryStackId.Parse(value.StackId))).ToArray();
            if (metadata.Skip(1).Any(value => !metadata[0].IsStackCompatibleWith(value))) throw new InvalidOperationException("Quest placement would combine distinct stack meanings.");
        }
        var uniqueIds = retained.Where(value => value.Unique is not null).Select(value => value.Unique!.EntityId).ToHashSet();
        var stackIds = retained.Where(value => value.Stack is not null).Select(value => (value.Owner, value.Stack!.StackId)).ToHashSet();
        var movedUniqueIds = binding.UniqueItemIds.ToHashSet();
        var deltas = _sites.Deltas.Where(entry => entry.Value.Effects.Any(effect => effect.ItemId is ulong id && movedUniqueIds.Contains(id))
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
            State.QuestItems.MoveBoundItem(binding, destination, seeds, stackIds);
            foreach (var item in retained)
            {
                if (item.Unique is { } unique) State.ItemInstances.AdmitRetainedUnique(unique.EntityId, item.Metadata, destination);
                else State.ItemInstances.AdmitRetainedStack(item.Owner, destination, InventoryStackId.Parse(item.Stack!.StackId), item.Metadata);
            }
            foreach (var delta in deltas) _sites.ReplaceDelta(delta.Key, delta.Value);
            _groundContainers.RemoveRetainedContents(uniqueIds, stackIds);
        });
    }
}
