using Rusty.Engine;
using WorldRpg.Kit.Actors;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Admits Daggerfall definitions and binds their product-owned values to Engine mechanics state.</summary>
internal sealed class DaggerfallMechanicsCatalog : IDisposable
{
    private const long MinimumStatValue = 0;
    private const long MaximumStatValue = 10_000;
    private readonly IMechanicsService _mechanics;
    private readonly MechanicsCatalog _catalog;
    private readonly Dictionary<MechanicsEntity, ulong> _lifecycleStamps = [];
    private bool _disposed;

    internal DaggerfallMechanicsCatalog(IMechanicsService mechanics, DaggerfallDefinitions definitions)
    {
        _mechanics = mechanics;
        MechanicsCatalog catalog = mechanics.CreateCatalog(new MechanicsCatalogCreateRequest("daggerfall_active_slice_v1"));
        try
        {
            _catalog = catalog;
            DefineStats();
            DefineTracks();
            mechanics.DefineDamageKind(new MechanicsDamageKindDefinitionRequest(_catalog, DaggerfallMechanicsIds.PhysicalDamage.Value));
            DefineItems(definitions.Items.Values);
            DefineEquipmentSlots(definitions.EquipmentSlots.Values);
            mechanics.AdmitCatalog(_catalog);
        }
        catch
        {
            catalog.Dispose();
            throw;
        }
    }

    internal MechanicsCatalog Catalog => _catalog;

    internal MechanicsEntity Bind(DaggerfallActorDefinition definition, DaggerfallVitalValues vitals, ulong entityId, IReadOnlyList<MechanicsInitialInventoryStack>? initialInventory = null, IReadOnlyList<MechanicsInitialEquipmentAssignment>? initialEquipment = null, IReadOnlyList<MechanicsEntity>? containedItems = null)
    {
        MechanicsEntity entity = _mechanics.BindEntity(new MechanicsEntityBindRequest(_catalog, entityId, definition.Id.Value.Replace("-", "_", StringComparison.Ordinal)));
        try
        {
            _mechanics.SetInitialComponents(new MechanicsInitialComponentsRequest(
                entity, true, InitialStats(definition.Stats, vitals),
                true, InitialTracks(vitals),
                false, ReadOnlyMemory<MechanicsInitialIntrinsicSource>.Empty,
                false, ReadOnlyMemory<MechanicsInitialActiveEffect>.Empty,
                initialInventory is not null, initialInventory?.ToArray() ?? [], ReadOnlyMemory<MechanicsInitialInventoryCapacityLimit>.Empty,
                false, string.Empty, initialEquipment is not null, initialEquipment?.ToArray() ?? []));
            if (containedItems is not null)
                foreach (MechanicsEntity item in containedItems)
                {
                    MechanicsContainmentReceipt containment = _mechanics.ReadContainment(new MechanicsContainmentReadRequest(item));
                    _mechanics.StageInitialContainment(new MechanicsInitialContainmentRequest(entity, containment.ChildEntityId, containment.StateRevision));
                }
            MechanicsEntityReceipt receipt = _mechanics.CommitEntity(entity);
            _lifecycleStamps.Add(entity, receipt.Lifecycle.Stamp);
            return entity;
        }
        catch
        {
            entity.Dispose();
            throw;
        }
    }

