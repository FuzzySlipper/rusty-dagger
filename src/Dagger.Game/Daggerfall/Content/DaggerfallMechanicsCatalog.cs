using Rusty.Engine;
using RustyDagger.Game.Modules.Actors;

namespace RustyDagger.Game.Daggerfall.Content;

/// <summary>Admits Daggerfall definitions and binds their product-owned values to Engine mechanics state.</summary>
internal sealed class DaggerfallMechanicsCatalog : IDisposable
{
    private const long MinimumStatValue = 0;
    private const long MaximumStatValue = 10_000;
    private readonly IMechanicsService _mechanics;
    private readonly MechanicsCatalog _catalog;

    internal DaggerfallMechanicsCatalog(IMechanicsService mechanics)
    {
        _mechanics = mechanics;
        MechanicsCatalog catalog = mechanics.CreateCatalog(new MechanicsCatalogCreateRequest("daggerfall_active_slice_v1"));
        try
        {
            _catalog = catalog;
            DefineStats();
            DefineTracks();
            mechanics.AdmitCatalog(_catalog);
        }
        catch
        {
            catalog.Dispose();
            throw;
        }
    }

    internal MechanicsEntity Bind(DaggerfallActorDefinition definition, ulong entityId)
    {
        MechanicsEntity entity = _mechanics.BindEntity(new MechanicsEntityBindRequest(_catalog, entityId, definition.Id.Replace("-", "_", StringComparison.Ordinal)));
        try
        {
            SetStats(entity, definition.Stats, definition.Vitals);
            _mechanics.SetInitialTrack(new MechanicsInitialTrackRequest(entity, DaggerfallMechanicsIds.Health.Value, definition.Vitals.HealthMaximum));
            _mechanics.SetInitialTrack(new MechanicsInitialTrackRequest(entity, DaggerfallMechanicsIds.Stamina.Value, definition.Vitals.StaminaMaximum));
            _mechanics.SetInitialTrack(new MechanicsInitialTrackRequest(entity, DaggerfallMechanicsIds.Magicka.Value, definition.Vitals.MagickaMaximum));
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
        foreach (DaggerfallStatId stat in new[] { DaggerfallMechanicsIds.Strength, DaggerfallMechanicsIds.Agility, DaggerfallMechanicsIds.Intelligence, DaggerfallMechanicsIds.Endurance, DaggerfallMechanicsIds.Luck, DaggerfallMechanicsIds.LongBlade, DaggerfallMechanicsIds.HandToHand, DaggerfallMechanicsIds.Dodging, DaggerfallMechanicsIds.HealthMaximum, DaggerfallMechanicsIds.StaminaMaximum, DaggerfallMechanicsIds.MagickaMaximum })
            _mechanics.DefineStat(new MechanicsStatDefinitionRequest(_catalog, stat.Value, MinimumStatValue, MaximumStatValue));
    }

    private void DefineTracks()
    {
        DefineTrack(DaggerfallMechanicsIds.Health, DaggerfallMechanicsIds.HealthMaximum);
        DefineTrack(DaggerfallMechanicsIds.Stamina, DaggerfallMechanicsIds.StaminaMaximum);
        DefineTrack(DaggerfallMechanicsIds.Magicka, DaggerfallMechanicsIds.MagickaMaximum);
    }

    private void DefineTrack(DaggerfallTrackId track, DaggerfallStatId maximum) => _mechanics.DefineTrack(new MechanicsTrackDefinitionRequest(_catalog, track.Value, MinimumStatValue, MechanicsTrackMaximumKind.Stat, 0, maximum.Value));

    private void SetStats(MechanicsEntity entity, DaggerfallStatBases stats, DaggerfallVitalValues vitals)
    {
        Set(entity, DaggerfallMechanicsIds.Strength, stats.Strength); Set(entity, DaggerfallMechanicsIds.Agility, stats.Agility); Set(entity, DaggerfallMechanicsIds.Intelligence, stats.Intelligence); Set(entity, DaggerfallMechanicsIds.Endurance, stats.Endurance); Set(entity, DaggerfallMechanicsIds.Luck, stats.Luck); Set(entity, DaggerfallMechanicsIds.LongBlade, stats.LongBlade); Set(entity, DaggerfallMechanicsIds.HandToHand, stats.HandToHand); Set(entity, DaggerfallMechanicsIds.Dodging, stats.Dodging); Set(entity, DaggerfallMechanicsIds.HealthMaximum, vitals.HealthMaximum); Set(entity, DaggerfallMechanicsIds.StaminaMaximum, vitals.StaminaMaximum); Set(entity, DaggerfallMechanicsIds.MagickaMaximum, vitals.MagickaMaximum);
    }

    private void Set(MechanicsEntity entity, DaggerfallStatId stat, long value) => _mechanics.SetInitialStat(new MechanicsInitialStatRequest(entity, stat.Value, value));
}
