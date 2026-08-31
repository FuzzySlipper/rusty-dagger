using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Kit.Actors;

/// <summary>Ruleset-supplied limits for one Engine navigation evaluation.</summary>
public readonly record struct ActorNavigationRequest(WorldPoint Target, float MaximumStepUnits, uint MaximumVisited)
{
    public ActorNavigationRequest Validate()
    {
        Target.Validate();
        if (!float.IsFinite(MaximumStepUnits) || MaximumStepUnits <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumStepUnits));
        }

        if (MaximumVisited == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumVisited));
        }

        return this;
    }
}

/// <summary>
/// Applies Engine navigation facts to a generic actor pose. The Engine owns
/// navigation, collision, and path evaluation; rulesets supply a target and
/// bounded request policy.
/// </summary>
public sealed class ActorNavigationCoordinator
{
    private readonly ISpatialService _spatial;
    private readonly SpatialSession _session;

    /// <summary>The supplied session remains owned by its spatial composition system.</summary>
    public ActorNavigationCoordinator(ISpatialService spatial, SpatialSession session)
    {
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// Evaluates one Engine-owned navigation step. Only an explicit Reached
    /// outcome changes product pose; every other Engine fact leaves it intact.
    /// </summary>
    public NavigationStepReceipt Evaluate(ActorState actor, ActorNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(actor);
        request.Validate();

        ActorPose before = actor.Pose;
        NavigationStepReceipt receipt = _spatial.EvaluateNavigationStep(new NavigationStepRequest(
            _session,
            before.Position.ToVector(),
            request.Target.ToVector(),
            request.MaximumStepUnits,
            request.MaximumVisited));

        if (receipt.Outcome == NavigationPathOutcome.Reached)
        {
            actor.ApplyPose(WithHeadingForAcceptedWaypoint(before, receipt.NextWaypoint));
        }

        return receipt;
    }

    private static ActorPose WithHeadingForAcceptedWaypoint(ActorPose before, Vector3 waypoint)
    {
        WorldPoint next = WorldPoint.From(waypoint);
        float deltaX = next.X - before.Position.X;
        float deltaZ = next.Z - before.Position.Z;
        float heading = (deltaX == 0f && deltaZ == 0f)
            ? before.HeadingYawRadians
            : MathF.Atan2(deltaX, -deltaZ);
        return new ActorPose(next, heading);
    }
}
