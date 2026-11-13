using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>What a monster's landed hit carries: a disease, paralysis or a transformation infection.</summary>
    private void DeliverMonsterHit(DaggerfallMonsterHitExposure hit)
    {
        var consequence = hit.Hit.Consequence;
        var diseases = DaggerfallMonsterHitPolicy.Diseases(consequence);
        if (diseases.Count > 0)
        {
            // Once admitted, disease belongs to its target, not to a living infecting monster.
            _diseases.Inflict(new(hit.Instance, "monster-hit", null, hit.Target, diseases));
            return;
        }
        if (consequence == DaggerfallMonsterHitConsequence.Paralysis)
        {
            if (!State.Effects.Active.Any(effect => effect.Definition.Key == "paralyze"
                && checked((long)effect.Context.Target.Value) == hit.Target))
                Casting.TriggerMonsterParalysis(hit.Attacker, hit.Target);
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
            Infections.Inflict(new(hit.Instance, "monster-hit", hit.Attacker,
                hit.Target, infection, _activeProfileKey.Site.Region));
    }
}
