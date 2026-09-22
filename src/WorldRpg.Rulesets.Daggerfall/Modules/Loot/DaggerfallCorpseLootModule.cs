using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Loot;

/// <summary>
/// Daggerfall death-loot and explicit corpse interaction policy.  Engine
/// Mechanics owns the inventory contents and Engine Perception owns the
/// visibility classification; this module only supplies Daggerfall's loot,
/// eligibility, and deterministic selection meaning.  The current actor
/// model has no authored live enemy inventories; donor-style transfer of one
/// is therefore deliberately deferred rather than represented by a mirror.
/// </summary>
internal sealed class DaggerfallCorpseLootModule
{
    private readonly IPerceptionService _perception;
    private readonly SpatialMovementSystem _spatial;
    private readonly MechanicsInventoryContainerCoordinator _containers;
    private readonly DaggerfallItemInstances _itemInstances;
    private readonly EntityId _playerOwner;
    private readonly ActorsState _actors;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _definitions;
    private readonly DaggerfallDefinitions _catalog;
    private readonly DaggerfallEncumbrancePolicy? _encumbrance;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly ProgressionState _progression;
    private readonly DaggerfallLootInteractionTuning _tuning;
    private readonly DaggerfallCharacterState _character;
    private readonly DaggerfallLootPopulation _population;
    private readonly CorpseLootCoordinator _corpseLoot;

    internal DaggerfallCorpseLootModule(
        IPerceptionService perception,
        SpatialMovementSystem spatial,
        MechanicsInventoryContainerCoordinator containers,
        DaggerfallItemInstances itemInstances,
        EntityId playerOwner,
        ActorsState actors,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions,
        DaggerfallDefinitions catalog,
        DaggerfallEncumbrancePolicy? encumbrance,
        IRandomService random,
        DaggerfallUniqueItemAllocator uniqueItems,
        ProgressionState progression,
        DaggerfallLootInteractionTuning tuning,
        DaggerfallCharacterState character)
    {
        _perception = perception ?? throw new ArgumentNullException(nameof(perception));
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _itemInstances = itemInstances ?? throw new ArgumentNullException(nameof(itemInstances));
        _playerOwner = playerOwner;
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _encumbrance = encumbrance;
        ArgumentNullException.ThrowIfNull(random);
        _uniqueItems = uniqueItems ?? throw new ArgumentNullException(nameof(uniqueItems));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _population = new DaggerfallLootPopulation(_catalog, random, _uniqueItems);
        _corpseLoot = new CorpseLootCoordinator(_actors.Entities, _containers);
    }

    // Focused module tests can exercise corpse ownership with no player inventory/stat fixture.
    // Session composition always supplies the live capacity policy above.
    internal DaggerfallCorpseLootModule(
        IPerceptionService perception, SpatialMovementSystem spatial, MechanicsInventoryContainerCoordinator containers,
        DaggerfallItemInstances itemInstances, EntityId playerOwner, ActorsState actors,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions, DaggerfallDefinitions catalog,
        IRandomService random, DaggerfallUniqueItemAllocator uniqueItems, ProgressionState progression,
        DaggerfallLootInteractionTuning tuning, DaggerfallCharacterState character)
        : this(perception, spatial, containers, itemInstances, playerOwner, actors, definitions, catalog,
            null, random, uniqueItems, progression, tuning, character) { }

