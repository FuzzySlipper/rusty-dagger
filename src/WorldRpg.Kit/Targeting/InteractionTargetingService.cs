using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Interaction;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;

namespace WorldRpg.Kit.Targeting;

/// <summary>One currently loaded world object offered to a contextual interaction.</summary>
/// <remarks>
/// The ruleset supplies the Engine query identity and retains the durable identity separately.
/// The current Engine entity becomes the interaction target revision, so reuse of a query identity
/// cannot silently authorize an action for an older loaded object.
/// <para>
/// <paramref name="SurfaceNormal"/> marks a target that lies on a surface rather than in open space, such as
/// a door plane set into a wall: its position is on that surface and the normal names its open side. Engine
/// line of sight runs all the way to the target point, so a surface coincident with the point would hide
/// the target from every side. A surface target is therefore sighted at its position lifted off the surface
/// by <see cref="InteractionTargetingService.SurfaceSeparation"/> along the normal, and only from its open
/// side: from behind the surface it is occluded.
/// </para>
/// </remarks>
public readonly record struct InteractionTargetCandidate(
    EntityId Entity,
    DurableIdentityReference Identity,
    ulong QueryIdentity,
    WorldPoint Position,
    int Precedence,
    string Label = "world object",
    double? ReachDistance = null,
    Vector3? SurfaceNormal = null)
{
    /// <summary>The point Engine focus and line of sight use: the position, lifted off its surface when it has one.</summary>
    public Vector3 SightPoint => SurfaceNormal is Vector3 normal
        ? Position.ToVector() + (Vector3.Normalize(normal) * InteractionTargetingService.SurfaceSeparation)
        : Position.ToVector();

    /// <summary>Whether an observer stands on the open side of a surface target; a target in open space faces everyone.</summary>
    public bool FacesObserver(WorldPoint observer) =>
        SurfaceNormal is not Vector3 normal || Vector3.Dot(observer.ToVector() - Position.ToVector(), normal) > 0f;

    public void Validate()
    {
        Identity.Validate();
        if (QueryIdentity == 0) throw new ArgumentOutOfRangeException(nameof(QueryIdentity));
        Position.Validate();
        if (Precedence < 0) throw new ArgumentOutOfRangeException(nameof(Precedence));
        if (string.IsNullOrWhiteSpace(Label)) throw new ArgumentException("An interaction target requires a label.", nameof(Label));
        if (ReachDistance is double reach && (!double.IsFinite(reach) || reach < 0d || reach > float.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(ReachDistance));
        if (SurfaceNormal is Vector3 normal && (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z)
            || normal.LengthSquared() < 1e-12f))
            throw new ArgumentOutOfRangeException(nameof(SurfaceNormal), "A surface target requires a finite, non-zero surface normal.");
    }
}

/// <summary>Product action invoked only after Engine focus and fresh target revalidation succeed.</summary>
public readonly record struct InteractionTargetingActionResult(bool Performed, string Message);

/// <summary>One explicit contextual action over a selected loaded object.</summary>
public delegate InteractionTargetingActionResult InteractionTargetingAction(InteractionTargetCandidate target);

/// <summary>Copied Engine visibility, focus, and use evidence for one contextual action.</summary>
public sealed record InteractionTargetingEvidence(
    PerceptionQueryRequest Request,
    PerceptionReadoutResult Receipt,
    InteractionQuery Query,
    InteractionReadout Focus,
    InteractionUseReceipt Use,
    DurableIdentityReference? SelectedIdentity);

/// <summary>
/// Adapts current product candidates to the Engine interaction owner. The ruleset supplies labels,
/// precedence, reach policy, and the typed action; Engine owns candidate ranking, sticky focus, and
/// use-time identity/reach/visibility revalidation. Each action issues exactly one perception query.
/// </summary>
public sealed class InteractionTargetingService(IPerceptionService perception, SpatialMovementSystem spatial, EntityDirectory entities)
{
    /// <summary>
    /// How far a surface target's sight point stands off its surface: enough that a ray from the open side
    /// stops short of the surface the target lies on, small enough not to move the target off its surface
    /// for reach and focus.
    /// </summary>
    public const float SurfaceSeparation = .01f;

    private const uint VisibilityPageSize = 64;
    private readonly IPerceptionService _perception = perception ?? throw new ArgumentNullException(nameof(perception));
    private readonly SpatialMovementSystem _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
    private readonly EntityDirectory _entities = entities ?? throw new ArgumentNullException(nameof(entities));

