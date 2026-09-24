using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Property;

/// <summary>
/// Engine-backed property containers keyed by durable property identity. The Engine owns contents;
/// this coordinator only materializes the named owner, transfers through Kit, and records Daggerfall
/// item meaning under the property owner scope.
/// </summary>
internal sealed class DaggerfallPropertyStorage
{
    internal static readonly EntityTypeId PropertyContainerType = new("daggerfall.property-container");

    private readonly MechanicsInventoryContainerCoordinator _containers;
    private readonly DaggerfallItemInstances _instances;
    private readonly DaggerfallDefinitions _definitions;
    private readonly DurableIdentityAllocator _identities;
    private readonly DaggerfallPropertyState _state;
    private readonly EntityId _player;
    private readonly Dictionary<DaggerfallPropertyStorageKey, Binding> _bindings = [];

    internal DaggerfallPropertyStorage(MechanicsInventoryContainerCoordinator containers,
        DaggerfallItemInstances instances, DaggerfallDefinitions definitions, DurableIdentityAllocator identities,
        DaggerfallPropertyState state, EntityId player)
    {
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        if (player.Value == 0) throw new ArgumentOutOfRangeException(nameof(player));
        _player = player;
    }

    internal IReadOnlyList<DaggerfallPropertyStorageKey> MaterializedKeys =>
        Array.AsReadOnly(_bindings.Keys.OrderBy(key => key.Value, StringComparer.Ordinal).ToArray());

    /// <summary>Creates empty owners for newly purchased properties on first access.</summary>
    internal EntityId Ensure(DaggerfallPropertyStorageKey key)
    {
        key.Validate();
        if (_bindings.TryGetValue(key, out Binding? binding)) return binding.Owner;
        if (_state.TryGetStorageSave(key, out DaggerfallPropertyStorageSave saved))
            return MaterializeSaved(saved);

        DurableIdentityReference identity = _identities.Allocate(DurableIdentityKind.Container);
        long id = ToContainerId(identity);
        EntityId owner = _containers.Entities.Create(identity, PropertyContainerType);
        try
        {
            _containers.RegisterOwner(owner);
            _state.BindStorage(key, id);
            _bindings.Add(key, new Binding(id, owner));
            return owner;
        }
        catch
        {
            if (_containers.Entities.Store.IsAlive(owner)) _containers.Entities.Destroy(identity);
            _identities.Remove(identity);
            throw;
        }
    }

    internal bool TryGet(DaggerfallPropertyStorageKey key, out EntityId owner)
    {
        key.Validate();
        if (_bindings.TryGetValue(key, out Binding? binding))
        {
            owner = binding.Owner;
            return true;
        }
        owner = default;
        return false;
    }

    internal InventoryView Read(DaggerfallPropertyStorageKey key) => _containers.Read(Ensure(key));

    /// <summary>Restores every saved property container and its Engine-backed contents.</summary>
    internal void Restore(DaggerfallPropertySave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate();
        if (_bindings.Count != 0)
            throw new InvalidOperationException("Property storage is already materialized.");
        foreach (DaggerfallPropertyStorageSave storage in saved.Storage)
            _ = MaterializeSaved(storage);
    }

    /// <summary>Captures live Kit contents, retaining a saved snapshot for an unmaterialized owner.</summary>
    internal DaggerfallInventorySave Capture(DaggerfallPropertyStorageKey key)
    {
        key.Validate();
        if (!_bindings.TryGetValue(key, out Binding? binding))
            return _state.TryGetStorageSave(key, out DaggerfallPropertyStorageSave saved)
                ? saved.Inventory
                : new DaggerfallInventorySave([], [], []);
        return CaptureContents(binding.Owner, binding.Id);
    }

    internal InventoryContainerTransferReceipt TransferTo(DaggerfallPropertyStorageKey key,
        InventoryContainerSelection selection, ulong expectedWorldRevision)
    {
        ArgumentNullException.ThrowIfNull(selection);
        EntityId destination = Ensure(key);
        InventoryView player = _containers.Read(_player);
        ValidateSelection(selection, player);
        EnsureUniqueMetadata(selection, DaggerfallItemOwner.Player);
        long propertyId = RequirePropertyContainerId(destination);
        InventoryContainerSelection prepared = PrepareTransferSelection(selection, DaggerfallItemOwnerFor(key), destination,
            DaggerfallItemOwner.Player, propertyId);
        InventoryContainerTransferReceipt transfer = _containers.Transfer(_player, destination, prepared, expectedWorldRevision);
        SyncToProperty(transfer, key);
        return transfer;
    }

    internal InventoryContainerTransferReceipt TransferFrom(DaggerfallPropertyStorageKey key,
        InventoryContainerSelection selection, ulong expectedWorldRevision)
    {
        ArgumentNullException.ThrowIfNull(selection);
        EntityId source = Ensure(key);
        InventoryView property = _containers.Read(source);
        ValidateSelection(selection, property);
        EnsureUniqueMetadata(selection, DaggerfallItemOwnerFor(key));
        long propertyId = RequirePropertyContainerId(source);
        InventoryContainerSelection prepared = PrepareTransferSelection(selection, DaggerfallItemOwner.Player, _player,
            DaggerfallItemOwnerFor(key), propertyId);
        InventoryContainerTransferReceipt transfer = _containers.Transfer(source, _player, prepared, expectedWorldRevision);
        SyncFromProperty(transfer, key);
        return transfer;
    }

