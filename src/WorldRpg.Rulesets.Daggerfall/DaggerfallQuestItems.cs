using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallQuestItemResult { Changed, AlreadyCarried, NotCarried, Unavailable }
internal sealed record DaggerfallQuestCustodySave(string InstanceId, long Id, DaggerfallInventorySave Inventory);
internal sealed record DaggerfallQuestCustody(string InstanceId, long Id, EntityId Owner);

/// <summary>Retains taken quest items in actual Engine inventories for identity-preserving reoffers.</summary>
internal sealed class DaggerfallQuestItems(DaggerfallState state, DurableIdentityAllocator identities,
    DaggerfallUniqueItemAllocator uniqueItems, Func<DaggerfallItemOwner, EntityId> ownerEntity, Action<ulong> destroyUnique, Action<DaggerfallItemOwner, InventoryStackId> consumeStack)
{
    private static readonly EntityTypeId CustodyType = new("daggerfall.quest-custody");
    private readonly Dictionary<string, DaggerfallQuestCustody> _custody = new(StringComparer.Ordinal);
    internal IEnumerable<DaggerfallQuestCustody> Custody => _custody.Values;

    private static string Symbol(string symbol) => DaggerfallQuestInstanceSave.Canonical(symbol, "quest item");
    private static DaggerfallQuestResourceState Resource(DaggerfallQuestRuntimeInstance instance, string symbol) =>
        instance.Resources.SingleOrDefault(value => value.Symbol == Symbol(symbol) && value.SelectedItem is not null)
        ?? throw new ArgumentException($"Quest '{instance.InstanceId}' has no selected Item '{symbol}'.");

    internal bool Have(DaggerfallQuestRuntimeInstance instance, string symbol)
    {
        _ = Resource(instance, symbol);
        return MatchingUnique(instance.InstanceId, Symbol(symbol), DaggerfallItemOwner.Player).Any()
            || MatchingStacks(instance.InstanceId, Symbol(symbol), DaggerfallItemOwner.Player).Any();
    }

    internal DaggerfallQuestItemResult Take(DaggerfallQuestRuntimeInstance instance, string symbol)
    {
        var resource = Resource(instance, symbol);
        symbol = Symbol(symbol);
        var unique = MatchingUnique(instance.InstanceId, symbol, DaggerfallItemOwner.Player).ToArray();
        var stacks = MatchingStacks(instance.InstanceId, symbol, DaggerfallItemOwner.Player).ToArray();
        if (unique.Length == 0 && stacks.Length == 0) return DaggerfallQuestItemResult.NotCarried;
        var custody = RequireCustody(instance.InstanceId);
        var primaryUnique = unique.Where(value => resource.Binding.UniqueItemIds.Contains(value.Key)).Take(1).ToArray();
        var primaryStack = stacks.Where(value => resource.Binding.Stacks.FirstOrDefault() is { } primary
            && primary.Owner.Scope == value.Owner.Scope && primary.Owner.Id == value.Owner.Id && primary.StackId == value.Stack.Value).Take(1).ToArray();
        Move(primaryUnique, primaryStack, DaggerfallItemOwner.Quest(custody.Id), unique.Except(primaryUnique).ToArray(), stacks.Except(primaryStack).ToArray());
        if (primaryUnique.Length == 1)
            SetResource(instance, resource with { SelectedItem = resource.SelectedItem! with { Metadata = primaryUnique[0].Value with { Owner = DaggerfallItemOwner.Player, HeldCast = null } } });
        else if (primaryStack.Length == 1)
        {
            var prototype = primaryStack[0];
            ulong quantity = state.Containers.Read(custody.Owner).Stacks.Single(value => value.Id == prototype.Stack).Quantity;
            var updated = instance.Resources.Single(value => value.Symbol == symbol);
            SetResource(instance, updated with { SelectedItem = resource.SelectedItem! with { Quantity = quantity, Metadata = prototype.Metadata with { Owner = DaggerfallItemOwner.Player, HeldCast = null } } });
        }
        return DaggerfallQuestItemResult.Changed;
    }

    internal DaggerfallQuestItemResult Get(DaggerfallQuestRuntimeInstance instance, string symbol)
    {
        var resource = Resource(instance, symbol);
        var created = resource.SelectedItem!;
        if (created.TemplateIndex != 276 && Have(instance, symbol))
        {
            _ = Take(instance, symbol);
            resource = Resource(instance, symbol);
            created = resource.SelectedItem!;
        }
        if (created.TemplateIndex == 276)
            return state.Currency.ReceiveGold(created.Quantity) ? DaggerfallQuestItemResult.Changed : DaggerfallQuestItemResult.Unavailable;
        // An already-carried canonical item needs no removal/recreation to be handed back.
        // Reoffers never reroll definitions or replace a live item's current meaning.
        if (resource.Binding.UniqueItemIds is [var id] && state.ItemInstances.ContainsUnique(id))
        {
            var item = state.ItemInstances.RequireUnique(id);
            if (item.Owner == DaggerfallItemOwner.Player) return DaggerfallQuestItemResult.AlreadyCarried;
            Move([new(id, item)], [], DaggerfallItemOwner.Player);
            return DaggerfallQuestItemResult.Changed;
        }
        if (resource.Binding.Stacks.Length > 0)
        {
            var stacks = resource.Binding.Stacks.Take(1).Select(value => (Owner: new DaggerfallItemOwner(value.Owner.Scope, value.Owner.Id), Stack: InventoryStackId.Parse(value.StackId)))
                .Where(value => value.Owner != DaggerfallItemOwner.Player)
                .Select(value => (value.Owner, value.Stack, Metadata: state.ItemInstances.RequireStack(value.Owner, value.Stack))).ToArray();
            if (stacks.Length == 0) return DaggerfallQuestItemResult.AlreadyCarried;
            Move([], stacks, DaggerfallItemOwner.Player);
            return DaggerfallQuestItemResult.Changed;
        }
        if (resource.Binding.Kind != DaggerfallQuestResourceBindingKind.Pending)
            return DaggerfallQuestItemResult.Unavailable; // Consumption is not a new virtual resource.
        var custody = RequireCustody(instance.InstanceId);
        var owner = DaggerfallItemOwner.Quest(custody.Id);
        InventoryStackId? stack = created.Stackable ? InventoryStackId.Parse($"daggerfall.quest.{custody.Id}.{Symbol(symbol)}") : null;
        DurableIdentityReference? identity = created.Stackable ? null : uniqueItems.AllocateReference();
        try { state.Containers.Seed(custody.Owner, [new(created.Item, created.Quantity, identity, stack)]); }
        catch
        {
            if (identity is { } issued) uniqueItems.Remove(issued);
            throw;
        }
        var metadata = created.Metadata with { Owner = owner };
        if (stack is not null)
        {
            state.ItemInstances.RegisterStack(owner, stack, metadata);
            SetResource(instance, resource with { Binding = DaggerfallQuestResourceBinding.Stack(new(owner.Scope, owner.Id), stack.Value) });
            Move([], [(owner, stack, metadata)], DaggerfallItemOwner.Player);
        }
        else
        {
            state.ItemInstances.RegisterUnique(identity!.Value.Value, metadata);
            SetResource(instance, resource with { Binding = DaggerfallQuestResourceBinding.UniqueItem(identity.Value.Value) });
            Move([new(identity.Value.Value, metadata)], [], DaggerfallItemOwner.Player);
        }
        return DaggerfallQuestItemResult.Changed;
    }

    internal DaggerfallQuestItemResult MakePermanent(DaggerfallQuestRuntimeInstance instance, string symbol)
    {
        var resource = Resource(instance, symbol);
        // The virtual prototype and all carried copies cease to be quest items. Ownership,
        // condition, enchantment and native containment remain unchanged.
        foreach (var item in MatchingUnique(instance.InstanceId, Symbol(symbol), DaggerfallItemOwner.Player).ToArray())
            state.ItemInstances.ReplaceUnique(item.Key, Permanent(item.Value));
        foreach (var stack in MatchingStacks(instance.InstanceId, Symbol(symbol), DaggerfallItemOwner.Player).ToArray())
            state.ItemInstances.ReplaceStack(stack.Owner, stack.Stack, Permanent(stack.Metadata));
        // A retained canonical prototype also carries the permanent meaning into a later Get.
        foreach (ulong id in resource.Binding.UniqueItemIds.Where(state.ItemInstances.ContainsUnique))
            state.ItemInstances.ReplaceUnique(id, Permanent(state.ItemInstances.RequireUnique(id)));
        foreach (var stack in resource.Binding.Stacks)
        {
            var owner = new DaggerfallItemOwner(stack.Owner.Scope, stack.Owner.Id);
            var id = InventoryStackId.Parse(stack.StackId);
            state.ItemInstances.ReplaceStack(owner, id, Permanent(state.ItemInstances.RequireStack(owner, id)));
        }
        SetResource(instance, resource with { SelectedItem = resource.SelectedItem! with { Metadata = Permanent(resource.SelectedItem.Metadata) } });
        return DaggerfallQuestItemResult.Changed;
    }

    private static DaggerfallItemInstanceMetadata Permanent(DaggerfallItemInstanceMetadata metadata) => metadata with { QuestId = null, QuestItemSymbol = null };
    private static void SetResource(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestResourceState resource) =>
        instance.Resources = instance.Resources.Select(value => value.Symbol == resource.Symbol ? resource : value).ToArray();
    private IEnumerable<KeyValuePair<ulong, DaggerfallItemInstanceMetadata>> MatchingUnique(string quest, string symbol, DaggerfallItemOwner owner) =>
        state.ItemInstances.UniqueItems.Where(value => value.Value.Owner == owner && value.Value.QuestId == quest && value.Value.QuestItemSymbol == symbol);
    private IEnumerable<(DaggerfallItemOwner Owner, InventoryStackId Stack, DaggerfallItemInstanceMetadata Metadata)> MatchingStacks(string quest, string symbol, DaggerfallItemOwner owner) =>
        state.ItemInstances.StackItems.Where(value => value.Owner == owner && value.Metadata.QuestId == quest && value.Metadata.QuestItemSymbol == symbol);

    private DaggerfallQuestCustody RequireCustody(string instanceId)
    {
        if (_custody.TryGetValue(instanceId, out var current)) return current;
        var identity = identities.Allocate(DurableIdentityKind.Container);
        var owner = state.Containers.Entities.Create(identity, CustodyType);
        state.Containers.RegisterOwner(owner);
        var custody = new DaggerfallQuestCustody(instanceId, checked((long)identity.Value), owner);
        _custody.Add(instanceId, custody);
        return custody;
    }

    private void Move(KeyValuePair<ulong, DaggerfallItemInstanceMetadata>[] unique,
        (DaggerfallItemOwner Owner, InventoryStackId Stack, DaggerfallItemInstanceMetadata Metadata)[] stacks, DaggerfallItemOwner destination,
        KeyValuePair<ulong, DaggerfallItemInstanceMetadata>[]? discardedUnique = null,
        (DaggerfallItemOwner Owner, InventoryStackId Stack, DaggerfallItemInstanceMetadata Metadata)[]? discardedStacks = null)
    {
        discardedUnique ??= [];
        discardedStacks ??= [];
        EntityId target = ownerEntity(destination);
        var quantities = stacks.Select(value => state.Containers.Read(ownerEntity(value.Owner)).Stacks.Single(stack => stack.Id == value.Stack).Quantity).ToArray();
        foreach (var stack in stacks) state.ItemInstances.EnsureTransferCompatible(stack.Owner, destination, stack.Stack, stack.Stack);
        // Equipment removal and every selected copy transfer publish together. A failure
        // leaves the player's inventory/equipment and metadata untouched.
        using (var edit = state.InventoryStore.Prepare())
        {
            foreach (var item in unique)
            {
                EntityId entity = state.Actors.Entities.Resolve(new(DurableIdentityKind.Item, item.Key));
                EntityId source = ownerEntity(item.Value.Owner);
                if (state.InventoryStore.TryGetEquipment(source, out var equipment) && equipment!.Assignments.Any(value => value.Item == entity))
                    edit.Unequip(source, entity);
                edit.TransferUnique(entity, source, target);
            }
            for (int index = 0; index < stacks.Length; index++)
                edit.TransferFungible(ownerEntity(stacks[index].Owner), target, stacks[index].Stack, quantities[index]);
            foreach (var item in discardedUnique)
            {
                var entity = state.Actors.Entities.Resolve(new(DurableIdentityKind.Item, item.Key));
                var source = ownerEntity(item.Value.Owner);
                if (state.InventoryStore.TryGetEquipment(source, out var equipment) && equipment!.Assignments.Any(value => value.Item == entity)) edit.Unequip(source, entity);
                edit.DestroyUnique(entity);
            }
            foreach (var stack in discardedStacks)
                edit.Consume(ownerEntity(stack.Owner), stack.Stack, state.Containers.Read(ownerEntity(stack.Owner)).Stacks.Single(value => value.Id == stack.Stack).Quantity);
            edit.Publish();
        }
        foreach (var item in unique) state.ItemInstances.MoveUnique(item.Key, destination);
        foreach (var stack in stacks) state.ItemInstances.TransferStack(stack.Owner, destination, stack.Stack, stack.Stack, true);
        foreach (var item in discardedUnique)
        {
            state.ItemInstances.RemoveUnique(item.Key);
            var identity = new DurableIdentityReference(DurableIdentityKind.Item, item.Key);
            state.Actors.Entities.Destroy(identity);
            uniqueItems.Remove(identity);
        }
        foreach (var stack in discardedStacks) state.ItemInstances.RemoveStack(stack.Owner, stack.Stack);
        state.HeldEnchantments.Refresh();
    }

    internal void RemoveCustody(string instanceId)
    {
        if (!_custody.Remove(instanceId, out var custody)) return;
        var owner = DaggerfallItemOwner.Quest(custody.Id);
        foreach (var item in state.ItemInstances.UniqueItems.Where(value => value.Value.Owner == owner).ToArray()) destroyUnique(item.Key);
        foreach (var stack in state.ItemInstances.StackItems.Where(value => value.Owner == owner).ToArray()) consumeStack(owner, stack.Stack);
        var identity = new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)custody.Id));
        state.Actors.Entities.Destroy(identity);
        identities.Remove(identity);
    }

    internal void Restore(IEnumerable<DaggerfallQuestCustodySave> saved)
    {
        foreach (var value in saved)
        {
            var owner = state.Containers.Entities.Create(new(DurableIdentityKind.Container, checked((ulong)value.Id)), CustodyType);
            state.Containers.RegisterOwner(owner);
            _custody.Add(value.InstanceId, new(value.InstanceId, value.Id, owner));
            var seeds = value.Inventory.Stacks.Select(stack => new InventoryContainerSeed(new(stack.ItemId), stack.Quantity, Stack: InventoryStackId.Parse(stack.StackId)))
                .Concat(value.Inventory.UniqueItems.Select(item => new InventoryContainerSeed(new(item.ItemId), UniqueItem: new(DurableIdentityKind.Item, item.EntityId)))).ToArray();
            if (seeds.Length > 0) state.Containers.Seed(owner, seeds);
            foreach (var stack in value.Inventory.Stacks) state.ItemInstances.RegisterStack(DaggerfallItemOwner.Quest(value.Id), InventoryStackId.Parse(stack.StackId), DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata));
            foreach (var item in value.Inventory.UniqueItems) state.ItemInstances.RegisterUnique(item.EntityId, DaggerfallItemInstanceMetadata.Restore(item.ItemId, item.Metadata));
        }
    }
}
