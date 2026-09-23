using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Transport;

/// <summary>One Engine-backed wagon owner and its durable Daggerfall identity.</summary>
internal sealed record DaggerfallWagon(long Id, EntityId Owner);

/// <summary>One current wagon save, including the same item meaning used by every other container.</summary>
internal sealed record DaggerfallWagonSave(long Id, DaggerfallInventorySave Inventory)
{
    internal DaggerfallWagonSave Validate()
    {
        if (Id <= 0) throw new ArgumentOutOfRangeException(nameof(Id));
        ArgumentNullException.ThrowIfNull(Inventory);
        Inventory.Validate();
        if (Inventory.Equipment.Length != 0)
            throw new ArgumentException("A wagon cannot carry equipped items.", nameof(Inventory));
        return this;
    }
}

/// <summary>
/// Daggerfall wagon policy over Kit's shared container lifecycle. The wagon has no product-side
/// contents mirror: Engine inventory owns quantities and containment, while item instances retain
/// Daggerfall metadata under the durable wagon owner.
/// </summary>
internal sealed class DaggerfallWagonStorage
{
    private static readonly EntityTypeId WagonContainerType = new("daggerfall.wagon-container");
    private readonly MechanicsInventoryContainerCoordinator _containers;
    private readonly DaggerfallItemInstances _instances;
    private readonly DaggerfallDefinitions _definitions;
    private readonly EntityId _player;
    private readonly DurableIdentityAllocator _identities;
    private readonly DaggerfallTransportTuning _tuning;
    private DaggerfallWagon? _wagon;

    internal DaggerfallWagonStorage(MechanicsInventoryContainerCoordinator containers, DaggerfallItemInstances instances,
        DaggerfallDefinitions definitions, EntityId player, DurableIdentityAllocator identities,
        DaggerfallTransportTuning? tuning = null)
    {
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        if (player.Value == 0) throw new ArgumentOutOfRangeException(nameof(player));
        _player = player;
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _tuning = (tuning ?? DaggerfallTransportTuning.Donor).Validate();
    }

    internal DaggerfallWagon? Current => _wagon;
    internal long? Id => _wagon?.Id;
    internal bool Exists => _wagon is not null;
    internal DaggerfallTransportTuning Tuning => _tuning;
    internal long CurrentWeightClassicUnits => _wagon is DaggerfallWagon wagon
        ? WeightClassicUnits(_containers.Read(wagon.Owner))
        : 0;

    /// <summary>Materializes the persistent owner when a cart first needs remote storage.</summary>
    internal DaggerfallWagon EnsureCreated()
    {
        if (_wagon is not null) return _wagon;
        DurableIdentityReference identity = _identities.Allocate(DurableIdentityKind.Container);
        long id;
        try
        {
            id = checked((long)identity.Value);
        }
        catch (OverflowException exception)
        {
            _identities.Remove(identity);
            throw new InvalidOperationException("A wagon identity cannot fit the Daggerfall durable identity range.", exception);
        }

        EntityId owner = _containers.Entities.Create(identity, WagonContainerType);
        try
        {
            _containers.RegisterOwner(owner);
            _wagon = new DaggerfallWagon(id, owner);
            return _wagon;
        }
        catch
        {
            _containers.Entities.Destroy(identity);
            _identities.Remove(identity);
            throw;
        }
    }

    internal InventoryView? Read() => _wagon is DaggerfallWagon wagon ? _containers.Read(wagon.Owner) : null;

    internal bool CanAccess(InventoryView player, DaggerfallTransportAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(player);
        context.Validate();
        return context.WagonAllowed(HasCart(player), _tuning.WagonAccessRange);
    }

    internal InventoryContainerTransferReceipt TransferToWagon(InventoryContainerSelection selection,
        DaggerfallTransportAccessContext context, ulong expectedWorldRevision)
    {
        ArgumentNullException.ThrowIfNull(selection);
        InventoryView playerBefore = _containers.Read(_player);
        EnsureAccessible(playerBefore, context);
        if (playerBefore.StoreRevision != expectedWorldRevision)
            throw new InvalidOperationException("Inventory changed. Choose the item again.");
        DaggerfallItemDefinition definition = RequireSelectionDefinition(selection, playerBefore);
        if (IsTransportation(definition))
            throw new InvalidOperationException("Transportation items cannot be stored in the wagon.");
        EnsureUniqueMetadata(selection, DaggerfallItemOwner.Player);
        EnsureWagonCapacity(selection, definition);
        DaggerfallWagon wagon = EnsureCreated();
        InventoryContainerSelection transferSelection = PrepareTransferSelection(selection, DaggerfallItemOwner.Wagon(wagon.Id), wagon.Owner);
        InventoryContainerTransferReceipt transfer = _containers.Transfer(_player, wagon.Owner, transferSelection,
            _containers.Read(_player).StoreRevision);
        SyncToWagon(transfer, wagon.Id);
        return transfer;
    }

