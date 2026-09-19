using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Kit.Targeting;

public sealed class TargetingComponent
{
    public EntityId? Current { get; internal set; }
    public void Clear() => Current = null;
}

public sealed record TargetingEvidence(PerceptionQueryRequest Request, PerceptionReadoutLeaseReceipt Receipt, long? SelectedTargetId);

/// <summary>Ruleset eligibility over live actors; geometry remains Engine-owned.</summary>
public interface ITargetingPolicy
{
    bool IsValidTarget(ActorState actor);
    double MaximumDistance(double? actionReach);
    double MinimumFacingCosine { get; }
}

public sealed class TargetingService(IPerceptionService perception, SpatialMovementSystem spatial, ActorsState actors, ITargetingPolicy policy)
{
    private const uint VisibilityPageSize = 64;
    public TargetingEvidence? LastEvidence { get; private set; }
    public bool IsValidTarget(ActorState actor) => !actor.IsDefeated && policy.IsValidTarget(actor);
    public EntityId? Current
    {
        get
        {
            TargetingComponent state = actors.Player.Targeting;
            if (state.Current is EntityId id && (!actors.Store.IsAlive(id)
                || !actors.Store.Has<ActorBody>(id) || !IsValidTarget(new ActorState(new Actor(actors.Store, id))))) state.Clear();
            return state.Current;
        }
    }
    public void Clear() { actors.Player.Targeting.Clear(); LastEvidence = null; }
    public long? Select(WorldPoint? position, Vector3 forward, double? actionReach)
    {
        if (position is not WorldPoint origin) { Clear(); return null; }
        ulong observer = checked((ulong)actors.Player.DurableId);
        PerceptionTarget[] targets = actors.All.Where(IsValidTarget).OrderBy(a => a.DurableId)
            .Select(a => new PerceptionTarget(checked((ulong)a.DurableId), a.Position.ToVector())).ToArray();
        PerceptionQueryRequest request = new(spatial.Session,
            new[] { new PerceptionObserver(observer, origin.ToVector(), forward, policy.MaximumDistance(actionReach), policy.MinimumFacingCosine, 1d) },
            targets, ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 0, VisibilityPageSize);
        PerceptionReadoutLeaseReceipt receipt = perception.QueryVisibility(request);
        long? selected = receipt.Pairs.ToArray().Where(p => p.Observer == observer && p.Kind == PerceptionPairKind.Visible
            && p.Target <= long.MaxValue && actors.TryGet((long)p.Target, out ActorState actor) && IsValidTarget(actor))
            .OrderBy(p => p.Distance).ThenBy(p => p.Target).Select(p => (long?)p.Target).FirstOrDefault();
        actors.Player.Targeting.Current = selected is long durable ? actors.Entities.Resolve(ActorsState.Identity(durable)) : null;
        LastEvidence = new(request, receipt, selected);
        return selected;
    }
}
