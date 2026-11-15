using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Whether admitted static geometry stands between a sound's source and the listener, asked of the Engine's own
/// segment query at eye height. A source standing inside geometry would otherwise report every sound as covered,
/// so the segment starts clear of it.
/// </summary>
internal sealed class DaggerfallSoundCover
{
    /// <summary>How far along the segment the query starts, clear of the source's own position.</summary>
    internal const float CastStartMeters = .3f;

    private readonly SpatialMovementSystem _spatial;
    private readonly float _eyeHeight;

    internal DaggerfallSoundCover(SpatialMovementSystem spatial, float eyeHeight)
    {
        _spatial = spatial;
        _eyeHeight = eyeHeight;
        Blocks = Blocked;
    }

    /// <summary><see cref="Blocked"/> as one delegate, handed to the appearance every update without allocating.</summary>
    internal Func<WorldPoint, WorldPoint, bool> Blocks { get; }

    internal bool Blocked(WorldPoint origin, WorldPoint aim)
    {
        Vector3 from = origin.ToVector() + Vector3.UnitY * _eyeHeight;
        Vector3 to = aim.ToVector() + Vector3.UnitY * _eyeHeight;
        Vector3 delta = to - from;
        float distance = delta.Length();
        if (!float.IsFinite(distance) || distance <= CastStartMeters) return false;
        Vector3 direction = delta / distance;
        SpatialHit hit = _spatial.CastRay(from + direction * CastStartMeters, direction, distance - CastStartMeters);
        return hit.Present && hit.Kind == SpatialHitKind.StaticMesh;
    }
}
