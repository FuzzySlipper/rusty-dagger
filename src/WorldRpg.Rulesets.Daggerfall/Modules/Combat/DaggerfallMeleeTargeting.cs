using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>Copied Engine perception evidence and the Daggerfall target selected from it for one ordinary melee attempt.</summary>
internal sealed record DaggerfallMeleeTargetingEvidence(
    PerceptionQueryRequest Request,
    PerceptionReadoutLeaseReceipt Receipt,
    long? SelectedTargetId);

/// <summary>
/// Daggerfall-owned target eligibility and deterministic choice over a single
/// Engine visibility query. The Engine remains authoritative for range,
/// facing-cone, and occlusion classification.
/// </summary>
internal sealed class DaggerfallMeleeTargetingModule
{
    private const long PlayerId = DaggerfallActorIdentity.PlayerEntityId;
    private readonly IPerceptionService _perception;
    private readonly SpatialMovementSystem _spatial;
    private readonly ActorsState _actors;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _definitions;
    private readonly DaggerfallMeleeTargetingTuning _tuning;

    internal DaggerfallMeleeTargetingModule(
        IPerceptionService perception,
        SpatialMovementSystem spatial,
        ActorsState actors,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions,
        DaggerfallMeleeTargetingTuning tuning)
    {
        _perception = perception ?? throw new ArgumentNullException(nameof(perception));
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
    }

    internal DaggerfallMeleeTargetingEvidence? LastEvidence { get; private set; }

    internal void ClearEvidence() => LastEvidence = null;

    internal long? Select(PlayerControlState player, LookReceipt look, double? actionReach)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Position is not WorldPoint position)
        {
            ClearEvidence();
            return null;
        }

        double maximumDistance = ResolveMaximumDistance(actionReach);
        PerceptionTarget[] targets = _actors.All.Values
            .Where(IsEligible)
            .OrderBy(actor => actor.EntityId)
            .Select(actor => new PerceptionTarget(checked((ulong)actor.EntityId), actor.Position.ToVector()))
            .ToArray();
        PerceptionObserver[] observers =
        [
            new PerceptionObserver(
                checked((ulong)PlayerId),
                position.ToVector(),
                look.Forward,
                maximumDistance,
                _tuning.MinimumFacingCosine,
                1d),
        ];
        PerceptionQueryRequest request = new(
            _spatial.Session,
            observers,
            targets,
            ReadOnlyMemory<SpatialEntityCollider>.Empty);
        PerceptionReadoutLeaseReceipt receipt = _perception.QueryVisibility(request);
        long? selected = receipt.Pairs.ToArray()
            .Where(pair => pair.Observer == (ulong)PlayerId
                && pair.Kind == PerceptionPairKind.Visible
                && pair.Target <= long.MaxValue
                && IsEligibleTarget(checked((long)pair.Target)))
            .OrderBy(pair => pair.Distance)
            .ThenBy(pair => pair.Target)
            .Select(pair => checked((long)pair.Target))
            .Select(entityId => (long?)entityId)
            .FirstOrDefault();
        LastEvidence = new DaggerfallMeleeTargetingEvidence(request, receipt, selected);
        return selected;
    }

    private bool IsEligible(ActorState actor) => actor.EntityId != PlayerId
        && actor.EntityId > 0
        && actor.Mechanics.Entity.Value == (ulong)actor.EntityId
        && _definitions.ContainsKey(actor.EntityId)
        && !actor.IsDefeated;

    private bool IsEligibleTarget(long entityId) => _actors.TryGet(entityId, out ActorState actor) && IsEligible(actor);

    private double ResolveMaximumDistance(double? actionReach)
    {
        if (actionReach is not double reach) return _tuning.MaximumDistance;
        if (!double.IsFinite(reach) || reach <= 0d) throw new InvalidOperationException("Player melee action reach must be a positive finite authored value.");
        return Math.Min(_tuning.MaximumDistance, reach);
    }
}