    public InteractionTargetingEvidence? LastEvidence { get; private set; }

    /// <summary>
    /// Runs one normal Engine world interaction. A query has one Perception readout which is retained
    /// only for the synchronous focus/use pair; the second scene read still resolves loaded identity
    /// before Engine revalidates it.
    /// </summary>
    public InteractionUseReceipt Activate(
        EntityId observer,
        WorldPoint? origin,
        Vector3 forward,
        double maximumDistance,
        double minimumFacingCosine,
        IEnumerable<InteractionTargetCandidate> candidates,
        InteractionTargetingAction action)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(action);
        if (origin is not WorldPoint position || !double.IsFinite(maximumDistance) || maximumDistance <= 0d
            || maximumDistance > float.MaxValue || !double.IsFinite(minimumFacingCosine) || minimumFacingCosine is < -1d or > 1d)
        {
            LastEvidence = null;
            return new(null, InteractionReason.NoCandidate, false, "No eligible target within reach.", "focus", "invalid-query");
        }

        InteractionTargetCandidate[] declared = candidates.Select(candidate =>
        {
            candidate.Validate();
            return candidate;
        }).ToArray();
        if (declared.GroupBy(candidate => candidate.QueryIdentity).Any(group => group.Skip(1).Any()))
            throw new InvalidOperationException("One Engine interaction identity cannot be registered as multiple targets.");
        if (!declared.Any(IsCurrent))
        {
            LastEvidence = null;
            return new(null, InteractionReason.NoCandidate, false, "No eligible target within reach.", "focus", "empty-scene");
        }

