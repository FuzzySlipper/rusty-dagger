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
    private readonly EntityId _playerOwner;
    private readonly ActorsState _actors;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _definitions;
    private readonly DaggerfallDefinitions _catalog;
    private readonly IRandomService _random;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly ProgressionState _progression;
    private readonly DaggerfallLootInteractionTuning _tuning;
    private readonly CorpseLootCoordinator _corpseLoot;

    internal DaggerfallCorpseLootModule(
        IPerceptionService perception,
        SpatialMovementSystem spatial,
        MechanicsInventoryContainerCoordinator containers,
        EntityId playerOwner,
        ActorsState actors,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions,
        DaggerfallDefinitions catalog,
        IRandomService random,
        DaggerfallUniqueItemAllocator uniqueItems,
        ProgressionState progression,
        DaggerfallLootInteractionTuning tuning)
    {
        _perception = perception ?? throw new ArgumentNullException(nameof(perception));
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _playerOwner = playerOwner;
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _uniqueItems = uniqueItems ?? throw new ArgumentNullException(nameof(uniqueItems));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _corpseLoot = new CorpseLootCoordinator(_actors.Entities, _containers);
    }

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
        IReadOnlyList<InventoryContainerSeed> seeds = GenerateSeeds(fact, actor);
        // Donor RemoveLootContainer disables interaction but preserves the
        // corpse marker. Even an empty generated corpse is targetable once so
        // the player receives a truthful semantic result.
        state.Actor.Add(_corpseLoot.Create(CorpseIdentity(fact.ActorId), CorpseType, fact.OriginatingSequence, seeds));
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
            // The transfer commits first. All code after it is deterministic local bookkeeping and
            // completed-change facts, which is why a rejection appends nothing.
            if (pending.Selection is { } selection)
                _corpseLoot.Transfer(current, _playerOwner, selection, pending.ExpectedWorldRevision!.Value);
            else _corpseLoot.TransferAll(current, _playerOwner, pending.ExpectedWorldRevision);
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

    private static readonly EntityTypeId CorpseType = new("daggerfall.corpse");
    private static DurableIdentityReference CorpseIdentity(long actorId) => new(DurableIdentityKind.Container, checked((ulong)actorId));

    private static IReadOnlyList<InventoryContainerSeed> RestoreSeeds(DaggerfallCorpseSave value) => value.Stacks
        .Select(stack => new InventoryContainerSeed(new InventoryItemId(stack.ItemId), stack.Quantity))
        .Concat(value.UniqueItems.Select(unique => new InventoryContainerSeed(
            new InventoryItemId(unique.ItemId),
            UniqueItem: new DurableIdentityReference(DurableIdentityKind.Item, unique.EntityId))))
        .ToArray();

    private IReadOnlyList<InventoryContainerSeed> GenerateSeeds(ActorDiedFact fact, DaggerfallActorDefinition actor)
    {
        if (actor.LootTableKey is not string tableKey) return [];
        DaggerfallLootResult loot = DaggerfallLootPolicy.Generate(
            _catalog,
            tableKey,
            _progression.Level,
            (id, minimum, maximum) => checked((int)_random.DrawKeyed(new KeyedRngRequest(
                LootRandomKey.Seed,
                LootRandomKey.Scope,
                LootRandomKey.For(fact.OriginatingGeneration, fact.OriginatingSequence, fact.ActorId, id),
                minimum,
                maximum)).Value));
        List<InventoryContainerSeed> seeds = [];
        foreach (DaggerfallLootDrop drop in loot.Drops)
        {
            DaggerfallItemDefinition item = _catalog.Items[new DaggerfallItemId(drop.ItemId)];
            if (item.IsFungible)
            {
                seeds.Add(new InventoryContainerSeed(new InventoryItemId(drop.ItemId), checked((ulong)drop.Quantity)));
                continue;
            }
            int ordinal = seeds.Count;
            // Pick the drop's durable identity first, then derive the Engine handle
            // from it; the seed carries both facts so the container coordinator can
            // materialize the item without re-deciding what the identity means.
            DurableIdentityReference identity = _uniqueItems.AllocateReference();
            seeds.Add(new InventoryContainerSeed(
                new InventoryItemId(drop.ItemId),
                UniqueItem: identity));
        }
        return seeds;
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
