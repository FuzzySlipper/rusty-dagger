using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Loot;

/// <summary>One materialized dropped-item container in the shared Engine inventory store.</summary>
internal sealed record DaggerfallGroundContainer(DaggerfallWorldProfileKey Profile, long Id, EntityId Owner, WorldPoint Position, string? PropertyPlacement = null, long StockedDay = 0);

/// <summary>
/// Daggerfall policy for ground piles. Kit's container coordinator owns every transfer; this owner
/// supplies the durable container identity, position, and Daggerfall metadata move after commit.
/// </summary>
internal sealed class DaggerfallGroundContainers
{
    internal static readonly EntityTypeId GroundContainerType = new("daggerfall.ground-container");
    private readonly MechanicsInventoryContainerCoordinator _containers;
    private readonly DaggerfallItemInstances _instances;
    private readonly EntityId _player;
    private readonly DurableIdentityAllocator _identities;
    private readonly Dictionary<long, DaggerfallGroundContainer> _ground = [];
    private readonly Dictionary<long, DaggerfallGroundContainerSave> _unloaded = [];
    private DaggerfallWorldProfileKey _activeProfile;
    private HashSet<DaggerfallExteriorCellId>? _residentCells;
    private DaggerfallExteriorWorldOrigin _origin;

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
        foreach (var value in _unloaded.Values.Where(value => value.Profile.Require() == _activeProfile).ToArray())
            _unloaded[value.Id] = value with { X = value.X + delta.X, Y = value.Y + delta.Y, Z = value.Z + delta.Z };
    }

    /// <summary>Detached values for issued piles with no current Engine owner.</summary>
    internal IReadOnlyCollection<DaggerfallGroundContainerSave> Unloaded => _unloaded.Values.ToArray();

    /// <summary>Completes a committed quest transfer from detached containment values.</summary>
    internal void RemoveRetainedContents(IReadOnlySet<ulong> uniqueItems, IReadOnlySet<(DaggerfallItemOwner Owner, string StackId)> stacks)
    {
        foreach (var value in _unloaded.Values.ToArray())
        {
            if (!value.Inventory.UniqueItems.Any(item => uniqueItems.Contains(item.EntityId))
                && !value.Inventory.Stacks.Any(item => stacks.Contains((DaggerfallItemOwner.Ground(value.Id), item.StackId)))) continue;
            var inventory = value.Inventory with
            {
                UniqueItems = value.Inventory.UniqueItems.Where(item => !uniqueItems.Contains(item.EntityId)).ToArray(),
                Stacks = value.Inventory.Stacks.Where(item => !stacks.Contains((DaggerfallItemOwner.Ground(value.Id), item.StackId))).ToArray(),
            };
            if (value.PropertyPlacement is null && inventory.Stacks.Length == 0 && inventory.UniqueItems.Length == 0)
            {
                _unloaded.Remove(value.Id);
                _identities.Remove(new(DurableIdentityKind.Container, checked((ulong)value.Id)));
            }
            else _unloaded[value.Id] = value with { Inventory = inventory };
        }
    }

    internal DaggerfallGroundContainerSave[] Capture() => _ground.Values.Select(Capture)
        .Concat(_unloaded.Values).OrderBy(value => value.Id).ToArray();

    internal void SwitchProfile(DaggerfallWorldProfileKey profile)
    {
        profile.Validate();
        if (profile == _activeProfile) return;
        foreach (var container in _ground.Values.Where(value => value.Profile != profile).ToArray()) Unload(container);
        _activeProfile = profile;
        _residentCells = null;
        Reconcile();
    }

    /// <summary>Loose piles share the existing terrain window and the Engine's current origin.</summary>
    internal void ReconcileExteriorResidency(IReadOnlyCollection<DaggerfallExteriorCellId> cells, DaggerfallExteriorWorldOrigin origin)
    {
        if (_activeProfile.Kind != DaggerfallWorldProfileKind.Exterior)
            throw new InvalidOperationException("Only an exterior profile has cell-owned ground piles.");
        _residentCells = cells.ToHashSet();
        _origin = origin;
        Reconcile();
    }

    private bool ShouldMaterialize(DaggerfallWorldProfileKey profile, WorldPoint position)
    {
        if (profile != _activeProfile) return false;
        if (_residentCells is null) return true;
        return _residentCells.Contains(DaggerfallExteriorSessionOrigin.CellForLocalPosition(
            position, _origin, DaggerfallExteriorWorldBounds.Daggerfall));
    }

    private void Reconcile()
    {
        foreach (var container in _ground.Values.Where(value => !ShouldMaterialize(value.Profile, value.Position)).ToArray()) Unload(container);
        foreach (var value in _unloaded.Values.Where(value => ShouldMaterialize(value.Profile.Require(), new(value.X, value.Y, value.Z))).ToArray())
        {
            Materialize(value);
            _unloaded.Remove(value.Id);
        }
    }

    private DaggerfallGroundContainerSave Capture(DaggerfallGroundContainer container)
    {
        var (stacks, uniques) = DaggerfallInventorySaveBoundary.CaptureContents(_containers.Read(container.Owner),
            DaggerfallItemOwner.Ground(container.Id), _instances, _containers.Entities);
        return new(DaggerfallWorldProfileKeySave.Capture(container.Profile), container.Id,
            container.Position.X, container.Position.Y, container.Position.Z, new(stacks, uniques, []), container.PropertyPlacement, container.StockedDay);
    }

    private void Unload(DaggerfallGroundContainer container)
    {
        DaggerfallGroundContainerSave saved = Capture(container);
        InventoryView contents = _containers.Read(container.Owner);
        using (var edit = _containers.Entities.Store.Get<InventoryComponent>(container.Owner).Store.Prepare())
        {
            foreach (var stack in contents.Stacks) edit.Consume(container.Owner, stack.Id, stack.Quantity);
            foreach (var item in contents.UniqueItems) edit.DestroyUnique(item.Entity);
            edit.RetireOwner(container.Owner);
            edit.Publish();
        }
        _unloaded.Add(container.Id, saved);
        _ground.Remove(container.Id);
        _instances.RemoveOwner(DaggerfallItemOwner.Ground(container.Id), retireBindings: false);
        foreach (var item in saved.Inventory.UniqueItems)
        {
            _instances.RemoveUnique(item.EntityId);
            _containers.Entities.Destroy(new(DurableIdentityKind.Item, item.EntityId));
        }
        _containers.Entities.Destroy(new(DurableIdentityKind.Container, checked((ulong)container.Id)));
    }

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

    /// <summary>Materializes source furniture through the same durable container and item owners as ground loot.</summary>
    internal DaggerfallGroundContainer EnsureProperty(DaggerfallPropertyContainerPlacement placement,
        long day, bool playerOwned, DaggerfallItemFactory factory, DaggerfallUniqueItemAllocator uniqueItems,
        Func<string, int, int, int> draw, int level, string race, string gender)
    {
        var container = All.Values.SingleOrDefault(value => value.PropertyPlacement == placement.Id);
        if (container is not null && (playerOwned || container.StockedDay >= day)) return container;
        if (container is null)
        {
            var identity = _identities.Allocate(DurableIdentityKind.Container);
            var entity = _containers.Entities.Create(identity, GroundContainerType);
            _containers.RegisterOwner(entity);
            container = new(_activeProfile, checked((long)identity.Value), entity, placement.Position, placement.Id, day);
            _ground.Add(container.Id, container);
        }
        else
        {
            // Source restocking replaces old generated furniture contents once per game day.
            var contents = _containers.Read(container.Owner);
            using var edit = _containers.Entities.Store.Get<InventoryComponent>(container.Owner).Store.Prepare();
            foreach (var stack in contents.Stacks) edit.Consume(container.Owner, stack.Id, stack.Quantity);
            foreach (var item in contents.UniqueItems) edit.DestroyUnique(item.Entity);
            edit.Publish();
            foreach (var stack in contents.Stacks) _instances.RemoveStack(DaggerfallItemOwner.Ground(container.Id), stack.Id);
            foreach (var item in contents.UniqueItems)
            {
                var identity = _containers.Entities.IdentityOf(item.Entity);
                _instances.RemoveUnique(identity.Value);
                _containers.Entities.Destroy(identity);
                uniqueItems.Remove(identity);
            }
        }
        container = container with { StockedDay = day };
        _ground[container.Id] = container;
        if (playerOwned || placement.ItemGroups.Length == 0) return container;
        string key = $"property:{_activeProfile.LogicalId}:{placement.Id}:{day}";
        string group = placement.ItemGroups[draw(key + ":group", 0, placement.ItemGroups.Length - 1)];
        if (group is "MensClothing" or "WomensClothing") group = gender == "male" ? "MensClothing" : "WomensClothing";
        List<InventoryContainerSeed> seeds = [];
        List<(DaggerfallCreatedItem Item, InventoryStackId? Stack, DurableIdentityReference? Unique)> items = [];
        int continuation = 100;
        int ordinal = 0;
        do
        {
            string itemKey = $"{key}:{ordinal}";
            var item = factory.Create(new(group, itemKey, DaggerfallItemOwner.Ground(container.Id), Level: level,
                Race: race, Gender: gender));
            InventoryStackId? stack = item.Stackable ? InventoryStackId.Parse($"daggerfall.property.{container.Id}.{day}.{ordinal}") : null;
            DurableIdentityReference? unique = item.Stackable ? null : uniqueItems.AllocateReference();
            seeds.Add(new(item.Item, item.Quantity, Stack: stack, UniqueItem: unique));
            items.Add((item, stack, unique));
            ordinal++;
            continuation >>= 1;
        } while (draw(key + $":continue:{ordinal}", 0, 99) <= continuation);
        _containers.Seed(container.Owner, seeds);
        foreach (var (item, stack, unique) in items)
        {
            if (stack is not null) _instances.RegisterStack(item.Metadata.Owner, stack, item.Metadata);
            else _instances.RegisterUnique(unique!.Value.Value, item.Metadata);
        }
        return container;
    }

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

    internal DaggerfallQuestResourceBinding PlaceQuestItem(DaggerfallQuestResourceBinding binding, WorldPoint position, DaggerfallItemOwner[] owners, Action<DaggerfallItemOwner> transfer)
    {
        if (owners.Length == 0) throw new NotSupportedException("A consumed quest item has no physical owner to place again.");
        if (owners.All(owner => owner.Scope == "ground" && _ground.ContainsKey(owner.Id)))
        {
            RelocateQuestItem(binding, position);
            return binding;
        }
        var identity = _identities.Allocate(DurableIdentityKind.Container);
        long id = checked((long)identity.Value);
        try
        {
            var owner = _containers.Entities.Create(identity, GroundContainerType);
            _containers.RegisterOwner(owner);
            transfer(DaggerfallItemOwner.Ground(id));
            _ground.Add(id, new(_activeProfile, id, owner, position));
            foreach (long source in owners.Where(value => value.Scope == "ground").Select(value => value.Id).Distinct())
                RetireEmptyPile(source);
            return binding.UniqueItemIds.Length > 0 ? binding : binding with
                { Stacks = binding.Stacks.Select(stack => stack with { Owner = new("ground", id) }).Distinct().ToArray() };
        }
        catch
        {
            if (_containers.Entities.TryResolve(identity, out var owner))
            {
                // RegisterOwner registers the inventory before attaching its component.
                // A failed attachment still needs its native owner retired.
                var store = _containers.Entities.Store;
                var inventoryStore = store.TryGet<InventoryComponent>(owner, out var inventory) ? inventory.Store
                    : store.Get<InventoryComponent>(_player).Store;
                if (inventoryStore.TryGetInventory(owner, out _))
                {
                    using var edit = inventoryStore.Prepare();
                    edit.RetireOwner(owner);
                    edit.Publish();
                }
                _containers.Entities.Destroy(identity);
            }
            _identities.Remove(identity);
            throw;
        }
    }

    private void RelocateQuestItem(DaggerfallQuestResourceBinding binding, WorldPoint position)
    {
        if (binding.UniqueItemIds.Length == 1 && !_instances.ContainsUnique(binding.UniqueItemIds[0])
            || binding.UniqueItemIds.Length == 0 && binding.Stacks.Length == 0)
            throw new NotSupportedException("A consumed quest item has no physical owner to place again.");
        DaggerfallItemOwner[] owners = binding.UniqueItemIds.Length == 1
            ? [_instances.RequireUnique(binding.UniqueItemIds[0]).Owner]
            : binding.Stacks.Select(stack => new DaggerfallItemOwner(stack.Owner.Scope, stack.Owner.Id)).Distinct().ToArray();
        if (owners.Any(owner => owner.Scope != "ground" || !_ground.ContainsKey(owner.Id)))
            throw new NotSupportedException("A quest item can be placed again only while all its canonical owners are ground piles.");
        foreach (var stack in binding.Stacks)
        {
            var owner = new DaggerfallItemOwner(stack.Owner.Scope, stack.Owner.Id);
            var id = InventoryStackId.Parse(stack.StackId);
            var metadata = _instances.RequireStack(owner, id);
            if (!_containers.Read(_ground[owner.Id].Owner).Stacks.Any(value => value.Id == id && value.Definition.Value == metadata.ItemId && value.Quantity > 0))
                throw new NotSupportedException($"Quest stack '{stack.StackId}' has no actual ground contents to place.");
        }
        foreach (var owner in owners)
        {
            var container = _ground[owner.Id];
            _ground[container.Id] = container with { Profile = _activeProfile, Position = position };
        }
    }

    internal InventoryStackId ResolveTakeDestination(long id, InventoryStackId source, InventoryStackId freshDestination)
    {
        DaggerfallItemInstanceMetadata metadata = _instances.RequireStack(DaggerfallItemOwner.Ground(id), source);
        if (_ground[id].PropertyPlacement is not null)
            return InventoryStackId.Parse($"{freshDestination.Value}.{_containers.Read(_player).StoreRevision}");
        return _containers.Read(_player).Stacks.OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Select(stack => (stack.Id, Metadata: _instances.RequireStack(DaggerfallItemOwner.Player, stack.Id)))
            .Where(candidate => metadata.IsStackCompatibleWith(candidate.Metadata))
            .Select(candidate => candidate.Id).FirstOrDefault() ?? freshDestination;
    }

    internal InventoryContainerTransferReceipt Take(long id, InventoryContainerSelection selection, ulong expectedWorldRevision)
    {
        if (!TryGet(id, out DaggerfallGroundContainer? container))
            throw new InvalidOperationException("That container is no longer available.");
        if (_containers.Read(_player).StoreRevision != expectedWorldRevision)
            throw new InvalidOperationException("Inventory changed. Choose the item again.");
        if (selection.Stack is InventoryStackId source && selection.DestinationStack is InventoryStackId destination)
            _instances.EnsureTransferCompatible(DaggerfallItemOwner.Ground(id), DaggerfallItemOwner.Player, source, destination);
        InventoryContainerTransferReceipt transfer = _containers.Transfer(container.Owner, _player, selection);
        SyncToPlayer(transfer, id);
        RetireEmptyPile(id);
        return transfer;
    }

    private void RetireEmptyPile(long id)
    {
        if (!_ground.TryGetValue(id, out var container)) return;
        InventoryView remaining = _containers.Read(container.Owner);
        if (container.PropertyPlacement is null && remaining.Stacks.Count == 0 && remaining.UniqueItems.Count == 0)
        {
            using var edit = _containers.Entities.Store.Get<InventoryComponent>(container.Owner).Store.Prepare();
            edit.RetireOwner(container.Owner);
            edit.Publish();
            _ground.Remove(id);
            _containers.Entities.Destroy(new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)id)));
            _identities.Remove(new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)id)));
        }
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
            _unloaded.Add(value.Id, value);
        }
        Reconcile();
    }

    private void Materialize(DaggerfallGroundContainerSave value)
    {
        DurableIdentityReference identity = new(DurableIdentityKind.Container, checked((ulong)value.Id));
        EntityId owner = _containers.Entities.Create(identity, GroundContainerType);
        bool seeded = false;
        try
        {
            _containers.RegisterOwner(owner);
            if (value.Inventory.Stacks.Length != 0 || value.Inventory.UniqueItems.Length != 0)
            {
                InventoryContainerSeed[] seeds = value.Inventory.Stacks
                    .Select(stack => new InventoryContainerSeed(new InventoryItemId(stack.ItemId), stack.Quantity, Stack: InventoryStackId.Parse(stack.StackId)))
                    .Concat(value.Inventory.UniqueItems.Select(unique => new InventoryContainerSeed(
                        new InventoryItemId(unique.ItemId), UniqueItem: new DurableIdentityReference(DurableIdentityKind.Item, unique.EntityId), CapacityCosts: DaggerfallEncumbrancePolicy.CapacityOverride(unique.Metadata.WeightClassicUnits))))
                    .ToArray();
                _containers.Seed(owner, seeds);
                seeded = true;
                RegisterMetadata(value);
            }
            _ground.Add(value.Id, new DaggerfallGroundContainer(value.Profile.Require(), value.Id, owner, new WorldPoint(value.X, value.Y, value.Z), value.PropertyPlacement, value.StockedDay));
        }
        catch
        {
            var store = _containers.Entities.Store.Get<InventoryComponent>(_player).Store;
            if (store.TryGetInventory(owner, out _))
            {
                InventoryView contents = store.Read(owner);
                using var edit = store.Prepare();
                foreach (var stack in contents.Stacks) edit.Consume(owner, stack.Id, stack.Quantity);
                foreach (var item in contents.UniqueItems) edit.DestroyUnique(item.Entity);
                edit.RetireOwner(owner);
                edit.Publish();
            }
            if (seeded)
            {
                _instances.RemoveOwner(DaggerfallItemOwner.Ground(value.Id), retireBindings: false);
                foreach (var item in value.Inventory.UniqueItems)
                {
                    _instances.RemoveUnique(item.EntityId);
                    _containers.Entities.Destroy(new(DurableIdentityKind.Item, item.EntityId));
                }
            }
            _containers.Entities.Destroy(identity);
            throw;
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

    private void SyncFromPlayer(InventoryContainerTransferReceipt transfer, ulong groundId) =>
        _instances.ApplyTransfer(transfer, _containers.Read(_player), _containers.Entities,
            DaggerfallItemOwner.Player, DaggerfallItemOwner.Ground(checked((long)groundId)));

    private void SyncToPlayer(InventoryContainerTransferReceipt transfer, long groundId) =>
        _instances.ApplyTransfer(transfer, _containers.Read(_ground[groundId].Owner), _containers.Entities,
            DaggerfallItemOwner.Ground(groundId), DaggerfallItemOwner.Player);

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
