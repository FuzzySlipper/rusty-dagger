using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>One live projection for casting and direct-input artifact saving throws.</summary>
internal static class DaggerfallMagicProfiles
{
    internal static DaggerfallMagicTargetProfile Create(StatsComponent stats, DaggerfallActorDefinition? definition,
        DaggerfallCharacterState? character, DaggerfallDefinitions catalog, DaggerfallMagicDefense defense)
    {
        var career = character?.Career ?? (definition?.Career is string id ? catalog.Catalogs.RequireCareer(id) : null);
        var mobile = definition is null ? null : catalog.Mobiles.ForActor(definition.Id.Value);
        DaggerfallMagicTolerance Tolerance(int flag) => (career is not null
            ? DaggerfallCareerTolerances.Tolerance(career, flag)
            : DaggerfallCareerTolerances.Tolerance(mobile?.ResistanceFlags ?? 0, mobile?.ImmunityFlags ?? 0,
                mobile?.LowToleranceFlags ?? 0, mobile?.CriticalWeaknessFlags ?? 0, flag)) switch
        {
            DaggerfallDiseaseCareerTolerance.Immune => DaggerfallMagicTolerance.Immune,
            DaggerfallDiseaseCareerTolerance.Resistant => DaggerfallMagicTolerance.Resistant,
            DaggerfallDiseaseCareerTolerance.LowTolerance => DaggerfallMagicTolerance.LowTolerance,
            DaggerfallDiseaseCareerTolerance.CriticalWeakness => DaggerfallMagicTolerance.CriticalWeakness,
            _ => DaggerfallMagicTolerance.Normal,
        };
        int Read(DaggerfallStatId id) => DaggerfallCasting.Read(stats, id.Value);
        var biography = character?.Background?.Modifiers;
        return new(Read(DaggerfallMechanicsIds.Willpower),
            new(Tolerance(1), Tolerance(2), Tolerance(4), Tolerance(8), Tolerance(16), Tolerance(32), Tolerance(64)),
            character?.Race, biography?.MagicResistance ?? 0, biography?.PoisonResistance ?? 0, biography?.DiseaseResistance ?? 0,
            new(Read(DaggerfallMechanicsIds.ResistanceDiseaseOrPoison), Read(DaggerfallMechanicsIds.ResistanceFire),
                Read(DaggerfallMechanicsIds.ResistanceFrost), Read(DaggerfallMechanicsIds.ResistanceShock), Read(DaggerfallMechanicsIds.ResistanceMagic)),
            defense.Resistances);
    }
}
