using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Daggerfall.Content;
using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.PlayerControl;

namespace RustyDagger.Game.Modules.Encounters;

internal static class EncounterSystem
{
    internal static EncounterDefinition? ActiveEncounter(DaggerfallState state)
    {
        if (state.PlayerControl.Position is not WorldPoint playerPosition) return null;
        foreach (EncounterDefinition encounter in DaggerfallDefinitions.Encounters.Values)
        {
            if (state.Actors.TryGet(encounter.MemberEntityId, out ActorState actor) && !actor.IsDead && playerPosition.HorizontalDistanceTo(actor.Position) < 12f)
                return encounter;
        }
        return null;
    }
}
