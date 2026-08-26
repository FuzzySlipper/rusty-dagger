using System.Numerics;
using Rusty.Engine;

namespace RustyDagger.Game;

/// <summary>Owns Dagger's one Engine spatial session and persistent character continuation.</summary>
public sealed class SpatialGameplayService : IDisposable
{
    private readonly IEngineContext _engine;
    private readonly SpatialSession _session;
    private ulong _movementSequence;
    private bool _disposed;

    public SpatialGameplayService(IEngineContext engine, PrivateersHoldInputs inputs)
    {
        _engine = engine;
        _session = engine.Spatial.CreateSession(new SpatialSessionConfig(.5, 32, 0));
        if (inputs.Collision.Vertices.Length != 0)
        {
            var asset = new StaticMeshAsset(1, 0, checked((uint)inputs.Collision.Vertices.Length), 0, checked((uint)inputs.Collision.Triangles.Length));
            var instance = new StaticMeshInstance(1, 1, IdentityTransform());
            engine.Spatial.ReplaceCollision(new CollisionReplaceRequest(_session, new[] { asset }, inputs.Collision.Vertices, inputs.Collision.Triangles, new[] { instance }));
        }
        if (inputs.Navigation.Length != 0)
            engine.Spatial.ReplaceNavigation(new NavigationReplaceRequest(_session, new PlanarNavConfig(1, .5, 32, 2), inputs.Navigation));
    }

    public void Step(DaggerGameState state, GameplayUpdate update)
    {
        if (_disposed || state.Player.Position is not WorldPoint position) return;
        var receipt = _engine.Spatial.ProposeCharacterStep(new CharacterStepRequest(
            _session,
            position.ToVector(),
            state.Player.Motion,
            default,
            ControllerConfig(),
            new CharacterControllerCommand(
                update.PlanarIntent,
                state.Player.YawRadians,
                JumpPressed: 0,
                JumpHeld: 0,
                CrouchRequested: 0,
                Reserved: 0,
                update.DeltaSeconds,
                ++_movementSequence)));
        if (receipt.StepAccepted != 0)
        {
            state.Player.MoveTo(receipt.Transform.Translation);
            state.Player.Motion = receipt.Motion;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session.Dispose();
        _disposed = true;
    }

    private static Transform IdentityTransform() => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    private static CharacterControllerConfig ControllerConfig() => new(
        StandingHeight: 1.8f,
        CrouchedHeight: 1.2f,
        Radius: .3f,
        ContactSkin: .02f,
        ForwardSpeed: 3.5f,
        BackwardSpeed: 3.5f,
        StrafeSpeed: 3.5f,
        Acceleration: 25f,
        Braking: 30f,
        Friction: 8f,
        Gravity: 19.6f,
        JumpSpeed: 5f,
        MaximumSlopeRadians: .78f,
        MaximumStepHeight: .4f,
        FloorSnapDistance: .15f,
        MaximumDisplacementPerStep: .35f);
}
