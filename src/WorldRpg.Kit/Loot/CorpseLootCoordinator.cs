using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;

namespace WorldRpg.Kit.Loot;

/// <summary>Entity-local link from a defeated actor to its corpse inventory.</summary>
public sealed class CorpseLootComponent
{
    internal CorpseLootComponent(EntityId owner, ulong originatingSequence, bool hasRegisteredInventory, bool isInteractable)
    {
        Owner = owner;
        OriginatingSequence = originatingSequence;
        HasRegisteredInventory = hasRegisteredInventory;
        IsInteractable = isInteractable;
    }

    public EntityId Owner { get; }
    public ulong OriginatingSequence { get; }
    public bool HasRegisteredInventory { get; }
    public bool IsInteractable { get; private set; }

    internal void SetInteractable(bool value) => IsInteractable = value;
}

/// <summary>Result of one completed corpse-loot action.</summary>
public sealed record CorpseLootTransferResult(InventoryContainerTransferReceipt? Transfer, bool WasEmpty, bool IsEmpty);

/// <summary>
/// Creates corpse-owned inventory entities and moves their contents through the
/// canonical inventory coordinator. Rulesets retain corpse identity, item
/// generation, UI facts, and interaction eligibility.
/// </summary>
public sealed class CorpseLootCoordinator
{
    private readonly EntityDirectory _entities;
    private readonly MechanicsInventoryContainerCoordinator _containers;

    public CorpseLootCoordinator(EntityDirectory entities, MechanicsInventoryContainerCoordinator containers)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
    }

    public CorpseLootComponent Create(
        DurableIdentityReference identity,
        EntityTypeId type,
        ulong originatingSequence,
        IReadOnlyList<InventoryContainerSeed> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        EntityId owner = _entities.Create(identity, type);
        bool registered = seeds.Count > 0;
        if (registered)
        {
            _containers.RegisterOwner(owner);
            _containers.Seed(owner, seeds);
        }
        return new CorpseLootComponent(owner, originatingSequence, registered, isInteractable: true);
    }

    /// <summary>Restores current corpse inventory data without re-running item generation.</summary>
    public CorpseLootComponent Restore(
        DurableIdentityReference identity,
        EntityTypeId type,
        ulong originatingSequence,
        bool hasRegisteredInventory,
        bool isInteractable,
        IReadOnlyList<InventoryContainerSeed> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        EntityId owner = _entities.Create(identity, type);
        if (hasRegisteredInventory)
        {
            _containers.RegisterOwner(owner);
            if (seeds.Count > 0) _containers.Seed(owner, seeds);
        }
        return new CorpseLootComponent(owner, originatingSequence, hasRegisteredInventory, isInteractable);
    }

    public InventoryView? Read(CorpseLootComponent corpse)
    {
        ArgumentNullException.ThrowIfNull(corpse);
        return corpse.HasRegisteredInventory ? _containers.Read(corpse.Owner) : null;
    }

    public CorpseLootTransferResult TransferAll(CorpseLootComponent corpse, EntityId recipient, ulong? expectedWorldRevision = null) =>
        TransferCore(corpse, recipient, selection: null, expectedWorldRevision);

    public CorpseLootTransferResult Transfer(
        CorpseLootComponent corpse,
        EntityId recipient,
        InventoryContainerSelection selection,
        ulong expectedWorldRevision)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return TransferCore(corpse, recipient, selection, expectedWorldRevision);
    }

    private CorpseLootTransferResult TransferCore(
        CorpseLootComponent corpse,
        EntityId recipient,
        InventoryContainerSelection? selection,
        ulong? expectedWorldRevision)
    {
        ArgumentNullException.ThrowIfNull(corpse);
        if (!corpse.IsInteractable) throw new InvalidOperationException("This corpse is no longer interactable.");
        if (!corpse.HasRegisteredInventory)
        {
            corpse.SetInteractable(false);
            return new CorpseLootTransferResult(null, WasEmpty: true, IsEmpty: true);
        }

        InventoryContainerTransferReceipt receipt = selection is null
            ? _containers.TransferAll(corpse.Owner, recipient, expectedWorldRevision)
            : _containers.Transfer(corpse.Owner, recipient, selection, expectedWorldRevision!.Value);
        InventoryView remaining = _containers.Read(corpse.Owner);
        bool empty = remaining.Stacks.Count == 0 && remaining.UniqueItems.Count == 0;
        corpse.SetInteractable(!empty);
        return new CorpseLootTransferResult(receipt, WasEmpty: false, IsEmpty: empty);
    }
}
