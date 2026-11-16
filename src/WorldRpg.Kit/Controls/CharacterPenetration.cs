using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

/// <summary>
/// The one rule for a body the Engine will not step because it overlaps collision deeper than the controller's bounded
/// recovery resolves. <c>ProposeCharacterStep</c> then throws <c>unresolved-character-controller-penetration</c> and
/// leaves the choice to the product, and every input from inside the overlap is refused the same way. So the body is
/// stood at the nearest clear spot within the controller's own recovery reach, at rest, and steps on from there; with
/// none clear it is held where it is, reported once. The player's step and an actor's step or throw share it, so a forced
/// developer teleport, a remembered pose that collision now overlaps, or collision arriving through a body never faults
/// the runtime.
/// </summary>
public static class CharacterPenetration
{
    /// <summary>How many distances, evenly spaced out to the reach, are tried.</summary>
    private const int Rings = 4;

    /// <summary>How many bearings around the body are tried at each distance, after straight up.</summary>
    private const int Bearings = 8;

    /// <summary>Whether a failed character step is the Engine refusing a body its recovery could not resolve.</summary>
    public static bool IsUnresolved(EngineCallException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (failure.Service != "Spatial" || failure.Operation != "ProposeCharacterStep") return false;
        foreach (EngineDiagnostic diagnostic in failure.Diagnostics.Span)
            if (diagnostic.Code == "unresolved-character-controller-penetration") return true;
        return false;
    }

    /// <summary>
    /// The nearest capsule centre to <paramref name="centre"/> where a body of <paramref name="stance"/> overlaps nothing in
    /// the admitted scene or the environment's obstacles, no farther than the controller's recovery may push a body
    /// (<see cref="CharacterRecoveryConfig.MaximumDistance"/>); null when none is. Spots are tried a quarter of the reach
    /// apart, straight up first and then around the body, never lower than it, so a body is not pushed through its floor;
    /// a spot with a surface between it and the body's centre (the far side of a wall) is not taken.
    /// </summary>
    public static Vector3? NearestClearCentre(ISpatialService spatial, SpatialSession session, CharacterControllerConfig config,
        CharacterStance stance, Vector3 centre, CharacterStepEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(spatial);
        ArgumentNullException.ThrowIfNull(session);
        float reach = config.Recovery.MaximumDistance;
        if (!(reach > 0f)) return null;
        CharacterShapeConfig shape = config.Shape;
        float height = stance == CharacterStance.Crouched ? shape.CrouchedHeight : shape.StandingHeight;
        double halfHeight = Math.Max(0d, (height * .5d) - shape.Radius);
        SpatialQueryFilter filter = new(uint.MaxValue, uint.MaxValue);
        ReadOnlyMemory<SpatialEntityCollider> obstacles = SpatialMovementSystem.SpatialColliders(environment);
        for (int ring = 1; ring <= Rings; ring++)
        {
            float distance = reach * ring / Rings;
            for (int bearing = -1; bearing < Bearings; bearing++)
            {
                Vector3 direction = bearing < 0 ? Vector3.UnitY : Around(bearing * MathF.Tau / Bearings);
                Vector3 candidate = centre + (direction * distance);
                if (spatial.OverlapCapsule(new SpatialCapsuleQueryRequest(session, candidate, halfHeight, shape.Radius,
                        Vector3.Zero, 0d, filter, obstacles, ReadOnlyMemory<ulong>.Empty)).Present)
                    continue;
                SpatialHit between = spatial.CastRay(new SpatialRaycastRequest(session, centre, direction, distance, filter,
                    obstacles, ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<SpatialEntityCollider>.Empty));
                // A centre already inside a solid has no side to keep to.
                if (between.Present && !between.StartSolid) continue;
                return candidate;
            }
        }
        return null;
    }

    private static Vector3 Around(float radians)
    {
        (float sin, float cos) = MathF.SinCos(radians);
        return new Vector3(sin, 0f, cos);
    }
}
