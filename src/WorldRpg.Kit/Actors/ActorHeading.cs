using System.Numerics;

namespace WorldRpg.Kit.Actors;

/// <summary>
/// The world yaw convention shared by actor poses and player control: zero faces negative Z and
/// positive yaw turns toward positive X.
/// </summary>
public static class ActorHeading
{
    /// <summary>The unit horizontal direction a yaw faces.</summary>
    public static Vector3 Forward(float yawRadians) => new(MathF.Sin(yawRadians), 0f, -MathF.Cos(yawRadians));

    /// <summary>The yaw that faces a horizontal direction; the direction's vertical part is ignored.</summary>
    public static float Yaw(Vector3 direction) => MathF.Atan2(direction.X, -direction.Z);
}
