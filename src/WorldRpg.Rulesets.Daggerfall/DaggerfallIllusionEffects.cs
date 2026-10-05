using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal static class DaggerfallIllusionEffects
{
    internal const string LightKey = "light-normal";
    internal static DaggerfallEffectDefinition NormalLight() => new(LightKey, LightKey,
        DaggerfallEffectStacking.RefreshDuration, 1, 1,
        Apply: ValidateLight, Resume: ValidateLight,
        Spell: new(15, -1, SpellMaker: true, SupportsDuration: true,
            AllowedTargets: DaggerfallMagicAllowedTargets.CasterOnly, AllowedElements: DaggerfallMagicAllowedElements.Magic),
        ExtendIncumbentDuration: true, IncumbentSettingsMatch: (_, _) => true);

    private static IEnumerable<WorldRpg.Kit.Effects.IActiveEffectContribution> ValidateLight(DaggerfallActiveEffect effect)
    {
        var state = DaggerfallMysticismEffects.Read(effect, 15, -1);
        if (effect.Context.Target.Value != DaggerfallActorIdentity.PlayerEntityId || state.Amount != 0)
            throw new ArgumentException("Normal light requires the player and duration-only state.");
        return [];
    }

    internal static DaggerfallEffectDefinition MorphSelf(Func<bool> morph) => new("morph-self", "morph-self",
        DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
        Apply: effect => { DaggerfallMysticismEffects.Read(effect, 29, -1); return []; },
        MagicRound: effect =>
        {
            if (effect.Context.Target.Value != DaggerfallActorIdentity.PlayerEntityId || !morph())
                effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.NoMatch;
            effect.ExpireAfterCurrentRound = true;
        },
        Resume: _ => throw new ArgumentException("Morph persists in the racial override, not an active spell."),
        Spell: new(29, -1, AllowedTargets: DaggerfallMagicAllowedTargets.CasterOnly,
            AllowedElements: DaggerfallMagicAllowedElements.Magic), ShowSpellIcon: false);
}
