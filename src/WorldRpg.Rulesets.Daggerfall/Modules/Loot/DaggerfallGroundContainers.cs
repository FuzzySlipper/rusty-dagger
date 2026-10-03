using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Loot;

/// <summary>One durable dropped-item container. Its contents remain in the shared Engine inventory store.</summary>
internal sealed record DaggerfallGroundContainer(DaggerfallWorldProfileKey Profile, long Id, EntityId Owner, WorldPoint Position);

/// <summary>
/// Daggerfall policy for ground piles. Kit's container coordinator owns every transfer; this owner
/// supplies the durable container identity, position, and Daggerfall metadata move after commit.
/// </summary>
internal sealed class DaggerfallGroundContainers
{
    private static readonly EntityTypeId GroundContainerType = new("daggerfall.ground-container");
    private readonly MechanicsInventoryContainerCoordinator _containers;
    private readonly DaggerfallItemInstances _instances;
    private readonly EntityId _player;
    private readonly DurableIdentityAllocator _identities;
    private readonly Dictionary<long, DaggerfallGroundContainer> _ground = [];
    private DaggerfallWorldProfileKey _activeProfile;

    internal DaggerfallGroundContainers(MechanicsInventoryContainerCoordinator containers, DaggerfallItemInstances instances,
        EntityId player, DurableIdentityAllocator identities, DaggerfallWorldProfileKey activeProfile)
    {
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        if (player.Value == 0) throw new ArgumentOutOfRangeException(nameof(player));
        _player = player;
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _activeProfile = activeProfile.Validate();
    }

    /// <summary>Only piles belonging to the profile currently projected by the session are interactable.</summary>
    internal IReadOnlyDictionary<long, DaggerfallGroundContainer> All => _ground.Values
        .Where(container => container.Profile == _activeProfile)
        .ToDictionary(container => container.Id);

    internal void RebaseActive(System.Numerics.Vector3 delta)
    {
        foreach (DaggerfallGroundContainer container in All.Values)
            _ground[container.Id] = container with { Position = DaggerfallExteriorSessionOrigin.Shift(container.Position, delta) };
    }
    /// <summary>All loaded pile state for save capture. Off-profile owners remain Engine-backed but inaccessible.</summary>
    internal IReadOnlyCollection<DaggerfallGroundContainer> Persisted => _ground.Values.ToArray();

    internal void SwitchProfile(DaggerfallWorldProfileKey profile) => _activeProfile = profile.Validate();

