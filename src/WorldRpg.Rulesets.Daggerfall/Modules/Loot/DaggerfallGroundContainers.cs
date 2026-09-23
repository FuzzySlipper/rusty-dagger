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
            // Registering the destination inventory advances the shared store revision. The caller's
            // expected revision was already checked before allocation; use the post-registration
            // revision for this internal Engine transfer.
            InventoryContainerTransferReceipt transfer = _containers.Transfer(_player, owner, transferSelection,
                _containers.Read(_player).StoreRevision);
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
        if (selection.Stack is InventoryStackId source && selection.DestinationStack is InventoryStackId destination)
            _instances.EnsureTransferCompatible(DaggerfallItemOwner.Ground(id), DaggerfallItemOwner.Player, source, destination);
        InventoryContainerTransferReceipt transfer = _containers.Transfer(container.Owner, _player, selection, expectedWorldRevision);
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
            _instances.MoveUnique(unique.EntityId, DaggerfallItemOwner.Ground(checked((long)groundId)));
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
            _instances.MoveUnique(unique.EntityId, DaggerfallItemOwner.Player);
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
