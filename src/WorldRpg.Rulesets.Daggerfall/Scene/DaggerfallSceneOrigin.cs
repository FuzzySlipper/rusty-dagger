using System.Numerics;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How a scene-local position follows an Engine world-origin commit: it moves by the commit's local delta, as the Engine
/// moves every local transform, and a non-finite result is refused. Exterior commits the origin; everything placed in the
/// scene follows it this way.
/// </summary>
internal static class DaggerfallSceneOrigin
{
    internal static WorldPoint Shift(WorldPoint position, Vector3 localDelta)
    {
        Vector3 shifted = position.ToVector() + localDelta;
        if (!float.IsFinite(shifted.X) || !float.IsFinite(shifted.Y) || !float.IsFinite(shifted.Z))
            throw new InvalidOperationException("WorldOrigin rebase produced a non-finite product position.");
        return WorldPoint.From(shifted);
    }
}
