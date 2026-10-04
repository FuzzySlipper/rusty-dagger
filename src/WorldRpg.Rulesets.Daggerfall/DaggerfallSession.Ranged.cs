using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

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
        AdvanceSpellFlights(facts.FixedDeltaSeconds);
        DeliverFacts();
    }

    /// <summary>
    /// Advances each admitted dungeon missile along its launch direction and submits only the
    /// newly traversed segment to the Engine spatial owner. The action target is an aim fact at
    /// launch; it is never consulted again to re-aim a live missile.
    /// </summary>
    private void AdvanceSpellFlights(double fixedDeltaSeconds)
    {
        if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));

        float displacement = checked((float)(DaggerfallDungeonSpellPolicy.MissileMovementSpeedMetresPerSecond * fixedDeltaSeconds));
        foreach (DaggerfallLiveSpell bundle in Casting.PendingFlightOperations)
        {
            if (bundle.ReleaseOrigin is not Vector3 from
                || !DaggerfallDungeonSpellPolicy.TryNormalizeDirection(bundle.ReleaseDirection ?? default, out Vector3 direction))
            {
                // An admitted release with no usable launch direction is a terminal miss. The
                // source target is not a fallback caster or a proof of impact.
                _ = Casting.Deliver(bundle, []);
                continue;
            }

            bundle.DungeonFlightElapsedSeconds += fixedDeltaSeconds;
            Vector3 to = from + direction * displacement;
            DaggerfallCastResult impact = DeliverSpellImpact(bundle, from, to);
            if (impact.Outcome == DaggerfallCastOutcome.Released && !bundle.Delivered)
            {
                bundle.ReleaseOrigin = to;
                if (bundle.DungeonFlightElapsedSeconds > DaggerfallDungeonSpellPolicy.MissileLifespanSeconds)
                    _ = Casting.Deliver(bundle, []);
            }
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
        return [.. Casting.PendingRangedFlights.Select(bundle =>
        {
            Vector3 origin = bundle.ReleaseOrigin
                ?? (bundle.ActionSource is { } source
                    ? source.Origin + Vector3.UnitY * DaggerfallDungeonSpellPolicy.MissileOriginHeightMetres
                    : Vector3.Zero);
            return DaggerfallDungeonSpellPolicy.TryNormalizeDirection(bundle.ReleaseDirection ?? default, out Vector3 direction)
                ? new DaggerfallDungeonSpellFlightView(bundle.Sequence, WorldPoint.From(origin), direction)
                : (DaggerfallDungeonSpellFlightView?)null;
        }).Where(view => view is not null).Select(view => view!.Value)];
    }
}

/// <summary>A transient dungeon action missile rendered from the same admitted arrow appearance.</summary>
internal readonly record struct DaggerfallDungeonSpellFlightView(long Sequence, WorldPoint Position, Vector3 Direction);