        InteractionScene scene = new(_perception, _spatial, _entities, observer, position, forward,
            (float)maximumDistance, (float)minimumFacingCosine, declared, action);
        WorldInteraction interaction = new(scene, targetedUseEnabled: false);
        InteractionReadout focus = interaction.Update();
        InteractionUseReceipt use = interaction.UseFocused();
        LastEvidence = scene.Evidence(focus, use);
        return use;
    }

    /// <summary>Fresh Engine rejection facts without focus acquisition, activation or last-action changes.</summary>
    public WorldInteractionReadout Inspect(EntityId observer, WorldPoint origin, Vector3 forward,
        double maximumDistance, double minimumFacingCosine, IEnumerable<InteractionTargetCandidate> candidates) =>
        CreateInspection(observer, origin, forward, maximumDistance, minimumFacingCosine, candidates).Inspect();

    /// <summary>The same read-only scene for Engine's standard inspection commands; targeted use remains disabled.</summary>
    public WorldInteraction CreateInspection(EntityId observer, WorldPoint origin, Vector3 forward,
        double maximumDistance, double minimumFacingCosine, IEnumerable<InteractionTargetCandidate> candidates)
    {
        InteractionTargetCandidate[] declared = candidates.ToArray();
        foreach (var candidate in declared) candidate.Validate();
        InteractionScene scene = new(_perception, _spatial, _entities, observer, origin, forward,
            (float)maximumDistance, (float)minimumFacingCosine, declared, null);
        return new WorldInteraction(scene, targetedUseEnabled: false);
    }

    private bool IsCurrent(InteractionTargetCandidate candidate) =>
        _entities.TryResolve(candidate.Identity, out EntityId current) && current == candidate.Entity;

    private sealed class InteractionScene(
        IPerceptionService perception,
        SpatialMovementSystem spatial,
        EntityDirectory entities,
        EntityId observer,
        WorldPoint origin,
        Vector3 forward,
        float maximumDistance,
        float minimumFacingCosine,
        InteractionTargetCandidate[] declared,
        InteractionTargetingAction? action) : IWorldInteractionScene
    {
        private readonly IPerceptionService _perception = perception;
        private readonly SpatialMovementSystem _spatial = spatial;
        private readonly EntityDirectory _entities = entities;
        private readonly EntityId _observer = observer;
        private readonly WorldPoint _origin = origin;
        private readonly InteractionTargetCandidate[] _declared = declared;
        private readonly InteractionTargetingAction? _action = action;
        private readonly InteractionQuery _query = Query(origin, forward, maximumDistance, minimumFacingCosine, declared);
        private static InteractionQuery Query(WorldPoint origin, Vector3 forward, float defaultReach,
            float minimumFacingCosine, InteractionTargetCandidate[] candidates)
        {
            // Visibility/focus must cover the authored reach of every target. A target without
            // an override still uses the ordinary default in ToEngineCandidate below.
            float queryReach = Math.Max(defaultReach,
                candidates.Select(candidate => (float)(candidate.ReachDistance ?? defaultReach)).DefaultIfEmpty(defaultReach).Max());
            float angle = MathF.Acos(minimumFacingCosine);
            return new(origin.ToVector(), forward, angle, angle, queryReach, queryReach, AngularWeight: 1, DistanceWeight: 1);
        }
        private PerceptionQueryRequest? _request;
        private PerceptionReadoutResult? _receipt;
        private Dictionary<ulong, InteractionVisibility>? _visibility;
        private Dictionary<ulong, InteractionTargetCandidate>? _current;

        public InteractionSceneSnapshot ReadInteraction()
        {
            EnsureVisibility();
            InteractionTargetCandidate[] loaded = _declared.Where(IsCurrent).ToArray();
            InteractionCandidate[] candidates = loaded.Select(ToEngineCandidate).ToArray();
            _current = loaded.ToDictionary(candidate => candidate.QueryIdentity);
            return new(_query, candidates, "contextual-activation", "activate");
        }

        public InteractionActionResult UseInteraction(InteractionTarget target)
        {
            if (_current is null || !_current.TryGetValue(target.Id, out InteractionTargetCandidate candidate)
                || candidate.Entity.Value != target.Revision || !IsCurrent(candidate))
            {
                return new(false, "The selected target is no longer loaded.");
            }
            InteractionTargetingActionResult result = (_action ?? throw new InvalidOperationException("Inspection cannot activate a target."))(candidate);
            return new(result.Performed, result.Message);
        }

        internal InteractionTargetingEvidence Evidence(InteractionReadout focus, InteractionUseReceipt use)
        {
            EnsureVisibility();
            DurableIdentityReference? selected = use.Target is { } target && _current is not null
                && _current.TryGetValue(target.Id, out InteractionTargetCandidate candidate)
                    ? candidate.Identity
                    : null;
            return new(_request!.Value, _receipt!.Value, _query, focus, use, selected);
        }

        private void EnsureVisibility()
        {
            if (_receipt is not null) return;
            InteractionTargetCandidate[] loaded = _declared.Where(IsCurrent).ToArray();
            _request = new PerceptionQueryRequest(
                _spatial.Session,
                new PerceptionObserver[] { new(_observer.Value, _origin.ToVector(), _query.Direction, _query.MaximumDistance, minimumFacingCosine, 1d) },
                loaded.Select(candidate => new PerceptionTarget(candidate.QueryIdentity, candidate.SightPoint)).ToArray(),
                ReadOnlyMemory<SpatialEntityCollider>.Empty,
                0,
                0,
                VisibilityPageSize);
            _receipt = _perception.QueryVisibility(_request.Value);
            _visibility = _receipt.Value.Pairs.ToArray()
                .Where(pair => pair.Observer == _observer.Value)
                .GroupBy(pair => pair.Target)
                .ToDictionary(
                    group => group.Key,
                    group => group.Any(pair => pair.Kind == PerceptionPairKind.Visible)
                        ? InteractionVisibility.Visible
                        : InteractionVisibility.Occluded);
            // A surface target seen from behind its surface is hidden by it, whatever the surface's back face does.
            foreach (InteractionTargetCandidate candidate in loaded)
                if (!candidate.FacesObserver(_origin) && _visibility.ContainsKey(candidate.QueryIdentity))
                    _visibility[candidate.QueryIdentity] = InteractionVisibility.Occluded;
        }

        private InteractionCandidate ToEngineCandidate(InteractionTargetCandidate candidate) => new(
            new InteractionTarget(candidate.QueryIdentity, candidate.Entity.Value),
            candidate.Label,
            candidate.SightPoint,
            candidate.ReachDistance is double reach ? (float)reach : maximumDistance,
            _visibility!.TryGetValue(candidate.QueryIdentity, out InteractionVisibility visibility)
                ? visibility : InteractionVisibility.Unknown,
            InteractionAvailability.Available,
            Priority: -candidate.Precedence);

        private bool IsCurrent(InteractionTargetCandidate candidate) =>
            _entities.TryResolve(candidate.Identity, out EntityId current) && current == candidate.Entity;
    }
}
