using System.Numerics;
using Rusty.Engine;
using RustyDagger.Game.Daggerfall.Content;

namespace RustyDagger.Game.Modules.PlayerControl;

/// <summary>Owns Dagger's one Engine spatial session and persistent character continuation.</summary>
internal sealed class SpatialMovementSystem : IDisposable
{
    private readonly ISpatialService _spatial;
    private readonly SpatialTuning _tuning;
    private readonly SpatialSession _session;
    private ulong _movementSequence;
    private bool _disposed;

    internal SpatialMovementSystem(ISpatialService spatial, PrivateersHoldInputs inputs, SpatialTuning tuning)
    {
        _spatial = spatial;
        _tuning = tuning;
        SpatialSession session = spatial.CreateSession(new SpatialSessionConfig(tuning.CollisionVoxelSize, tuning.CollisionChunkSize, 0));
        try
        {
            if (inputs.Collision.Vertices.Length != 0)
            {
                var asset = new StaticMeshAsset(1, 0, checked((uint)inputs.Collision.Vertices.Length), 0, checked((uint)inputs.Collision.Triangles.Length));
                var instance = new StaticMeshInstance(1, 1, IdentityTransform());
                spatial.ReplaceCollision(new CollisionReplaceRequest(session, new[] { asset }, inputs.Collision.Vertices, inputs.Collision.Triangles, new[] { instance }));
            }
            if (inputs.Navigation.Length != 0)
                spatial.ReplaceNavigation(new NavigationReplaceRequest(session, new PlanarNavConfig(1, tuning.NavigationCellSize, tuning.NavigationChunkSize, tuning.NavigationMaximumStepCells), inputs.Navigation));
            _session = session;
        }
        catch { session.Dispose(); throw; }
    }

    internal void Step(PlayerControlState player, ProductUpdateState update)
    {
        if (_disposed || player.Position is not WorldPoint position) return;
        var receipt = _spatial.ProposeCharacterStep(new CharacterStepRequest(
            _session,
            position.ToVector(),
            player.Motion,
            default,
            _tuning.Controller,
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
