using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// One movable model admitted from normalized RDB content. Its render artifact and
/// source-derived Engine pose are kept apart from its model-local collision triangles.
/// Door models are represented here too, but remain owned by DaggerfallDoorRuntime.
/// </summary>
internal sealed record DaggerfallDungeonActionModelDefinition(
    string ActionId,
    string? DoorId,
    DaggerfallRdbDoorId? DoorIdentity,
    string Description,
    ushort ModelIndex,
    byte RawIndex,
    DaggerfallDoorVisual Visual,
    Transform InitialTransform,
    Vector3 LocalBoundsMin,
    Vector3 LocalBoundsMax,
    Vector3[] CollisionVertices,
    Triangle[] CollisionTriangles)
{
    internal DaggerfallDungeonActionModelDefinition Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ActionId);
        ArgumentNullException.ThrowIfNull(Description);
        Visual.Validate();
        if (!IsFinite(InitialTransform.Translation)
            || !IsFinite(InitialTransform.Rotation)
            || !IsFinite(InitialTransform.Scale)
            || InitialTransform.Scale != Vector3.One)
            throw new ArgumentOutOfRangeException(nameof(InitialTransform), "Action model transforms must be finite and use unit scale.");
        if (!IsFinite(LocalBoundsMin) || !IsFinite(LocalBoundsMax)
            || LocalBoundsMin.X > LocalBoundsMax.X
            || LocalBoundsMin.Y > LocalBoundsMax.Y
            || LocalBoundsMin.Z > LocalBoundsMax.Z)
            throw new ArgumentException("Action model local bounds must be finite and ordered.");
        ArgumentNullException.ThrowIfNull(CollisionVertices);
        ArgumentNullException.ThrowIfNull(CollisionTriangles);
        foreach (Vector3 vertex in CollisionVertices)
            if (!IsFinite(vertex)) throw new ArgumentOutOfRangeException(nameof(CollisionVertices));
        foreach (Triangle triangle in CollisionTriangles)
        {
            uint vertexCount = checked((uint)CollisionVertices.Length);
            if (triangle.A >= vertexCount
                || triangle.B >= vertexCount
                || triangle.C >= vertexCount)
                throw new ArgumentException("Action model collision triangles must address model-local vertices.", nameof(CollisionTriangles));
        }
        if (CollisionTriangles.Length != 0 && CollisionVertices.Length == 0)
            throw new ArgumentException("Action model collision triangles require vertices.", nameof(CollisionVertices));
        return this;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
