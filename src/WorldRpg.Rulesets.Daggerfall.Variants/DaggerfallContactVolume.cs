using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Variants;

/// <summary>The box a swing or a touch sweeps in front of an actor's eye, turned to its heading.</summary>
internal static class DaggerfallContactVolume
{
    /// <summary>
    /// A box as wide and tall as given, as deep as <paramref name="reach"/>, starting at the eye and dropped
    /// by <paramref name="drop"/> toward the body it meets, posed on the horizontal heading of <paramref name="forward"/>.
    /// </summary>
    internal static (SpatialShape Shape, SpatialPose Pose) Ahead(Vector3 eye, Vector3 forward, double width, double height, double drop, double reach)
    {
        Vector3 level = new(forward.X, 0f, forward.Z);
        float yaw = level.LengthSquared() > 1e-6f ? MathF.Atan2(level.X, level.Z) : 0f;
        SpatialShape shape = SpatialShape.Box(
            new Vector3((float)width / 2f, (float)height / 2f, (float)reach / 2f),
            new Vector3(0f, -(float)drop, (float)reach / 2f));
        return (shape, new SpatialPose(eye, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw)));
    }
}
