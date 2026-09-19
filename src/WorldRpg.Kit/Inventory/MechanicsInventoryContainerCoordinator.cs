using System.Collections.Frozen;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.World;

namespace WorldRpg.Kit.Inventory;

/// <summary>One product-authored item to materialize into a registered inventory container.</summary>
public sealed record InventoryContainerSeed(
    InventoryItemId Item,
    ulong Quantity = 1,
    DurableIdentityReference? UniqueItem = null)
{
    public InventoryContainerSeed Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Item.Value);
        ArgumentOutOfRangeException.ThrowIfZero(Quantity);
        if (UniqueItem is DurableIdentityReference identity)
        {
            identity.Validate();
            if (identity.Kind != DurableIdentityKind.Item || Quantity != 1)
                throw new ArgumentException("Unique inventory seeds require one durable item identity and quantity one.", nameof(UniqueItem));
        }
        return this;
    }
}

/// <summary>A caller-selected amount of one stack, or one unique item, to transfer.</summary>
public sealed record InventoryContainerSelection(InventoryItemId Item, ulong Quantity, ulong? UniqueEntityId = null);

/// <summary>One copied fungible transfer performed by a container move.</summary>
public readonly record struct InventoryContainerStackTransfer(InventoryItemId Item, ulong Quantity);

/// <summary>One copied unique-item transfer performed by a container move.</summary>
public readonly record struct InventoryContainerUniqueTransfer(InventoryItemId Item, ulong EntityId);

/// <summary>Copied state summary for a registered container before or after one operation.</summary>
public readonly record struct InventoryContainerSummary(
    EntityId Owner,
    ulong InventoryRevision,
    int StackCount,
    int UniqueItemCount);

/// <summary>Copied evidence for one atomic container seed.</summary>
public sealed class InventoryContainerSeedReceipt
{
    internal InventoryContainerSeedReceipt(
        ulong worldRevisionBefore,
        ulong worldRevisionAfter,
        InventoryContainerSummary before,
        InventoryContainerSummary after)
    {
        WorldRevisionBefore = worldRevisionBefore;
        WorldRevisionAfter = worldRevisionAfter;
        Before = before;
        After = after;
    }

    public ulong WorldRevisionBefore { get; }
    public ulong WorldRevisionAfter { get; }
    public InventoryContainerSummary Before { get; }
    public InventoryContainerSummary After { get; }
}

/// <summary>Copied evidence for one atomic transfer between containers.</summary>
public sealed class InventoryContainerTransferReceipt
{
    internal InventoryContainerTransferReceipt(
        ulong worldRevisionBefore,
        ulong worldRevisionAfter,
        InventoryContainerSummary sourceBefore,
        InventoryContainerSummary sourceAfter,
        InventoryContainerSummary destinationBefore,
        InventoryContainerSummary destinationAfter,
        IEnumerable<InventoryContainerStackTransfer> stacks,
        IEnumerable<InventoryContainerUniqueTransfer> uniqueItems)
    {
        WorldRevisionBefore = worldRevisionBefore;
        WorldRevisionAfter = worldRevisionAfter;
        SourceBefore = sourceBefore;
        SourceAfter = sourceAfter;
        DestinationBefore = destinationBefore;
        DestinationAfter = destinationAfter;
        Stacks = Array.AsReadOnly(stacks.ToArray());
        UniqueItems = Array.AsReadOnly(uniqueItems.ToArray());
    }

    public ulong WorldRevisionBefore { get; }
    public ulong WorldRevisionAfter { get; }
    public InventoryContainerSummary SourceBefore { get; }
    public InventoryContainerSummary SourceAfter { get; }
    public InventoryContainerSummary DestinationBefore { get; }
    public InventoryContainerSummary DestinationAfter { get; }
    public IReadOnlyList<InventoryContainerStackTransfer> Stacks { get; }
    public IReadOnlyList<InventoryContainerUniqueTransfer> UniqueItems { get; }
}

/// <summary>
/// Owner-aware, thin coordination over one shared managed inventory world.
/// The Engine remains the contents, capacity, containment, and publication
/// authority; this class only maps product item identities and groups caller
/// approved container operations into one candidate publication. Definition
/// mappings remain product-facing while the Engine owns inventory state.
/// </summary>
public sealed class MechanicsInventoryContainerCoordinator
{
    private readonly InventoryStore _store;
    private readonly EntityDirectory _entities;
    private readonly FrozenDictionary<InventoryItemId, ItemDefinition> _definitions;
    private readonly FrozenDictionary<ItemDefinitionId, InventoryItemId> _definitionIds;

    public MechanicsInventoryContainerCoordinator(
        InventoryStore world,
        EntityDirectory entities,
        IReadOnlyDictionary<InventoryItemId, ItemDefinition> definitions)
    {
        _store = world ?? throw new ArgumentNullException(nameof(world));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        ArgumentNullException.ThrowIfNull(definitions);
        Dictionary<InventoryItemId, ItemDefinition> snapshot = SnapshotDefinitions(definitions);
        _definitions = snapshot.ToFrozenDictionary();
        _definitionIds = snapshot.ToFrozenDictionary(entry => entry.Value.Id, entry => entry.Key);
    }

