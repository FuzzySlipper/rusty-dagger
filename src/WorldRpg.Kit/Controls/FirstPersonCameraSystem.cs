using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

/// <summary>Typed product tuning for the Engine-owned first-person camera.</summary>
public sealed record FirstPersonCameraTuning(
    float EyeHeight,
    double FieldOfViewYDegrees,
    double NearPlane,
    double FarPlane)
{
    public FirstPersonCameraTuning Validate()
    {
        if (!float.IsFinite(EyeHeight) || EyeHeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(EyeHeight));
        if (!double.IsFinite(FieldOfViewYDegrees) || FieldOfViewYDegrees <= 0d || FieldOfViewYDegrees >= 180d)
            throw new ArgumentOutOfRangeException(nameof(FieldOfViewYDegrees));
        if (!double.IsFinite(NearPlane) || NearPlane <= 0d)
            throw new ArgumentOutOfRangeException(nameof(NearPlane));
        if (!double.IsFinite(FarPlane) || FarPlane <= NearPlane)
            throw new ArgumentOutOfRangeException(nameof(FarPlane));
        return this;
    }
}

/// <summary>
/// Keeps one Engine-owned camera synchronized with an authoritative player
/// control state. The product supplies pose facts; the Engine owns realization.
/// </summary>
public sealed class FirstPersonCameraSystem : IDisposable
{
    private static readonly CameraViewport FullViewport = new(0d, 0d, 1d, 1d);
    private static readonly double RadiansToDegrees = 180d / Math.PI;

    private readonly ICameraViewService _cameraView;
    private readonly Camera _camera;
    private readonly PlayerControlState _player;
    private readonly FirstPersonCameraTuning _tuning;
    private double _farPlane;
    private bool _disposed;
    private double _punchPitchDegrees;
    private double _punchYawDegrees;

    public FirstPersonCameraSystem(
        ICameraViewService cameraView,
        PlayerControlState player,
        FirstPersonCameraTuning tuning)
    {
        _cameraView = cameraView ?? throw new ArgumentNullException(nameof(cameraView));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _farPlane = _tuning.FarPlane;

        Camera? camera = null;
        try
        {
            camera = _cameraView.CreateCamera(Descriptor(_player));
            _cameraView.SetActiveCamera(camera);
            _camera = camera;
        }
        catch
        {
            camera?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Updates the one Engine-owned camera. The optional eye-height offset lets a product realize
    /// a presentation pose, such as a defeat fall, without creating a second camera or mutating
    /// the authoritative player control state.
    /// </summary>
    public void Update(PlayerControlState player, float eyeHeightOffset = 0f)
    {
        if (_disposed) return;
        if (!ReferenceEquals(_player, player))
        {
            throw new InvalidOperationException("A first-person camera remains bound to its constructed player control state.");
        }
        if (!float.IsFinite(eyeHeightOffset)) throw new ArgumentOutOfRangeException(nameof(eyeHeightOffset));

        _cameraView.UpdateCamera(new CameraUpdateRequest(_camera, Descriptor(_player, eyeHeightOffset)));
    }

    /// <summary>Knocks the view by these angles, on top of the look. The player's aim never moves.</summary>
    public void Punch(double pitchDegrees, double yawDegrees)
    {
        _punchPitchDegrees += pitchDegrees;
        _punchYawDegrees += yawDegrees;
    }

    /// <summary>Lets a punch ease back at <paramref name="ratePerSecond"/>, over admitted time.</summary>
    public void SettlePunch(double seconds, double ratePerSecond)
    {
        double remaining = Math.Exp(-ratePerSecond * seconds);
        _punchPitchDegrees *= remaining;
        _punchYawDegrees *= remaining;
    }

    /// <summary>The derived eye point submitted to the Engine camera descriptor.</summary>
    public WorldPoint Viewpoint => ViewpointFor(_player);

    /// <summary>
    /// How far the world draws from the next update on, or the tuned plane with none: the far plane clips everything, so a
    /// place that draws out past it names its own. Null returns to the tuned plane.
    /// </summary>
    public void DrawOutTo(double? farPlane)
    {
        double plane = farPlane ?? _tuning.FarPlane;
        if (!double.IsFinite(plane) || plane <= _tuning.NearPlane) throw new ArgumentOutOfRangeException(nameof(farPlane));
        _farPlane = plane;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Destroying the camera clears it as the active camera only while it still is. Clearing the active
        // camera here would also clear a replacement session's camera, which is built before this one ends.
        _camera.Dispose();
    }

    private CameraDescriptor Descriptor(PlayerControlState player, float eyeHeightOffset = 0f)
    {
        WorldPoint viewpoint = ViewpointFor(player, eyeHeightOffset);
        return new CameraDescriptor(
            new CameraPose(
                viewpoint.ToVector(),
                player.PitchRadians * RadiansToDegrees + _punchPitchDegrees,
                player.YawRadians * RadiansToDegrees + _punchYawDegrees),
            CameraBasisMode.Derived,
            default,
            new CameraProjection(
                CameraProjectionKind.Perspective,
                _tuning.FieldOfViewYDegrees,
                0d,
                _tuning.NearPlane,
                _farPlane),
            FullViewport);
    }

    private WorldPoint ViewpointFor(PlayerControlState player, float eyeHeightOffset = 0f)
    {
        WorldPoint position = player.Position ?? throw new InvalidOperationException("A first-person camera requires a player position.");
        return WorldPoint.From(position.ToVector() + (Vector3.UnitY * (_tuning.EyeHeight + eyeHeightOffset)));
    }
}