    internal IReadOnlyDictionary<long, CorpseContainer> Corpses => _actors.All
        .Select(actor => actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse) && corpse is not null
            ? new CorpseContainer(actor.DurableId, corpse.Owner, corpse.OriginatingSequence, corpse.HasRegisteredInventory, corpse.IsInteractable)
            : null)
        .Where(corpse => corpse is not null)
        .Cast<CorpseContainer>()
        .ToDictionary(corpse => corpse.ActorId);
    internal CorpseLootEvidence? LastEvidence { get; private set; }
    internal CorpseLootCommitEvidence? LastCommit { get; private set; }

    /// <summary>Recreates durable corpse ownership and current Engine contents without re-running loot policy.</summary>
    internal void Restore(IReadOnlyList<DaggerfallCorpseSave> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        foreach (DaggerfallCorpseSave value in saved.OrderBy(corpse => corpse.ActorId))
        {
            value.Validate();
            if (!_actors.TryGet(value.ActorId, out ActorState? actor) || !actor.IsDefeated)
                throw new ArgumentException($"Saved corpse '{value.ActorId}' does not correspond to a defeated registered actor.", nameof(saved));
            if (actor.Actor.TryGet<CorpseLootComponent>(out _))
                throw new InvalidOperationException("Corpse state can only be restored into a fresh session.");
            CorpseLootComponent corpse = _corpseLoot.Restore(
                CorpseIdentity(value.ActorId),
                CorpseType,
                value.OriginatingSequence,
                value.IsRegistered,
                value.IsInteractable,
                RestoreSeeds(value));
            actor.Actor.Add(corpse);
            RegisterRestoredMetadata(value);
        }
    }

    /// <summary>
    /// Creates and seeds a corpse-owned Engine inventory exactly once after
    /// actor mechanics has applied a death. Repeated death notifications keep
    /// the same actor-attached component and never duplicate its contents.
    /// </summary>
    internal void Create(ActorDiedFact fact)
    {
        if (!_actors.TryGet(fact.ActorId, out ActorState? state)
            || fact.ActorId <= 0
            || !state.IsDefeated
            || !_definitions.TryGetValue(fact.ActorId, out DaggerfallActorDefinition? actor)) return;

        if (state.Actor.TryGet<CorpseLootComponent>(out _)) return;

        // A corpse is a distinct container, even when the actor already has a quiver.
        DaggerfallLootPopulationResult generated = _population.Generate(new DaggerfallLootPopulationRequest(
            new DaggerfallLootPopulationId("corpse", checked((ulong)fact.ActorId)),
            DaggerfallItemOwner.Corpse(fact.ActorId),
            actor.LootTableKey ?? "-",
            _progression.Level,
            fact.OriginatingGeneration,
            fact.OriginatingSequence,
            _character.Identity.RaceId,
            _character.Identity.Gender == DaggerfallCharacterGender.Male ? "male" : "female",
            _character.Identity.Gender == DaggerfallCharacterGender.Male ? "MensClothing" : "WomensClothing"));
        // Donor RemoveLootContainer disables interaction but preserves the
        // corpse marker. Even an empty generated corpse is targetable once so
        // the player receives a truthful semantic result.
        state.Actor.Add(_corpseLoot.Create(CorpseIdentity(fact.ActorId), CorpseType, fact.OriginatingSequence, generated.Seeds));
        _population.RegisterGeneratedMetadata(generated, _itemInstances);
    }

    /// <summary>Reads Engine visibility and prepares, but does not publish, an explicit loot action.</summary>
    internal PendingCorpseLoot? PrepareLoot(PlayerControlState player, LookReceipt look, long? targetActorId = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Position is not WorldPoint position)
        {
            LastEvidence = null;
            return null;
        }

        (ActorState Actor, CorpseLootComponent Corpse)[] eligible = _actors.All
            .Where(actor => actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse)
                && corpse is { IsInteractable: true } && (targetActorId is null || actor.DurableId == targetActorId))
            .Select(actor =>
            {
                if (!actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse) || corpse is null)
                    throw new InvalidOperationException("Eligible corpse lost its component before the query was assembled.");
                return (Actor: actor, Corpse: corpse);
            })
            .OrderBy(value => value.Actor.DurableId)
            .ToArray();
        if (eligible.Length == 0)
        {
            LastEvidence = null;
            return null;
        }

        PerceptionQueryRequest request = new(
            _spatial.Session,
            new PerceptionObserver[] { new((ulong)DaggerfallActorIdentity.PlayerEntityId, position.ToVector(), look.Forward, _tuning.MaximumDistance, _tuning.MinimumFacingCosine, 1d) },
            eligible.Select(value => new PerceptionTarget((ulong)value.Actor.DurableId, value.Actor.Position.ToVector())).ToArray(),
            ReadOnlyMemory<SpatialEntityCollider>.Empty,
            DaggerfallPerceptionQueryDefaults.AnyProjectionIdentity,
            DaggerfallPerceptionQueryDefaults.FirstPairCursor,
            DaggerfallPerceptionQueryDefaults.CompleteQueryPageSize);
        PerceptionReadoutLeaseReceipt receipt = _perception.QueryVisibility(request);
        long? selected = receipt.Pairs.ToArray()
            .Where(pair => pair.Observer == (ulong)DaggerfallActorIdentity.PlayerEntityId
                && pair.Kind == PerceptionPairKind.Visible
                && pair.Target <= long.MaxValue
                && _actors.TryGet((long)pair.Target, out ActorState actor)
                && actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse)
                && corpse is { IsInteractable: true })
            .OrderBy(pair => pair.Distance)
            .ThenBy(pair => pair.Target)
            .Select(pair => (long?)pair.Target)
            .FirstOrDefault();
        LastEvidence = new CorpseLootEvidence(request, receipt, selected);
        if (selected is not long actorId || !_actors.TryGet(actorId, out ActorState selectedActor)
            || !selectedActor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse)
            || corpse is null) return null;

        if (!corpse.HasRegisteredInventory)
            return new PendingCorpseLoot(actorId, corpse, [], IsEmpty: true);

        // All fallible fact shaping happens before the later Engine publish.
        InventoryView contents = _corpseLoot.Read(corpse)!;
        LootAwardedFact[] facts = contents.Stacks
            .OrderBy(stack => stack.Definition.Value, StringComparer.Ordinal)
            .Select(stack => new LootAwardedFact(actorId, stack.Definition.Value, stack.Quantity, corpse.OriginatingSequence))
            .Concat(contents.UniqueItems
                .OrderBy(item => item.Entity.Value)
                .Select(item => new LootAwardedFact(actorId, item.Definition.Value, 1, corpse.OriginatingSequence)))
            .ToArray();
        return facts.Length == 0
            ? new PendingCorpseLoot(actorId, corpse, [], IsEmpty: true)
            : new PendingCorpseLoot(actorId, corpse, facts, IsEmpty: false);
    }

    /// <summary>
    /// Shapes loot for a corpse that the contextual activation owner already selected through an
    /// Engine visibility query. This preserves that target instead of issuing a second broad query
    /// which could choose an overlapping corpse, while still refusing an unloaded or no-longer
    /// interactable component.
    /// </summary>
    internal PendingCorpseLoot? PrepareResolvedLoot(long actorId)
    {
        if (!_actors.TryGet(actorId, out ActorState selectedActor)
            || !selectedActor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse)
            || corpse is not { IsInteractable: true }) return null;

        if (!corpse.HasRegisteredInventory)
            return new PendingCorpseLoot(actorId, corpse, [], IsEmpty: true);

        InventoryView contents = _corpseLoot.Read(corpse)!;
        LootAwardedFact[] facts = contents.Stacks
            .OrderBy(stack => stack.Definition.Value, StringComparer.Ordinal)
            .Select(stack => new LootAwardedFact(actorId, stack.Definition.Value, stack.Quantity, corpse.OriginatingSequence))
            .Concat(contents.UniqueItems
                .OrderBy(item => item.Entity.Value)
                .Select(item => new LootAwardedFact(actorId, item.Definition.Value, 1, corpse.OriginatingSequence)))
            .ToArray();
        return facts.Length == 0
            ? new PendingCorpseLoot(actorId, corpse, [], IsEmpty: true)
            : new PendingCorpseLoot(actorId, corpse, facts, IsEmpty: false);
    }

    /// <summary>
    /// Applies one validated take request during the admitted update: the Engine transfer commits
    /// first, and only then do its completed-change facts append. A Mechanics admission rejection
    /// leaves this corpse untouched for a later explicit retry; programming errors still escape.
    /// </summary>
    internal CorpseLootCommitResult TryCommitLoot(PendingCorpseLoot pending, FactBuffer<IProductFact> facts)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(facts);
        if (!_actors.TryGet(pending.ActorId, out ActorState actor)
            || !actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? current)
            || current is null
            || !ReferenceEquals(current, pending.Corpse)
            || !current.IsInteractable)
        {
            LastCommit = new CorpseLootCommitEvidence(pending.ActorId, false, "The prepared loot action is no longer current.");
            return CorpseLootCommitResult.Rejected;
        }

        if (pending.IsEmpty)
        {
            _corpseLoot.TransferAll(current, _playerOwner);
            facts.Append(new CorpseSearchedEmptyFact(pending.ActorId));
            LastCommit = new CorpseLootCommitEvidence(pending.ActorId, true, null);
            return CorpseLootCommitResult.Committed;
        }
        try
        {
            if (_encumbrance is not null && pending.Selection is { } requested
                && !_encumbrance.CanCarry(_catalog.RequireItem(new DaggerfallItemId(requested.Item.Value)), requested.Quantity))
            {
                LastCommit = new CorpseLootCommitEvidence(pending.ActorId, false, "You cannot carry any more.");
                return CorpseLootCommitResult.Rejected;
            }
            if (pending.Selection is { Stack: InventoryStackId source, DestinationStack: InventoryStackId destination })
                _itemInstances.EnsureTransferCompatible(DaggerfallItemOwner.Corpse(pending.ActorId), DaggerfallItemOwner.Player, source, destination);
            // The transfer commits first. All code after it is deterministic local bookkeeping and
            // completed-change facts, which is why a rejection appends nothing.
            CorpseLootTransferResult transfer = pending.Selection is { } selection
                ? _corpseLoot.Transfer(current, _playerOwner, selection, pending.ExpectedWorldRevision!.Value)
                : _corpseLoot.TransferAll(current, _playerOwner, pending.ExpectedWorldRevision);
            SyncTransferredMetadata(pending.ActorId, current, transfer.Transfer);
        }
        catch (Exception rejection) when (rejection is MechanicsException or InvalidOperationException)
        {
            LastCommit = new CorpseLootCommitEvidence(pending.ActorId, false, rejection.Message);
            return CorpseLootCommitResult.Rejected;
        }
        bool emptied = !current.IsInteractable;
        foreach (LootAwardedFact fact in pending.Facts) facts.Append(fact);
        if (emptied) facts.Append(new CorpseLootedFact(pending.ActorId));
        LastCommit = new CorpseLootCommitEvidence(pending.ActorId, true, null);
        return CorpseLootCommitResult.Committed;
    }

    internal InventoryView? ReadContents(long actorId) =>
        _actors.TryGet(actorId, out ActorState actor) && actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse) && corpse is not null
            ? _corpseLoot.Read(corpse) : null;

    internal string ContainerName(long actorId) => _definitions[actorId].Id.Value;

    /// <summary>Selects a stable compatible player stack before the Engine transfer is prepared.</summary>
    internal InventoryStackId ResolveTakeDestination(long corpseActorId, InventoryStackId source, InventoryStackId freshDestination)
    {
        DaggerfallItemInstanceMetadata sourceMetadata = _itemInstances.RequireStack(DaggerfallItemOwner.Corpse(corpseActorId), source);
        return _containers.Read(_playerOwner).Stacks
            .OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Select(stack => (Stack: stack.Id, Metadata: _itemInstances.RequireStack(DaggerfallItemOwner.Player, stack.Id)))
            .Where(candidate => sourceMetadata.IsStackCompatibleWith(candidate.Metadata))
            .Select(candidate => candidate.Stack)
            .FirstOrDefault() ?? freshDestination;
    }

    private static readonly EntityTypeId CorpseType = new("daggerfall.corpse");
    private static DurableIdentityReference CorpseIdentity(long actorId) => new(DurableIdentityKind.Container, checked((ulong)actorId));

    private static IReadOnlyList<InventoryContainerSeed> RestoreSeeds(DaggerfallCorpseSave value) => value.Stacks
        .Select(stack => new InventoryContainerSeed(new InventoryItemId(stack.ItemId), stack.Quantity, Stack: InventoryStackId.Parse(stack.StackId)))
        .Concat(value.UniqueItems.Select(unique => new InventoryContainerSeed(
            new InventoryItemId(unique.ItemId),
            UniqueItem: new DurableIdentityReference(DurableIdentityKind.Item, unique.EntityId))))
        .ToArray();

    private void RegisterRestoredMetadata(DaggerfallCorpseSave corpse)
    {
        DaggerfallItemOwner owner = DaggerfallItemOwner.Corpse(corpse.ActorId);
        foreach (DaggerfallStackSave stack in corpse.Stacks)
            _itemInstances.RegisterStack(owner, InventoryStackId.Parse(stack.StackId), DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata));
        foreach (DaggerfallUniqueSave unique in corpse.UniqueItems)
            _itemInstances.RegisterUnique(unique.EntityId, DaggerfallItemInstanceMetadata.Restore(unique.ItemId, unique.Metadata));
    }

    private void SyncTransferredMetadata(long corpseActorId, CorpseLootComponent corpse, InventoryContainerTransferReceipt? transfer)
    {
        if (transfer is null) return;
        InventoryView remaining = _corpseLoot.Read(corpse)!;
        DaggerfallItemOwner source = DaggerfallItemOwner.Corpse(corpseActorId);
        foreach (InventoryContainerStackTransfer stack in transfer.Stacks)
        {
            bool exhausted = !remaining.Stacks.Any(value => value.Id == stack.SourceStack);
            _itemInstances.TransferStack(source, DaggerfallItemOwner.Player, stack.SourceStack, stack.DestinationStack, exhausted);
        }
        foreach (InventoryContainerUniqueTransfer unique in transfer.UniqueItems)
        {
            DurableIdentityReference identity = _actors.Entities.IdentityOf(new EntityId(unique.EntityId));
            _itemInstances.MoveUnique(identity.Value, DaggerfallItemOwner.Player);
        }
    }

}

/// <summary>Ruleset-owned durable mapping from a defeated actor to its Engine inventory owner.</summary>
internal sealed record CorpseContainer(long ActorId, EntityId Owner, ulong OriginatingSequence, bool IsRegistered, bool IsInteractable);

/// <summary>One validated take request: the selection and revision a take applies, if it commits.</summary>
internal sealed record PendingCorpseLoot(long ActorId, CorpseLootComponent Corpse, IReadOnlyList<LootAwardedFact> Facts, bool IsEmpty,
    InventoryContainerSelection? Selection = null, ulong? ExpectedWorldRevision = null);

internal enum CorpseLootCommitResult { Committed, Rejected }
internal sealed record CorpseLootCommitEvidence(long ActorId, bool Committed, string? Rejection);

/// <summary>Copied Engine visibility receipt and the deterministic corpse choice for one explicit interaction.</summary>
internal sealed record CorpseLootEvidence(PerceptionQueryRequest Request, PerceptionReadoutLeaseReceipt Receipt, long? SelectedActorId);
internal sealed record GeneratedLootSeed(InventoryContainerSeed Seed, DaggerfallItemInstanceMetadata? Metadata);
