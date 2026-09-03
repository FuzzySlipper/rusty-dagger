using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
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
    private readonly Dictionary<long, CorpseContainer> _corpses = [];

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
    }

    internal IReadOnlyDictionary<long, CorpseContainer> Corpses => _corpses;
    internal CorpseLootEvidence? LastEvidence { get; private set; }
    internal CorpseLootCommitEvidence? LastCommit { get; private set; }

    /// <summary>Recreates durable corpse ownership and current Engine contents without re-running loot policy.</summary>
    internal void Restore(IReadOnlyList<DaggerfallCorpseSave> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (_corpses.Count != 0) throw new InvalidOperationException("Corpse state can only be restored into a fresh session.");
        foreach (DaggerfallCorpseSave value in saved.OrderBy(corpse => corpse.ActorId))
        {
            value.Validate();
            if (!_actors.TryGet(value.ActorId, out ActorState? actor) || !actor.IsDefeated)
                throw new ArgumentException($"Saved corpse '{value.ActorId}' does not correspond to a defeated authored actor.", nameof(saved));
            EntityId owner = new(checked((ulong)value.ActorId));
            CorpseContainer corpse = new(value.ActorId, owner, value.OriginatingSequence, [], value.IsRegistered, value.IsRegistered, value.IsInteractable);
            if (value.IsRegistered)
            {
                _containers.RegisterOwner(owner);
                List<InventoryContainerSeed> seeds = value.Stacks
                    .Select(stack => new InventoryContainerSeed(new InventoryItemId(stack.ItemId), stack.Quantity))
                    .Concat(value.UniqueItems.Select(unique => new InventoryContainerSeed(
                        new InventoryItemId(unique.ItemId),
                        UniqueIdentity: $"daggerfall.restore.corpse.{value.ActorId}.{unique.EntityId}",
                        UniqueEntityId: unique.EntityId)))
                    .ToList();
                if (seeds.Count > 0) _containers.Seed(owner, seeds);
            }
            _corpses.Add(value.ActorId, corpse);
        }
    }

    /// <summary>
    /// Creates and seeds a corpse-owned Engine inventory exactly once. This is
    /// idempotent reconciliation, not FactBuffer rollback: actor Mechanics has
    /// already committed by the time its death fact is reacted. A later
    /// presentation failure replays the fact against the same tracked owner,
    /// seeds, and keyed random result without duplicating Engine contents.
    /// </summary>
    internal void Create(ActorDiedFact fact)
    {
        if (!_actors.TryGet(fact.ActorId, out ActorState? state)
            || fact.ActorId <= 0
            || !state.IsDefeated
            || !_definitions.TryGetValue(fact.ActorId, out DaggerfallActorDefinition? actor)) return;

        if (_corpses.TryGetValue(fact.ActorId, out CorpseContainer? existing))
        {
            if (!existing.IsRegistered && existing.Seeds.Count > 0)
            {
                _containers.RegisterOwner(existing.Owner);
                existing = existing with { IsRegistered = true };
                _corpses[fact.ActorId] = existing;
            }
            if (existing.IsRegistered && !existing.IsSeeded) SeedRegistered(existing, existing.Seeds);
            return;
        }

        // Actor ids are already validated as non-zero Mechanics ids at session
        // composition.  An enemy has no inventory registered yet, so the same
        // stable id is safe as its durable corpse owner in this InventoryWorld.
        IReadOnlyList<InventoryContainerSeed> seeds = GenerateSeeds(fact, actor);
        EntityId owner = new(checked((ulong)fact.ActorId));
        // Donor RemoveLootContainer disables interaction but preserves the
        // corpse marker. Even an empty generated corpse is targetable once so
        // the player receives a truthful semantic result.
        CorpseContainer corpse = new(fact.ActorId, owner, fact.OriginatingSequence, seeds, IsRegistered: false, IsSeeded: false, IsInteractable: true);
        _corpses.Add(fact.ActorId, corpse);
        if (seeds.Count > 0)
        {
            // Generated contents and their unique identities are fully
            // validated before this product reserves an Engine inventory owner.
            // The Engine API intentionally has no unregister operation, so an
            // empty corpse does not create a needless registered owner.
            _containers.RegisterOwner(owner);
            corpse = corpse with { IsRegistered = true };
            _corpses[fact.ActorId] = corpse;
            SeedRegistered(corpse, seeds);
        }
    }

    /// <summary>Reads Engine visibility and prepares, but does not publish, an explicit loot action.</summary>
    internal PendingCorpseLoot? PrepareLoot(PlayerControlState player, LookReceipt look)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Position is not WorldPoint position)
        {
            LastEvidence = null;
            return null;
        }

        CorpseContainer[] eligible = _corpses.Values
            .Where(corpse => corpse.IsInteractable)
            .OrderBy(corpse => corpse.ActorId)
            .ToArray();
        if (eligible.Length == 0)
        {
            LastEvidence = null;
            return null;
        }

        PerceptionQueryRequest request = new(
            _spatial.Session,
            new PerceptionObserver[] { new((ulong)DaggerfallActorIdentity.PlayerEntityId, position.ToVector(), look.Forward, _tuning.MaximumDistance, _tuning.MinimumFacingCosine, 1d) },
            eligible.Select(corpse => new PerceptionTarget((ulong)corpse.ActorId, _actors.All[corpse.ActorId].Position.ToVector())).ToArray(),
            ReadOnlyMemory<SpatialEntityCollider>.Empty,
            DaggerfallPerceptionQueryDefaults.AnyProjectionIdentity,
            DaggerfallPerceptionQueryDefaults.FirstPairCursor,
            DaggerfallPerceptionQueryDefaults.CompleteQueryPageSize);
        PerceptionReadoutLeaseReceipt receipt = _perception.QueryVisibility(request);
        long? selected = receipt.Pairs.ToArray()
            .Where(pair => pair.Observer == (ulong)DaggerfallActorIdentity.PlayerEntityId
                && pair.Kind == PerceptionPairKind.Visible
                && pair.Target <= long.MaxValue
                && _corpses.TryGetValue((long)pair.Target, out CorpseContainer? corpse)
                && corpse.IsInteractable)
            .OrderBy(pair => pair.Distance)
            .ThenBy(pair => pair.Target)
            .Select(pair => (long?)pair.Target)
            .FirstOrDefault();
        LastEvidence = new CorpseLootEvidence(request, receipt, selected);
        if (selected is not long actorId || !_corpses.TryGetValue(actorId, out CorpseContainer? container)) return null;

        if (!container.IsRegistered)
            return new PendingCorpseLoot(container, [], IsEmpty: true);

        // All fallible fact shaping happens before the later Engine publish.
        InventoryView contents = _containers.Read(container.Owner);
        LootAwardedFact[] facts = contents.Stacks
            .OrderBy(stack => stack.Definition.Value, StringComparer.Ordinal)
            .Select(stack => new LootAwardedFact(actorId, stack.Definition.Value, stack.Quantity, container.OriginatingSequence))
            .Concat(contents.UniqueItems
                .OrderBy(item => item.Entity.Value)
                .Select(item => new LootAwardedFact(actorId, item.Definition.Value, 1, container.OriginatingSequence)))
            .ToArray();
        return facts.Length == 0
            ? new PendingCorpseLoot(container, [], IsEmpty: true)
            : new PendingCorpseLoot(container, facts, IsEmpty: false);
    }

    /// <summary>
    /// Attempts the single Engine publication after the admitted update has
    /// already committed. A Mechanics admission rejection leaves this corpse
    /// untouched for a later explicit retry; programming errors still escape.
    /// </summary>
    internal CorpseLootCommitResult TryCommitLoot(PendingCorpseLoot pending, FactBuffer<IProductFact> facts)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(facts);
        if (!_corpses.TryGetValue(pending.Container.ActorId, out CorpseContainer? current)
            || current != pending.Container
            || !current.IsInteractable) throw new InvalidOperationException("The prepared corpse loot action is no longer current.");

        if (pending.IsEmpty)
        {
            _corpses[current.ActorId] = current with { IsInteractable = false };
            facts.Append(new CorpseSearchedEmptyFact(current.ActorId));
            LastCommit = new CorpseLootCommitEvidence(pending.Container.ActorId, true, null);
            return CorpseLootCommitResult.Committed;
        }
        try
        {
            // TransferAll is the sole Engine publication.  All code after it
            // is deterministic local bookkeeping and preconstructed facts.
            _containers.TransferAll(current.Owner, _playerOwner);
        }
        catch (MechanicsException rejection)
        {
            LastCommit = new CorpseLootCommitEvidence(pending.Container.ActorId, false, rejection.Message);
            return CorpseLootCommitResult.Rejected;
        }
        _corpses[current.ActorId] = current with { IsInteractable = false };
        foreach (LootAwardedFact fact in pending.Facts) facts.Append(fact);
        facts.Append(new CorpseLootedFact(current.ActorId));
        LastCommit = new CorpseLootCommitEvidence(pending.Container.ActorId, true, null);
        return CorpseLootCommitResult.Committed;
    }

    private void SeedRegistered(CorpseContainer corpse, IReadOnlyList<InventoryContainerSeed> seeds)
    {
        if (seeds.Count == 0) return;
        _containers.Seed(corpse.Owner, seeds);
        _corpses[corpse.ActorId] = corpse with { IsSeeded = true, IsInteractable = true };
    }

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
            seeds.Add(new InventoryContainerSeed(
                new InventoryItemId(drop.ItemId),
                UniqueIdentity: $"daggerfall.loot.a{fact.ActorId}.g{fact.OriginatingGeneration}.s{fact.OriginatingSequence}.{ordinal}",
                UniqueEntityId: _uniqueItems.Allocate()));
        }
        return seeds;
    }
}

/// <summary>Ruleset-owned durable mapping from a defeated actor to its Engine inventory owner.</summary>
internal sealed record CorpseContainer(long ActorId, EntityId Owner, ulong OriginatingSequence, IReadOnlyList<InventoryContainerSeed> Seeds, bool IsRegistered, bool IsSeeded, bool IsInteractable);

/// <summary>Prevalidated product policy waiting for the outer Engine publication boundary.</summary>
internal sealed record PendingCorpseLoot(CorpseContainer Container, IReadOnlyList<LootAwardedFact> Facts, bool IsEmpty);

internal enum CorpseLootCommitResult { Committed, Rejected }
internal sealed record CorpseLootCommitEvidence(long ActorId, bool Committed, string? Rejection);

/// <summary>Copied Engine visibility receipt and the deterministic corpse choice for one explicit interaction.</summary>
internal sealed record CorpseLootEvidence(PerceptionQueryRequest Request, PerceptionReadoutLeaseReceipt Receipt, long? SelectedActorId);
