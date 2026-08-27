using RustyDagger.Game.Facts;

namespace RustyDagger.Game.Modules.Encounters;

internal sealed class EncounterReaction(EncounterState encounters)
{
    internal void React(ActorDiedFact fact) => encounters.RecordDefeat(fact.ActorId);
}
