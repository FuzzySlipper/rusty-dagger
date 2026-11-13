using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What a monster's landed hit carries: a disease, paralysis or a transformation infection.</summary>
/// <param name="paralyze">Paralyzes a target as a monster's hit does: the attacker, then the target.</param>
/// <param name="region">The region the player is in now, where an infection is caught.</param>
internal sealed class DaggerfallMonsterHits(DaggerfallDiseases diseases, DaggerfallTransformationInfections infections,
    DaggerfallEffectLifecycle effects, Action<long, long> paralyze, Func<int> region)
{
    internal void Deliver(DaggerfallMonsterHitExposure hit)
    {
        var consequence = hit.Hit.Consequence;
        var inflicted = DaggerfallMonsterHitPolicy.Diseases(consequence);
        if (inflicted.Count > 0)
        {
            // Once admitted, disease belongs to its target, not to a living infecting monster.
            diseases.Inflict(new(hit.Instance, "monster-hit", null, hit.Target, inflicted));
            return;
        }
        if (consequence == DaggerfallMonsterHitConsequence.Paralysis)
        {
            if (!effects.Active.Any(effect => effect.Definition.Key == "paralyze"
                && checked((long)effect.Context.Target.Value) == hit.Target))
                paralyze(hit.Attacker, hit.Target);
            return;
        }
        DaggerfallInfectionKind? kind = consequence switch
        {
            DaggerfallMonsterHitConsequence.Vampire => DaggerfallInfectionKind.Vampire,
            DaggerfallMonsterHitConsequence.Werewolf => DaggerfallInfectionKind.Werewolf,
            DaggerfallMonsterHitConsequence.Wereboar => DaggerfallInfectionKind.Wereboar,
            _ => null,
        };
        if (kind is { } infection)
            infections.Inflict(new(hit.Instance, "monster-hit", hit.Attacker,
                hit.Target, infection, region()));
    }
}
