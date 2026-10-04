using System.Numerics;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>
/// Fixed movement facts for a dungeon action's ordinary spell missile. These are
/// structural donor facts, not a second simulation clock or a tuning profile.
/// </summary>
internal static class DaggerfallDungeonSpellPolicy
{
    // DaggerfallMissile.MovementSpeed in DFU.
    internal const float MissileMovementSpeedMetresPerSecond = 25f;

    // DaggerfallMissile.LifespanInSeconds in DFU.
    internal const double MissileLifespanSeconds = 8d;

    // DaggerfallAction.CastSpell raises the action transform by 40 *
    // MeshReader.GlobalScale (0.025 m per classic world unit).
    internal const float MissileOriginHeightMetres = 40f * .025f;

    internal static bool TryNormalizeDirection(Vector3 direction, out Vector3 normalized)
    {
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z)
            || direction.LengthSquared() <= .000001f)
        {
            normalized = default;
            return false;
        }

        normalized = Vector3.Normalize(direction);
        return float.IsFinite(normalized.X) && float.IsFinite(normalized.Y) && float.IsFinite(normalized.Z);
    }
}