    private EntityId MaterializeSaved(DaggerfallPropertyStorageSave saved)
    {
        saved.Validate();
        DaggerfallPropertyStorageKey key = new DaggerfallPropertyStorageKey(saved.Key).Validate();
        if (_bindings.ContainsKey(key))
            throw new InvalidOperationException($"Property storage key '{key.Value}' is already materialized.");
        DurableIdentityReference identity = new(DurableIdentityKind.Container, checked((ulong)saved.ContainerId));
        if (_identities.Classify(identity) != DurableIdentityClassification.Live)
            throw new ArgumentException($"Saved property storage {saved.ContainerId} is not live in the persisted identity ledger.", nameof(saved));
        DaggerfallItemOwner itemOwner = DaggerfallItemOwnerFor(saved.ContainerId);
        InventoryStackId[] savedStacks = saved.Inventory.Stacks.Select(stack => InventoryStackId.Parse(stack.StackId)).ToArray();
        ulong[] savedUniqueItems = saved.Inventory.UniqueItems.Select(unique => unique.EntityId).ToArray();
        if (savedStacks.Any(stack => _instances.ContainsStack(itemOwner, stack))
            || savedUniqueItems.Any(_instances.ContainsUnique))
            throw new InvalidOperationException($"Property storage {saved.ContainerId} item metadata is already materialized.");

        EntityId owner = _containers.Entities.Create(identity, PropertyContainerType);
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
                        new InventoryItemId(unique.ItemId), UniqueItem: new DurableIdentityReference(
                            DurableIdentityKind.Item, unique.EntityId))))
                    .ToArray();
                _containers.Seed(owner, seeds);
                seeded = true;
            }
            RegisterMetadata(saved, itemOwner);
            _state.BindStorage(key, saved.ContainerId);
            _bindings.Add(key, new Binding(saved.ContainerId, owner));
            return owner;
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
            if (_containers.Entities.Store.IsAlive(owner)) _containers.Entities.Destroy(identity);
            throw;
        }
    }

    private void RegisterMetadata(DaggerfallPropertyStorageSave saved, DaggerfallItemOwner owner)
    {
        foreach (DaggerfallStackSave stack in saved.Inventory.Stacks)
        {
            DaggerfallItemInstanceMetadata metadata = DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata);
            if (metadata.Owner != owner)
                throw new ArgumentException($"Saved property stack '{stack.StackId}' has the wrong item owner.", nameof(saved));
            _instances.RegisterStack(owner, InventoryStackId.Parse(stack.StackId), metadata);
        }
        foreach (DaggerfallUniqueSave unique in saved.Inventory.UniqueItems)
        {
            DaggerfallItemInstanceMetadata metadata = DaggerfallItemInstanceMetadata.Restore(unique.ItemId, unique.Metadata);
            if (metadata.Owner != owner)
                throw new ArgumentException($"Saved property item '{unique.EntityId}' has the wrong item owner.", nameof(saved));
            _instances.RegisterUnique(unique.EntityId, metadata);
        }
    }

    private DaggerfallInventorySave CaptureContents(EntityId owner, long containerId)
    {
        DaggerfallItemOwner itemOwner = DaggerfallItemOwnerFor(containerId);
        InventoryView contents = _containers.Read(owner);
        DaggerfallStackSave[] stacks = contents.Stacks.OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Select(stack => new DaggerfallStackSave(stack.Id.Value, stack.Definition.Value, stack.Quantity,
                _instances.RequireStack(itemOwner, stack.Id).Capture())).ToArray();
        DaggerfallUniqueSave[] uniques = contents.UniqueItems.OrderBy(item => item.Entity.Value)
            .Select(item =>
            {
                ulong identity = _containers.Entities.IdentityOf(item.Entity).Value;
                return new DaggerfallUniqueSave(item.Definition.Value, identity, _instances.RequireUnique(identity).Capture());
            }).ToArray();
        return new DaggerfallInventorySave(stacks, uniques, []);
    }

    private void ValidateSelection(InventoryContainerSelection selection, InventoryView owner)
    {
        _ = RequireSelectionDefinition(selection, owner);
    }

    private DaggerfallItemDefinition RequireSelectionDefinition(InventoryContainerSelection selection, InventoryView owner)
    {
        if (!_definitions.TryResolveItem(new DaggerfallItemId(selection.Item.Value), out DaggerfallItemDefinition definition))
            throw new InvalidOperationException($"Daggerfall does not define item '{selection.Item.Value}'.");
        if (selection.UniqueEntityId is ulong unique)
        {
            if (selection.Quantity != 1 || !owner.UniqueItems.Any(item => item.Entity.Value == unique && item.Definition.Value == selection.Item.Value))
                throw new InvalidOperationException("That unique item is no longer in this property container.");
        }
        else if (selection.Stack is not InventoryStackId stack
            || !owner.Stacks.Any(item => item.Id == stack && item.Definition.Value == selection.Item.Value && item.Quantity >= selection.Quantity))
            throw new InvalidOperationException("That stack is no longer in this property container with the requested quantity.");
        return definition;
    }

    private InventoryContainerSelection PrepareTransferSelection(InventoryContainerSelection selection,
        DaggerfallItemOwner destinationOwner, EntityId destination, DaggerfallItemOwner sourceOwner,
        long propertyId)
    {
        if (selection.UniqueEntityId is not null) return selection;
        InventoryStackId source = selection.Stack ?? throw new ArgumentException("A property transfer requires a stack identity.", nameof(selection));
        InventoryView destinationView = _containers.Read(destination);
        InventoryStackId? compatible = destinationView.Stacks
            .OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Where(stack => _instances.RequireStack(destinationOwner, stack.Id) is DaggerfallItemInstanceMetadata metadata
                && _instances.RequireStack(sourceOwner, source).IsStackCompatibleWith(metadata))
            .Select(stack => stack.Id)
            .FirstOrDefault();
        InventoryStackId target = selection.DestinationStack ?? compatible
            ?? InventoryStackId.Parse($"daggerfall.property.{propertyId}.{source.Value}");
        _instances.EnsureTransferCompatible(sourceOwner, destinationOwner, source, target);
        return selection with { DestinationStack = target };
    }

    private void SyncToProperty(InventoryContainerTransferReceipt transfer, DaggerfallPropertyStorageKey key)
    {
        long id = RequirePropertyContainerId(Ensure(key));
        DaggerfallItemOwner propertyOwner = DaggerfallItemOwnerFor(id);
        InventoryView playerAfter = _containers.Read(_player);
        foreach (InventoryContainerStackTransfer stack in transfer.Stacks)
        {
            bool exhausted = !playerAfter.Stacks.Any(value => value.Id == stack.SourceStack);
            _instances.TransferStack(DaggerfallItemOwner.Player, propertyOwner, stack.SourceStack, stack.DestinationStack, exhausted);
        }
        foreach (InventoryContainerUniqueTransfer unique in transfer.UniqueItems)
            _instances.MoveUnique(DurableItemId(unique.EntityId), propertyOwner);
    }

    private void SyncFromProperty(InventoryContainerTransferReceipt transfer, DaggerfallPropertyStorageKey key)
    {
        long id = RequirePropertyContainerId(Ensure(key));
        DaggerfallItemOwner propertyOwner = DaggerfallItemOwnerFor(id);
        InventoryView propertyAfter = _containers.Read(Ensure(key));
        foreach (InventoryContainerStackTransfer stack in transfer.Stacks)
        {
            bool exhausted = !propertyAfter.Stacks.Any(value => value.Id == stack.SourceStack);
            _instances.TransferStack(propertyOwner, DaggerfallItemOwner.Player, stack.SourceStack, stack.DestinationStack, exhausted);
        }
        foreach (InventoryContainerUniqueTransfer unique in transfer.UniqueItems)
            _instances.MoveUnique(DurableItemId(unique.EntityId), DaggerfallItemOwner.Player);
    }

    private void EnsureUniqueMetadata(InventoryContainerSelection selection, DaggerfallItemOwner expectedOwner)
    {
        if (selection.UniqueEntityId is not ulong runtimeEntityId) return;
        if (_instances.RequireUnique(DurableItemId(runtimeEntityId)).Owner != expectedOwner)
            throw new InvalidOperationException("That unique item is not owned by the selected property container.");
    }

    private ulong DurableItemId(ulong runtimeEntityId)
    {
        DurableIdentityReference identity = _containers.Entities.IdentityOf(new EntityId(runtimeEntityId));
        if (identity.Kind != DurableIdentityKind.Item)
            throw new InvalidOperationException($"Inventory entity {runtimeEntityId} does not carry a durable item identity.");
        return identity.Value;
    }

    private long RequirePropertyContainerId(EntityId owner)
    {
        Binding? binding = _bindings.Values.SingleOrDefault(value => value.Owner == owner);
        return binding?.Id ?? throw new InvalidOperationException($"Entity {owner.Value} is not a materialized property container.");
    }

    private long ToContainerId(DurableIdentityReference identity)
    {
        try { return checked((long)identity.Value); }
        catch (OverflowException exception)
        {
            _identities.Remove(identity);
            throw new InvalidOperationException("A property container identity cannot fit the Daggerfall durable identity range.", exception);
        }
    }

    private static DaggerfallItemOwner DaggerfallItemOwnerFor(long id) => DaggerfallItemOwner.Property(id);

    private DaggerfallItemOwner DaggerfallItemOwnerFor(DaggerfallPropertyStorageKey key) =>
        DaggerfallItemOwnerFor(RequirePropertyContainerId(Ensure(key)));

    private sealed record Binding(long Id, EntityId Owner);
}
