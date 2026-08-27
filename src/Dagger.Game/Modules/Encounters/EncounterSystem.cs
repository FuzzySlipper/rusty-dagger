using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.PlayerControl;

namespace RustyDagger.Game.Modules.Encounters;

/// <summary>Encounter targets selected by product content before this module is composed.</summary>
internal sealed record EncounterTarget(string Id, string Name, string Objective, long MemberEntityId, float ActivationRadius);

internal sealed class EncounterSystem(IReadOnlyList<EncounterTarget> encounters)
{
    internal EncounterTarget? ActiveEncounter(PlayerControlState playerControl, ActorsState actors)
    {
        if (playerControl.Position is not WorldPoint playerPosition) return null;
        foreach (EncounterTarget encounter in encounters)
        {
            if (actors.TryGet(encounter.MemberEntityId, out ActorState actor) && !actor.IsDefeated && playerPosition.HorizontalDistanceTo(actor.Position) < encounter.ActivationRadius)
                return encounter;
        }
        return null;
    }
}