    /// <summary>Moves a current player selection into a new, positioned world pile.</summary>
    internal DaggerfallGroundContainer Drop(InventoryContainerSelection selection, WorldPoint position, ulong expectedWorldRevision)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentOutOfRangeException(nameof(position), "Ground placement must be finite.");
        // Validate the selected live source before allocating an irreversible durable container identity.
        InventoryView playerBefore = ValidatePlayerSelection(selection);
        if (playerBefore.StoreRevision != expectedWorldRevision)
            throw new InvalidOperationException("Inventory changed. Choose the item again.");
        DurableIdentityReference identity = _identities.Allocate(DurableIdentityKind.Container);
        InventoryContainerSelection transferSelection = selection;
        if (selection.Stack is InventoryStackId source && selection.DestinationStack is null
            && playerBefore.Stacks.Single(stack => stack.Id == source).Quantity > selection.Quantity)
        {
            transferSelection = selection with
            {
                DestinationStack = InventoryStackId.Parse($"daggerfall.ground.drop.{identity.Value}.{source.Value}"),
            };
        }
        EntityId owner = _containers.Entities.Create(identity, GroundContainerType);
        try
        {
            _containers.RegisterOwner(owner);
            // The caller's expected revision was checked before allocation.
            InventoryContainerTransferReceipt transfer = _containers.Transfer(_player, owner, transferSelection);
            SyncFromPlayer(transfer, identity.Value);
            return _ground.AddAndReturn(checked((long)identity.Value), new DaggerfallGroundContainer(_activeProfile, checked((long)identity.Value), owner, position));
        }
        catch
        {
            // No inventory mutation has committed when this path is reached before Transfer publishes.
            // A committed transfer has no fallible bookkeeping after SyncFromPlayer.
            _containers.Entities.Destroy(identity);
            throw;
        }
    }

    internal bool TryGet(long id, out DaggerfallGroundContainer container) => _ground.TryGetValue(id, out container!)
        && container.Profile == _activeProfile;

    internal InventoryView? Read(long id) => TryGet(id, out DaggerfallGroundContainer? container)
        ? _containers.Read(container.Owner) : null;

    /// <summary>Creates a selected quest item directly in the actual ground inventory owner.</summary>
    internal DaggerfallQuestResourceBinding CreateQuestItem(DaggerfallCreatedItem created, WorldPoint position,
        DaggerfallUniqueItemAllocator uniqueItems)
    {
        var containerIdentity = _identities.Allocate(DurableIdentityKind.Container);
        long id = checked((long)containerIdentity.Value);
        var metadata = created.Metadata with { Owner = DaggerfallItemOwner.Ground(id) };
        InventoryStackId? stack = created.Stackable ? InventoryStackId.Parse($"daggerfall.quest.ground.{id}") : null;
        DurableIdentityReference? unique = null;
        bool seeded = false;
        try
        {
            var owner = _containers.Entities.Create(containerIdentity, GroundContainerType);
            _containers.RegisterOwner(owner);
            unique = created.Stackable ? null : uniqueItems.AllocateReference();
            _containers.Seed(owner, [new(created.Item, created.Quantity, Stack: stack, UniqueItem: unique)]);
            seeded = true;
            DaggerfallQuestResourceBinding binding;
            if (stack is not null)
            {
                _instances.RegisterStack(metadata.Owner, stack, metadata);
                binding = DaggerfallQuestResourceBinding.Stack(new(metadata.Owner.Scope, metadata.Owner.Id), stack.Value);
            }
            else
            {
                _instances.RegisterUnique(unique!.Value.Value, metadata);
                binding = DaggerfallQuestResourceBinding.UniqueItem(unique.Value.Value);
            }
            _ground.Add(id, new(_activeProfile, id, owner, position));
            return binding;
        }
        catch
        {
            // Seed cleans its own unpublished candidate; after publication retire contents
            // through the same Engine store before releasing their product identities.
            if (seeded && _containers.Entities.TryResolve(containerIdentity, out var owner))
            {
                var inventory = _containers.Entities.Store.Get<InventoryComponent>(owner);
                if (unique is { } item && _containers.Entities.TryResolve(item, out var entity))
                {
                    using var edit = inventory.Store.Prepare();
                    edit.DestroyUnique(entity);
                    edit.Publish();
                    _containers.Entities.Destroy(item);
                }
                else if (stack is not null)
                    inventory.Consume(stack, created.Quantity);
            }
            if (stack is not null && _instances.ContainsStack(metadata.Owner, stack)) _instances.RemoveStack(metadata.Owner, stack);
            if (unique is { } identity)
            {
                if (_instances.ContainsUnique(identity.Value)) _instances.RemoveUnique(identity.Value);
                uniqueItems.Remove(identity);
            }
            _containers.Entities.Destroy(containerIdentity);
            _identities.Remove(containerIdentity);
            throw;
        }
    }

    internal void RelocateQuestItem(DaggerfallQuestResourceBinding binding, WorldPoint position)
    {
        if (binding.UniqueItemIds.Length == 1 && !_instances.ContainsUnique(binding.UniqueItemIds[0])
            || binding.UniqueItemIds.Length == 0 && binding.Stacks.Length == 0)
            throw new NotSupportedException("A consumed quest item has no physical owner to place again.");
        DaggerfallItemOwner[] owners = binding.UniqueItemIds.Length == 1
            ? [_instances.RequireUnique(binding.UniqueItemIds[0]).Owner]
            : binding.Stacks.Select(stack => new DaggerfallItemOwner(stack.Owner.Scope, stack.Owner.Id)).Distinct().ToArray();
        if (owners.Any(owner => owner.Scope != "ground" || !_ground.ContainsKey(owner.Id)))
            throw new NotSupportedException("A quest item can be placed again only while all its canonical owners are ground piles.");
        foreach (var owner in owners)
        {
            var container = _ground[owner.Id];
            _ground[container.Id] = container with { Profile = _activeProfile, Position = position };
        }
    }

    internal InventoryStackId ResolveTakeDestination(long id, InventoryStackId source, InventoryStackId freshDestination)
    {
        DaggerfallItemInstanceMetadata metadata = _instances.RequireStack(DaggerfallItemOwner.Ground(id), source);
        return _containers.Read(_player).Stacks.OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Select(stack => (stack.Id, Metadata: _instances.RequireStack(DaggerfallItemOwner.Player, stack.Id)))
            .Where(candidate => metadata.IsStackCompatibleWith(candidate.Metadata))
            .Select(candidate => candidate.Id).FirstOrDefault() ?? freshDestination;
    }

    internal InventoryContainerTransferReceipt Take(long id, InventoryContainerSelection selection, ulong expectedWorldRevision)
    {
        if (!TryGet(id, out DaggerfallGroundContainer? container))
            throw new InvalidOperationException("That dropped item pile is no longer available.");
        if (_containers.Read(_player).StoreRevision != expectedWorldRevision)
            throw new InvalidOperationException("Inventory changed. Choose the item again.");
        if (selection.Stack is InventoryStackId source && selection.DestinationStack is InventoryStackId destination)
            _instances.EnsureTransferCompatible(DaggerfallItemOwner.Ground(id), DaggerfallItemOwner.Player, source, destination);
        InventoryContainerTransferReceipt transfer = _containers.Transfer(container.Owner, _player, selection);
        SyncToPlayer(transfer, id);
        InventoryView remaining = _containers.Read(container.Owner);
        if (remaining.Stacks.Count == 0 && remaining.UniqueItems.Count == 0)
        {
            _ground.Remove(id);
            _containers.Entities.Destroy(new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)id)));
            _identities.Remove(new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)id)));
        }
        return transfer;
    }

    internal void Restore(IEnumerable<DaggerfallGroundContainerSave> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        foreach (DaggerfallGroundContainerSave value in saved.OrderBy(value => value.Id))
        {
            value.Validate();
            DurableIdentityReference identity = new(DurableIdentityKind.Container, checked((ulong)value.Id));
            if (_identities.Classify(identity) != DurableIdentityClassification.Live)
                throw new ArgumentException($"Saved ground container {value.Id} is not live in the persisted identity ledger.");
            EntityId owner = _containers.Entities.Create(identity, GroundContainerType);
            _containers.RegisterOwner(owner);
            if (value.Inventory.Stacks.Length != 0 || value.Inventory.UniqueItems.Length != 0)
            {
                InventoryContainerSeed[] seeds = value.Inventory.Stacks
                    .Select(stack => new InventoryContainerSeed(new InventoryItemId(stack.ItemId), stack.Quantity, Stack: InventoryStackId.Parse(stack.StackId)))
                    .Concat(value.Inventory.UniqueItems.Select(unique => new InventoryContainerSeed(
                        new InventoryItemId(unique.ItemId), UniqueItem: new DurableIdentityReference(DurableIdentityKind.Item, unique.EntityId))))
                    .ToArray();
                _containers.Seed(owner, seeds);
                RegisterMetadata(value);
            }
            _ground.Add(value.Id, new DaggerfallGroundContainer(value.Profile.Require(), value.Id, owner, new WorldPoint(value.X, value.Y, value.Z)));
        }
    }

    private InventoryView ValidatePlayerSelection(InventoryContainerSelection selection)
    {
        InventoryView current = _containers.Read(_player);
        if (selection.UniqueEntityId is ulong unique)
        {
            if (!current.UniqueItems.Any(item => item.Entity.Value == unique && item.Definition.Value == selection.Item.Value))
                throw new InvalidOperationException("That item is no longer in your inventory.");
            return current;
        }
        if (selection.Stack is not InventoryStackId stack || !current.Stacks.Any(item => item.Id == stack && item.Definition.Value == selection.Item.Value && item.Quantity >= selection.Quantity))
            throw new InvalidOperationException("That stack is no longer in your inventory with the requested quantity.");
        return current;
    }

    private void SyncFromPlayer(InventoryContainerTransferReceipt transfer, ulong groundId)
    {
        InventoryView sourceAfter = _containers.Read(_player);
        foreach (InventoryContainerStackTransfer stack in transfer.Stacks)
        {
            bool exhausted = !sourceAfter.Stacks.Any(value => value.Id == stack.SourceStack);
            _instances.TransferStack(DaggerfallItemOwner.Player, DaggerfallItemOwner.Ground(checked((long)groundId)), stack.SourceStack, stack.DestinationStack, exhausted);
        }
        foreach (InventoryContainerUniqueTransfer unique in transfer.UniqueItems)
            _instances.MoveUnique(_containers.Entities.IdentityOf(new EntityId(unique.EntityId)).Value, DaggerfallItemOwner.Ground(checked((long)groundId)));
    }

    private void SyncToPlayer(InventoryContainerTransferReceipt transfer, long groundId)
    {
        DaggerfallGroundContainer container = _ground[groundId];
        InventoryView sourceAfter = _containers.Read(container.Owner);
        foreach (InventoryContainerStackTransfer stack in transfer.Stacks)
        {
            bool exhausted = !sourceAfter.Stacks.Any(value => value.Id == stack.SourceStack);
            _instances.TransferStack(DaggerfallItemOwner.Ground(groundId), DaggerfallItemOwner.Player, stack.SourceStack, stack.DestinationStack, exhausted);
        }
        foreach (InventoryContainerUniqueTransfer unique in transfer.UniqueItems)
            _instances.MoveUnique(_containers.Entities.IdentityOf(new EntityId(unique.EntityId)).Value, DaggerfallItemOwner.Player);
    }

    private void RegisterMetadata(DaggerfallGroundContainerSave value)
    {
        DaggerfallItemOwner owner = DaggerfallItemOwner.Ground(value.Id);
        foreach (DaggerfallStackSave stack in value.Inventory.Stacks)
            _instances.RegisterStack(owner, InventoryStackId.Parse(stack.StackId), DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata));
        foreach (DaggerfallUniqueSave unique in value.Inventory.UniqueItems)
            _instances.RegisterUnique(unique.EntityId, DaggerfallItemInstanceMetadata.Restore(unique.ItemId, unique.Metadata));
    }
}

internal static class DaggerfallGroundContainerDictionaryExtensions
{
    internal static TValue AddAndReturn<TKey, TValue>(this IDictionary<TKey, TValue> values, TKey key, TValue value) where TKey : notnull
    {
        values.Add(key, value);
        return value;
    }
}
