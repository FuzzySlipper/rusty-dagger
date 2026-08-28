using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Encounters;

internal sealed class EncounterReaction(EncounterState encounters)
{
    internal void React(ActorDiedFact fact) => encounters.RecordDefeat(fact.ActorId);
}
