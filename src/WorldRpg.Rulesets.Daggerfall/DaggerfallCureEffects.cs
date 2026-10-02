using System.Text.Json;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Effects;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Compiled cure policy delegates selection and cleanup to each existing condition owner.</summary>
internal static class DaggerfallCureEffects
{
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(
        Func<long, int> cureDiseases, Func<Actor, bool> curePoisons, Func<long, int> cureParalysis)
    {
        foreach (var (key, subtype) in new[] { ("cure-disease", 0), ("cure-poison", 1), ("cure-paralyzation", 2) })
        {
            int selected = subtype;
            yield return new(key, key, DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
                Apply: effect => Validate(effect, selected), Resume: effect => Validate(effect, selected),
                MagicRound: effect =>
                {
                    long target = checked((long)effect.Context.Target.Value);
                    bool cured = selected switch
                    {
                        0 => cureDiseases(target) > 0,
                        1 => curePoisons(effect.Target),
                        2 => cureParalysis(target) > 0,
                        _ => throw new InvalidOperationException("Unknown compiled cure scope."),
                    };
                    effect.NoMatchingCondition = !cured;
                    effect.ExpireAfterCurrentRound = true;
                },
                Spell: new(3, selected, RollChanceOnCast: true));
        }
    }

    private static IEnumerable<IActiveEffectContribution> Validate(DaggerfallActiveEffect effect, int subtype)
    {
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        if (state?.Settings is not { Type: 3 } settings || settings.SubType != subtype || state.CasterLevel < 1
            || state.Amount < 0 || state.SavePercent is < 1 or > 100)
            throw new ArgumentException("Cure state does not match its admitted compiled scope.");
        return [];
    }
}