    internal InventoryContainerTransferReceipt TransferFromWagon(InventoryContainerSelection selection,
        DaggerfallTransportAccessContext context, ulong expectedWorldRevision)
    {
        ArgumentNullException.ThrowIfNull(selection);
        DaggerfallWagon wagon = _wagon ?? throw new InvalidOperationException("The wagon has no materialized storage.");
        InventoryView playerBefore = _containers.Read(_player);
        EnsureAccessible(playerBefore, context);
        InventoryView wagonBefore = _containers.Read(wagon.Owner);
        if (playerBefore.StoreRevision != expectedWorldRevision)
            throw new InvalidOperationException("Inventory changed. Choose the item again.");
        _ = RequireSelectionDefinition(selection, wagonBefore);
        EnsureUniqueMetadata(selection, DaggerfallItemOwner.Wagon(wagon.Id));
        InventoryContainerSelection transferSelection = PrepareTransferSelection(selection, DaggerfallItemOwner.Player, _player);
        InventoryContainerTransferReceipt transfer = _containers.Transfer(wagon.Owner, _player, transferSelection,
            expectedWorldRevision);
        SyncFromWagon(transfer, wagon.Id);
        return transfer;
    }

    internal DaggerfallWagonSave? Capture()
    {
        if (_wagon is not DaggerfallWagon wagon) return null;
        (DaggerfallStackSave[] stacks, DaggerfallUniqueSave[] uniques) = CaptureContents(_containers.Read(wagon.Owner), DaggerfallItemOwner.Wagon(wagon.Id));
        return new DaggerfallWagonSave(wagon.Id, new DaggerfallInventorySave(stacks, uniques, [])).Validate();
    }

    /// <summary>Rebuilds the Engine owner and metadata from one current-schema save section.</summary>
    internal void Restore(DaggerfallWagonSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate();
        if (_wagon is not null) throw new InvalidOperationException("Wagon storage is already materialized.");
        DurableIdentityReference identity = new(DurableIdentityKind.Container, checked((ulong)saved.Id));
        if (_identities.Classify(identity) != DurableIdentityClassification.Live)
            throw new ArgumentException($"Saved wagon {saved.Id} is not live in the persisted identity ledger.", nameof(saved));
        DaggerfallItemOwner itemOwner = DaggerfallItemOwner.Wagon(saved.Id);
        InventoryStackId[] savedStacks = saved.Inventory.Stacks.Select(stack => InventoryStackId.Parse(stack.StackId)).ToArray();
        ulong[] savedUniqueItems = saved.Inventory.UniqueItems.Select(unique => unique.EntityId).ToArray();
        if (savedStacks.Any(stack => _instances.ContainsStack(itemOwner, stack))
            || savedUniqueItems.Any(_instances.ContainsUnique))
            throw new InvalidOperationException($"Wagon {saved.Id} item metadata is already materialized.");
        EntityId owner = _containers.Entities.Create(identity, WagonContainerType);
        bool seeded = false;
        try
        {
            _containers.RegisterOwner(owner);
            if (saved.Inventory.Stacks.Length != 0 || saved.Inventory.UniqueItems.Length != 0)
            {
                InventoryContainerSeed[] seeds = saved.Inventory.Stacks
                    .Select(stack => new InventoryContainerSeed(new InventoryItemId(stack.ItemId), stack.Quantity,
                        Stack: InventoryStackId.Parse(stack.StackId)))
                    .Concat(saved.Inventory.UniqueItems.Select(unique => new InventoryContainerSeed(
                        new InventoryItemId(unique.ItemId), UniqueItem: new DurableIdentityReference(DurableIdentityKind.Item, unique.EntityId))))
                    .ToArray();
                _containers.Seed(owner, seeds);
                seeded = true;
            }
            RegisterMetadata(saved);
            _wagon = new DaggerfallWagon(saved.Id, owner);
        }
        catch
        {
            foreach (InventoryStackId stack in savedStacks)
                if (_instances.ContainsStack(itemOwner, stack)) _instances.RemoveStack(itemOwner, stack);
            foreach (ulong unique in savedUniqueItems)
                if (_instances.ContainsUnique(unique)) _instances.RemoveUnique(unique);
            if (seeded)
                foreach (ulong unique in savedUniqueItems)
                    _containers.Entities.Destroy(new DurableIdentityReference(DurableIdentityKind.Item, unique));
            _containers.Entities.Destroy(identity);
            throw;
        }
    }

