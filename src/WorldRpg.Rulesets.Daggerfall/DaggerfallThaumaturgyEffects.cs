using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Compiled Thaumaturgy movement and spell-defense effects over the shared effect/cast owners.</summary>
internal static class DaggerfallThaumaturgyEffects
{
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions()
    {
        yield return Movement(
            key: "levitate",
            type: 14,
            target: DaggerfallMagicAllowedTargets.CasterOnly,
            protection: new DaggerfallMovementProtection(PreventsFallDamage: false, GrantsLevitation: true));
        yield return Movement(
            key: "water-walking",
            type: 31,
            target: DaggerfallMagicAllowedTargets.All,
            protection: new DaggerfallMovementProtection(PreventsFallDamage: false, GrantsWaterWalking: true));

        yield return Defense(
            key: "spell-reflection",
            type: 21,
            reflection: true);
        yield return Defense(
            key: "spell-resistance",
            type: 22,
            reflection: false);
    }

    private static DaggerfallEffectDefinition Movement(string key, int type,
        DaggerfallMagicAllowedTargets target, DaggerfallMovementProtection protection) =>
        new(key, key, DaggerfallEffectStacking.RefreshDuration, 1, 1,
            Apply: effect => ValidateEffect(effect, type, -1),
            Resume: effect => ValidateEffect(effect, type, -1),
            MovementProtection: protection,
            Spell: new(type, -1, SpellMaker: true, SupportsDuration: true, AllowedTargets: target),
            ExtendIncumbentDuration: true,
            IncumbentSettingsMatch: (_, _) => true);

    private static DaggerfallEffectDefinition Defense(string key, int type, bool reflection) =>
        new(key, key, DaggerfallEffectStacking.RefreshDuration, 1, 1,
            Apply: effect => ValidateEffect(effect, type, -1),
            Resume: effect => ValidateEffect(effect, type, -1),
            Spell: new(type, -1, SpellMaker: true, SupportsDuration: true),
            MagicDefense: effect => DefenseFor(effect, reflection),
            ExtendIncumbentDuration: true,
            IncumbentSettingsMatch: (_, _) => true);

    private static IEnumerable<WorldRpg.Kit.Effects.IActiveEffectContribution> ValidateEffect(
        DaggerfallActiveEffect effect, int type, int subtype)
    {
        DaggerfallCastEffectState? state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        if (state?.Settings is not { Type: var actualType, SubType: var actualSubtype }
            || actualType != type || actualSubtype != subtype
            || state.CasterLevel < 1 || state.Amount != 0 || state.SavePercent is < 1 or > 100)
            throw new ArgumentException($"Thaumaturgy effect state does not match compiled variant {type}:{subtype}.");
        return [];
    }

    private static DaggerfallMagicDefense DefenseFor(DaggerfallActiveEffect effect, bool reflection)
    {
        DaggerfallCastEffectState state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)
            ?? throw new ArgumentException("Thaumaturgy defense state is missing.");
        int chance = DaggerfallMagicAdmissionPolicy.CalculateEffectChance(state.Settings, state.CasterLevel);
        if (chance is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(chance));
        return reflection
            ? new DaggerfallMagicDefense(0, chance, [])
            : new DaggerfallMagicDefense(0, 0, [], AllResistanceChance: chance);
    }
}
