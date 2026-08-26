using Rusty.Engine.Native;

namespace RustyDagger.Product;

/// <summary>Owns Dagger's one Engine spatial session and persistent character continuation.</summary>
public sealed class SpatialGameplayService : IDisposable
{
    private readonly EngineApi _engine;
    private readonly NativeSpatialSessionHandle _session;
    private ulong _movementSequence;
    private bool _disposed;

    public SpatialGameplayService(EngineApi engine, PrivateersHoldInputs inputs)
    {
        _engine = engine;
        _session = engine.Spatial.CreateSession(new NativeSpatialSessionConfig { collision_voxel_size = .5, collision_chunk_size = 32 });
        if (inputs.Collision.Vertices.Length != 0)
        {
            var asset = new NativeStaticMeshAsset { id = 1, first_vertex = 0, vertex_count = checked((uint)inputs.Collision.Vertices.Length), first_triangle = 0, triangle_count = checked((uint)inputs.Collision.Triangles.Length) };
            var instance = new NativeStaticMeshInstance { id = 1, asset = 1, transform = IdentityTransform() };
            engine.Spatial.ReplaceCollision(_session, [asset], inputs.Collision.Vertices, inputs.Collision.Triangles, [instance]);
        }
        if (inputs.Navigation.Length != 0)
            engine.Spatial.ReplaceNavigation(_session, new NativePlanarNavConfig { grid_id = 1, cell_size = .5, chunk_size = 32, max_step_cells = 2 }, inputs.Navigation);
    }

    public void Step(DaggerGameState state, GameplayTurn turn)
    {
        if (_disposed || state.Player.Position is not WorldPoint position) return;
        var receipt = _engine.Spatial.ProposeCharacterStep(new NativeCharacterStepRequest
        {
            session = _session,
            position = position.ToNative(),
            motion = state.Player.Motion,
            support = default,
            config = ControllerConfig(),
            command = new NativeCharacterControllerCommand
            {
                planar_intent = turn.PlanarIntent,
                heading_yaw_radians = state.Player.YawRadians,
                step_seconds = turn.DeltaSeconds,
                sequence = ++_movementSequence,
            },
        });
        if (receipt.step_accepted != 0)
        {
            state.Player.MoveTo(receipt.transform.translation);
            state.Player.Motion = receipt.motion;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine.Spatial.DestroySession(_session);
        _disposed = true;
    }

    private static NativeTransform IdentityTransform() => new() { rotation = new NativeQuat { w = 1 }, scale = new NativeVec3 { x = 1, y = 1, z = 1 } };
    private static NativeCharacterControllerConfig ControllerConfig() => new()
    {
        standing_height = 1.8f, crouched_height = 1.2f, radius = .3f, contact_skin = .02f,
        forward_speed = 3.5f, backward_speed = 3.5f, strafe_speed = 3.5f, acceleration = 25f,
        braking = 30f, friction = 8f, gravity = 19.6f, jump_speed = 5f, maximum_slope_radians = .78f,
        maximum_step_height = .4f, floor_snap_distance = .15f, maximum_displacement_per_step = .35f,
    };
}
