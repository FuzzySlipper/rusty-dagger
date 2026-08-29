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

    internal DaggerfallMechanicsCatalog(IMechanicsService mechanics, IEnumerable<DaggerfallItemDefinition>? items = null)
    {
        _mechanics = mechanics;
        MechanicsCatalog catalog = mechanics.CreateCatalog(new MechanicsCatalogCreateRequest("daggerfall_active_slice_v1"));
        try
        {
            _catalog = catalog;
            DefineStats();
            DefineTracks();
            DefineItems(items ?? []);
            mechanics.AdmitCatalog(_catalog);
        }
        catch
        {
            catalog.Dispose();
            throw;
        }
    }

    internal MechanicsEntity Bind(DaggerfallActorDefinition definition, ulong entityId, IReadOnlyList<MechanicsInitialInventoryStack>? initialInventory = null)
    {
        MechanicsEntity entity = _mechanics.BindEntity(new MechanicsEntityBindRequest(_catalog, entityId, definition.Id.Value.Replace("-", "_", StringComparison.Ordinal)));
        try
        {
            _mechanics.SetInitialComponents(new MechanicsInitialComponentsRequest(
                entity, true, InitialStats(definition.Stats, definition.InitialVitals),
                true, InitialTracks(definition.InitialVitals),
                false, ReadOnlyMemory<MechanicsInitialIntrinsicSource>.Empty,
                false, ReadOnlyMemory<MechanicsInitialActiveEffect>.Empty,
                initialInventory is not null, initialInventory?.ToArray() ?? [], ReadOnlyMemory<MechanicsInitialInventoryCapacityLimit>.Empty,
                false, string.Empty, false, ReadOnlyMemory<MechanicsInitialEquipmentAssignment>.Empty));
            _mechanics.CommitEntity(entity);
            return entity;
        }
        catch
        {
            entity.Dispose();
            throw;
        }
    }

    public void Dispose() => _catalog.Dispose();

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
                _catalog, item.Id.Value, MechanicsItemKind.Fungible, item.MaximumQuantity,
                ReadOnlyMemory<MechanicsText>.Empty, ReadOnlyMemory<MechanicsItemCapacityCostInput>.Empty,
                false, 0, string.Empty, ReadOnlyMemory<MechanicsText>.Empty));
        }
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
