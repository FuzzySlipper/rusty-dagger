using System.Collections.Frozen;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace WorldRpg.Kit.Inventory;

/// <summary>One product-authored item to materialize into a registered inventory container.</summary>
public sealed record InventoryContainerSeed(
    InventoryItemId Item,
    ulong Quantity = 1,
    string? UniqueIdentity = null,
    ulong? UniqueEntityId = null)
{
    public InventoryContainerSeed Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Item.Value);
        ArgumentOutOfRangeException.ThrowIfZero(Quantity);

        bool unique = UniqueIdentity is not null || UniqueEntityId is not null;
        if (unique && (string.IsNullOrWhiteSpace(UniqueIdentity) || UniqueEntityId is null || UniqueEntityId == 0))
        {
            throw new ArgumentException(
                "Unique inventory seeds require a non-empty identity and non-zero entity id.",
                nameof(UniqueEntityId));
        }

        return this;
    }
}

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

/// <summary>Copied evidence for one atomic move of every item in a container.</summary>
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
/// mappings and unique materialization provenance are scoped to this coordinator
/// instance; callers recreate any provenance required across persisted sessions.
/// </summary>
public sealed class MechanicsInventoryContainerCoordinator
{
    private readonly InventoryWorld _world;
    private readonly FrozenDictionary<InventoryItemId, ItemDefinition> _definitions;
    private readonly FrozenDictionary<ItemDefinitionId, InventoryItemId> _definitionIds;
    private readonly HashSet<string> _materializedUniqueIdentities = new(StringComparer.Ordinal);

    public MechanicsInventoryContainerCoordinator(
        InventoryWorld world,
        IReadOnlyDictionary<InventoryItemId, ItemDefinition> definitions)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentNullException.ThrowIfNull(definitions);
        Dictionary<InventoryItemId, ItemDefinition> snapshot = SnapshotDefinitions(definitions);
        _definitions = snapshot.ToFrozenDictionary();
        _definitionIds = snapshot.ToFrozenDictionary(entry => entry.Value.Id, entry => entry.Key);
    }

    /// <summary>Registers one durable inventory owner. Empty owners intentionally remain registered.</summary>
    public void RegisterOwner(EntityId owner)
    {
        RequireOwner(owner, nameof(owner));
        _world.RegisterInventory(new InventoryState(owner));
    }

    /// <summary>Returns the Engine's copied read model for one registered owner.</summary>
    public InventoryView Read(EntityId owner)
    {
        RequireRegistered(owner, nameof(owner));
        return _world.Read(owner);
    }

    /// <summary>
    /// Materializes mixed fungible and unique contents on one detached candidate.
    /// Unique source identities are committed only after Engine publication succeeds.
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
        InventoryView beforeView = _world.Read(owner);
        ulong worldRevisionBefore = _world.Revision;
        InventoryWorldCandidate candidate = _world.Prepare(worldRevisionBefore);
        foreach (InventoryContainerSeed seed in values)
        {
            ItemDefinition definition = RequireDefinition(seed.Item);
            if (seed.UniqueEntityId is ulong uniqueEntityId)
            {
                candidate.MaterializeUnique(new ItemState(new EntityId(uniqueEntityId), definition), owner);
            }
            else
            {
                candidate.Grant(owner, definition, seed.Quantity);
            }
        }

        candidate.Publish();
        foreach (InventoryContainerSeed seed in values.Where(seed => seed.UniqueIdentity is not null))
        {
            _materializedUniqueIdentities.Add(seed.UniqueIdentity!);
        }

        InventoryView afterView = _world.Read(owner);
        return new InventoryContainerSeedReceipt(
            worldRevisionBefore,
            _world.Revision,
            Summarize(beforeView),
            Summarize(afterView));
    }

    /// <summary>
    /// Moves all directly contained items from one registered owner to another
    /// through one detached candidate and one Engine publication.
    /// </summary>
    public InventoryContainerTransferReceipt TransferAll(EntityId source, EntityId destination)
    {
        RequireRegistered(source, nameof(source));
        RequireRegistered(destination, nameof(destination));
        if (source == destination)
        {
            throw new ArgumentException("A container transfer requires distinct owners.", nameof(destination));
        }

        InventoryView sourceBeforeView = _world.Read(source);
        InventoryView destinationBeforeView = _world.Read(destination);
        InventoryStack[] stacks = sourceBeforeView.Stacks
            .OrderBy(stack => stack.Definition.Value, StringComparer.Ordinal)
            .ToArray();
        Rusty.Engine.Mechanics.UniqueInventoryItem[] uniqueItems = sourceBeforeView.UniqueItems
            .OrderBy(item => item.Entity.Value)
            .ToArray();
        ulong worldRevisionBefore = _world.Revision;
        InventoryWorldCandidate candidate = _world.Prepare(worldRevisionBefore);

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
        InventoryView sourceAfterView = _world.Read(source);
        InventoryView destinationAfterView = _world.Read(destination);
        return new InventoryContainerTransferReceipt(
            worldRevisionBefore,
            _world.Revision,
            Summarize(sourceBeforeView),
            Summarize(sourceAfterView),
            Summarize(destinationBeforeView),
            Summarize(destinationAfterView),
            stacks.Select(stack => new InventoryContainerStackTransfer(MapDefinition(stack.Definition), stack.Quantity)),
            uniqueItems.Select(item => new InventoryContainerUniqueTransfer(MapDefinition(item.Definition), item.Entity.Value)));
    }

    private void ValidateSeeds(IEnumerable<InventoryContainerSeed> seeds)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var entities = new HashSet<ulong>();
        foreach (InventoryContainerSeed seed in seeds)
        {
            ItemDefinition definition = RequireDefinition(seed.Item);
            if (seed.UniqueEntityId is ulong entityId)
            {
                if (definition.Kind != ItemKind.Unique || seed.Quantity != 1)
                {
                    throw new InvalidOperationException($"Unique seed '{seed.Item.Value}' has an invalid item shape.");
                }
                if (!identities.Add(seed.UniqueIdentity!) || _materializedUniqueIdentities.Contains(seed.UniqueIdentity!))
                {
                    throw new InvalidOperationException($"Unique inventory identity '{seed.UniqueIdentity}' was already materialized.");
                }
                if (!entities.Add(entityId))
                {
                    throw new InvalidOperationException($"Unique inventory entity '{entityId}' appears more than once in the seed.");
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

    private void RequireRegistered(EntityId owner, string parameterName)
    {
        RequireOwner(owner, parameterName);
        if (!_world.TryGetInventory(owner, out _))
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