    private void EnsureAccessible(InventoryView player, DaggerfallTransportAccessContext context)
    {
        if (!CanAccess(player, context))
            throw new InvalidOperationException("The wagon is unavailable here. A cart and a reachable dungeon exit are required.");
    }

    private DaggerfallItemDefinition RequireSelectionDefinition(InventoryContainerSelection selection, InventoryView owner)
    {
        if (!_definitions.TryResolveItem(new DaggerfallItemId(selection.Item.Value), out DaggerfallItemDefinition definition))
            throw new InvalidOperationException($"Daggerfall does not define item '{selection.Item.Value}'.");
        if (selection.UniqueEntityId is ulong unique)
        {
            if (selection.Quantity != 1 || !owner.UniqueItems.Any(item => item.Entity.Value == unique && item.Definition.Value == selection.Item.Value))
                throw new InvalidOperationException("That unique item is no longer in this inventory.");
        }
        else if (selection.Stack is not InventoryStackId stack
            || !owner.Stacks.Any(item => item.Id == stack && item.Definition.Value == selection.Item.Value && item.Quantity >= selection.Quantity))
            throw new InvalidOperationException("That stack is no longer in this inventory with the requested quantity.");
        return definition;
    }

    private void EnsureWagonCapacity(InventoryContainerSelection selection, DaggerfallItemDefinition definition)
    {
        long currentWeight = _wagon is DaggerfallWagon current
            ? WeightClassicUnits(_containers.Read(current.Owner))
            : 0;
        long requestedWeight = checked((long)DaggerfallEncumbrancePolicy.ClassicWeightCost(definition) * checked((long)selection.Quantity));
        if (requestedWeight < 0 || currentWeight > _tuning.WagonCapacityClassicUnits - requestedWeight)
            throw new InvalidOperationException("The wagon cannot hold that much weight.");
    }

    private void EnsureUniqueMetadata(InventoryContainerSelection selection, DaggerfallItemOwner expectedOwner)
    {
        if (selection.UniqueEntityId is not ulong runtimeEntityId) return;
        ulong durableItemId = DurableItemId(runtimeEntityId);
        if (_instances.RequireUnique(durableItemId).Owner != expectedOwner)
            throw new InvalidOperationException("That unique item is not owned by the selected container.");
    }

    private InventoryContainerSelection PrepareTransferSelection(InventoryContainerSelection selection,
        DaggerfallItemOwner destinationOwner, EntityId destination)
    {
        if (selection.UniqueEntityId is not null) return selection;
        InventoryStackId source = selection.Stack ?? throw new ArgumentException("A wagon transfer requires a stack identity.", nameof(selection));
        DaggerfallItemOwner sourceOwner = destinationOwner == DaggerfallItemOwner.Wagon(_wagon?.Id ?? 0)
            ? DaggerfallItemOwner.Player
            : DaggerfallItemOwner.Wagon(_wagon?.Id ?? throw new InvalidOperationException("The wagon is not materialized."));
        InventoryView destinationView = _containers.Read(destination);
        InventoryStackId? compatible = destinationView.Stacks
            .OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Where(stack => _instances.RequireStack(destinationOwner, stack.Id) is DaggerfallItemInstanceMetadata metadata
                && _instances.RequireStack(sourceOwner, source).IsStackCompatibleWith(metadata))
            .Select(stack => stack.Id)
            .FirstOrDefault();
        InventoryStackId target = selection.DestinationStack ?? compatible
            ?? InventoryStackId.Parse($"daggerfall.wagon.{_wagon?.Id ?? 0}.{source.Value}");
        _instances.EnsureTransferCompatible(sourceOwner, destinationOwner, source, target);
        return selection with { DestinationStack = target };
    }

    private void SyncToWagon(InventoryContainerTransferReceipt transfer, long wagonId)
    {
        InventoryView playerAfter = _containers.Read(_player);
        foreach (InventoryContainerStackTransfer stack in transfer.Stacks)
        {
            bool exhausted = !playerAfter.Stacks.Any(value => value.Id == stack.SourceStack);
            _instances.TransferStack(DaggerfallItemOwner.Player, DaggerfallItemOwner.Wagon(wagonId),
                stack.SourceStack, stack.DestinationStack, exhausted);
        }
        foreach (InventoryContainerUniqueTransfer unique in transfer.UniqueItems)
            _instances.MoveUnique(DurableItemId(unique.EntityId), DaggerfallItemOwner.Wagon(wagonId));
    }

