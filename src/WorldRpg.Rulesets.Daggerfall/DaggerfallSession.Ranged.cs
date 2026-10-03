using System.Numerics;
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
        AdvanceDungeonSpellFlights();
        DeliverFacts();
    }

    private void AdvanceDungeonSpellFlights()
    {
        IReadOnlyDictionary<long, WorldPoint> positions = CurrentPositions();
        foreach (DaggerfallLiveSpell bundle in Casting.PendingDungeonFlights)
        {
            if (bundle.ActionSource is not { } source) continue;
            if (!positions.TryGetValue(source.TargetId, out WorldPoint target))
            {
                _ = Casting.Deliver(bundle, [source.TargetId]);
                continue;
            }
            Vector3 from = bundle.ReleaseOrigin ?? source.Origin;
            Vector3 to = target.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight;
            _ = DeliverSpellImpact(bundle, from, to);
        }
    }

    private IReadOnlyDictionary<long, WorldPoint> CurrentPositions()
    {
        Dictionary<long, WorldPoint> positions = [];
        if (State.PlayerControl.Position is WorldPoint player) positions.Add(DaggerfallActorIdentity.PlayerEntityId, player);
        foreach (ActorState actor in State.Actors.All) positions[actor.DurableId] = actor.Position;
        return positions;
    }

    internal IReadOnlyList<DaggerfallDungeonSpellFlightView> ReadDungeonSpellFlights()
    {
        IReadOnlyDictionary<long, WorldPoint> positions = CurrentPositions();
        return [.. Casting.PendingDungeonFlights.Select(bundle =>
        {
            DaggerfallActionCastSource source = bundle.ActionSource!;
            Vector3 origin = bundle.ReleaseOrigin ?? source.Origin;
            Vector3 aim = positions.TryGetValue(source.TargetId, out WorldPoint target)
                ? target.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight
                : origin + (bundle.ReleaseDirection is { } direction && direction.LengthSquared() > .000001f
                    ? Vector3.Normalize(direction) : Vector3.UnitZ);
            Vector3 line = aim - origin;
            return new DaggerfallDungeonSpellFlightView(bundle.Sequence, WorldPoint.From(origin),
                line.LengthSquared() > .000001f ? Vector3.Normalize(line) : Vector3.UnitZ);
        })];
    }
}

/// <summary>A transient dungeon action missile rendered from the same admitted arrow appearance.</summary>
internal readonly record struct DaggerfallDungeonSpellFlightView(long Sequence, WorldPoint Position, Vector3 Direction);