    public EntityDirectory Entities => _entities;

    /// <summary>Registers one durable inventory owner. Empty owners intentionally remain registered.</summary>
    public void RegisterOwner(EntityId owner)
    {
        RequireOwner(owner, nameof(owner));
        if (!_entities.Store.IsAlive(owner))
            throw new InvalidOperationException($"Inventory owner {owner.Value} is not a live Engine entity.");
        if (_entities.Store.Has<InventoryComponent>(owner))
            throw new InvalidOperationException($"Inventory owner {owner.Value} already has an inventory component.");
        _store.RegisterInventory(new InventoryState(owner));
        _entities.Store.Add(owner, new InventoryComponent(_store, owner));
    }

    /// <summary>Returns the Engine's copied read model for one registered owner.</summary>
    public InventoryView Read(EntityId owner)
    {
        RequireRegistered(owner, nameof(owner));
        return _store.Read(owner);
    }

    /// <summary>
    /// Materializes mixed fungible and unique contents on one detached candidate.
    /// Newly created unique entities are destroyed if the candidate cannot publish.
    /// </summary>
    public InventoryContainerSeedReceipt Seed(EntityId owner, IEnumerable<InventoryContainerSeed> seeds)
    {
        RequireRegistered(owner, nameof(owner));
        ArgumentNullException.ThrowIfNull(seeds);

        InventoryContainerSeed[] values = seeds.Select(seed => seed.Validate()).ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("At least one inventory seed is required.", nameof(seeds));
        }

        ValidateSeeds(values);
        InventoryView beforeView = _store.Read(owner);
        ulong worldRevisionBefore = _store.Revision;
        List<DurableIdentityReference> created = [];
        try
        {
            using InventoryEdit candidate = _store.Prepare(worldRevisionBefore);
            foreach (InventoryContainerSeed seed in values)
            {
                ItemDefinition definition = RequireDefinition(seed.Item);
                if (seed.UniqueItem is DurableIdentityReference identity)
                {
                    EntityId item = CreateItemEntity(identity, definition);
                    created.Add(identity);
                    candidate.MaterializeUnique(new ItemState(item, definition), owner);
                }
                else candidate.Grant(owner, definition, seed.Quantity);
            }
            candidate.Publish();
        }
        catch
        {
            foreach (DurableIdentityReference identity in created) _entities.Destroy(identity);
            throw;
        }

