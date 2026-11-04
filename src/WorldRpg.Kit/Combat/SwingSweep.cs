using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Kit.Combat;

/// <summary>
/// An attack's path in view space, so a swaying camera doesn't move it: from one bearing to another, in degrees right of
/// the view, in a plane tilted clockwise about it by <paramref name="TiltDegrees"/> (at 90 a positive bearing is down),
/// out to <paramref name="Range"/>.
/// </summary>
public readonly record struct SwingArc(float StartDegrees, float EndDegrees, float TiltDegrees, float Range)
{
    /// <summary>The world direction the arc points at <paramref name="cycle"/> (0 to 1) of its sweep, for a view along <paramref name="forward"/>.</summary>
    public Vector3 Direction(float cycle, Vector3 forward)
    {
        Vector3 view = Vector3.Normalize(forward);
        Vector3 across = Vector3.Cross(view, Vector3.UnitY);
        Vector3 right = across.LengthSquared() > 1e-6f ? Vector3.Normalize(across) : Vector3.UnitX;
        Vector3 up = Vector3.Cross(right, view);
        float tilt = float.DegreesToRadians(TiltDegrees);
        Vector3 axis = up * MathF.Cos(tilt) + right * MathF.Sin(tilt);
        float bearing = float.DegreesToRadians(float.Lerp(StartDegrees, EndDegrees, cycle));
        return Vector3.Normalize(Vector3.Transform(view, Quaternion.CreateFromAxisAngle(axis, -bearing)));
    }
}

/// <summary>One ray a sweep cast, and what it met, for a developer readout.</summary>
public readonly record struct SwingRay(Vector3 From, Vector3 To, ContactKind Kind);

/// <summary>
/// One swing's sweep through the actor bodies. Each advance fans thin rays from the eye across the part of the arc covered
/// since the last, in the order the swing crosses it, and answers the first body they meet that this swing hasn't, so a
/// cut meets the first body in its path and misses one it passes beside. World geometry stops a ray, not the swing. It
/// traces the authored arc rather than a drawn blade, so what is drawn can't move what is hit.
/// </summary>
/// <param name="contact">The contact queries the rays go through.</param>
/// <param name="source">The swinging actor, whose own body is ignored.</param>
/// <param name="arc">The arc swept.</param>
/// <param name="rayStepDegrees">The widest gap between rays, narrower than a body at the arc's range.</param>
/// <param name="passes">The bodies this swing goes through, as if they weren't there.</param>
public sealed class SwingSweep(ActorContact contact, long source, SwingArc arc, float rayStepDegrees = 6f, Func<long, bool>? passes = null)
{
    private readonly HashSet<long> _struck = [];
    private readonly List<SwingRay> _rays = [];
    private float _cycle = -1f;

    /// <summary>Every ray cast so far.</summary>
    public IReadOnlyList<SwingRay> Rays => _rays;

    /// <summary>Sweeps on to <paramref name="cycle"/> (0 to 1) of the arc; the body met first, or null for none.</summary>
    public long? Advance(Vector3 eye, Vector3 forward, float cycle, CharacterStepEnvironment? environment = null)
    {
        if (!float.IsFinite(cycle) || cycle is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(cycle));
        float from = MathF.Max(_cycle, 0f);
        int gaps = (int)MathF.Ceiling(MathF.Abs(arc.EndDegrees - arc.StartDegrees) * (cycle - from) / rayStepDegrees);
        for (int ray = _cycle < 0f ? 0 : 1; ray <= gaps; ray++)
        {
            float at = gaps == 0 ? cycle : float.Lerp(from, cycle, ray / (float)gaps);
            Vector3 to = eye + arc.Direction(at, forward) * arc.Range;
            SegmentContact met = contact.Segment(source, eye, to, environment, passes);
            _rays.Add(new(eye, met.Kind == ContactKind.None ? to : met.Point, met.Kind));
            if (met.Kind != ContactKind.Actor || !_struck.Add(met.ActorId!.Value)) continue;
            _cycle = at;
            return met.ActorId;
        }
        _cycle = cycle;
        return null;
    }
}
