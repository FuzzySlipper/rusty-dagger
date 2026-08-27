using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Facts;
using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.Combat;

namespace RustyDagger.Game.Modules.Progression;

internal sealed class ProgressionReaction(ProgressionState progression)
{
    private readonly HashSet<long> _awardedActors = [];

    internal void React(ActorDiedFact fact, DaggerfallState state, ProductFactBuffer facts)
    {
        if (_awardedActors.Contains(fact.ActorId) || !state.Actors.TryGet(fact.ActorId, out ActorState actor) || !actor.IsDead) return;
        _awardedActors.Add(fact.ActorId);
        progression.Award(actor.Definition.ExperienceReward);
        facts.Append(new ExperienceAwardedFact(actor.EntityId, actor.Definition.ExperienceReward));
    }
}