        InventoryView afterView = _store.Read(owner);
        return new InventoryContainerSeedReceipt(
            worldRevisionBefore,
            _store.Revision,
            Summarize(beforeView),
            Summarize(afterView));
    }

    /// <summary>
    /// Moves all directly contained items from one registered owner to another
    /// through one detached candidate and one Engine publication.
    /// </summary>
    public InventoryContainerTransferReceipt TransferAll(EntityId source, EntityId destination, ulong? expectedWorldRevision = null) =>
        TransferCore(source, destination, null, expectedWorldRevision);

    /// <summary>Transfers a selected amount through the same Engine candidate and revision guard.</summary>
    public InventoryContainerTransferReceipt Transfer(EntityId source, EntityId destination, InventoryContainerSelection selection, ulong expectedWorldRevision)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentOutOfRangeException.ThrowIfZero(selection.Quantity);
        if (selection.UniqueEntityId is not null && selection.Quantity != 1)
            throw new ArgumentException("A unique transfer moves exactly one item.", nameof(selection));
        return TransferCore(source, destination, selection, expectedWorldRevision);
    }

    private InventoryContainerTransferReceipt TransferCore(EntityId source, EntityId destination, InventoryContainerSelection? selection, ulong? expectedWorldRevision)
    {
        RequireRegistered(source, nameof(source));
        RequireRegistered(destination, nameof(destination));
        if (source == destination)
        {
            throw new ArgumentException("A container transfer requires distinct owners.", nameof(destination));
        }

        InventoryView sourceBeforeView = _store.Read(source);
        InventoryView destinationBeforeView = _store.Read(destination);
        InventoryStack[] stacks = sourceBeforeView.Stacks
            .OrderBy(stack => stack.Definition.Value, StringComparer.Ordinal)
            .ToArray();
        Rusty.Engine.Mechanics.UniqueInventoryItem[] uniqueItems = sourceBeforeView.UniqueItems
            .OrderBy(item => item.Entity.Value)
            .ToArray();
        ulong worldRevisionBefore = _store.Revision;
        InventoryEdit candidate = _store.Prepare(expectedWorldRevision ?? worldRevisionBefore);
        if (selection is not null)
        {
            ItemDefinition definition = RequireDefinition(selection.Item);
            if (selection.UniqueEntityId is ulong entity)
            {
                uniqueItems = uniqueItems.Where(item => item.Entity.Value == entity && item.Definition == definition.Id).ToArray();
                if (uniqueItems.Length != 1) throw new InvalidOperationException("The selected item is no longer in this container.");
                stacks = [];
            }
            else
            {
                if (definition.Kind != ItemKind.Fungible) throw new ArgumentException("A stack transfer requires a fungible item.", nameof(selection));
                stacks = [new InventoryStack(definition.Id, selection.Quantity)];
                uniqueItems = [];
            }
        }

        foreach (InventoryStack stack in stacks)
        {
            candidate.TransferFungible(source, destination, RequireMappedDefinition(stack.Definition), stack.Quantity);
        }
        foreach (Rusty.Engine.Mechanics.UniqueInventoryItem item in uniqueItems)
        {
            RequireMappedDefinition(item.Definition);
            candidate.TransferUnique(item.Entity, source, destination);
        }

        candidate.Publish();
        InventoryView sourceAfterView = _store.Read(source);
        InventoryView destinationAfterView = _store.Read(destination);
        return new InventoryContainerTransferReceipt(
            worldRevisionBefore,
            _store.Revision,
            Summarize(sourceBeforeView),
            Summarize(sourceAfterView),
            Summarize(destinationBeforeView),
            Summarize(destinationAfterView),
            stacks.Select(stack => new InventoryContainerStackTransfer(MapDefinition(stack.Definition), stack.Quantity)),
            uniqueItems.Select(item => new InventoryContainerUniqueTransfer(MapDefinition(item.Definition), item.Entity.Value)));
    }

    private void ValidateSeeds(IEnumerable<InventoryContainerSeed> seeds)
    {
        var identities = new HashSet<DurableIdentityReference>();
        foreach (InventoryContainerSeed seed in seeds)
        {
            ItemDefinition definition = RequireDefinition(seed.Item);
            if (seed.UniqueItem is DurableIdentityReference identity)
            {
                if (definition.Kind != ItemKind.Unique || seed.Quantity != 1)
                {
                    throw new InvalidOperationException($"Unique seed '{seed.Item.Value}' has an invalid item shape.");
                }
                if (!identities.Add(identity))
                {
                    throw new InvalidOperationException($"Unique inventory identity '{identity}' appears more than once in the seed.");
                }
            }
            else if (definition.Kind != ItemKind.Fungible)
            {
                throw new InvalidOperationException($"Fungible seed '{seed.Item.Value}' requires a fungible item definition.");
            }
        }
    }

    private static Dictionary<InventoryItemId, ItemDefinition> SnapshotDefinitions(
        IReadOnlyDictionary<InventoryItemId, ItemDefinition> definitions)
    {
        var snapshot = new Dictionary<InventoryItemId, ItemDefinition>();
        var definitionIds = new HashSet<ItemDefinitionId>();
        foreach ((InventoryItemId item, ItemDefinition definition) in definitions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Value);
            ArgumentNullException.ThrowIfNull(definition);
            if (!string.Equals(item.Value, definition.Id.Value, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Inventory mapping key '{item.Value}' must match managed definition '{definition.Id.Value}'.",
                    nameof(definitions));
            }
            if (!definitionIds.Add(definition.Id))
            {
                throw new ArgumentException($"Managed definition '{definition.Id.Value}' is mapped more than once.", nameof(definitions));
            }
            snapshot.Add(item, definition);
        }
        return snapshot;
    }

    private ItemDefinition RequireDefinition(InventoryItemId item) =>
        _definitions.TryGetValue(item, out ItemDefinition? definition)
            ? definition
            : throw new InvalidOperationException($"Managed inventory does not define item '{item.Value}'.");

    private ItemDefinition RequireMappedDefinition(ItemDefinitionId definition) =>
        _definitionIds.ContainsKey(definition)
            ? _definitions[_definitionIds[definition]]
            : throw new InvalidOperationException($"Managed inventory definition '{definition.Value}' is not mapped by this coordinator.");

    private InventoryItemId MapDefinition(ItemDefinitionId definition) =>
        _definitionIds.TryGetValue(definition, out InventoryItemId item)
            ? item
            : throw new InvalidOperationException($"Managed inventory definition '{definition.Value}' is not mapped by this coordinator.");

    public DurableIdentityReference GetDurableItemId(EntityId item)
    {
        DurableIdentityReference identity = _entities.IdentityOf(item);
        identity.Validate();
        if (identity.Kind != DurableIdentityKind.Item)
            throw new ArgumentException("An inventory item requires a durable item identity.", nameof(item));
        return identity;
    }

    private EntityId CreateItemEntity(DurableIdentityReference identity, ItemDefinition definition)
    {
        identity.Validate();
        if (identity.Kind != DurableIdentityKind.Item)
            throw new ArgumentException("An inventory item requires a durable item identity.", nameof(identity));
        return _entities.Create(identity, new EntityTypeId(definition.Id.Value));
    }

    private void RequireRegistered(EntityId owner, string parameterName)
    {
        RequireOwner(owner, parameterName);
        if (!_store.TryGetInventory(owner, out _))
        {
            throw new InvalidOperationException($"Inventory owner {owner.Value} is not registered.");
        }
    }

    private static void RequireOwner(EntityId owner, string parameterName)
    {
        if (owner.Value == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Inventory owner ids must be non-zero.");
        }
    }

    private static InventoryContainerSummary Summarize(InventoryView view) =>
        new(view.Owner, view.InventoryRevision, view.Stacks.Count, view.UniqueItems.Count);
}
