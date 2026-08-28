using System.Numerics;
using Rusty.Engine;
namespace WorldRpg.Kit.Controls;

/// <summary>Owns one Engine spatial session and persistent character continuation.</summary>
public sealed class SpatialMovementSystem : IDisposable
{
    private readonly ISpatialService _spatial;
    private readonly SpatialTuning _tuning;
    private readonly CharacterControllerConfig _controller;
    private readonly SpatialSession _session;
    private ulong _movementSequence;
    private bool _disposed;

    public SpatialMovementSystem(ISpatialService spatial, SpatialSceneInputs inputs, SpatialTuning tuning)
    {
        _spatial = spatial;
        _tuning = tuning;
        _controller = spatial.DefaultCharacterControllerConfig();
        SpatialSession session = spatial.CreateSession(new SpatialSessionConfig(tuning.CollisionVoxelSize, tuning.CollisionChunkSize, 0));
        try
        {
            if (inputs.CollisionVertices.Length != 0)
            {
                var asset = new StaticMeshAsset(1, 0, checked((uint)inputs.CollisionVertices.Length), 0, checked((uint)inputs.CollisionTriangles.Length));
                var instance = new StaticMeshInstance(1, 1, IdentityTransform());
                spatial.ReplaceCollision(new CollisionReplaceRequest(session, new[] { asset }, inputs.CollisionVertices, inputs.CollisionTriangles, new[] { instance }));
            }
            if (inputs.NavigationCells.Length != 0)
                spatial.ReplaceNavigation(new NavigationReplaceRequest(session, new PlanarNavConfig(1, tuning.NavigationCellSize, tuning.NavigationChunkSize, tuning.NavigationMaximumStepCells), inputs.NavigationCells));
            _session = session;
        }
        catch { session.Dispose(); throw; }
    }

    public void Step(PlayerControlState player, ProductUpdateState update)
    {
        if (_disposed || player.Position is not WorldPoint position) return;
        var receipt = _spatial.ProposeCharacterStep(new CharacterStepRequest(
            _session,
            position.ToVector(),
            player.Motion,
            default,
            _controller,
            new CharacterControllerCommand(
                update.PlanarIntent,
                player.YawRadians,
                JumpPressed: false,
                JumpHeld: false,
                CrouchRequested: false,
                ExternalVelocity: Vector3.Zero,
                ExternalImpulse: Vector3.Zero,
                update.DeltaSeconds,
                ++_movementSequence)));
        if (receipt.Step.Accepted)
        {
            player.MoveTo(receipt.Transform.Translation);
            player.Motion = receipt.Motion;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session.Dispose();
        _disposed = true;
    }

    private static Transform IdentityTransform() => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

}

/// <summary>Ruleset-adapted spatial data; the Kit never reads source formats or content names.</summary>
public sealed record SpatialSceneInputs(
    ReadOnlyMemory<Vector3> CollisionVertices,
    ReadOnlyMemory<Triangle> CollisionTriangles,
    ReadOnlyMemory<PlanarNavCell> NavigationCells)
{
    public static readonly SpatialSceneInputs Empty = new(ReadOnlyMemory<Vector3>.Empty, ReadOnlyMemory<Triangle>.Empty, ReadOnlyMemory<PlanarNavCell>.Empty);
}
