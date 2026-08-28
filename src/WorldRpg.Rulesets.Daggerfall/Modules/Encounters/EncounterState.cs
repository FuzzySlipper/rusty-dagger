namespace WorldRpg.Rulesets.Daggerfall.Modules.Encounters;

internal sealed class EncounterState
{
    internal long? LastDefeatedActorId { get; private set; }
    internal void RecordDefeat(long actorId) => LastDefeatedActorId = actorId;
}
