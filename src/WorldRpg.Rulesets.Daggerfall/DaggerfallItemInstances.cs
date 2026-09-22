using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One durable Daggerfall inventory owner, independent of its transient Engine entity.</summary>
internal sealed record DaggerfallItemOwner(string Scope, long Id)
{
    internal static DaggerfallItemOwner Player { get; } = new("player", DaggerfallActorIdentity.PlayerEntityId);
    internal static DaggerfallItemOwner Actor(long id) => new("actor", id);
    internal static DaggerfallItemOwner Corpse(long actorId) => new("corpse", actorId);

    internal DaggerfallItemOwner Validate()
    {
        if (Scope is not ("player" or "actor" or "corpse") || Id <= 0)
            throw new ArgumentException("Item ownership must name a known positive durable owner.");
        return this;
    }
}

/// <summary>
/// Daggerfall meaning carried by one stack or unique item. The Engine retains
/// quantities, containment, and equipment; these fields decide when two
/// Daggerfall instances may share a stack.
/// </summary>
internal sealed record DaggerfallItemInstanceMetadata(
    string ItemId,
    string Material,
    int Variant,
    int CurrentCondition,
    int MaximumCondition,
    bool Identified,
    bool Stolen,
    string? QuestId,
    string? QuestItemSymbol,
    string? Enchantment,
    DaggerfallItemOwner Owner,
    string? Race = null,
    string? Gender = null,
    string? Dye = null,
    int? BookId = null)
{
    internal DaggerfallItemInstanceMetadata Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Material);
        if (Variant < 0 || CurrentCondition < 0 || MaximumCondition < 0 || CurrentCondition > MaximumCondition)
            throw new ArgumentOutOfRangeException(nameof(CurrentCondition), "Item variant and condition must be within their authored range.");
        if ((QuestId is null) != (QuestItemSymbol is null))
            throw new ArgumentException("Quest item identity requires both quest and symbol.");
        if (Race is { Length: 0 } || Gender is { Length: 0 } || Dye is { Length: 0 })
            throw new ArgumentException("Item appearance metadata cannot contain empty values.");
        if (BookId < 0)
            throw new ArgumentOutOfRangeException(nameof(BookId), "Book identity cannot be negative.");
        Owner.Validate();
        return this;
    }

    internal bool IsStackCompatibleWith(DaggerfallItemInstanceMetadata other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(ItemId, other.ItemId, StringComparison.Ordinal)
            && string.Equals(Material, other.Material, StringComparison.Ordinal)
            && Variant == other.Variant
            && CurrentCondition == other.CurrentCondition
            && MaximumCondition == other.MaximumCondition
            && Identified == other.Identified
            && Stolen == other.Stolen
            && string.Equals(QuestId, other.QuestId, StringComparison.Ordinal)
            && string.Equals(QuestItemSymbol, other.QuestItemSymbol, StringComparison.Ordinal)
            && string.Equals(Enchantment, other.Enchantment, StringComparison.Ordinal)
            && string.Equals(Race, other.Race, StringComparison.Ordinal)
            && string.Equals(Gender, other.Gender, StringComparison.Ordinal)
            && string.Equals(Dye, other.Dye, StringComparison.Ordinal)
            && BookId == other.BookId;
    }

    internal static DaggerfallItemInstanceMetadata Default(DaggerfallItemDefinition definition, DaggerfallItemOwner owner) =>
        new DaggerfallItemInstanceMetadata(definition.Id.Value, definition.Weapon?.Material ?? definition.Armor?.Material ?? "none", 0, 1, 1,
            Identified: true, Stolen: false, QuestId: null, QuestItemSymbol: null, Enchantment: null, owner).Validate();

    internal DaggerfallItemMetadataSave Capture() => new(Material, Variant, CurrentCondition, MaximumCondition,
        Identified, Stolen, QuestId, QuestItemSymbol, Enchantment, new DaggerfallItemOwnerSave(Owner.Scope, Owner.Id), Race, Gender, Dye, BookId);

    internal static DaggerfallItemInstanceMetadata Restore(string itemId, DaggerfallItemMetadataSave saved) =>
        new DaggerfallItemInstanceMetadata(itemId, saved.Material, saved.Variant, saved.CurrentCondition, saved.MaximumCondition,
            saved.Identified, saved.Stolen, saved.QuestId, saved.QuestItemSymbol, saved.Enchantment,
            new DaggerfallItemOwner(saved.Owner.Scope, saved.Owner.Id), saved.Race, saved.Gender, saved.Dye, saved.BookId).Validate();
}

/// <summary>
/// Ruleset-owned item-instance metadata indexed by durable owner and explicit
/// Engine stack identity. It deliberately has no quantities or containment
/// mirror; callers first commit the Engine operation, then update this meaning.
/// </summary>
internal sealed class DaggerfallItemInstances
{
    private readonly Dictionary<(DaggerfallItemOwner Owner, string Stack), DaggerfallItemInstanceMetadata> _stacks = [];
    private readonly Dictionary<ulong, DaggerfallItemInstanceMetadata> _unique = [];

    internal void RegisterStack(DaggerfallItemOwner owner, InventoryStackId stack, DaggerfallItemInstanceMetadata metadata)
    {
        owner.Validate();
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(metadata);
        MetadataFor(owner, metadata).Validate();
        if (!_stacks.TryAdd((owner, stack.Value), MetadataFor(owner, metadata)))
            throw new InvalidOperationException($"Item stack '{stack.Value}' is already placed for {owner.Scope} {owner.Id}.");
    }

    internal void RegisterDefaultStack(DaggerfallItemOwner owner, InventoryStack stack, DaggerfallItemDefinition definition) =>
        RegisterStack(owner, stack.Id, DaggerfallItemInstanceMetadata.Default(definition, owner));