    internal MechanicsEntity BindUniqueItem(DaggerfallItemDefinition definition, ulong entityId)
    {
        if (definition.IsFungible) throw new ArgumentException($"Item '{definition.Id.Value}' is fungible and cannot be bound as a unique entity.", nameof(definition));
        MechanicsEntity entity = _mechanics.BindEntity(new MechanicsEntityBindRequest(_catalog, entityId, $"item:{definition.Id.Value}:{entityId}"));
        try
        {
            _mechanics.SetInitialComponents(new MechanicsInitialComponentsRequest(
                entity, false, ReadOnlyMemory<MechanicsInitialStatValue>.Empty,
                false, ReadOnlyMemory<MechanicsInitialTrackValue>.Empty,
                false, ReadOnlyMemory<MechanicsInitialIntrinsicSource>.Empty,
                false, ReadOnlyMemory<MechanicsInitialActiveEffect>.Empty,
                false, ReadOnlyMemory<MechanicsInitialInventoryStack>.Empty, ReadOnlyMemory<MechanicsInitialInventoryCapacityLimit>.Empty,
                true, definition.Id.Value, false, ReadOnlyMemory<MechanicsInitialEquipmentAssignment>.Empty));
            MechanicsEntityReceipt receipt = _mechanics.CommitEntity(entity);
            _lifecycleStamps.Add(entity, receipt.Lifecycle.Stamp);
            return entity;
        }
        catch
        {
            entity.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        List<Exception>? failures = null;
        foreach ((MechanicsEntity entity, ulong stamp) in _lifecycleStamps.Reverse().ToArray())
        {
            try
            {
                _mechanics.SetEntityLifecycle(new MechanicsLifecycleRequest(entity, MechanicsEntityLifecycle.Tombstoned, MechanicsLifecycleGuard.Exact, stamp));
                entity.Dispose();
                _lifecycleStamps.Remove(entity);
            }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (_lifecycleStamps.Count != 0)
            throw new AggregateException(failures ?? [new InvalidOperationException("Mechanics entity cleanup did not complete.")]);
        try { _catalog.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
        _disposed = true;
    }

    private void DefineStats()
    {
        foreach (DaggerfallStatId stat in new[] { DaggerfallMechanicsIds.Strength, DaggerfallMechanicsIds.Intelligence, DaggerfallMechanicsIds.Willpower, DaggerfallMechanicsIds.Agility, DaggerfallMechanicsIds.Endurance, DaggerfallMechanicsIds.Personality, DaggerfallMechanicsIds.Speed, DaggerfallMechanicsIds.Luck, DaggerfallMechanicsIds.Reflexes, DaggerfallMechanicsIds.LongBlade, DaggerfallMechanicsIds.HandToHand, DaggerfallMechanicsIds.Dodging, DaggerfallMechanicsIds.HealthMaximum, DaggerfallMechanicsIds.StaminaMaximum, DaggerfallMechanicsIds.MagickaMaximum })
            _mechanics.DefineStat(new MechanicsStatDefinitionRequest(_catalog, stat.Value, MinimumStatValue, MaximumStatValue));
    }

    private void DefineTracks()
    {
        DefineTrack(DaggerfallMechanicsIds.Health, DaggerfallMechanicsIds.HealthMaximum);
        DefineTrack(DaggerfallMechanicsIds.Stamina, DaggerfallMechanicsIds.StaminaMaximum);
        DefineTrack(DaggerfallMechanicsIds.Magicka, DaggerfallMechanicsIds.MagickaMaximum);
    }

    private void DefineTrack(DaggerfallTrackId track, DaggerfallStatId maximum) => _mechanics.DefineTrack(new MechanicsTrackDefinitionRequest(_catalog, track.Value, MinimumStatValue, MechanicsTrackMaximumKind.Stat, 0, maximum.Value));

    internal void DefineItems(IEnumerable<DaggerfallItemDefinition> items)
    {
        foreach (DaggerfallItemDefinition item in items)
        {
            _mechanics.DefineItem(new MechanicsItemDefinitionRequest(
                _catalog, item.Id.Value, item.IsFungible ? MechanicsItemKind.Fungible : MechanicsItemKind.Unique, item.MaximumQuantity,
                item.Equipment?.Classifications.Select(value => new MechanicsText(value)).ToArray() ?? [], ReadOnlyMemory<MechanicsItemCapacityCostInput>.Empty,
                item.Equipment is not null, item.Equipment?.RequiredSlots ?? 0, item.Equipment?.ExclusiveGroup ?? string.Empty, ReadOnlyMemory<MechanicsText>.Empty));
        }
    }

    private void DefineEquipmentSlots(IEnumerable<DaggerfallEquipmentSlotDefinition> slots)
    {
        foreach (DaggerfallEquipmentSlotDefinition slot in slots)
            _mechanics.DefineEquipmentSlot(new MechanicsEquipmentSlotDefinitionRequest(_catalog, slot.Id.Value, slot.AllowedClassifications.Select(value => new MechanicsText(value)).ToArray()));
    }

    private static ReadOnlyMemory<MechanicsInitialStatValue> InitialStats(DaggerfallStatBases stats, DaggerfallVitalValues vitals) => new MechanicsInitialStatValue[]
    {
        new(DaggerfallMechanicsIds.Strength.Value, stats.Strength), new(DaggerfallMechanicsIds.Intelligence.Value, stats.Intelligence), new(DaggerfallMechanicsIds.Willpower.Value, stats.Willpower), new(DaggerfallMechanicsIds.Agility.Value, stats.Agility), new(DaggerfallMechanicsIds.Endurance.Value, stats.Endurance), new(DaggerfallMechanicsIds.Personality.Value, stats.Personality), new(DaggerfallMechanicsIds.Speed.Value, stats.Speed), new(DaggerfallMechanicsIds.Luck.Value, stats.Luck), new(DaggerfallMechanicsIds.Reflexes.Value, stats.Reflexes), new(DaggerfallMechanicsIds.LongBlade.Value, stats.LongBlade), new(DaggerfallMechanicsIds.HandToHand.Value, stats.HandToHand), new(DaggerfallMechanicsIds.Dodging.Value, stats.Dodging), new(DaggerfallMechanicsIds.HealthMaximum.Value, vitals.HealthMaximum), new(DaggerfallMechanicsIds.StaminaMaximum.Value, vitals.StaminaMaximum), new(DaggerfallMechanicsIds.MagickaMaximum.Value, vitals.MagickaMaximum),
    };

    private static ReadOnlyMemory<MechanicsInitialTrackValue> InitialTracks(DaggerfallVitalValues vitals) => new MechanicsInitialTrackValue[]
    {
        new(DaggerfallMechanicsIds.Health.Value, vitals.HealthMaximum),
        new(DaggerfallMechanicsIds.Stamina.Value, vitals.StaminaMaximum),
        new(DaggerfallMechanicsIds.Magicka.Value, vitals.MagickaMaximum),
    };
}
