using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// Advances Daggerfall-owned travelling ranged releases after sprite impact notices have been
    /// consumed. The release step is the latest admitted simulation step, not the attack's earlier
    /// decision step, so sprite playback delay cannot make a new shot look already arrived.
    /// </summary>
    partial void UpdateRangedFlight(ProductUpdateFacts facts)
    {
        if (_latestUpdateGeneration is not ulong generation || _latestSimulationStep is not ulong simulationStep) return;
        _combat.AdvanceRangedFlight(generation, simulationStep, facts.FixedDeltaSeconds, CurrentPositions(), _facts);
        DeliverFacts();
    }

    private IReadOnlyDictionary<long, WorldPoint> CurrentPositions()
    {
        Dictionary<long, WorldPoint> positions = [];
        if (State.PlayerControl.Position is WorldPoint player) positions.Add(DaggerfallActorIdentity.PlayerEntityId, player);
        foreach (ActorState actor in State.Actors.All) positions[actor.DurableId] = actor.Position;
        return positions;
    }
}