    internal DaggerfallItemInstanceMetadata RequireStack(DaggerfallItemOwner owner, InventoryStackId stack) =>
        _stacks.TryGetValue((owner.Validate(), stack?.Value ?? throw new ArgumentNullException(nameof(stack))), out DaggerfallItemInstanceMetadata? metadata)
            ? metadata
            : throw new InvalidOperationException($"Item stack '{stack.Value}' has no Daggerfall metadata for {owner.Scope} {owner.Id}.");

    internal void ReplaceStack(DaggerfallItemOwner owner, InventoryStackId stack, DaggerfallItemInstanceMetadata metadata)
    {
        _ = RequireStack(owner, stack);
        _stacks[(owner, stack.Value)] = MetadataFor(owner, metadata).Validate();
    }

    /// <summary>Retires meaning when its Engine-backed stack reaches zero quantity.</summary>
    internal void RemoveStack(DaggerfallItemOwner owner, InventoryStackId stack)
    {
        _ = RequireStack(owner, stack);
        _stacks.Remove((owner, stack.Value));
    }

    /// <summary>Splits through Engine first, then assigns the copied compatible metadata.</summary>
    internal void SplitStack(DaggerfallItemOwner owner, MechanicsInventoryCoordinator inventory,
        InventoryStackId source, InventoryStackId split, ulong quantity)
    {
        DaggerfallItemInstanceMetadata metadata = RequireStack(owner, source);
        inventory.Split(source, split, quantity);
        RegisterStack(owner, split, metadata);
    }

    /// <summary>Checks Daggerfall meaning before asking Engine to merge the selected stacks.</summary>
    internal void MergeStacks(DaggerfallItemOwner owner, MechanicsInventoryCoordinator inventory,
        InventoryStackId source, InventoryStackId destination)
    {
        DaggerfallItemInstanceMetadata sourceMetadata = RequireStack(owner, source);
        DaggerfallItemInstanceMetadata destinationMetadata = RequireStack(owner, destination);
        if (!sourceMetadata.IsStackCompatibleWith(destinationMetadata))
            throw new InvalidOperationException("Only Daggerfall-compatible item instances may merge.");
        inventory.Merge(source, destination);
        _stacks.Remove((owner, source.Value));
    }

    /// <summary>Checks whether an Engine transfer may merge into its selected destination.</summary>
    internal void EnsureTransferCompatible(DaggerfallItemOwner sourceOwner, DaggerfallItemOwner destinationOwner,
        InventoryStackId source, InventoryStackId destination)
    {
        DaggerfallItemInstanceMetadata sourceMetadata = RequireStack(sourceOwner, source);
        if (_stacks.TryGetValue((destinationOwner.Validate(), destination.Value), out DaggerfallItemInstanceMetadata? destinationMetadata)
            && !sourceMetadata.IsStackCompatibleWith(destinationMetadata))
            throw new InvalidOperationException("Only Daggerfall-compatible item instances may transfer into an existing stack.");
    }

    /// <summary>Moves or copies metadata after its corresponding Engine transfer has committed.</summary>
    internal void TransferStack(DaggerfallItemOwner sourceOwner, DaggerfallItemOwner destinationOwner,
        InventoryStackId source, InventoryStackId destination, bool sourceWasExhausted)
    {
        EnsureTransferCompatible(sourceOwner, destinationOwner, source, destination);
        DaggerfallItemInstanceMetadata sourceMetadata = RequireStack(sourceOwner, source);
        if (!_stacks.ContainsKey((destinationOwner.Validate(), destination.Value)))
            RegisterStack(destinationOwner, destination, sourceMetadata);

        if (sourceWasExhausted)
            _stacks.Remove((sourceOwner.Validate(), source.Value));
    }

    internal void RegisterUnique(ulong itemId, DaggerfallItemInstanceMetadata metadata)
    {
        if (itemId == 0) throw new ArgumentOutOfRangeException(nameof(itemId));
        ArgumentNullException.ThrowIfNull(metadata);
        if (!_unique.TryAdd(itemId, metadata.Validate()))
            throw new InvalidOperationException($"Unique item '{itemId}' is already placed.");
    }

    internal void RegisterDefaultUnique(ulong itemId, DaggerfallItemDefinition definition, DaggerfallItemOwner owner) =>
        RegisterUnique(itemId, DaggerfallItemInstanceMetadata.Default(definition, owner));

    internal DaggerfallItemInstanceMetadata RequireUnique(ulong itemId) =>
        _unique.TryGetValue(itemId, out DaggerfallItemInstanceMetadata? metadata)
            ? metadata
            : throw new InvalidOperationException($"Unique item '{itemId}' has no Daggerfall metadata.");

    internal void ReplaceUnique(ulong itemId, DaggerfallItemInstanceMetadata metadata)
    {
        _ = RequireUnique(itemId);
        _unique[itemId] = metadata.Validate();
    }

    internal void MoveUnique(ulong itemId, DaggerfallItemOwner owner) =>
        _unique[itemId] = MetadataFor(owner, RequireUnique(itemId)).Validate();

    /// <summary>Retires all stack meaning whose Engine owner has been removed.</summary>
    internal void RemoveOwner(DaggerfallItemOwner owner)
    {
        owner.Validate();
        foreach ((DaggerfallItemOwner current, string stack) in _stacks.Keys.Where(key => key.Owner == owner).ToArray())
            _stacks.Remove((current, stack));
    }

    internal void RemoveUnique(ulong itemId) => _unique.Remove(itemId);

    private static DaggerfallItemInstanceMetadata MetadataFor(DaggerfallItemOwner owner, DaggerfallItemInstanceMetadata metadata) =>
        metadata with { Owner = owner.Validate() };
}
