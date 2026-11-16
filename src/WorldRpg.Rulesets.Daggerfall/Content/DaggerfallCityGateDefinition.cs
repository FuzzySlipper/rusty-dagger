using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>One state of a city gate: the mesh it draws and the model-local collision it blocks with.</summary>
internal sealed record DaggerfallCityGateState(DaggerfallDoorVisual Visual, Vector3 LocalBoundsMin, Vector3 LocalBoundsMax,
    Vector3[] CollisionVertices, Triangle[] CollisionTriangles)
{
    /// <summary>
    /// The ARCH3D record id of the model this state draws (446 open, 447 closed), by which its visual takes a remodel
    /// (<see cref="DaggerfallSiteGeometry.Remodels"/>).
    /// </summary>
    internal string? ModelId { get; init; }

    internal DaggerfallCityGateState Validate()
    {
        Visual.Validate();
        if (CollisionTriangles.Length == 0) throw new ArgumentException("A city gate state must block with its own collision.", nameof(CollisionTriangles));
        if (CollisionTriangles.Any(triangle => Math.Max(triangle.A, Math.Max(triangle.B, triangle.C)) >= CollisionVertices.Length))
            throw new ArgumentException("A city gate collision triangle names a vertex the state does not carry.", nameof(CollisionTriangles));
        return this;
    }
}

/// <summary>
/// One city gate placed in a profile: its pose in the profile frame and its open and closed states. The donor's
/// <c>DaggerfallCityGate</c> swaps the placement's model between them at dusk and dawn.
/// </summary>
internal sealed record DaggerfallCityGateDefinition(string Id, Transform Pose, DaggerfallCityGateState Open, DaggerfallCityGateState Closed)
{
    internal DaggerfallCityGateDefinition Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        Open.Validate();
        Closed.Validate();
        return this;
    }
}