    private void SyncFromWagon(InventoryContainerTransferReceipt transfer, long wagonId)
    {
        DaggerfallWagon wagon = _wagon ?? throw new InvalidOperationException("The wagon is not materialized.");
        InventoryView wagonAfter = _containers.Read(wagon.Owner);
        foreach (InventoryContainerStackTransfer stack in transfer.Stacks)
        {
            bool exhausted = !wagonAfter.Stacks.Any(value => value.Id == stack.SourceStack);
            _instances.TransferStack(DaggerfallItemOwner.Wagon(wagonId), DaggerfallItemOwner.Player,
                stack.SourceStack, stack.DestinationStack, exhausted);
        }
        foreach (InventoryContainerUniqueTransfer unique in transfer.UniqueItems)
            _instances.MoveUnique(DurableItemId(unique.EntityId), DaggerfallItemOwner.Player);
    }

    private void RegisterMetadata(DaggerfallWagonSave saved)
    {
        DaggerfallItemOwner owner = DaggerfallItemOwner.Wagon(saved.Id);
        foreach (DaggerfallStackSave stack in saved.Inventory.Stacks)
        {
            DaggerfallItemInstanceMetadata metadata = DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata);
            if (metadata.Owner != owner)
                throw new ArgumentException($"Saved wagon stack '{stack.StackId}' has the wrong item owner.", nameof(saved));
            _instances.RegisterStack(owner, InventoryStackId.Parse(stack.StackId), metadata);
        }
        foreach (DaggerfallUniqueSave unique in saved.Inventory.UniqueItems)
        {
            DaggerfallItemInstanceMetadata metadata = DaggerfallItemInstanceMetadata.Restore(unique.ItemId, unique.Metadata);
            if (metadata.Owner != owner)
                throw new ArgumentException($"Saved wagon item '{unique.EntityId}' has the wrong item owner.", nameof(saved));
            _instances.RegisterUnique(unique.EntityId, metadata);
        }
    }

    private long WeightClassicUnits(InventoryView inventory)
    {
        long weight = 0;
        foreach (InventoryStack stack in inventory.Stacks)
        {
            DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(stack.Definition.Value));
            weight = checked(weight + checked((long)DaggerfallEncumbrancePolicy.ClassicWeightCost(definition) * checked((long)stack.Quantity)));
        }
        foreach (Rusty.Engine.Mechanics.UniqueInventoryItem item in inventory.UniqueItems)
        {
            DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(item.Definition.Value));
            weight = checked(weight + checked((long)DaggerfallEncumbrancePolicy.ClassicWeightCost(definition)));
        }
        return weight;
    }

    private static bool HasCart(InventoryView inventory) => inventory.UniqueItems.Any(item => item.Definition.Value == DaggerfallTransportPolicy.CartItemId);

    private static bool IsTransportation(DaggerfallItemDefinition definition) =>
        definition.Template?.Groups.Contains("Transportation", StringComparer.Ordinal) == true;

    private (DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems) CaptureContents(InventoryView inventory, DaggerfallItemOwner owner)
    {
        DaggerfallStackSave[] stacks = inventory.Stacks.OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Select(stack => new DaggerfallStackSave(stack.Id.Value, stack.Definition.Value, stack.Quantity,
                _instances.RequireStack(owner, stack.Id).Capture()))
            .ToArray();
        DaggerfallUniqueSave[] uniques = inventory.UniqueItems
            .Select(item =>
            {
                ulong durableItemId = DurableItemId(item.Entity.Value);
                return new DaggerfallUniqueSave(item.Definition.Value, durableItemId,
                    _instances.RequireUnique(durableItemId).Capture());
            })
            .OrderBy(item => item.EntityId)
            .ToArray();
        return (stacks, uniques);
    }

    private ulong DurableItemId(ulong runtimeEntityId)
    {
        DurableIdentityReference identity = _containers.Entities.IdentityOf(new EntityId(runtimeEntityId));
        if (identity.Kind != DurableIdentityKind.Item)
            throw new InvalidOperationException($"Inventory entity {runtimeEntityId} does not carry a durable item identity.");
        return identity.Value;
    }
}
